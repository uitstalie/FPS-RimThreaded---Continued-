using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Verse;

namespace FastLoad
{
    /// <summary>
    /// 运行时（游戏内）主线程归因：把主线程 tick 链上各段的**累加耗时**按
    /// 类型 / mod / 阶段统计出来，写进 FastLoad 报告。
    ///
    /// 为什么这样做：实测主线程占用约 **1.0 核（75% 的进程 CPU）**，其余线程合计仅 0.33 核，
    /// 且 CPU 已跑在 P 核满频上 ⇒ 想更快只能找出"主线程每 tick 在算什么"。
    /// Dubs Performance Analyzer 不落文件（只有游戏内窗口），所以这里用与加载期相同的办法：
    /// Harmony 前后缀累加 + **exclusive 嵌套扣除**，得到可复现的绝对值，而不是瞬时快照。
    ///
    /// 归因口径：
    /// * 阶段（PhaseKey）：`TickManager.TickManagerUpdate`、`DoSingleTick` 等总账单；
    /// * 类型 + mod：对 `MapComponent`/`GameComponent`/`WorldComponent`/`JobDriver` 等
    ///   以 `this` 的**具体类型**归因，再用现成的"程序集→packageId"映射变成 mod 名。
    ///
    /// 开销：挂钩的都是每 tick/每帧高频方法，**只建议在分析时开启**（设置里有开关）。
    /// </summary>
    public static class RuntimeProfiler
    {
        private sealed class Frame
        {
            public double Start;
            public double Child;
            public string TypeKey;
            public string ModKey;
            public string PhaseKey;
        }

        [ThreadStatic] private static List<Frame> Frames;
        [ThreadStatic] private static List<Frame> FramePool;

        /// <summary>一个线程的累加器（调用路径只写自己的，不需要锁）。</summary>
        private sealed class Accumulator
        {
            public readonly Dictionary<string, double> TypeMs = new Dictionary<string, double>();
            public readonly Dictionary<string, double> ModMs = new Dictionary<string, double>();
            public readonly Dictionary<string, double> PhaseMs = new Dictionary<string, double>();
            public readonly Dictionary<string, long> TypeCount = new Dictionary<string, long>();
            public readonly Dictionary<string, double> PhaseInclusiveMs = new Dictionary<string, double>();
        }

        [ThreadStatic] private static Accumulator Local;
        private static readonly List<Accumulator> All = new List<Accumulator>();
        private static readonly object Gate = new object();

        /// <summary>
        /// A1 修复：归因**只统计主线程**（默认开）。
        ///
        /// 背景：本类的钩子默认挂在 P2 并行集内的方法上（`Mote.Tick` / `Projectile.Tick` /
        /// `TickList.Tick` 等，见 FastLoadMod.InstallRuntimeHooks），worker 线程也会进出这些前缀/
        /// 终结器。虽然累加器本身是 <see cref="ThreadStaticAttribute"/>（每线程一份），跨线程仍有
        /// 三处**无同步共享**：
        ///   1. `AppendReport` 在主线程枚举**其它线程正在写**的 Dictionary
        ///      ⇒ `Collection was modified` / 撕裂读；
        ///   2. `_ticks` / `_frames` / `_windowTicks` / `_windowStart` 是无锁 `++`（跨线程丢写）；
        ///   3. `Diag()` 可能从 worker 调 `Profiler.Mark`（共享 List 无锁 `Add` + 非主线程 `Log.Message`）。
        ///
        /// 修法选 (b)「非主线程不记录」而不是 (a)「累加处加锁」，理由：
        ///   * (b) 是**结构性**消除：非主线程根本不产生任何共享写，连 `AppendReport` 的跨线程枚举
        ///     问题一并消失，不需要在 `AppendReport` 里长时间持锁；
        ///   * (b) 在热路径上只是一次静态 bool 读，**零锁开销**（本类每秒被调数百万次，
        ///     加锁会直接抵消掉它要测量的性能）；
        ///   * 与本类既定口径一致 —— 类注释第一行就是「运行时（游戏内）**主线程**归因」，
        ///     worker 线程的样本本来就不该混进来（会污染"主线程每 tick 在算什么"的结论）。
        /// 代价：拿不到 worker 线程内部耗时。P2 并行部分本来就由 `TickListParallel` 单独计时。
        /// </summary>
        public static bool MainThreadOnly = true;

