using System;
using System.Collections.Concurrent;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// Actions queued from worker threads that must run on the main thread
    /// (Unity API calls, sound playback, anything not thread-safe).
    /// Drained once per frame and after each parallel section.
    /// Public so other mods can marshal work to the main thread through it.
    /// </summary>
    public static class MainThreadQueue
    {
        private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

        public static void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }
            if (UnityData.IsInMainThread)
            {
                Run(action);
            }
            else
            {
                queue.Enqueue(action);
            }
        }

        public static void Drain()
        {
            if (!UnityData.IsInMainThread)
            {
                return;
            }
            Action action;
            while (queue.TryDequeue(out action))
            {
                Run(action);
            }
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("[RimThreadedTTR] Exception in main-thread-queued action: " + ex);
            }
        }
    }
}
