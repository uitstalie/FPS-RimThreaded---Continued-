using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 真多线程第 1 步：**每 HediffSet 实例锁**。
    ///
    /// 实测崩点（并行 Pawn tick）：
    ///   InvalidOperationException: Collection was modified
    ///     at List`1+Enumerator.MoveNextRare
    ///     at Verse.Pawn_HealthTracker.HealthTick()      ← 枚举 hediff 集合时被别的线程改
    ///     at Verse.Pawn.Tick() → Thing.DoTick() → TickListParallel.DoTickThing()
    ///
    /// 做法：`HealthTick`（读）与所有写入口（`HediffSet.AddDirect`/`Clear`/… 及
    /// `Pawn_HealthTracker.RemoveHediff`）**在同一实例上互斥**。
    /// 因为锁的是"每个小人的 HediffSet 对象"，不同小人之间没有竞争 ⇒ 并行度保留。
    /// `Monitor` 可重入 ⇒ 嵌套调用安全；用 finalizer 保证异常路径也会释放锁。
    /// </summary>
    public static class HediffLock
    {
        public static bool Enabled;
        public static long Enters;
        public static long Waits;

        public static bool Enter_Prefix(object __instance)
        {
            if (!Enabled || __instance == null) return true;
            if (!Monitor.TryEnter(__instance))
            {
                Waits++;
                Monitor.Enter(__instance);          // 有竞争：等锁（记录次数以便评估开销）
            }
            Enters++;
            return true;
        }

        public static Exception Exit_Finalizer(object __instance, Exception __exception)
        {
            if (Enabled && __instance != null) Monitor.Exit(__instance);
            return null;
        }

        public static void Apply(Harmony harmony)
        {
            MethodInfo enter = AccessTools.Method(typeof(HediffLock), "Enter_Prefix");
            MethodInfo exit = AccessTools.Method(typeof(HediffLock), "Exit_Finalizer");
            int n = 0;
            // 读侧：健康 tick（枚举 hediff）
            n += PatchAll(harmony, typeof(Pawn_HealthTracker), new[] { "HealthTick", "HealthTickInterval" }, enter, exit);
            // 写侧：HediffSet 的修改入口（含所有重载）
            n += PatchAll(harmony, typeof(HediffSet), new[] { "AddDirect", "Clear", "RemoveHediff", "SetHediff" }, enter, exit);
            n += PatchAll(harmony, typeof(Pawn_HealthTracker), new[] { "RemoveHediff", "AddHediff", "AddOrUpdateHediff" }, enter, exit);
            Enabled = true;
            Log.Message("[RimThreadedTTR] HediffSet 每实例锁已启用：" + n + " 个方法（读+写）");
        }

        private static int PatchAll(Harmony harmony, Type type, string[] names, MethodInfo enter, MethodInfo exit)
        {
            int n = 0;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                bool hit = false;
                for (int i = 0; i < names.Length; i++) if (m.Name == names[i]) { hit = true; break; }
                if (!hit) continue;
                try
                {
                    harmony.Patch(m, new HarmonyMethod(enter), null, null, new HarmonyMethod(exit));
                    n++;
                }
                catch (Exception e)
                {
                    Log.Warning("[RimThreadedTTR] HediffSet 锁挂载失败 " + type.Name + "." + m.Name + ": " + e.Message);
                }
            }
            return n;
        }
    }
}