        /// <summary>A1 实测证据：被主线程闸门丢掉的非主线程样本数（>0 就说明 P2 worker 真的在进出这些钩子）。</summary>
        public static long OffMainThreadSamples;

        /// <summary>
        /// A1 自检：起一个后台线程，把全部记录入口各走一遍（Enter/Exit、SampledEnter/SampledExit、
        /// CountTick/CountFrame、Diag，共 7 次调用），然后核对：
        /// （a）这些调用**全部**被闸门拦下（OffMainThreadSamples 恰好增加 7）；
        /// （b）没有新建任何累加器（`All` 数量不变）⇒ 即"并行模式下不再有并发写同一字典"。
        /// 返回一行可直接打进日志的结论。
        /// </summary>
        public static string SelfTestMainThreadGate()
        {
            long skipsBefore = OffMainThreadSamples;
            int accBefore;
            lock (Gate) { accBefore = All.Count; }
            Exception error = null;
            System.Threading.Thread worker = new System.Threading.Thread(delegate ()
            {
                try
                {
                    Enter(null, "__A1_selftest__");   // 1
                    Exit();                            // 2
                    SampledEnter(null);                // 3
                    SampledExit();                     // 4
                    CountTick();                       // 5
                    CountFrame();                      // 6
                    Diag("__A1_selftest__");           // 7
                }
                catch (Exception e)
                {
                    error = e;
                }
            });
            worker.IsBackground = true;
            worker.Start();
            worker.Join(2000);
            long skips = OffMainThreadSamples - skipsBefore;
            int accAfter;
            lock (Gate) { accAfter = All.Count; }
            return "A1 自检（后台线程走全部 7 个记录入口）：被闸门拦下 " + skips + "/7 · 新增累加器 "
                + (accAfter - accBefore) + " 个（应为 0）· 异常=" + (error == null ? "无" : error.GetType().Name);
        }

        /// <summary>本线程是否参与归因（非主线程一律不记录）。</summary>
        public static bool IsRecording
        {
            get
            {
                if (!MainThreadOnly) return true;
                try { return UnityData.IsInMainThread; }
                catch { return true; }   // 判定失败时宁可按"主线程"处理（单线程加载期）
            }
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> LabelCache =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, string>();
        private static long _ticks;
        private static long _frames;

        public static long Ticks;
        public static long FramesSeen;
        /// <summary>采样率：热钩子每 N 次调用只记录 1 次（时间按 N 放大，近似真实总量）。</summary>
        public static int SampleRate = 100;
        private static long _sampleCounter;
        public static long SampledTotal;
        public static long StackOverflowResets;

        [ThreadStatic] private static List<bool> SampleFlags;

        /// <summary>
        /// 热钩子的采样前缀。**必须嵌套安全**：`ThingWithComps.Tick` 里会嵌套 `CompTick`，
        /// 用单个 bool 标志会让内层把外层的标记清掉 ⇒ 外层永不弹栈（帧泄漏、归因错位）。
        /// 这里用线程局部的 bool 栈，进入/退出严格配对。
        /// </summary>
        public static void SampledEnter(object instance)
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1：非主线程不记录（也不压栈，保持配对）
            if (SampleFlags == null) SampleFlags = new List<bool>();
            bool take = SampleRate <= 1 || (Interlocked.Increment(ref _sampleCounter) % SampleRate) == 0;
            SampleFlags.Add(take);
            if (!take) return;
            SampledTotal++;
            Enter(instance, null);
        }

        public static void SampledExit()
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1
            if (SampleFlags == null || SampleFlags.Count == 0) return;
            int last = SampleFlags.Count - 1;
            bool take = SampleFlags[last];
            SampleFlags.RemoveAt(last);
            if (take) ExitScaled(SampleRate);
        }
        private static double _windowStart;
        private static long _windowTicks;
        public static double CurrentTps;
        public static double PeakTps;

