using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace RimThreadedTTR
{
    /// <summary>
    /// P2：`TickList.Tick()` 主循环的**循环级切片并行**（零逐调用拦截）。
    ///
    /// 为什么不能用"拦截 Thing.DoTick 收集"：实测每次拦截 ≈ **2.1 µs**，
    /// 而 Normal 列表约 222 个 thing/tick ⇒ 收集就要 0.47 ms/tick，比要并行的 356 µs 还贵。
    ///
    /// 因此这里**整体替换** `TickList.Tick()`：把其私有字段取出来，忠实复刻原逻辑
    /// （注册/注销 → `thingLists[TicksGame % interval]` → 跳过 Destroyed → DoTick），
    /// 只把主循环换成 GenThreading.ParallelFor（派发仅 26 µs）。
    ///
    /// 渐进放量：Pawn / Building 默认仍**串行**（改地图风险最高，属 P3 范围），
    /// 其余普通 Thing（filth/item/mote/projectile/plant…）并行。
    /// 异常不在 worker 上打日志（RimWorld 的 Log 非线程安全）——先收集，回到主线程再报。
    /// </summary>
    public static class TickListParallel
    {
        public static bool Enabled;
        public static int Workers = 4;
        public static int MinItems = 32;
        public static bool KeepPawnBuildingSerial = true;

        /// <summary>运行时开关文件：存在 ⇒ 停用并行（回到原版），便于**同一次会话内**做 A/B。</summary>
        public const string DisableFlagPath = "/tmp/ttr-p2-off";
        public const string WorkersFlagPath = "/tmp/ttr-workers";
        public const string IncludeAllFlagPath = "/tmp/ttr-p2-all";
        public static int DefaultWorkers = 14;
        public static bool DefaultKeepPawnBuildingSerial = true;
        public static bool SettingsDefault;
        private static int checkCountdown = 1;

        public static long Batches;
        public static long Items;
        public static long SerialFallbacks;
        public static double LastParallelMs;

        private static FieldInfo fTickType;
        private static FieldInfo fThingLists;
        private static FieldInfo fToRegister;
        private static FieldInfo fToDeregister;

        private static readonly List<Thing> parallelPart = new List<Thing>(2048);
        private static readonly List<Thing> serialPart = new List<Thing>(2048);
        private static readonly List<Exception> errors = new List<Exception>(8);
        private static readonly object errorGate = new object();

        public static bool Init()
        {
            try
            {
                Type t = typeof(TickList);
                fTickType = AccessTools.Field(t, "tickType");
                fThingLists = AccessTools.Field(t, "thingLists");
                fToRegister = AccessTools.Field(t, "thingsToRegister");
                fToDeregister = AccessTools.Field(t, "thingsToDeregister");
                return fTickType != null && fThingLists != null && fToRegister != null && fToDeregister != null;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] TickList 并行初始化失败: " + e.Message);
                return false;
            }
        }

        private static int IntervalOf(TickList list)
        {
            TickerType tt = (TickerType)fTickType.GetValue(list);
            switch (tt)
            {
                case TickerType.Normal: return 1;
                case TickerType.Rare: return 250;
                case TickerType.Long: return 2000;
                default: return -1;
            }
        }

        /// <summary>前缀：返回 false 表示我们自己跑完了，跳过原版。</summary>
        public static bool Tick_Prefix(TickList __instance)
        {
            if (--checkCountdown <= 0)                       // 每 ~600 tick 复查一次开关文件
            {
                checkCountdown = 600;
                bool on = SettingsDefault;
                try
                {
                    if (System.IO.File.Exists(DisableFlagPath)) on = false;
                    if (System.IO.File.Exists(IncludeAllFlagPath)) KeepPawnBuildingSerial = false;
                    else KeepPawnBuildingSerial = DefaultKeepPawnBuildingSerial;
                    int w;
                    if (System.IO.File.Exists(WorkersFlagPath)
                        && int.TryParse(System.IO.File.ReadAllText(WorkersFlagPath).Trim(), out w)
                        && w >= 1 && w <= 32)
                    {
                        Workers = w;
                    }
                    else
                    {
                        Workers = DefaultWorkers;
                    }
                }
                catch { }
                Enabled = on;
            }
            if (!Enabled || __instance == null) return true;
            if (DebugSettings.fastEcology) return true;          // 开发用分支：直接交回原版
            try
            {
                Run(__instance);
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[RimThreadedTTR] TickList 并行失败，回退原版: " + e, 552311);
                return true;                                     // 交给原版（可能重复 tick 少量东西，可接受）
            }
        }

        private static void Run(TickList list)
        {
            int interval = IntervalOf(list);
            if (interval <= 0) return;                            // 未知类型：什么都不做（原版会走 default）

            List<List<Thing>> buckets = (List<List<Thing>>)fThingLists.GetValue(list);
            List<Thing> toReg = (List<Thing>)fToRegister.GetValue(list);
            List<Thing> toDereg = (List<Thing>)fToDeregister.GetValue(list);

            // ── 注册 / 注销（与 BucketOf 等价：abs(hash) % interval）──
            for (int i = 0; i < toReg.Count; i++)
            {
                Thing t = toReg[i];
                int h = t.GetHashCode();
                if (h < 0) h = -h;
                buckets[h % interval].Add(t);
            }
            toReg.Clear();
            for (int i = 0; i < toDereg.Count; i++)
            {
                Thing t = toDereg[i];
                int h = t.GetHashCode();
                if (h < 0) h = -h;
                buckets[h % interval].Remove(t);
            }
            toDereg.Clear();

            // ── 主循环 ──
            List<Thing> bucket = buckets[Find.TickManager.TicksGame % interval];
            int n = bucket.Count;
            if (n == 0) return;

            if (!Enabled || Workers <= 1 || n < MinItems)
            {
                SerialFallbacks++;
                for (int i = 0; i < n; i++) DoTickThing(bucket[i]);
                return;
            }

            parallelPart.Clear();
            serialPart.Clear();
            for (int i = 0; i < n; i++)
            {
                Thing t = bucket[i];
                if (t.Destroyed) continue;
                if (KeepPawnBuildingSerial && (t is Pawn || t is Building)) serialPart.Add(t);
                else parallelPart.Add(t);
            }

            double t0 = Time.realtimeSinceStartup;
            if (parallelPart.Count >= MinItems)
            {
                GenThreading.ParallelFor(0, parallelPart.Count, DoTickAt, Workers);
                Batches++;
                Items += parallelPart.Count;
            }
            else
            {
                for (int i = 0; i < parallelPart.Count; i++) DoTickThing(parallelPart[i]);
            }
            for (int i = 0; i < serialPart.Count; i++) DoTickThing(serialPart[i]);
            LastParallelMs = (Time.realtimeSinceStartup - t0) * 1000.0;

            FlushErrors();                                        // 回主线程报错（Log 非线程安全）
        }

        private static void DoTickAt(int i)
        {
            DoTickThing(parallelPart[i]);
        }

        private static void DoTickThing(Thing t)
        {
            if (t == null || t.Destroyed) return;
            try
            {
                t.DoTick();
            }
            catch (Exception e)
            {
                lock (errorGate) { if (errors.Count < 16) errors.Add(e); }
            }
        }

        private static void FlushErrors()
        {
            if (errors.Count == 0) return;
            Exception first = errors[0];
            int count = errors.Count;
            errors.Clear();
            Log.ErrorOnce("[RimThreadedTTR] 并行 tick 抛出 " + count + " 个异常（首个）: " + first, 552312);
        }
    }
}
