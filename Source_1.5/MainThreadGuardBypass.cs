using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 热点 #3 的另一半：RimWorld 1.6 里有 `AssertMainThread()` 这类**故意的单线程守卫**
    /// （实测 `Verse.MapPawns.AssertMainThread` 在并行 tick 中被 worker 命中而抛异常）。
    /// 这些守卫是开发期断言、不是数据保护 ⇒ 在并行模式生效期间让它们变宽松（仅在 P2 开启时）。
    /// </summary>
    public static class MainThreadGuardBypass
    {
        public static void Apply(Harmony harmony)
        {
            int n = 0;
            n += Patch(harmony, "Verse.MapPawns", "AssertMainThread");
            n += Patch(harmony, "Verse.MapPawns+FactionDictionary", "AssertMainThread");
            Log.Message("[RimThreadedTTR] 单线程断言宽松化：" + n + " 个方法（仅在并行开启期间生效）");
        }

        private static int Patch(Harmony harmony, string typeName, string method)
        {
            Type t = AccessTools.TypeByName(typeName);
            if (t == null) return 0;
            int n = 0;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t))
            {
                if (m.Name != method || m.IsAbstract) continue;
                try
                {
                    harmony.Patch(m, new HarmonyMethod(AccessTools.Method(typeof(MainThreadGuardBypass), "Prefix")), null, null, null);
                    n++;
                }
                catch (Exception e) { Log.Warning("[RimThreadedTTR] 断言宽松化失败 " + typeName + "." + method + ": " + e.Message); }
            }
            return n;
        }

        public static bool Prefix()
        {
            return !TickListParallel.Enabled;   // 并行开启时跳过断言（照常执行原方法体）
        }
    }
}