        /// <summary>进入一个被计时的区间（instance 可为 null，此时只统计 PhaseKey）。</summary>
        public static void Enter(object instance, string phaseKey)
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1
            try
            {
                string typeKey = null;
                string modKey = null;
                if (instance != null)
                {
                    Type type = instance.GetType();
                    typeKey = type.FullName;
                    if (!LabelCache.TryGetValue(type, out modKey))
                    {
                        modKey = StaticCtorTiming.LabelFor(type);
                        LabelCache[type] = modKey;
                    }
                }
                if (Frames == null) Frames = new List<Frame>();
                if (Frames.Count > 512)
                {
                    // 安全阀：不该发生（说明有前后缀没配对），清空以免内存增长
                    Frames.Clear();
                    StackOverflowResets++;
                }
                if (FramePool == null) FramePool = new List<Frame>();
                Frame frame;
                int last = FramePool.Count - 1;
                if (last >= 0)
                {
                    frame = FramePool[last];
                    FramePool.RemoveAt(last);
                }
                else
                {
                    frame = new Frame();
                }
                frame.Start = Profiler.Now;
                frame.Child = 0.0;
                frame.TypeKey = typeKey;
                frame.ModKey = modKey;
                frame.PhaseKey = phaseKey;
                Frames.Add(frame);
            }
            catch
            {
                // 计时失败不影响游戏
            }
        }

        public static void Exit() { ExitScaled(1); }

        /// <summary>离开区间：计算 exclusive 时间并累加（scale&gt;1 时按采样率放大）。</summary>
        public static void ExitScaled(int scale)
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1
            try
            {
                if (Frames == null || Frames.Count == 0) return;
                Frame frame = Frames[Frames.Count - 1];
                Frames.RemoveAt(Frames.Count - 1);
                double elapsed = (Profiler.Now - frame.Start) * 1000.0;
                double exclusive = elapsed - frame.Child;
                if (exclusive < 0) exclusive = 0;
                if (scale > 1) exclusive *= scale;   // 采样：按 1/N 放大成近似总量
                if (Frames.Count > 0) Frames[Frames.Count - 1].Child += elapsed;

                if (Local == null)
                {
                    Local = new Accumulator();
                    lock (Gate) { All.Add(Local); }
                }
                if (frame.PhaseKey != null)
                {
                    Add(Local.PhaseMs, frame.PhaseKey, exclusive);
                    Add(Local.PhaseInclusiveMs, frame.PhaseKey, elapsed * (scale > 1 ? scale : 1));
                }
                if (frame.TypeKey != null)
                {
                    Add(Local.TypeMs, frame.TypeKey, exclusive);
                    long count;
                    Local.TypeCount.TryGetValue(frame.TypeKey, out count);
                    Local.TypeCount[frame.TypeKey] = count + 1;
                }
                if (frame.ModKey != null) Add(Local.ModMs, frame.ModKey, exclusive);
                if (FramePool == null) FramePool = new List<Frame>();
                frame.TypeKey = null;
                frame.ModKey = null;
                frame.PhaseKey = null;
                FramePool.Add(frame);
            }
            catch
            {
                // 同上
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> DiagSeen =
            new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>
        /// 诊断：首次触发时打一条日志。
        /// **注意**：旧实现每次调用都要 `Profiler.Count`（拿锁）——在 `Pawn.Tick` 这类热路径上
        /// 光锁开销就能把游戏拖卡，现改为无锁一次性（TryAdd）。
        /// </summary>
        public static void Diag(string label)
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1：worker 线程不得调 Profiler.Mark
            if (DiagSeen.ContainsKey(label)) return;      // 快速路径：绝大多数调用只做一次字典查询
            try
            {
                if (DiagSeen.TryAdd(label, true))
                {
                    Profiler.Mark("运行时钩子首次触发: " + label);
                }
            }
            catch
            {
                // 诊断不得影响游戏
            }
        }

        /// <summary>计数 tick，并顺带算出**实测 TPS**（每秒一个窗口；用于对着 720/900 目标调优）。</summary>
        public static void CountTick()
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1：标量计数也无锁
            _ticks++;
            _windowTicks++;
            double now = Profiler.Now;
            if (_windowStart <= 0.0) { _windowStart = now; return; }
            double elapsed = now - _windowStart;
            if (elapsed < 1.0) return;
            double tps = _windowTicks / elapsed;
            CurrentTps = tps;
            if (elapsed <= 2.0 && tps > PeakTps) PeakTps = tps;   // 排除加载/卡顿造成的异常窗口
            _windowTicks = 0;
            _windowStart = now;
        }

        public static void CountFrame()
        {
            if (!IsRecording) { OffMainThreadSamples++; return; }   // A1：标量计数也无锁
            _frames++;
        }

        private static void Add(Dictionary<string, double> map, string key, double value)
        {
            double current;
            map.TryGetValue(key, out current);
            map[key] = current + value;
        }

        /// <summary>把运行时归因写进报告。</summary>
        public static void AppendReport(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- 运行时归因（主线程，累加值；仅供参考，挂钩本身有开销）---");

            var typeTotals = new Dictionary<string, double>();
            var modTotals = new Dictionary<string, double>();
            var phaseTotals = new Dictionary<string, double>();
            var phaseInclusive = new Dictionary<string, double>();
            var countTotals = new Dictionary<string, long>();
            List<Accumulator> snapshot;
            // A1：记录端已限定主线程；这里再持锁聚合一次，杜绝"读时被写"（开销可忽略，非热路径）
            lock (Gate)
            {
                snapshot = new List<Accumulator>(All);
                foreach (Accumulator acc in snapshot)
                {
                    foreach (var kv in acc.TypeMs) Add(typeTotals, kv.Key, kv.Value);
                    foreach (var kv in acc.ModMs) Add(modTotals, kv.Key, kv.Value);
                    foreach (var kv in acc.PhaseMs) Add(phaseTotals, kv.Key, kv.Value);
                    foreach (var kv in acc.PhaseInclusiveMs) Add(phaseInclusive, kv.Key, kv.Value);
                    foreach (var kv in acc.TypeCount)
                    {
                        long c;
                        countTotals.TryGetValue(kv.Key, out c);
                        countTotals[kv.Key] = c + kv.Value;
                    }
                }
            }
            var phases = new List<KeyValuePair<string, double>>(phaseTotals);
            var byType = new List<KeyValuePair<string, double>>(typeTotals);
            var byMod = new List<KeyValuePair<string, double>>(modTotals);
            long ticks = _ticks;
            long frames = _frames;
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  观测: {0} tick / {1} 帧 · 实测 TPS: 当前 {2:F0} / 峰值 {3:F0} · 热路径采样 1/{4}（已采样 {5} 次）",
                ticks, frames, CurrentTps, PeakTps, SampleRate, SampledTotal));

            phases.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  -- 总账单（含子项 = 该项真实总耗时；独占 = 扣掉已测子项）--");
            int shown = 0;
            foreach (var kv in phases)
            {
                if (shown++ >= 16) break;
                double incl;
                phaseInclusive.TryGetValue(kv.Key, out incl);
                double perTick = ticks > 0 ? incl / ticks : 0.0;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-44} 含子项 {1,9:F0} ms · 独占 {2,8:F0} ms · {3:F3} ms/tick",
                    Trim(kv.Key, 44), incl, kv.Value, perTick));
            }
            if (phases.Count == 0) sb.AppendLine("  （无：运行时归因可能未开启）");

            byMod.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (ParallelTicker.Enabled)
            {
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  并行 tick: 批次 {0} · 对象 {1} · 线程 {2}", ParallelTicker.Runs, ParallelTicker.Items, ParallelTicker.Degree));
            }
            sb.AppendLine("  -- 按 mod（前 15）--");
            shown = 0;
            foreach (var kv in byMod)
            {
                if (kv.Value < 1.0 || shown++ >= 15) continue;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-46} {1,11:F0} ms", Trim(kv.Key, 46), kv.Value));
            }
            if (shown == 0) sb.AppendLine("  （无）");

            byType.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  -- 按类型（前 20）--");
            shown = 0;
            foreach (var kv in byType)
            {
                if (kv.Value < 1.0 || shown++ >= 20) continue;
                long count;
                countTotals.TryGetValue(kv.Key, out count);
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-46} {1,11:F0} ms  ({2} 次)", Trim(kv.Key, 46), kv.Value, count));
            }
            if (shown == 0) sb.AppendLine("  （无）");
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
