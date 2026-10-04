using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Verse;

namespace FastLoad
{
    /// <summary>
    /// 静态构造（<c>[StaticConstructorOnStartup]</c>）阶段计时。
    ///
    /// 关键事实（反编译证实）：真正干活的
    /// <c>System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(RuntimeTypeHandle)</c>
    /// 是 <c>extern</c>/PInvoke 方法，**Harmony 不能 patch**。
    /// 唯一入口是 <c>StaticConstructorOnStartupUtility.CallAll()</c>（单线程 foreach 调它）。
    ///
    /// 所以这里分两层：
    /// 1. 前后缀包住 <c>CallAll</c> ⇒ 拿到整段耗时；
    /// 2. **transpiler** 在 <c>CallAll</c> 内那处 <c>call RunClassConstructor</c> 之前
    ///    插入 <c>dup; call TypeTimerBefore(Type)</c>、之后插入 <c>call TypeTimerAfter()</c>
    ///    ⇒ 逐类型的耗时（保留原循环与异常处理，不改变行为）。
    ///
    /// 注意：`RebindAllDefOfs`（早两次）、`GenTypes.get_AllTypes()`、`CreateModClasses()` 也会
    /// 在 `CallAll` 之前触发部分 cctor，那些不在这层统计里。
    /// </summary>
    public static class StaticCtorTiming
    {
        private sealed class Frame
        {
            public double Start;
            public double Child;
            public string TypeName;
            public string AssemblyName;
        }

        [ThreadStatic] private static List<Frame> Frames;
        private static readonly Dictionary<string, double> TypeMs = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> AsmMs = new Dictionary<string, double>();
        private static readonly Dictionary<string, long> TypeCount = new Dictionary<string, long>();
        private static readonly object Gate = new object();
        private static Dictionary<string, string> _assemblyToMod;

        public static double CallAllMs;
        public static long TypeInvocations;
        public static bool TranspilerApplied;

        public static bool Install(Harmony harmony)
        {
            try
            {
                MethodInfo callAll = AccessTools.Method(typeof(StaticConstructorOnStartupUtility), "CallAll");
                if (callAll == null)
                {
                    Log.Warning("[RimThreadedTTR] FastLoad: 找不到 StaticConstructorOnStartupUtility.CallAll");
                    return false;
                }

                MethodInfo prefix = AccessTools.Method(typeof(StaticCtorTiming), "CallAllPrefix");
                MethodInfo finalizer = AccessTools.Method(typeof(StaticCtorTiming), "CallAllFinalizer");
                MethodInfo transpiler = AccessTools.Method(typeof(StaticCtorTiming), "CallAllTranspiler");
                bool haveTranspiler = false;
                try
                {
                    harmony.Patch(callAll,
                        prefix: prefix == null ? null : new HarmonyMethod(prefix),
                        finalizer: finalizer == null ? null : new HarmonyMethod(finalizer),
                        transpiler: transpiler == null ? null : new HarmonyMethod(transpiler));
                    haveTranspiler = TranspilerApplied;
                }
                catch (Exception inner)
                {
                    // transpiler 失败不影响前后缀：退化成"只有整段计时"
                    Log.Warning("[RimThreadedTTR] FastLoad: CallAll transpiler 失败，退化为整段计时: " + inner.Message);
                    harmony.Patch(callAll,
                        prefix: prefix == null ? null : new HarmonyMethod(prefix),
                        finalizer: finalizer == null ? null : new HarmonyMethod(finalizer));
                }

                Log.Message("[RimThreadedTTR] FastLoad: 静态构造计时已安装（CallAll 前后缀"
                            + (haveTranspiler ? " + 逐类型 transpiler" : "，无逐类型") + "）");
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 安装静态构造计时失败: " + e.Message);
                return false;
            }
        }

        // ---------------- CallAll 整段 ----------------

        private static void CallAllPrefix()
        {
            Push("StaticConstructorOnStartupUtility.CallAll()", "(整段)");
        }

        private static Exception CallAllFinalizer(Exception __exception)
        {
            double elapsed = Pop();
            CallAllMs += elapsed;
            return null;   // 绝不改变 CallAll 的行为（尤其不能跳过它）
        }

        // ---------------- 逐类型（transpiler 插桩） ----------------

