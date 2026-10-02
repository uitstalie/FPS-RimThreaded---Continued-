using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 平台适配。Windows（.NET Framework）与 Linux 原生版（Mono）在
    /// **补丁安全性**与 **CPU 拓扑** 上都不一样，这里集中处理。
    /// </summary>
    public static class TTRPlatform
    {
        private static int _isMono = -1;
        private static int _perfThreads = -1;

        /// <summary>是否运行在 Mono 上（Linux 原生版是 Mono；Windows 版是 .NET Framework）。</summary>
        public static bool IsMono
        {
            get
            {
                if (_isMono < 0) _isMono = Type.GetType("Mono.Runtime") != null ? 1 : 0;
                return _isMono == 1;
            }
        }

        /// <summary>
        /// **P 核线程数**（不是逻辑核总数）。
        /// Linux：读 /sys/devices/system/cpu/cpuN/cpufreq/cpuinfo_max_freq，取最高频那一档的核数；
        /// 例如 i7-14700K 会得到 16（8 P 核 × HT），而 ProcessorCount 是 28。
        /// 失败则回退到 GenThreading.ProcessorCount。
        /// </summary>
        public static int PerformanceThreadCount
        {
            get
            {
                if (_perfThreads > 0) return _perfThreads;
                int result = 0;
                try
                {
                    const string root = "/sys/devices/system/cpu";
                    if (Directory.Exists(root))
                    {
                        long max = long.MinValue;
                        List<long> freqs = new List<long>();
                        string[] dirs = Directory.GetDirectories(root, "cpu[0-9]*");
                        for (int i = 0; i < dirs.Length; i++)
                        {
                            string f = Path.Combine(dirs[i], "cpufreq", "cpuinfo_max_freq");
                            if (!File.Exists(f)) continue;
                            long v;
                            if (!long.TryParse(File.ReadAllText(f).Trim(), out v)) continue;
                            freqs.Add(v);
                            if (v > max) max = v;
                        }
                        for (int i = 0; i < freqs.Count; i++) if (freqs[i] == max) result++;
                    }
                }
                catch
                {
                    result = 0;
                }
                if (result <= 0) result = GenThreading.ProcessorCount;
                _perfThreads = result;
                return result;
            }
        }
    }
}
