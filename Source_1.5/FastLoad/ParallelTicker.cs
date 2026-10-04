using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FastLoad
{
    /// <summary>
    /// 并行 tick 试点：把"每个小人各自独立、不碰地图共享状态"的子系统**收集**起来，
    /// 在 tick 末尾（MapPostTick 之后）用**原版** <see cref="GenThreading.ParallelForEach{T}"/> 并行执行。
    ///
    /// 设计要点：
    /// * 收集只发生在主线程（Pawn.Tick 内），列表无需加锁；
    /// * 目标方法本身是纯自包含的（已用 IL 核对：只调自己的 VerbTracker）；
    /// * 默认关闭；出问题可直接在设置里关掉。
    /// </summary>
    public static class ParallelTicker
    {
        public static bool Enabled;
        public static int Degree = 4;

        [ThreadStatic] private static List<Pawn_EquipmentTracker> _equip;
        [ThreadStatic] private static List<Pawn_NativeVerbs> _native;

        private static Action<Pawn_EquipmentTracker> _equipCall;
        private static Action<Pawn_NativeVerbs> _nativeCall;

        public static long Runs;          // 并行批次次数（诊断）
        public static long Items;         // 并行处理的对象数（诊断）
        /// <summary>结算期间的重入闸：**必须全局**（worker 线程也会走收集前缀）。</summary>
        private static volatile int _flushing;

        public static bool Init()
        {
            try
            {
                MethodInfo equip = AccessTools.Method(typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick", Type.EmptyTypes);
                MethodInfo native = AccessTools.Method(typeof(Pawn_NativeVerbs), "NativeVerbsTick", Type.EmptyTypes);
                if (equip == null || native == null) return false;
                _equipCall = (Action<Pawn_EquipmentTracker>)Delegate.CreateDelegate(typeof(Action<Pawn_EquipmentTracker>), equip);
                _nativeCall = (Action<Pawn_NativeVerbs>)Delegate.CreateDelegate(typeof(Action<Pawn_NativeVerbs>), native);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 并行 tick 初始化失败: " + e.Message);
                return false;
            }
        }

        // ── 收集前缀（返回 false = 跳过内联执行，改为稍后并行执行）──
        public static bool Collect_Equip(Pawn_EquipmentTracker __instance)
        {
            if (!Enabled || _flushing != 0 || __instance == null) return true;   // 结算中 ⇒ 放行原方法
            if (_equip == null) _equip = new List<Pawn_EquipmentTracker>(256);
            _equip.Add(__instance);
            return false;
        }

        public static bool Collect_Native(Pawn_NativeVerbs __instance)
        {
            if (!Enabled || _flushing != 0 || __instance == null) return true;
            if (_native == null) _native = new List<Pawn_NativeVerbs>(256);
            _native.Add(__instance);
            return false;
        }

        /// <summary>tick 末尾统一并行结算（挂在 Map.MapPostTick 之后）。</summary>
        public static void Flush()
        {
            if (!Enabled) return;
            FlushList(_equip, _equipCall);
            FlushList(_native, _nativeCall);
        }

        private static void FlushList<T>(List<T> list, Action<T> call)
        {
            if (list == null || list.Count == 0 || call == null) return;
            try
            {
                _flushing++;                              // 关闸：结算期间前缀一律放行原方法
                int n = list.Count;
                if (n < 8)                                // 太少就别付并行的开销
                {
                    for (int i = 0; i < n; i++) call(list[i]);
                }
                else
                {
                    GenThreading.ParallelForEach(list, call, Degree);
                }
                Runs++;
                Items += n;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[RimThreadedTTR] FastLoad: 并行 tick 异常（已回退串行）: " + e, 1872231);
                for (int i = 0; i < list.Count; i++)
                {
                    try { call(list[i]); } catch { }
                }
            }
            finally
            {
                list.Clear();
                _flushing--;
            }
        }
    }
}
