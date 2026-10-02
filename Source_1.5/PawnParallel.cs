using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// Pawn 子系统并行结算。
    ///
    /// 设计（吸取我们在 FastLoad 里的实测教训）：
    /// * **每个 tick 只派发一次**：把本 tick 收集到的所有子系统工作合并成一个大列表，
    ///   用一次 GenThreading.ParallelFor 处理（上次按子系统逐个派发，每批仅 ~55 项，
    ///   派发开销反而大于省下的工作量，是负收益）；
    /// * **只并行 IL 核对为自包含的子系统**：EquipmentTrackerTick / NativeVerbsTick
    ///   （只调各自的 VerbTracker）；HealthTick 因"损伤可致死 ⇒ 触发地图变更"默认关闭；
    /// * 主线程收集，列表不需要锁；结算期间用全局闸放行原方法，避免重入（Harmony 打过的
    ///   方法用其 MethodInfo 建委托调用时会再次进入收集前缀）。
    /// </summary>
    public static class PawnParallel
    {
        public static bool Enabled;
        public static int Workers = 4;
        public static bool IncludeHealth;

        private static readonly List<Pawn_EquipmentTracker> equip = new List<Pawn_EquipmentTracker>(512);
        private static readonly List<Pawn_NativeVerbs> native = new List<Pawn_NativeVerbs>(512);
        private static readonly List<Pawn_HealthTracker> health = new List<Pawn_HealthTracker>(512);

        private static Action<Pawn_EquipmentTracker> equipCall;
        private static Action<Pawn_NativeVerbs> nativeCall;
        private static Action<Pawn_HealthTracker> healthCall;

        private static volatile int flushing;
        public static long Batches;
        public static long Items;

        public static bool Init()
        {
            try
            {
                MethodInfo e = AccessTools.Method(typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick", Type.EmptyTypes);
                MethodInfo n = AccessTools.Method(typeof(Pawn_NativeVerbs), "NativeVerbsTick", Type.EmptyTypes);
                MethodInfo h = AccessTools.Method(typeof(Pawn_HealthTracker), "HealthTick", Type.EmptyTypes);
                if (e == null || n == null) return false;
                equipCall = (Action<Pawn_EquipmentTracker>)Delegate.CreateDelegate(typeof(Action<Pawn_EquipmentTracker>), e);
                nativeCall = (Action<Pawn_NativeVerbs>)Delegate.CreateDelegate(typeof(Action<Pawn_NativeVerbs>), n);
                if (h != null)
                {
                    healthCall = (Action<Pawn_HealthTracker>)Delegate.CreateDelegate(typeof(Action<Pawn_HealthTracker>), h);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] Pawn 并行初始化失败: " + ex.Message);
                return false;
            }
        }

        public static bool Collect_Equip(Pawn_EquipmentTracker __instance)
        {
            if (!Enabled || flushing != 0 || __instance == null) return true;
            equip.Add(__instance);
            return false;
        }

        public static bool Collect_Native(Pawn_NativeVerbs __instance)
        {
            if (!Enabled || flushing != 0 || __instance == null) return true;
            native.Add(__instance);
            return false;
        }

        public static bool Collect_Health(Pawn_HealthTracker __instance)
        {
            if (!Enabled || !IncludeHealth || flushing != 0 || __instance == null) return true;
            health.Add(__instance);
            return false;
        }

        /// <summary>tick 末尾统一结算（挂在 Map.MapPostTick 之后）。</summary>
        public static void Flush()
        {
            if (!Enabled) return;
            int total = equip.Count + native.Count + health.Count;
            if (total == 0) return;
            try
            {
                flushing++;
                if (total < 64)
                {
                    for (int i = 0; i < total; i++) RunOne(i);
                }
                else
                {
                    GenThreading.ParallelFor(0, total, RunOne, Workers);
                }
                Batches++;
                Items += total;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[RimThreadedTTR] Pawn 并行结算异常，已回退串行: " + ex, 771933);
                for (int i = 0; i < total; i++)
                {
                    try { RunOne(i); } catch { }
                }
            }
            finally
            {
                equip.Clear();
                native.Clear();
                health.Clear();
                flushing--;
            }
        }

        private static void RunOne(int i)
        {
            int n = equip.Count;
            if (i < n) { equipCall(equip[i]); return; }
            i -= n;
            n = native.Count;
            if (i < n) { nativeCall(native[i]); return; }
            i -= n;
            if (healthCall != null && i < health.Count) healthCall(health[i]);
        }
    }
}
