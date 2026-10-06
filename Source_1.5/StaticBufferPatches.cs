using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// B1：把原版"**静态**查询结果缓冲"改成"**每实例**缓冲"。
    ///
    /// 思路来自 Kingfisher 评估报告 §7.1①，但**完全从原版反编译结果独立实现**
    /// （没有复制/改写它的任何文件；它用的是 Prepatcher 系的 `[AddField]`，
    /// 我们只用纯 Harmony + `ConditionalWeakTable` 等价一个"每实例字段"）。
    ///
    /// 反编译实证（1.6.4871，/tmp/ildump 对应的原文）：
    /// <code>
    /// // Verse.ListerBuildings（第 84 / 86 行）
    /// private static List&lt;Building&gt; allBuildingsColonistOfDefResult = new List&lt;Building&gt;();
    /// private static List&lt;Building&gt; allBuildingsColonistOfGroupResult = new List&lt;Building&gt;();
    ///
    /// public List&lt;Building&gt; AllBuildingsColonistOfDef(ThingDef def)
    /// {
    ///     allBuildingsColonistOfDefResult.Clear();
    ///     for (int i = 0; i &lt; allBuildingsColonist.Count; i++)
    ///         if (allBuildingsColonist[i].def == def)
    ///             allBuildingsColonistOfDefResult.Add(allBuildingsColonist[i]);
    ///     return allBuildingsColonistOfDefResult;
    /// }
    ///
    /// // Verse.ImmunityHandler（第 21 行）
    /// private static readonly List&lt;ImmunityInfo&gt; tmpNeededImmunitiesNow = new List&lt;ImmunityInfo&gt;();
    ///
    /// private List&lt;ImmunityInfo&gt; NeededImmunitiesNow()
    /// {
    ///     tmpNeededImmunitiesNow.Clear();
    ///     List&lt;Hediff&gt; hediffs = pawn.health.hediffSet.hediffs;
    ///     for (int i = 0; i &lt; hediffs.Count; i++)
    ///         if (hediffs[i].def.PossibleToDevelopImmunityNaturally())
    ///             tmpNeededImmunitiesNow.Add(new ImmunityInfo { immunity = hediff.def, source = hediff.def });
    ///     return tmpNeededImmunitiesNow;
    /// }
    /// </code>
    ///
    /// 为什么这是风险：`ListerBuildings` 是**每张地图一份**、`ImmunityHandler` 是**每个小人一份**，
    /// 但缓冲是 `static` ⇒ 两张地图（或两个小人）同时查询会互相 `Clear()`/`Add()` 掉对方的结果。
    /// 这正是我们 P2 并行反复踩到的那类"跨线程共享临时缓冲"（MapPawns 池化列表）的同一形状。
    ///
    /// 改法（等价重写调用点，不用 Transpiler 注入字段）：
    /// 用 Prefix 返回 false 接管方法体，把结果写进 `ConditionalWeakTable&lt;实例, List&lt;T&gt;&gt;` 里的
    /// **每实例**缓冲。返回值的"生命周期契约"与原版一致：**该列表在同一个实例上再次调用前有效**
    /// （原版是"在任意调用前有效"——那是被我们去掉的跨实例共享；调用的**内容/顺序**完全不变）。
    ///
    /// 额外用 `[ThreadStatic]` 记住"上次那个实例 → 那份缓冲"，避开 `ConditionalWeakTable.GetValue`
    /// 的内部锁，同时不引入任何跨线程共享（每线程自己一份快路径）。
    /// </summary>
    public static class StaticBufferPatches
    {
        public static long OfDefQueries;
        public static long OfGroupQueries;
        public static long ImmunityQueries;

        private static readonly AccessTools.FieldRef<ListerBuildings, List<Building>> ColonistBuildings =
            AccessTools.FieldRefAccess<ListerBuildings, List<Building>>("allBuildingsColonist");

        private static readonly ConditionalWeakTable<ListerBuildings, List<Building>>.CreateValueCallback NewBuildingList =
            delegate (ListerBuildings key) { return new List<Building>(); };

        private static readonly ConditionalWeakTable<ListerBuildings, List<Building>> OfDefBuffers =
            new ConditionalWeakTable<ListerBuildings, List<Building>>();

        private static readonly ConditionalWeakTable<ListerBuildings, List<Building>> OfGroupBuffers =
            new ConditionalWeakTable<ListerBuildings, List<Building>>();

        private static readonly ConditionalWeakTable<ImmunityHandler, List<ImmunityHandler.ImmunityInfo>>.CreateValueCallback NewImmunityList =
            delegate (ImmunityHandler key) { return new List<ImmunityHandler.ImmunityInfo>(); };

        private static readonly ConditionalWeakTable<ImmunityHandler, List<ImmunityHandler.ImmunityInfo>> ImmunityBuffers =
            new ConditionalWeakTable<ImmunityHandler, List<ImmunityHandler.ImmunityInfo>>();

        [ThreadStatic] private static ListerBuildings tlsOfDefKey;
        [ThreadStatic] private static List<Building> tlsOfDefBuffer;
        [ThreadStatic] private static ListerBuildings tlsOfGroupKey;
        [ThreadStatic] private static List<Building> tlsOfGroupBuffer;
        [ThreadStatic] private static ImmunityHandler tlsImmunityKey;
        [ThreadStatic] private static List<ImmunityHandler.ImmunityInfo> tlsImmunityBuffer;

        private static List<Building> BufferFor(
            ConditionalWeakTable<ListerBuildings, List<Building>> table,
            ListerBuildings owner, ref ListerBuildings fastKey, ref List<Building> fastBuffer)
        {
            if (ReferenceEquals(owner, fastKey))
            {
                return fastBuffer;
            }
            List<Building> buffer = table.GetValue(owner, NewBuildingList);
            fastKey = owner;
            fastBuffer = buffer;
            return buffer;
        }

        // ---------- Verse.ListerBuildings.AllBuildingsColonistOfDef(ThingDef) ----------
        public static bool AllBuildingsColonistOfDef_Prefix(ListerBuildings __instance, ThingDef def, ref List<Building> __result)
        {
            try
            {
                List<Building> result = BufferFor(OfDefBuffers, __instance, ref tlsOfDefKey, ref tlsOfDefBuffer);
                List<Building> source = ColonistBuildings(__instance);
                result.Clear();                                  // 原版：allBuildingsColonistOfDefResult.Clear()
                for (int i = 0; i < source.Count; i++)            // 原版逐行等价
                {
                    if (source[i].def == def)
                    {
                        result.Add(source[i]);
                    }
                }
                __result = result;
                OfDefQueries++;
                return false;
            }
            catch (Exception)
            {
                return true;                                     // 任何异常 ⇒ 退回原版（含它的静态缓冲）
            }
        }

        // ---------- Verse.ListerBuildings.AllBuildingsColonistOfGroup(ThingRequestGroup) ----------
        // 同一个文件里的同一形状（allBuildingsColonistOfGroupResult，第 86 行），顺手一起改。
        public static bool AllBuildingsColonistOfGroup_Prefix(ListerBuildings __instance, ThingRequestGroup group, ref List<Building> __result)
        {
            try
            {
                List<Building> result = BufferFor(OfGroupBuffers, __instance, ref tlsOfGroupKey, ref tlsOfGroupBuffer);
                List<Building> source = ColonistBuildings(__instance);
                result.Clear();
                for (int i = 0; i < source.Count; i++)
                {
                    if (group.Includes(source[i].def))
                    {
                        result.Add(source[i]);
                    }
                }
                __result = result;
                OfGroupQueries++;
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ---------- Verse.ImmunityHandler.NeededImmunitiesNow()（private） ----------
        public static bool NeededImmunitiesNow_Prefix(ImmunityHandler __instance, ref List<ImmunityHandler.ImmunityInfo> __result)
        {
            try
            {
                List<ImmunityHandler.ImmunityInfo> result;
                if (ReferenceEquals(__instance, tlsImmunityKey))
                {
                    result = tlsImmunityBuffer;
                }
                else
                {
                    result = ImmunityBuffers.GetValue(__instance, NewImmunityList);
                    tlsImmunityKey = __instance;
                    tlsImmunityBuffer = result;
                }
                result.Clear();                                  // 原版：tmpNeededImmunitiesNow.Clear()
                List<Hediff> hediffs = __instance.pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)           // 原版逐行等价
                {
                    Hediff hediff = hediffs[i];
                    if (hediff.def.PossibleToDevelopImmunityNaturally())
                    {
                        ImmunityHandler.ImmunityInfo info = default(ImmunityHandler.ImmunityInfo);
                        info.immunity = hediff.def;
                        info.source = hediff.def;
                        result.Add(info);
                    }
                }
                __result = result;
                ImmunityQueries++;
                return false;
            }
            catch (Exception)
            {
                return true;                                     // 退回原版（含它的静态缓冲）
            }
        }

        /// <summary>由 TTRCore 调用（设置开关 + 失败只记日志，不影响游戏）。</summary>
        public static void Apply(Harmony harmony)
        {
            int ok = 0;
            ok += Patch(harmony, AccessTools.Method(typeof(ListerBuildings), "AllBuildingsColonistOfDef", new[] { typeof(ThingDef) }),
                "AllBuildingsColonistOfDef_Prefix") ? 1 : 0;
            ok += Patch(harmony, AccessTools.Method(typeof(ListerBuildings), "AllBuildingsColonistOfGroup", new[] { typeof(ThingRequestGroup) }),
                "AllBuildingsColonistOfGroup_Prefix") ? 1 : 0;
            ok += Patch(harmony, AccessTools.Method(typeof(ImmunityHandler), "NeededImmunitiesNow", Type.EmptyTypes),
                "NeededImmunitiesNow_Prefix") ? 1 : 0;
            Log.Message("[RimThreadedTTR] B1 静态结果缓冲 → 每实例缓冲：" + ok + "/3 项生效"
                + "（ListerBuildings.ofDef / ofGroup / ImmunityHandler.NeededImmunitiesNow）");
        }

        private static bool Patch(Harmony harmony, System.Reflection.MethodInfo target, string prefixName)
        {
            try
            {
                if (target == null)
                {
                    Log.Warning("[RimThreadedTTR] B1 目标不存在：" + prefixName);
                    return false;
                }
                System.Reflection.MethodInfo prefix = AccessTools.Method(typeof(StaticBufferPatches), prefixName);
                harmony.Patch(target, new HarmonyMethod(prefix), null, null, null);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] B1 挂载失败 " + prefixName + ": " + e.Message);
                return false;
            }
        }
    }
}
