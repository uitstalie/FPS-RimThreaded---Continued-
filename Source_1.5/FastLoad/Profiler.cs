using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FastLoad
{
    /// <summary>
    /// 启动计时。时钟用 Unity 的 <see cref="Time.realtimeSinceStartup"/>（进程启动即有效），
    /// 因此**即使 Harmony 挂钩全部失败**也能给出"绝对时间标记"时间线。
    /// 报告固定写到配置目录的 FastLoad-Startup.txt（每次调用都覆盖，始终是最新一次）。
    /// </summary>
    public static class Profiler
    {
        private static readonly List<KeyValuePair<string, float>> Marks = new List<KeyValuePair<string, float>>();
        private static readonly Dictionary<string, float> PhaseStart = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> Phases = new Dictionary<string, float>();
        private static readonly Dictionary<string, float[]> PerMod = new Dictionary<string, float[]>();
        private static readonly Dictionary<string, float> ModStart = new Dictionary<string, float>();
        private static int writes;

        // ---- 补丁应用按 mod / 按操作类型归因（exclusive 计时，处理嵌套） ----
        private sealed class PatchFrame
        {
            public double Start;
            public double Child;
            public string Mod;
            public string Type;
        }

        [ThreadStatic] private static List<PatchFrame> Frames;
        private static readonly Dictionary<string, double> PatchModMs = new Dictionary<string, double>();
        private static readonly Dictionary<string, long> PatchModCount = new Dictionary<string, long>();
        private static readonly Dictionary<string, double> PatchTypeMs = new Dictionary<string, double>();
        private static readonly object PatchLock = new object();

        public static float Now { get { return Time.realtimeSinceStartup; } }

        public static void Mark(string label)
        {
            float t = Now;
            Marks.Add(new KeyValuePair<string, float>(label, t));
            Log.Message(string.Format(CultureInfo.InvariantCulture, "[RimThreadedTTR] FastLoad: +{0:F2}s  {1}", t, label));
        }

        public static void PhaseBegin(string name) { PhaseStart[name] = Now; }

        public static void PhaseEnd(string name)
        {
            float start;
            if (PhaseStart.TryGetValue(name, out start))
            {
                float delta = Now - start;
                float cur;
                Phases.TryGetValue(name, out cur);
                Phases[name] = cur + delta;
                // 同时打进日志：RimSort 侧的时间线可据此对齐阶段边界
                Mark(string.Format(CultureInfo.InvariantCulture,
                    "{0} 结束（本次 {1:F0} ms）", name, delta * 1000f));
            }
        }

        private static float[] Bucket(string mod)
        {
            float[] arr;
            if (!PerMod.TryGetValue(mod, out arr)) { arr = new float[3]; PerMod[mod] = arr; }
            return arr;
        }

        public static void ModBegin(string mod, int slot)
        {
            if (!string.IsNullOrEmpty(mod)) ModStart[mod + "#" + slot] = Now;
        }

        public static void ModEnd(string mod, int slot)
        {
            if (string.IsNullOrEmpty(mod)) return;
            float start;
            if (ModStart.TryGetValue(mod + "#" + slot, out start))
            {
                Bucket(mod)[slot] += Now - start;
            }
        }

        public static void PatchBegin(PatchOperation op)
        {
            if (op == null) return;
            if (Frames == null) Frames = new List<PatchFrame>();
            Frames.Add(new PatchFrame { Start = Now, Child = 0.0, Mod = ModKeyFromPath(op.sourceFile), Type = op.GetType().Name });
        }

        public static void PatchEnd(PatchOperation op)
        {
            if (op == null || Frames == null || Frames.Count == 0) return;
            PatchFrame frame = Frames[Frames.Count - 1];
            Frames.RemoveAt(Frames.Count - 1);
            double elapsed = (Now - frame.Start) * 1000.0;   // ms
            double exclusive = elapsed - frame.Child;
            if (exclusive < 0) exclusive = 0;
            if (Frames.Count > 0) Frames[Frames.Count - 1].Child += elapsed;
            lock (PatchLock)
            {
                double cur;
                PatchModMs.TryGetValue(frame.Mod, out cur);
                PatchModMs[frame.Mod] = cur + exclusive;
                long cnt;
                PatchModCount.TryGetValue(frame.Mod, out cnt);
                PatchModCount[frame.Mod] = cnt + 1;
                double curT;
                PatchTypeMs.TryGetValue(frame.Type, out curT);
                PatchTypeMs[frame.Type] = curT + exclusive;
            }
        }

        /// <summary>从补丁文件路径推出 mod 名（workshop id 目录名或 Mods 下的一级目录）。</summary>
        private static string ModKeyFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(未知来源)";
            int idx = path.IndexOf("/Mods/", StringComparison.Ordinal);
            if (idx < 0) idx = path.IndexOf("\\Mods\\", StringComparison.Ordinal);
            int skip = idx >= 0 ? idx + 6 : -1;
            if (idx < 0)
            {
                idx = path.IndexOf("/294100/", StringComparison.Ordinal);
                skip = idx >= 0 ? idx + 8 : -1;
            }
            if (skip < 0) return System.IO.Path.GetFileName(path);
            int end = path.IndexOf('/', skip);
            return end > skip ? path.Substring(skip, end - skip) : path.Substring(skip);
        }

        /// <summary>写出报告（可重复调用，覆盖同一个文件）。</summary>
        private static int _writePending;

        /// <summary>
        /// 写报告。**格式化在主线程、文件 I/O 丢后台线程**：
        /// 之前是同步 File.WriteAllText，400+ KB 文本 + btrfs zstd 压缩直接卡主线程。
        /// 用 Interlocked 保证同一时刻只有一次写盘在飞；主线程只负责打一条日志。
        /// </summary>
        public static void Finish(string reason)
        {
            if (Interlocked.CompareExchange(ref _writePending, 1, 0) != 0) return;   // 上一次还没写完
            string text;
            string path;
            try
            {
                text = BuildReport(reason);
                path = Path.Combine(GenFilePaths.ConfigFolderPath, "FastLoad-Startup.txt");
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref _writePending, 0);
                Log.Error("[RimThreadedTTR] FastLoad: 生成报告失败: " + e);
                return;
            }
            try
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        File.WriteAllText(path, text, Encoding.UTF8);   // 后台线程写盘
                        Interlocked.Increment(ref WritesDone);
                    }
                    catch
                    {
                        // 写盘失败不影响游戏
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _writePending, 0);
                    }
                });
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref _writePending, 0);
                Log.Error("[RimThreadedTTR] FastLoad: 排队写报告失败: " + e);
                return;
            }
            // 这行在主线程打（RimWorld 的 Log 不适合后台线程调用），也充当时间线里的周期标记
            Log.Message("[RimThreadedTTR] FastLoad: 报告已写出（" + reason + "）→ " + path);
        }

        public static long WritesDone;

        private static string BuildReport(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== FastLoad 启动计时报告 ===");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "触发原因: {0}    当前 realtimeSinceStartup: {1:F2}s    第 {2} 次写出", reason, Now, ++writes));
            sb.AppendLine("活动 mod 数: " + (ModsConfig.ActiveModsInLoadOrder != null ? ModsConfig.ActiveModsInLoadOrder.Count().ToString() : "?"));
            sb.AppendLine();

            sb.AppendLine("--- 时间标记（绝对秒，从进程/引擎启动算起）---");
            foreach (var m in Marks.OrderBy(m => m.Value))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  +{0,7:F2}s  {1}", m.Value, m.Key));
            }
            if (Marks.Count == 0) sb.AppendLine("  （无：连静态构造都没跑到？）");

            sb.AppendLine();
            sb.AppendLine("--- 阶段（由 Harmony 挂钩累计；挂钩失败则缺项）---");
            if (Phases.Count == 0) sb.AppendLine("  （无：挂钩未生效）");
            foreach (var kv in Phases.OrderByDescending(k => k.Value))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-34} {1,9:F0} ms", kv.Key, kv.Value * 1000));
            }

            sb.AppendLine();
            sb.AppendLine("--- 每个 mod（按总耗时降序；挂钩失败则整表为空）---");
            sb.AppendLine(string.Format("  {0,-46} {1,9} {2,9} {3,9} {4,9}", "mod", "defs", "patches", "content", "合计"));
            var ordered = PerMod
                .Select(kv => new { Name = kv.Key, Arr = kv.Value, Sum = kv.Value[0] + kv.Value[1] + kv.Value[2] })
                .Where(x => x.Sum > 0f)
                .OrderByDescending(x => x.Sum)
                .ToList();
            foreach (var x in ordered)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-46} {1,7:F0}ms {2,7:F0}ms {3,7:F0}ms {4,7:F0}ms",
                    Trim(x.Name, 46), x.Arr[0] * 1000, x.Arr[1] * 1000, x.Arr[2] * 1000, x.Sum * 1000));
            }
            if (ordered.Count == 0) sb.AppendLine("  （空）");
            else sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-46} {1,29:F0} ms", "合计", ordered.Sum(x => x.Sum) * 1000));

            sb.AppendLine();
            sb.AppendLine("--- 补丁应用：按 mod 归因（exclusive，已扣除嵌套）---");
            List<KeyValuePair<string, double>> patchByMod;
            lock (PatchLock)
            {
                patchByMod = new List<KeyValuePair<string, double>>(PatchModMs);
            }
            patchByMod.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in patchByMod)
            {
                if (kv.Value < 1.0) continue;
                long count;
                lock (PatchLock) { PatchModCount.TryGetValue(kv.Key, out count); }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-46} {1,10:F0} ms  ({2} 次操作)", Trim(kv.Key, 46), kv.Value, count));
            }
            if (patchByMod.Count == 0) sb.AppendLine("  （无：Apply 未被调用或归因未生效）");

            sb.AppendLine();
            sb.AppendLine("--- 补丁应用：按操作类型 ---");
            List<KeyValuePair<string, double>> patchByType;
            lock (PatchLock)
            {
                patchByType = new List<KeyValuePair<string, double>>(PatchTypeMs);
            }
            patchByType.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in patchByType)
            {
                if (kv.Value < 1.0) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-46} {1,10:F0} ms", Trim(kv.Key, 46), kv.Value));
            }
            if (patchByType.Count == 0) sb.AppendLine("  （无）");

            sb.AppendLine();
            sb.AppendLine("--- 计数器（次数 / 合计毫秒 / 合计 MB）---");
            List<KeyValuePair<string, long[]>> counters;
            lock (CounterLock)
            {
                counters = new List<KeyValuePair<string, long[]>>(Counters);
            }
            counters.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));
            int shownCounter = 0;
            foreach (var kv in counters)
            {
                if (shownCounter++ >= 25) break;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-46} {1,8} 次 {2,10} ms {3,8:F1} MB",
                    Trim(kv.Key, 46), kv.Value[0], kv.Value[1], kv.Value[2] / 1048576.0));
            }
            if (counters.Count == 0) sb.AppendLine("  （无）");

            StaticCtorTiming.AppendReport(sb);
            RuntimeProfiler.AppendReport(sb);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  A1 主线程闸门：丢弃的非主线程样本 = {0}（此前这些会并发写归因 Dictionary）",
                RuntimeProfiler.OffMainThreadSamples));
            sb.AppendLine("  A1 自检结论：" + RuntimeProfiler.SelfTestMainThreadGate());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  P2 TickList 并行：批次 {0} · 对象 {1} · worker {2} · 串行回退 {3} · 错误 {4} · 因错误停用={5}",
                RimThreadedTTR.TickListParallel.Batches, RimThreadedTTR.TickListParallel.Items,
                RimThreadedTTR.TickListParallel.Workers, RimThreadedTTR.TickListParallel.SerialFallbacks,
                RimThreadedTTR.TickListParallel.ErrorCount, RimThreadedTTR.TickListParallel.DisabledByErrors));

            // ── B 组（从原版 IL 独立实现）的实测计数器：证明"真的被调用了"而不只是"挂上了" ──
            sb.AppendLine();
            sb.AppendLine("--- B 组（从原版 IL 独立实现，A4 报告 §7.1 的三个借鉴点）---");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  B1 每实例缓冲：ListerBuildings.ofDef {0} 次 · ofGroup {1} 次 · ImmunityHandler.NeededImmunitiesNow {2} 次",
                RimThreadedTTR.StaticBufferPatches.OfDefQueries,
                RimThreadedTTR.StaticBufferPatches.OfGroupQueries,
                RimThreadedTTR.StaticBufferPatches.ImmunityQueries));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  B2 List<T>._version：可用={0} · 储物记忆命中 {1} 次 · 因版本变化而作废重算 {2} 次",
                RimThreadedTTR.ListVersion<int>.Available,
                FPSPlus.HaulWealthPatches.HaulLookupsSkipped,
                FPSPlus.HaulWealthPatches.HaulLookupsInvalidatedByVersion));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  B3 ListerThings.Remove 尾部删除：{0} 次（3 处调用点已改写）",
                RimThreadedTTR.ListerThingsTailPatches.TailRemoves));

            sb.AppendLine();
            sb.AppendLine("--- XPath 快速路径 ---");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  索引命中 {0} 次 · 回退 {1} 次 · 表达式缓存命中 {2} · 索引重建 {3}",
                XPathIndex.FastHits, XPathIndex.Fallbacks, XPathIndex.ExprCacheHits, XPathIndex.IndexBuilds));

            return sb.ToString();
        }

        // ---- 逐贴图/音频/Shader 文件（ModContentLoader<T>.LoadItem）----
        [ThreadStatic] private static float _fileStart;
        [ThreadStatic] private static string _fileKey;
        [ThreadStatic] private static long _fileBytes;
        [ThreadStatic] private static string _filePrefix = "文件读取 ";

        /// <summary>设置本次文件读取的统计前缀（不同钩子来源用不同前缀）。</summary>
        public static void FileCounterPrefix(string prefix)
        {
            _filePrefix = string.IsNullOrEmpty(prefix) ? "文件读取 " : prefix;
        }

        public static void TextureFileBegin(VirtualFile file)
        {
            if (file == null)
            {
                TextureFileBeginPath(null, 0);
                return;
            }
            long length = 0;
            string path = null;
            try
            {
                length = file.Length;
                path = file.FullPath;
            }
            catch
            {
                length = 0;
            }
            TextureFileBeginPath(path, length);
        }

        /// <summary>按路径记录一次文件读取（mmap 等非 VirtualFile 入口用）。</summary>
        public static void TextureFileBeginPath(string path, long bytes)
        {
            _fileStart = Now;
            _fileBytes = bytes;
            string ext = "(未知)";
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    ext = Path.GetExtension(path).ToLowerInvariant();
                }
                catch
                {
                    ext = "(未知)";
                }
            }
            _fileKey = _filePrefix + ext;
        }

        public static void TextureFileEnd()
        {
            if (_fileKey == null) return;
            Count(_fileKey, (Now - _fileStart) * 1000.0, _fileBytes);
            _fileKey = null;
            _fileBytes = 0;
        }

        // ---- 通用计数器（逐贴图/逐 def/缓存命中率等）----
        public static readonly Dictionary<string, long[]> Counters = new Dictionary<string, long[]>();
        private static readonly object CounterLock = new object();

        /// <summary>累加一个计数器： [0]=次数 [1]=毫秒 [2]=字节。</summary>
        public static void Count(string key, double ms, long bytes)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (CounterLock)
            {
                long[] slot;
                if (!Counters.TryGetValue(key, out slot))
                {
                    slot = new long[3];
                    Counters[key] = slot;
                }
                slot[0]++;
                slot[1] += (long)ms;
                slot[2] += bytes;
            }
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}
