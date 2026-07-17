using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace FPSPlus
{
    // EXPERIMENTAL reach cache. Pawns ask "can I walk to X?" over and over -
    // and for blocked things the answer is "no" every single time, each
    // answer paid for with a region flood. We remember a NO for 60 ticks
    // (1 second at 1x).
    //
    // Safety rules, in order of importance:
    //  - only NO answers are cached; YES always runs vanilla
    //  - only for pawn queries, never for checks against another pawn
    //    (pawns move - a stale no could affect combat targeting)
    //  - the whole cache is wiped the moment ANY structure spawns/despawns
    //    or walkability changes anywhere on any map (region dirty hooks)
    //  - hard TTL of 60 ticks even without invalidation
    // Worst case: a pawn ignores a newly reachable target for up to 1 second.
    // OFF by default - experimental.
    public static class ReachCachePatches
    {
        public static long ReachChecksSkipped;

        private const int TtlTicks = 60;
        private const int MaxEntries = 4096;

        private struct Key : IEquatable<Key>
        {
            public int pawnId;
            public int cell;
            public int flags; // peMode | mode<<8 | danger<<16 | bash<<24

            public bool Equals(Key other)
            {
                return pawnId == other.pawnId && cell == other.cell && flags == other.flags;
            }

            public override bool Equals(object obj)
            {
                return obj is Key && Equals((Key)obj);
            }

            public override int GetHashCode()
            {
                int h = pawnId;
                h = h * 31 + cell;
                h = h * 31 + flags;
                return h;
            }
        }

        private static readonly Dictionary<Key, int> NoCache = new Dictionary<Key, int>();

        private static bool On()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.reachCache)
            {
                return false;
            }
            if (ConflictGuard.SuppressReach && !ConflictGuard.Ov)
            {
                return false;
            }
            return true;
        }

        public static void ClearAll()
        {
            NoCache.Clear();
        }

        private static bool TryBuildKey(LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams, out Key key)
        {
            key = default(Key);
            if (traverseParams.pawn == null || !dest.IsValid)
            {
                return false;
            }
            if (dest.HasThing && dest.Thing is Pawn)
            {
                return false; // moving targets are never cached
            }
            IntVec3 c = dest.Cell;
            key.pawnId = traverseParams.pawn.thingIDNumber;
            key.cell = c.x + (c.z << 12);
            int bash = (traverseParams.canBashDoors ? 1 : 0) | (traverseParams.canBashFences ? 2 : 0);
            key.flags = (int)peMode | ((int)traverseParams.mode << 8) | ((int)traverseParams.maxDanger << 16) | (bash << 24);
            return true;
        }

        public static bool CanReach_Prefix(LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams, ref bool __result, ref object __state)
        {
            __state = null;
            if (!On())
            {
                return true;
            }
            Key key;
            if (!TryBuildKey(dest, peMode, traverseParams, out key))
            {
                return true;
            }
            int expiry;
            if (NoCache.TryGetValue(key, out expiry))
            {
                if (Find.TickManager.TicksGame < expiry)
                {
                    ReachChecksSkipped++;
                    __result = false;
                    return false;
                }
                NoCache.Remove(key);
            }
            __state = key; // remember for the postfix
            return true;
        }

        public static void CanReach_Postfix(bool __result, object __state)
        {
            if (__result || __state == null || !On())
            {
                return;
            }
            if (NoCache.Count >= MaxEntries)
            {
                NoCache.Clear();
            }
            TickManager tm = Find.TickManager;
            if (tm != null)
            {
                NoCache[(Key)__state] = tm.TicksGame + TtlTicks;
            }
        }

        // region invalidation hooks - any structural change wipes the cache
        public static void RegionsChanged_Postfix()
        {
            if (NoCache.Count > 0)
            {
                NoCache.Clear();
            }
        }
    }
}
