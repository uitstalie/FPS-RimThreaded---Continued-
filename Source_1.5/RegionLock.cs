using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// 真多线程第 2 步：**区域系统锁**。
    ///
    /// 实测崩点（HediffSet 加锁后前移到这里）：
    ///   ... at Verse.RegionListersUpdater.DeregisterInRegions (Thing thing, Map map)
    ///       at Verse.Thing.set_Position (IntVec3 value)          ← 小人移动改坐标
    ///       at Verse.AI.Pawn_PathFollower.TryEnterNextPathCell ()
    ///       at Verse.AI.Pawn_PathFollower.PatherTick ()
    ///
    /// 做法：`RegionListersUpdater` 的注册/注销 + `RegionDirtyer` 的脏标记通知
    /// **共用一把全局锁**（粗粒度但无死锁风险；这些操作本身都是微秒级）。
    /// 先用它把"pawn 并行不再崩"这条链推通，之后再视吞吐决定是否细化为每地图锁。
    /// </summary>
    public static class RegionLock
    {
        public static bool Enabled;
        private static readonly object Gate = new object();
        public static long Enters;
        public static long Contentions;

        public static bool Enter_Prefix()
        {
            if (!Enabled) return true;
            if (!Monitor.TryEnter(Gate))
            {
                Contentions++;
                Monitor.Enter(Gate);
            }
            Enters++;
            return true;
        }

        public static Exception Exit_Finalizer(Exception __exception)
        {
            if (Enabled) Monitor.Exit(Gate);
            return null;
        }

        public static void Apply(Harmony harmony)
        {
            MethodInfo enter = AccessTools.Method(typeof(RegionLock), "Enter_Prefix");
            MethodInfo exit = AccessTools.Method(typeof(RegionLock), "Exit_Finalizer");
            int n = 0;
            n += PatchAll(harmony, "Verse.RegionListersUpdater", new[] { "RegisterInRegions", "DeregisterInRegions" }, enter, exit);
            // 热点 #5（实测）：RegionTraverser.BreadthFirstTraverse ← PawnUtility.EnemiesAreNearby ← MindStateTickInterval
            n += PatchAll(harmony, "Verse.RegionTraverser", new[] { "BreadthFirstTraverse" }, enter, exit);
            n += PatchAll(harmony, "Verse.RegionDirtyer", new[] { "Notify_WalkabilityChanged", "Notify_ThingAffectingRegionsSpawned", "Notify_ThingAffectingRegionsDespawned", "SetRegionDirty", "SetAllDirty", "DirtyRegionForThing" }, enter, exit);
            Enabled = true;
            Log.Message("[RimThreadedTTR] 区域系统锁已启用：" + n + " 个方法（RegionListersUpdater + RegionDirtyer）");
        }

        private static int PatchAll(Harmony harmony, string typeName, string[] names, MethodInfo enter, MethodInfo exit)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Log.Warning("[RimThreadedTTR] 区域锁：找不到类型 " + typeName);
                return 0;
            }
            int n = 0;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                bool hit = false;
                for (int i = 0; i < names.Length; i++) if (m.Name == names[i]) { hit = true; break; }
                if (!hit) continue;
                try { harmony.Patch(m, new HarmonyMethod(enter), null, null, new HarmonyMethod(exit)); n++; }
                catch (Exception e) { Log.Warning("[RimThreadedTTR] 区域锁挂载失败 " + typeName + "." + m.Name + ": " + e.Message); }
            }
            return n;
        }
    }
}
