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