        /// <summary>供 transpiler 插桩调用：进入某个类型的静态构造。</summary>
        public static void TypeTimerBefore(Type type)
        {
            Push(type == null ? "(未知)" : type.FullName,
                 type == null ? "(未知)" : AssemblyLabel(type));
            lock (Gate) { TypeInvocations++; }
        }

        /// <summary>供 transpiler 插桩调用：离开某个类型的静态构造。</summary>
        public static void TypeTimerAfter()
        {
            Pop();   // Pop 内部已按类型/程序集累计
        }

        private static IEnumerable<CodeInstruction> CallAllTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            MethodInfo runCctor = AccessTools.Method(
                typeof(RuntimeHelpers), "RunClassConstructor", new[] { typeof(RuntimeTypeHandle) });
            MethodInfo before = AccessTools.Method(typeof(StaticCtorTiming), "TypeTimerBefore", new[] { typeof(Type) });
            MethodInfo after = AccessTools.Method(typeof(StaticCtorTiming), "TypeTimerAfter");
            if (runCctor == null || before == null || after == null) return list;

            int inserted = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction instruction = list[i];
                if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) continue;
                if (!Equals(instruction.operand, runCctor)) continue;

                // 此处 IL 形如： ldloc.1 ; callvirt Type::get_TypeHandle ; call RunClassConstructor
                // **严格校验**：前一条必须是"取 TypeHandle"的调用，否则宁可放弃逐类型计时，
                // 也不冒险改动 CallAll 的 IL。
                int handleIndex = i - 1;
                if (handleIndex < 1) continue;
                CodeInstruction handleCall = list[handleIndex];
                if (handleCall.opcode != OpCodes.Call && handleCall.opcode != OpCodes.Callvirt) continue;
                MethodInfo handleMethod = handleCall.operand as MethodInfo;
                if (handleMethod == null || handleMethod.Name != "get_TypeHandle") continue;
                list.Insert(handleIndex, new CodeInstruction(OpCodes.Dup));
                list.Insert(handleIndex + 1, new CodeInstruction(OpCodes.Call, before));
                // RunClassConstructor 现在位于 i+2，其后插入 TypeTimerAfter()
                list.Insert(i + 3, new CodeInstruction(OpCodes.Call, after));
                inserted++;
                i += 3;
            }

            if (inserted > 0)
            {
                TranspilerApplied = true;
            }
            else
            {
                Log.Warning("[RimThreadedTTR] FastLoad: CallAll 里没找到 RunClassConstructor 调用点，逐类型计时不可用");
            }
            return list;
        }

        // ---------------- 计时栈 ----------------

        private static void Push(string typeName, string assemblyName)
        {
            try
            {
                if (Frames == null) Frames = new List<Frame>();
                Frames.Add(new Frame
                {
                    Start = Profiler.Now,
                    Child = 0.0,
                    TypeName = typeName,
                    AssemblyName = assemblyName,
                });
            }
            catch
            {
                // 计时失败不能影响静态构造
            }
        }

        /// <summary>弹栈并返回 exclusive 毫秒；同时把该时间计入按类型/按程序集统计。</summary>
        private static double Pop()
        {
            try
            {
                if (Frames == null || Frames.Count == 0) return 0.0;
                Frame frame = Frames[Frames.Count - 1];
                Frames.RemoveAt(Frames.Count - 1);
                double elapsed = (Profiler.Now - frame.Start) * 1000.0;
                double exclusive = elapsed - frame.Child;
                if (exclusive < 0) exclusive = 0;
                if (Frames.Count > 0) Frames[Frames.Count - 1].Child += elapsed;

                if (frame.TypeName != "(整段)")
                {
                    lock (Gate)
                    {
                        Add(TypeMs, frame.TypeName, exclusive);
                        Add(AsmMs, frame.AssemblyName, exclusive);
                        long count;
                        TypeCount.TryGetValue(frame.TypeName, out count);
                        TypeCount[frame.TypeName] = count + 1;
                    }
                }
                return elapsed;
            }
            catch
            {
                return 0.0;
            }
        }

        private static void Add(Dictionary<string, double> map, string key, double value)
        {
            double current;
            map.TryGetValue(key, out current);
            map[key] = current + value;
        }

        /// <summary>把类型所属程序集映射成可读 mod 名（供运行时归因复用）。</summary>
        public static string LabelFor(Type type)
        {
            return type == null ? "(未知)" : AssemblyLabel(type);
        }

        /// <summary>把程序集名尽量换成用户看得懂的 mod 名（packageId 或 mod 名）。</summary>
        private static string AssemblyLabel(Type type)
        {
            try
            {
                Assembly assembly = type.Assembly;
                string name = assembly.GetName().Name;
                if (_assemblyToMod == null) _assemblyToMod = BuildAssemblyMap();
                string mod;
                if (_assemblyToMod.TryGetValue(name, out mod)) return mod;
                return name;
            }
            catch
            {
                return "(未知程序集)";
            }
        }

        private static Dictionary<string, string> BuildAssemblyMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                object mods = GetRunningMods();
                IEnumerable list = mods as IEnumerable;
                if (list == null) return map;
                foreach (object item in list)
                {
                    ModContentPack mod = item as ModContentPack;
                    if (mod == null) continue;
                    string label;
                    try
                    {
                        label = !string.IsNullOrEmpty(mod.PackageId) ? mod.PackageId : mod.Name;
                    }
                    catch
                    {
                        label = mod.Name;
                    }
                    foreach (Assembly assembly in ModAssemblies(mod))
                    {
                        if (assembly == null) continue;
                        string name;
                        try
                        {
                            name = assembly.GetName().Name;
                        }
                        catch
                        {
                            continue;
                        }
                        if (!map.ContainsKey(name)) map[name] = label;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] FastLoad: 建立 程序集→mod 映射失败: " + e.Message);
            }
            return map;
        }

        private static object GetRunningMods()
        {
            string[] names = { "RunningModsListForReading", "RunningMods" };
            foreach (string name in names)
            {
                try
                {
                    PropertyInfo property = AccessTools.Property(typeof(LoadedModManager), name);
                    if (property != null) return property.GetValue(null, null);
                    FieldInfo field = AccessTools.Field(typeof(LoadedModManager), name);
                    if (field != null) return field.GetValue(null);
                }
                catch
                {
                    // 换下一个名字
                }
            }
            return null;
        }

        private static IEnumerable<Assembly> ModAssemblies(ModContentPack mod)
        {
            var result = new List<Assembly>();
            try
            {
                FieldInfo assembliesField = AccessTools.Field(typeof(ModContentPack), "assemblies");
                object handler = assembliesField == null ? null : assembliesField.GetValue(mod);
                if (handler == null) return result;
                object loaded = null;
                PropertyInfo property = AccessTools.Property(handler.GetType(), "loadedAssemblies");
                if (property != null) loaded = property.GetValue(handler, null);
                if (loaded == null)
                {
                    FieldInfo field = AccessTools.Field(handler.GetType(), "loadedAssemblies");
                    if (field != null) loaded = field.GetValue(handler);
                }
                IEnumerable list = loaded as IEnumerable;
                if (list == null) return result;
                foreach (object item in list)
                {
                    Assembly assembly = item as Assembly;
                    if (assembly != null) result.Add(assembly);
                }
            }
            catch
            {
                // 拿不到就退回程序集名
            }
            return result;
        }

        /// <summary>把静态构造统计写进报告。</summary>
        public static void AppendReport(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("--- 静态构造 [StaticConstructorOnStartup]（exclusive，已扣除嵌套）---");
            sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  CallAll 整段: {0:F0} ms · 逐类型插桩: {1} · 观察到 {2} 次构造",
                CallAllMs, TranspilerApplied ? "已生效" : "未生效", TypeInvocations));

            List<KeyValuePair<string, double>> byType;
            List<KeyValuePair<string, double>> byAsm;
            lock (Gate)
            {
                byType = new List<KeyValuePair<string, double>>(TypeMs);
                byAsm = new List<KeyValuePair<string, double>>(AsmMs);
            }
            byAsm.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  -- 按程序集/mod（前 15）--");
            int shown = 0;
            foreach (var kv in byAsm)
            {
                if (kv.Value < 1.0 || shown++ >= 15) continue;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-46} {1,9:F0} ms", Trim(kv.Key, 46), kv.Value));
            }
            if (shown == 0) sb.AppendLine("  （无）");

            byType.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.AppendLine("  -- 按类型（前 20）--");
            shown = 0;
            foreach (var kv in byType)
            {
                if (kv.Value < 1.0 || shown++ >= 20) continue;
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "  {0,-46} {1,9:F0} ms", Trim(kv.Key, 46), kv.Value));
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
