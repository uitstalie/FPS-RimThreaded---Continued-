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
    ///
    /// **A4 复核（2026-10-04）——只有"按间隔结算/纯视觉/幂等轮询"的才允许留守：**
    /// | 目标 | 分类 | 依据（反编译 1.6.4871） |
    /// |---|---|---|
    /// | WindManagerTick | 视觉 | 风值 = `BaseWindSpeedAt(TicksAbs)` 纯函数；唯一每 tick 累积的是 `plantSwayHead`（着色器摇摆相位）|
    /// | ListerHaulablesTick | 幂等轮询 | `groupCycleIndex++` 只是轮询游标；`Check()` 按当前状态重算"是否可搬运"，不累积量 ⇒ 只增加发现延迟 |
    /// | EffecterMaintainerTick | 视觉 | `Effecter.ticksLeft--` ⇒ 只影响视觉特效寿命/播放速率 |
    /// | PawnRenderer.EffectersTick | 视觉 | `PawnStatusEffecters` 重算待维护特效 + 清理过期对，无游戏状态 |
    /// | StorytellerTick | 间隔 | 主判定 `queuedIncident.FireTick <= TicksGame` 与 `TicksGame % 1000 == 0` ⇒ 可跳（重试判定 `%833` 最多晚 ~833 tick）|
    /// | GasGrid.Tick | **速率** | `cycleIndex*++` 游标 + 每次访问固定扣减密度 ⇒ 跳过一半 = 速率减半 ⇒ **默认 false** |
    /// | Need_Mood.NeedInterval | **速率** | `IsHashIntervalTick(150, delta)`，哈希偏移为奇数的小人命中 tick 全为奇数 ⇒ 被"只放行偶数"全部跳过 ⇒ **默认 false** |
    /// | Pawn_PathFollower.PatherTick（野生）| **速率** | `nextCellCostLeft -= CostToPayThisTick()`（每次约 1）⇒ 移动速度减半 ⇒ **默认 false** |
    /// | Pawn_MindState.MindStateTickInterval（野生）| **速率** | delta 由调用方给出，跳过即丢失 ⇒ 子区间速率减半 ⇒ **默认 false** |
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

        /// <summary>A：是否"所有小人"都降 AI（由设置打开）。</summary>
        public static bool AllPawnAI;

        public static bool WildMind_Prefix(Verse.AI.Pawn_MindState __instance)
        {
            if (!IsWildAnimal(__instance.pawn) && !AllPawnAI) return true;
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        public static bool WildPather_Prefix(Verse.AI.Pawn_PathFollower __instance)
        {
            if (!IsWildAnimal(PatherPawn(__instance)) && !AllPawnAI) return true;
            TickManager tm = Find.TickManager;
            return tm == null || (tm.TicksGame & 1) == 0;
        }

        public static void Apply(Harmony harmony, TTRSettings s)
        {
            int ok = 0;
            if (s.throttleSimulation)
            {
                ok += Patch(harmony, "Verse.WindManager", "WindManagerTick", Type.EmptyTypes, "Every4");
                // A4：GasGrid 已从本组移出 —— 它是"游标推进 + 固定扣减量"的每 tick 累积器，
                // 跳过一半 tick 会让气体消散/扩散速率减半。见 s.throttleGas。
                ok += Patch(harmony, "RimWorld.ListerHaulables", "ListerHaulablesTick", Type.EmptyTypes, "Every2");
            }
            if (s.throttleGas)
            {
                ok += Patch(harmony, "Verse.GasGrid", "Tick", Type.EmptyTypes, "Every2");
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
            ThrottlePatches.AllPawnAI = s.throttlePawnAI;
            if (s.throttleWildAnimals || s.throttlePawnAI)
            {
                // A4：本组默认 false（Pather 每次扣固定移动成本 ⇒ 速度减半；MindState 丢 delta ⇒ 子区间减半）。
                // 只有用户显式打开（或打开 throttlePawnAI）才挂。
                MethodInfo mind = AccessTools.Method(typeof(Verse.AI.Pawn_MindState), "MindStateTickInterval", new[] { typeof(int) });
                MethodInfo path = AccessTools.Method(typeof(Verse.AI.Pawn_PathFollower), "PatherTick", Type.EmptyTypes);
                if (mind != null) { harmony.Patch(mind, new HarmonyMethod(typeof(ThrottlePatches).GetMethod("WildMind_Prefix")), null, null, null); ok++; }
                if (path != null) { harmony.Patch(path, new HarmonyMethod(typeof(ThrottlePatches).GetMethod("WildPather_Prefix")), null, null, null); ok++; }
            }
            if (s.throttleMood)
            {
                // A4：默认 false —— 见 TTRSettings.throttleMood 注释（奇数哈希偏移的小人会被全部跳过）。
                ok += Patch(harmony, "RimWorld.Need_Mood", "NeedInterval", Type.EmptyTypes, "Every2");
            }
            Log.Message("[RimThreadedTTR] 降频补丁：" + ok + " 项生效（模拟[风+搬运]=" + s.throttleSimulation
                + " 视觉[Effecter+小人特效]=" + s.throttleVisual
                + " 气体=" + s.throttleGas + " 心情=" + s.throttleMood
                + " 野生AI=" + s.throttleWildAnimals + " 叙述者=" + s.throttleStoryteller
                + " Mugirl=" + s.throttleMugirl + "）");
            Log.Message("[RimThreadedTTR] 降频清单（A4）："
                + (s.throttleSimulation ? "WindManagerTick/4 · ListerHaulablesTick/2；" : "")
                + (s.throttleVisual ? "EffecterMaintainerTick/2 · PawnRenderer.EffectersTick/2；" : "")
                + (s.throttleGas ? "GasGrid.Tick/2（速率类，已由用户显式开启）；" : "")
                + (s.throttleStoryteller ? "StorytellerTick/2；" : "")
                + (s.throttleMugirl ? "Mugirl.CorporateNetwork.GameComponentTick/4（类型不存在时记 0）；" : "")
                + (s.throttleDoor ? "Building_Door.Tick/2（速率类）；" : "")
                + (s.throttleLayDown ? "JobDriver_LayDown.DriverTick/2（速率类）；" : "")
                + (s.throttleMood ? "Need_Mood.NeedInterval/2（速率类）；" : "")
                + ((s.throttleWildAnimals || s.throttlePawnAI) ? "野生/全部小人 MindState+Pather/2（速率类）；" : "")
                + "其余默认关闭");
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
