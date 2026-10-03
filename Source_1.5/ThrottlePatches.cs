using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 降频补丁（Performance-Optimizer 风格）：让"与调用次数成正比、且不需要每 tick 精确"的系统
    /// 每 N tick 才真正执行一次。前缀返回 false 即跳过原方法。
    ///
    /// 全部来自实测（RimWorld 1.6.4871，真实存档，µs/tick）：
    ///   WindManagerTick 140 → 每 4 tick ⇒ 省 ~105
    ///   GasGrid.Tick    125 → 每 2 tick ⇒ 省 ~62
    ///   ListerHaulablesTick 42 → 每 2 tick ⇒ 省 ~21
    ///   EffecterMaintainerTick / PawnRenderer.EffectersTick（纯视觉）⇒ 省 ~19
    /// 合计约 −200 µs/tick（相对 1.5 ms/tick 约 −13%）。
    ///
    /// 刻意**不做** FleckManagerTick：跳 tick 会让 fleck 存活更久、数量翻倍，每次调用工作量随之翻倍，
    /// 实测总耗时不变（34 → 36 µs/tick，白降且有视觉风险）。
    /// </summary>
    public static class ThrottlePatches
    {
        /// <summary>每 2 tick 执行一次。</summary>
        public static bool Every2()
        {
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        /// <summary>每 4 tick 执行一次。</summary>
        public static bool Every4()
        {
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 3) == 0;
        }

        /// <summary>Pawn_PathFollower.pawn 是 protected ⇒ 用 Harmony 的字段访问器。</summary>
        private static readonly AccessTools.FieldRef<Verse.AI.Pawn_PathFollower, Pawn> PatherPawn =
            AccessTools.FieldRefAccess<Verse.AI.Pawn_PathFollower, Pawn>("pawn");

        /// <summary>野生动物判定：无派系的动物（圈养动物属于玩家派系）。</summary>
        private static bool IsWildAnimal(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Animal && pawn.Faction == null;
        }

        /// <summary>睡觉小人的 JobDriver 每 2 tick（其余 JobDriver 不受影响）。</summary>
        public static bool LayDown_Prefix(Verse.AI.JobDriver __instance)
        {
            if (!(__instance is RimWorld.JobDriver_LayDown)) return true;
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        public static bool WildMind_Prefix(Verse.AI.Pawn_MindState __instance)
        {
            if (!IsWildAnimal(__instance.pawn)) return true;
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        public static bool WildPather_Prefix(Verse.AI.Pawn_PathFollower __instance)
        {
            if (!IsWildAnimal(PatherPawn(__instance))) return true;
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        public static void Apply(Harmony harmony, TTRSettings s)
        {
            int ok = 0;
            if (s.throttleSimulation)
            {
                ok += Patch(harmony, "Verse.WindManager", "WindManagerTick", Type.EmptyTypes, "Every4");
                ok += Patch(harmony, "Verse.GasGrid", "Tick", Type.EmptyTypes, "Every2");
                ok += Patch(harmony, "RimWorld.ListerHaulables", "ListerHaulablesTick", Type.EmptyTypes, "Every2");
            }
            if (s.throttleVisual)
            {
                ok += Patch(harmony, "RimWorld.EffecterMaintainer", "EffecterMaintainerTick", Type.EmptyTypes, "Every2");
                ok += Patch(harmony, "Verse.PawnRenderer", "EffectersTick", new[] { typeof(bool) }, "Every2");
            }
            if (s.throttleDoor)
            {
                ok += Patch(harmony, "RimWorld.Building_Door", "Tick", Type.EmptyTypes, "Every2");
            }
            if (s.throttleLayDown)
            {
                // JobDriver_LayDown 没有实现 DriverTick（继承自 JobDriver）⇒ 挂基类，前缀里只对 LayDown 生效
                MethodInfo driverTick = AccessTools.Method(typeof(Verse.AI.JobDriver), "DriverTick", Type.EmptyTypes);
                if (driverTick != null)
                {
                    harmony.Patch(driverTick, new HarmonyMethod(typeof(ThrottlePatches).GetMethod("LayDown_Prefix")), null, null, null);
                    ok++;
                }
            }
            if (s.throttleStoryteller)
            {
                ok += Patch(harmony, "RimWorld.Storyteller", "StorytellerTick", Type.EmptyTypes, "Every2");
            }
            if (s.throttleMugirl)
            {
                ok += Patch(harmony, "Mugirl.CorporateNetwork", "GameComponentTick", Type.EmptyTypes, "Every4");
            }
            if (s.throttleWildAnimals)
            {
                // 只降"野生动物"（无派系动物）的**思考/寻路**，需求与健康仍每 tick 结算
                MethodInfo mind = AccessTools.Method(typeof(Verse.AI.Pawn_MindState), "MindStateTickInterval", new[] { typeof(int) });
                MethodInfo path = AccessTools.Method(typeof(Verse.AI.Pawn_PathFollower), "PatherTick", Type.EmptyTypes);
                if (mind != null) { harmony.Patch(mind, new HarmonyMethod(typeof(ThrottlePatches).GetMethod("WildMind_Prefix")), null, null, null); ok++; }
                if (path != null) { harmony.Patch(path, new HarmonyMethod(typeof(ThrottlePatches).GetMethod("WildPather_Prefix")), null, null, null); ok++; }
            }
            if (s.throttleMood)
            {
                // 玩法节奏相关：默认关。需求（心情）每 2 次才结算一次。
                ok += Patch(harmony, "RimWorld.Need_Mood", "NeedInterval", Type.EmptyTypes, "Every2");
            }
            Log.Message("[RimThreadedTTR] 降频补丁：" + ok + " 项生效（模拟=" + s.throttleSimulation
                + " 视觉=" + s.throttleVisual + " 心情=" + s.throttleMood + "）");
        }

        private static int Patch(Harmony harmony, string typeName, string method, Type[] args, string prefixName)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null) return 0;
                MethodInfo target = AccessTools.Method(type, method, args);
                if (target == null) return 0;
                MethodInfo prefix = AccessTools.Method(typeof(ThrottlePatches), prefixName);
                harmony.Patch(target, new HarmonyMethod(prefix), null, null, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] 降频补丁失败 " + typeName + "." + method + ": " + e.Message);
                return 0;
            }
        }
    }
}
