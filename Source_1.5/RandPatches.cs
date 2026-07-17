using System;
using System.Collections.Generic;
using System.Threading;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// Verse.Rand stores its state in plain static fields (seed + iterations),
    /// so any Rand call from a background thread races against the main thread.
    /// The original RimThreaded fixed this by rewriting the fields as [ThreadStatic]
    /// via assembly-level field replacement. This fork achieves the same effect with
    /// Harmony prefixes: calls made on the main thread run vanilla code untouched;
    /// calls made on any other thread are served from a per-thread random state.
    /// </summary>
    public static class RandPatches
    {
        [ThreadStatic]
        private static bool initialized;

        [ThreadStatic]
        private static uint seed;

        [ThreadStatic]
        private static uint iterations;

        [ThreadStatic]
        private static Stack<ulong> stateStack;

        private static int seedSalt;

        private static void EnsureInit()
        {
            if (!initialized)
            {
                int salt = Interlocked.Increment(ref seedSalt);
                seed = (uint)(Environment.TickCount ^ (Thread.CurrentThread.ManagedThreadId * 2654435769u) ^ ((uint)salt << 16));
                iterations = 0u;
                stateStack = new Stack<ulong>();
                initialized = true;
            }
        }

        private static ulong StateCompressed
        {
            get
            {
                return (ulong)seed | ((ulong)iterations << 32);
            }
            set
            {
                seed = (uint)(value & 0xFFFFFFFFu);
                iterations = (uint)((value >> 32) & 0xFFFFFFFFu);
            }
        }

        // Rand.Value getter
        public static bool ValuePrefix(ref float __result)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            __result = (float)(((double)MurmurHash.GetInt(seed, iterations++) - -2147483648.0) / 4294967295.0);
            return false;
        }

        // Rand.Int getter
        public static bool IntPrefix(ref int __result)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            __result = MurmurHash.GetInt(seed, iterations++);
            return false;
        }

        // Rand.Seed setter
        public static bool SeedPrefix(int value)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            seed = (uint)value;
            iterations = 0u;
            return false;
        }

        // Rand.PushState()
        public static bool PushStatePrefix()
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            stateStack.Push(StateCompressed);
            return false;
        }

        // Rand.PushState(int replacementSeed)
        public static bool PushStateSeedPrefix(int replacementSeed)
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            stateStack.Push(StateCompressed);
            seed = (uint)replacementSeed;
            iterations = 0u;
            return false;
        }

        // Rand.PopState()
        public static bool PopStatePrefix()
        {
            if (UnityData.IsInMainThread)
            {
                return true;
            }
            EnsureInit();
            if (stateStack.Count > 0)
            {
                StateCompressed = stateStack.Pop();
            }
            return false;
        }
    }
}
