using System;
using System.Diagnostics;
using System.Threading;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 轻量 fork-join 派发器。
    ///
    /// 动机（实测）：`GenThreading.ParallelFor` 单次派发+汇合开销约 **90~130 µs/tick**
    /// （FastLoad 小批次 +7.6%、TTR Pawn 并行 +7.6% 两次验证），
    /// 而我们要并行的安全子系统集合只有 ~150 µs/tick ⇒ 不换派发器就是负收益。
    ///
    /// 做法：N 个**常驻** worker（后台线程）+ 自旋等待（spin-then-park）+ 预分配切片，
    /// **每 tick 零分配**、不使用 Task/ThreadPool；主线程自己也算一片。
    /// </summary>
    public static class LiteParallel
    {
        public static int Workers { get; private set; }
        private static Thread[] threads;
        private static int started;
        private static volatile bool stop;

        private static int generation;
        private static int remaining;
        private static Action<int> action;
        private static int from;
        private static int to;
        private static int chunk;

        private static readonly ManualResetEventSlim wake = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim done = new ManualResetEventSlim(false);

        public static bool Running { get { return started > 0 && !stop; } }

        public static void Start(int workers)
        {
            if (workers < 1) workers = 1;
            if (started == workers && !stop) return;
            Stop();
            stop = false;
            Workers = workers;
            started = workers;
            threads = new Thread[workers];
            for (int i = 0; i < workers; i++)
            {
                Thread t = new Thread(WorkerLoop);
                t.IsBackground = true;                 // 不阻止游戏退出
                t.Name = "TTR-Lite-" + i;
                threads[i] = t;
                t.Start(i);
            }
        }

        public static void Stop()
        {
            stop = true;
            wake.Set();
            threads = null;
            started = 0;
        }

        private static void WorkerLoop(object state)
        {
            int id = (int)state + 1;                   // 0 号片留给主线程
            int seen = 0;
            SpinWait spin = new SpinWait();
            while (!stop)
            {
                if (generation == seen)
                {
                    // 先自旋一小会儿（覆盖"很快就有下一个 tick"的常见情况），再阻塞等待，避免空转烧 CPU
                    for (int i = 0; i < 400 && generation == seen && !stop; i++) spin.SpinOnce();
                    if (generation == seen && !stop) wake.Wait(4);
                    continue;
                }
                seen = generation;
                RunSlice(id);
                if (Interlocked.Decrement(ref remaining) == 0) done.Set();
            }
        }

        /// <summary>把 [from, to) 均分给 Workers+1 片并行执行；工作量小时自动串行。</summary>
        public static void For(int fromInclusive, int toExclusive, Action<int> body)
        {
            int n = toExclusive - fromInclusive;
            if (n <= 0 || body == null) return;
            int w = Workers;
            if (w <= 1 || !Running || n < 2 * (w + 1))
            {
                for (int i = fromInclusive; i < toExclusive; i++) body(i);
                return;
            }

            from = fromInclusive;
            to = toExclusive;
            action = body;
            int parts = w + 1;
            chunk = (n + parts - 1) / parts;
            remaining = w;
            done.Reset();
            Interlocked.Increment(ref generation);
            wake.Set();

            RunSlice(0);                                // 主线程也干活

            SpinWait spin = new SpinWait();
            while (remaining > 0)
            {
                spin.SpinOnce();
                if (spin.NextSpinWillYield) done.Wait(1);
            }
            wake.Reset();
            action = null;
        }

        private static void RunSlice(int id)
        {
            Action<int> a = action;
            if (a == null) return;
            int start = from + id * chunk;
            int end = start + chunk;
            if (end > to) end = to;
            for (int i = start; i < end; i++) a(i);
        }

        /// <summary>启动自测：对比轻量派发器与 GenThreading.ParallelFor 的每次派发开销。</summary>
        public static string Benchmark(int iters)
        {
            if (iters < 100) iters = 100;
            Action<int> noop = delegate(int i) { };
            try
            {
                for (int i = 0; i < 200; i++) For(0, 512, noop);          // 预热
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < iters; i++) For(0, 512, noop);
                double lite = Us(t0, iters);

                for (int i = 0; i < 200; i++) GenThreading.ParallelFor(0, 512, noop, Workers);
                long t1 = Stopwatch.GetTimestamp();
                for (int i = 0; i < iters; i++) GenThreading.ParallelFor(0, 512, noop, Workers);
                double gen = Us(t1, iters);

                return string.Format("轻量派发 {0:F1} µs/次  vs  GenThreading {1:F1} µs/次（512 项空任务·{2} 线程）",
                    lite, gen, Workers);
            }
            catch (Exception e)
            {
                return "基准失败: " + e.Message;
            }
        }

        private static double Us(long t0, int iters)
        {
            return (Stopwatch.GetTimestamp() - t0) * 1000000.0 / Stopwatch.Frequency / iters;
        }
    }
}
