using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimThreadedTTR
{
    /// <summary>
    /// 探针：统计每 tick 有多少次 `Thing.DoTick()`（TickList 主循环的派发点）。
    /// 决定 P2 该用"逐调用拦截收集"还是"整体替换循环"：
    /// 每次拦截约 70 ns（我们实测过的前缀开销）⇒ 若 N 很大，收集开销会吃掉并行收益。
    /// </summary>
    public static class DoTickProbe
    {
        public static bool Enabled;
        public static long TotalCalls;
        public static long TotalTicks;
        public static double LastPerTick;
        public static double PeakPerTick;

        private static long calls;
        private static long ticks;
        private static float lastLog;

        public static bool Prefix()
        {
            if (Enabled) calls++;
            return true;
        }

        public static void TickPostfix()
        {
            if (!Enabled) return;
            ticks++;
            float now = Time.realtimeSinceStartup;
            if (now - lastLog < 10f) return;
            if (ticks > 0)
            {
                double per = (double)calls / ticks;
                LastPerTick = per;
                if (per > PeakPerTick) PeakPerTick = per;
                TotalCalls += calls;
                TotalTicks += ticks;
                Log.Message(string.Format(
                    "[TTR-probe] Thing.DoTick {0} 次 / {1} tick = {2:F1} 次/tick（本窗口）", calls, ticks, per));
            }
            calls = 0;
            ticks = 0;
            lastLog = now;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Thing), "DoTick", Type.EmptyTypes);
                if (target == null) { Log.Warning("[TTR-probe] 找不到 Thing.DoTick"); return; }
                harmony.Patch(target, new HarmonyMethod(AccessTools.Method(typeof(DoTickProbe), "Prefix")), null, null, null);
                MethodInfo post = AccessTools.Method(typeof(Map), "MapPostTick", Type.EmptyTypes);
                if (post != null)
                {
                    harmony.Patch(post, null, new HarmonyMethod(AccessTools.Method(typeof(DoTickProbe), "TickPostfix")), null, null);
                }
                Enabled = true;
                Log.Message("[TTR-probe] Thing.DoTick 探针已启用");
            }
            catch (Exception e)
            {
                Log.Warning("[TTR-probe] 启用失败: " + e.Message);
            }
        }
    }
}
