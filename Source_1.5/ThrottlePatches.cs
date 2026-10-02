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
