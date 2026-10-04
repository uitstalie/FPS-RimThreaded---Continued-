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
        /// <summary>选项 A：只让**野生动物**（无派系动物）走并行；殖民者/机械/建筑仍串行。
        /// 依据：mod 极少触碰野生动物的 hediff/job 链，能把"mod 直改内部状态"的风险压到最低。</summary>
        public static bool ParallelWildAnimals;
        public static long WildTicksParallel;

        private static bool IsWildAnimal(Pawn p)
        {
            return p != null && p.RaceProps != null && p.RaceProps.Animal && p.Faction == null && !p.Dead;
        }

        /// <summary>运行时开关文件：存在 ⇒ 停用并行（回到原版），便于**同一次会话内**做 A/B。</summary>
        public const string DisableFlagPath = "/tmp/ttr-p2-off";
        public const string WorkersFlagPath = "/tmp/ttr-workers";
        public const string IncludeAllFlagPath = "/tmp/ttr-p2-all";
        public static int DefaultWorkers = 14;
        public static bool DefaultKeepPawnBuildingSerial = true;
        public static bool SettingsDefault;
        private static int checkCountdown = 1;

        /// <summary>错误过多时自动停用并行（回退原版），防止把游戏搞坏。</summary>
        public static bool DisabledByErrors;
        public static long ErrorCount;
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
        private static readonly List<string> errors = new List<string>(8);
        private static readonly HashSet<string> errorKinds = new HashSet<string>();
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
                    try { ParallelWildAnimals = System.IO.File.Exists("/tmp/ttr-p2-wild"); } catch { }
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
            if (!Enabled || DisabledByErrors || __instance == null) return true;
            // 热点 #3：MapPawns 的派系列表是惰性构建的，且带 AssertMainThread 守卫。
            // 在**主线程**上先预热好，worker 之后只做读 ⇒ 不再触发断言/并发构建。
            try
            {
                var maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    if (maps[i] != null && maps[i].mapPawns != null) EnsureFactions(maps[i].mapPawns);
                }
            }
            catch { }
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
                if (KeepPawnBuildingSerial && (t is Building || (t is Pawn && !(ParallelWildAnimals && IsWildAnimal((Pawn)t)))))
                {
                    serialPart.Add(t);
                }
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

        /// <summary>天花板实验：跳过 Pawn/Building 的 tick（仅测量 TPS 上限，会破坏游戏状态）。</summary>
        public static bool SkipPawnBuilding;
        public static long SkippedPawnBuilding;

        private static bool ShouldSkip(Thing t)
        {
            if (!SkipPawnBuilding) return false;
            if (t is Pawn || t is Building)
            {
                SkippedPawnBuilding++;
                return true;
            }
            return false;
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
                lock (errorGate)
                {
                    ErrorCount++;
                    if (errors.Count < 5)
                    {
                        // 保留**带堆栈**的多行信息 ⇒ 直接告诉我们是哪一行并发访问了哪个集合
                        string s = e.GetType().Name + ": " + e.Message + "\n" + (e.StackTrace ?? "(no stack)");
                        string head = e.GetType().Name + "|" + (e.StackTrace ?? "").Split('\n')[0];
                        if (errorKinds.Add(head)) errors.Add(s);
                    }
                }
            }
        }

        // 反射缓存：MapPawns.EnsureFactionsListsInit 非 public
        private static Action<MapPawns> ensureFactions;

        private static void EnsureFactions(MapPawns mp)
        {
            try
            {
                if (ensureFactions == null)
                {
                    System.Reflection.MethodInfo mi = AccessTools.Method(typeof(MapPawns), "EnsureFactionsListsInit", Type.EmptyTypes);
                    if (mi == null) return;
                    ensureFactions = (Action<MapPawns>)Delegate.CreateDelegate(typeof(Action<MapPawns>), mi);
                }
                ensureFactions(mp);
            }
            catch { }
        }

        private static void FlushErrors()
        {
            if (errors.Count > 0)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[RimThreadedTTR] 并行 tick 异常样本（累计 ").Append(ErrorCount).Append(" 次，")
                  .Append(errors.Count).Append(" 种）:");
                for (int i = 0; i < errors.Count; i++)
                {
                    sb.Append("\n--- 样本 ").Append(i + 1).Append(" ---\n").Append(errors[i]);
                }
                errors.Clear();
                Log.Error(sb.ToString());     // 带堆栈 ⇒ 直接定位并发访问点
            }
            if (ErrorCount > 20 && !DisabledByErrors)          // 保险：反复出错 ⇒ 自动停用
            {
                DisabledByErrors = true;
                Log.Error("[RimThreadedTTR] 并行 tick 累计出错 " + ErrorCount + " 次，已自动停用并回退原版（游戏继续，但恢复串行）。");
            }
        }
    }
}
