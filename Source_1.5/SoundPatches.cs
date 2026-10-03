using System.Threading;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimThreadedTTR
{
    /// <summary>
    /// Unity's audio API must only be used from the main thread. Vanilla fleck
    /// simulation can play a landing sound (FleckThrown with a landSound def),
    /// which becomes unsafe once fleck simulation runs on worker threads.
    /// These prefixes intercept sound playback on background threads and queue
    /// it for the main thread instead - the same fix the original RimThreaded
    /// shipped for its fully-threaded tick. Also protects any other mod that
    /// (incorrectly) plays sounds from its own background threads.
    /// </summary>
    public static class SoundPatches
    {
        // SoundStarter.PlayOneShot(this SoundDef, SoundInfo)
        public static bool PlayOneShotPrefix(SoundDef soundDef, SoundInfo info)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            SoundDef capturedDef = soundDef;
            SoundInfo capturedInfo = info;
            MainThreadQueue.Enqueue(delegate
            {
                capturedDef.PlayOneShot(capturedInfo);
            });
            return false;
        }

        /// <summary>
        /// S1 第二半（实测热点 #4）：
        ///   at Verse.Sound.SustainerManager.UpdateAllSustainerScopes ()
        ///   at Verse.Sound.SoundStarter.TrySpawnSustainer (SoundDef, SoundInfo)
        ///   at RimWorld.Building_SteamGeyser.StartSpray ()
        /// `UpdateAllSustainerScopes` 是**主线程每帧都会做一次的**收尾（清扫失效 Sustainer 的作用域），
        /// worker 调它既多余、又会与主线程并发改 Sustainer 列表 ⇒ 非主线程直接跳过（主线程照常执行）。
        /// </summary>
        public static bool UpdateAllSustainerScopesPrefix()
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            Interlocked.Increment(ref SkippedSustainerScopeUpdates);
            return false;
        }

        public static long SkippedSustainerScopeUpdates;

        // SoundStarter.PlayOneShotOnCamera(this SoundDef, Map)
        public static bool PlayOneShotOnCameraPrefix(SoundDef soundDef, Map onlyThisMap)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            SoundDef capturedDef = soundDef;
            Map capturedMap = onlyThisMap;
            MainThreadQueue.Enqueue(delegate
            {
                capturedDef.PlayOneShotOnCamera(capturedMap);
            });
            return false;
        }
    }
}
