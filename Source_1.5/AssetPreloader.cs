using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimThreadedTTR
{
    /// <summary>
    /// 真多线程 S1：**资源预加载**。
    ///
    /// 实测崩点（真实 383 mod 环境、Pawn 并行时）：
    ///   Tried to get a resource "UI/Abilities/WorkDrive" from a different thread.
    ///   All resources must be loaded in the main thread.
    /// ⇒ 根因：`ContentFinder<Texture2D>.Get(path)` 在**缓存未命中**时会让 Unity 从磁盘加载资源，
    ///   而 Unity 只允许主线程做这件事。mod 代码在 Pawn.Tick 里取图标（能力/gizmo/特效）就会踩到。
    ///
    /// 解法：启动时在**主线程**把所有可能被 tick 用到的贴图路径**全部取一遍**（进 Unity 缓存），
    /// 之后 worker 的取用都命中缓存 ⇒ 不再触发跨线程加载。
    /// 覆盖：所有 Def 实例里名字含 iconPath/texPath/TexPath 的字符串字段（反射扫描，含各 mod 的 Def）。
    /// </summary>
    public static class AssetPreloader
    {
        public static int Loaded;
        public static int Failed;
        public static double Ms;

        public static void Preload()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            HashSet<string> seen = new HashSet<string>();
            List<string> paths = new List<string>();

            // 1) 已知 Def 类型的图标字段（最常见：能力图标、建筑/物品图标、状态图标）
            Collect(paths, seen, typeof(AbilityDef), "uiIconPath");
            Collect(paths, seen, typeof(ThingDef), "uiIconPath");
            Collect(paths, seen, typeof(HediffDef), "uiIconPath");
            Collect(paths, seen, typeof(TraitDef), "uiIconPath");
            Collect(paths, seen, typeof(ResearchProjectDef), "uiIconPath");
            Collect(paths, seen, typeof(GeneDef), "uiIconPath");
            Collect(paths, seen, typeof(RecipeDef), "uiIconPath");

            // 2) 通用反射：遍历 DefDatabase 里所有 Def，抓含 icon/tex 的字符串字段（各 mod 自定义 Def 也覆盖）
            try
            {
                foreach (Type defType in AllDefTypes())
                {
                    if (defType == null || defType.IsAbstract) continue;
                    FieldInfo[] fields = defType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    List<FieldInfo> hits = new List<FieldInfo>();
                    for (int i = 0; i < fields.Length; i++)
                    {
                        string n = fields[i].Name.ToLowerInvariant();
                        if (fields[i].FieldType == typeof(string) &&
                            (n.Contains("iconpath") || n.Contains("texpath") || n.EndsWith("path")))
                        {
                            hits.Add(fields[i]);
                        }
                    }
                    if (hits.Count == 0) continue;
                    IList defs = AllDefsOf(defType);
                    if (defs == null) continue;
                    foreach (object def in defs)
                    {
                        for (int i = 0; i < hits.Count; i++)
                        {
                            string v = hits[i].GetValue(def) as string;
                            if (!string.IsNullOrEmpty(v) && seen.Add(v)) paths.Add(v);
                        }
                    }
                }
            }
            catch (Exception e) { Log.Warning("[RimThreadedTTR] 资源预扫描部分失败（不影响主流程）: " + e.Message); }

            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    Texture2D t = ContentFinder<Texture2D>.Get(paths[i], false);
                    if (t != null) Loaded++; else Failed++;
                }
                catch { Failed++; }
            }
            sw.Stop();
            Ms = sw.Elapsed.TotalMilliseconds;
            Log.Message("[RimThreadedTTR] S1 资源预加载：" + Loaded + " 张贴图已进主线程缓存（未命中 " + Failed
                + "，扫描 " + paths.Count + " 条路径，耗时 " + Ms.ToString("F0") + " ms）");
        }

        /// <summary>所有非抽象 Def 子类（含各 mod 自定义 Def）。</summary>
        private static IEnumerable<Type> AllDefTypes()
        {
            List<Type> list = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                for (int i = 0; i < types.Length; i++)
                {
                    Type ty = types[i];
                    if (ty != null && !ty.IsAbstract && typeof(Def).IsAssignableFrom(ty)) list.Add(ty);
                }
            }
            return list;
        }

        /// <summary>DefDatabase&lt;T&gt;.AllDefsListForReading（反射取，避免泛型约束限制）。</summary>
        private static IList AllDefsOf(Type defType)
        {
            try
            {
                Type db = typeof(DefDatabase<>).MakeGenericType(defType);
                PropertyInfo pi = db.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
                if (pi == null) return null;
                return pi.GetValue(null, null) as IList;
            }
            catch { return null; }
        }

        private static void Collect(List<string> paths, HashSet<string> seen, Type defType, string field)
        {
            try
            {
                FieldInfo fi = defType.GetField(field, BindingFlags.Public | BindingFlags.Instance);
                if (fi == null) return;
                IList defs = AllDefsOf(defType);
                if (defs == null) return;
                foreach (object def in defs)
                {
                    string v = fi.GetValue(def) as string;
                    if (!string.IsNullOrEmpty(v) && seen.Add(v)) paths.Add(v);
                }
            }
            catch { }
        }
    }
}
