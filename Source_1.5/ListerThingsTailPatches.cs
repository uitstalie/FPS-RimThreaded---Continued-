using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// B3：`Verse.ListerThings.Remove(Thing)` 的"**尾部优先删除**"。
    ///
    /// 独立实现：只从原版反编译结果出发（没有复制 Kingfisher 的任何代码）。
    ///
    /// 原版反编译（1.6.4871，/tmp/ildump/Verse_ListerThings.cs:271-296）：
    /// <code>
    /// public void Remove(Thing t)
    /// {
    ///     if (!EverListable(t.def, use)) return;
    ///     if (listsByDef.TryGetValue(t.def, out var value)) value.Remove(t);           // ← List.Remove：从头 IndexOf
    ///     if (t is IHaulSource item) haulSources.Remove(item);                         // ← 同上
    ///     ThingRequestGroup[] allGroups = ThingListGroupHelper.AllGroups;
    ///     for (int i = 0; i &lt; allGroups.Length; i++)
    ///     {
    ///         ThingRequestGroup g = allGroups[i];
    ///         if ((use != ListerThingsUse.Region || g.StoreInRegion()) &amp;&amp; g.Includes(t.def))
    ///         {
    ///             listsByGroup[i].Remove(t);                                           // ← 同上
    ///             stateHashByGroup[(uint)g]++;                                         // 状态哈希：仍然执行
    ///         }
    ///     }
    ///     thingListChangedCallbacks?.onThingRemoved?.Invoke(t);                        // 回调：仍然执行
    /// }
    /// </code>
    ///
    /// 改法：只把 `List&lt;T&gt;.Remove(item)`（从头部扫）换成 `LastIndexOf + RemoveAt`（从尾部扫），
    /// 别的**一个字节都不动** —— 早退条件、`listsByDef` 分支、`stateHashByGroup[group]++`、
    /// `onThingRemoved` 回调全部保持原样。两者都使用 `EqualityComparer&lt;T&gt;.Default`
    /// （`List.Remove` → `Array.IndexOf`；`List.LastIndexOf` → `Array.LastIndexOf`），
    /// 唯一差别是"当同一个相等元素出现多次时删掉哪一个"。在本方法的调用契约里同一个
    /// Thing 引用在同一个列表里最多出现一次（Add/Remove 成对），因此**行为等价**。
    ///
    /// 为什么更快：`Add` 追加到尾部，随后 `Remove` 同一批对象（LIFO）时，尾扫几乎立刻命中，
    /// 避免了头扫在长列表（大基地的 Thing 列表可达数千项）上白走一遍。
    ///
    /// 注意（据实说明）：这**不修复**并发正确性。P2 并行集里的 `Mote.Tick` 自毁会从 worker
    /// 线程调用本方法，`List.RemoveAt` 的内部搬移与 `stateHashByGroup[g]++` 的非原子性
    /// 在改前改后完全一样（见评估报告 §5.3）。这里只做与原版等价的删除方向优化。
    /// </summary>
    public static class ListerThingsTailPatches
    {
        public static long TailRemoves;

        /// <summary>真正的替换实现：与 `List&lt;T&gt;.Remove(item)` 同签名、同返回语义。</summary>
        public static bool RemoveFromTail<T>(List<T> list, T item)
        {
            if (list == null)
            {
                return false;
            }
            int index = list.LastIndexOf(item);   // 与 List.Remove 相同的 EqualityComparer<T>.Default
            if (index < 0)
            {
                return false;
            }
            list.RemoveAt(index);
            TailRemoves++;
            return true;
        }

        private static readonly MethodInfo RemoveFromTailGeneric =
            AccessTools.Method(typeof(ListerThingsTailPatches), "RemoveFromTail");

        /// <summary>把目标方法里的每个 `List&lt;T&gt;.Remove(x)` 改写成 `RemoveFromTail(list, x)`。</summary>
        public static IEnumerable<CodeInstruction> Remove_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i < list.Count; i++)
            {
                CodeInstruction ci = list[i];
                if (ci.opcode != OpCodes.Callvirt && ci.opcode != OpCodes.Call)
                {
                    continue;
                }
                MethodInfo mi = ci.operand as MethodInfo;
                if (mi == null || mi.Name != "Remove" || mi.ReturnType != typeof(bool))
                {
                    continue;
                }
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length != 1)
                {
                    continue;
                }
                Type decl = mi.DeclaringType;
                if (decl == null || !decl.IsGenericType || decl.GetGenericTypeDefinition() != typeof(List<>))
                {
                    continue;
                }
                Type element = decl.GetGenericArguments()[0];
                if (ps[0].ParameterType != element)
                {
                    continue;
                }
                // 就地改写：保留 labels / blocks / 异常块信息（不新建 CodeInstruction 以免丢元数据）
                ci.opcode = OpCodes.Call;
                ci.operand = RemoveFromTailGeneric.MakeGenericMethod(element);
                replaced++;
            }
            if (replaced == 0)
            {
                Log.Warning("[RimThreadedTTR] B3 ListerThings.Remove：未找到任何 List<T>.Remove 调用点，"
                    + "本次不改写（退回原版行为）");
            }
            else
            {
                Log.Message("[RimThreadedTTR] B3 ListerThings.Remove：已把 " + replaced
                    + " 处 List<T>.Remove 改写为尾部删除（其余逻辑/回调/状态哈希不变）");
            }
            return list;
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(ListerThings), "Remove", new[] { typeof(Thing) });
                if (target == null)
                {
                    Log.Warning("[RimThreadedTTR] B3 目标 Verse.ListerThings.Remove(Thing) 不存在，跳过");
                    return;
                }
                harmony.Patch(target, null, null,
                    new HarmonyMethod(AccessTools.Method(typeof(ListerThingsTailPatches), "Remove_Transpiler")), null);
            }
            catch (Exception e)
            {
                Log.Warning("[RimThreadedTTR] B3 挂载失败: " + e.Message);
            }
        }
    }
}
