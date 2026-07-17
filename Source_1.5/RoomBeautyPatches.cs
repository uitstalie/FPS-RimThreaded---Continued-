using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Giant storage rooms are lag bombs for two reasons:
    //
    // 1. ROOM STATS: every hauled item marks the room dirty, and the next stat
    //    request re-scans EVERY thing in the room (beauty, wealth, space,
    //    impressiveness...). A 1000-item storage room being actively hauled
    //    into = constant full re-scans. We rate-limit recomputes to once per
    //    250 ticks per room; between recomputes the last values are used.
    //
    // 2. BEAUTY SAMPLING: each pawn periodically averages the beauty of ~100
    //    cells around it, checking every item in every cell - ~800 item stat
    //    reads per pawn per sample in a packed room. We cache the result per
    //    4x4-cell block for ~400 ticks.
    //
    // Cost of both: mood/room readouts react a few seconds later. Nothing else.
    public static class RoomBeautyPatches
    {
        // ---- beauty sample cache ----
        private struct BeautyRec
        {
            public int tick;
            public float value;
        }

        private static readonly Dictionary<long, BeautyRec> beautyCache = new Dictionary<long, BeautyRec>();
        private const int BeautyTtlTicks = 400;

        public static long BeautyCacheHits;

        private static long KeyFor(IntVec3 root, Map map)
        {
            return ((long)map.uniqueID << 40) | ((long)(root.x >> 2) << 20) | (long)(root.z >> 2);
        }

        public static bool AverageBeautyPerceptible_Prefix(IntVec3 root, Map map, ref float __result, out bool __state)
        {
            __state = false;
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.roomStatCache || map == null || !root.IsValid)
            {
                return true;
            }
            if (ConflictGuard.SuppressRoomStats && !ConflictGuard.Ov)
            {
                return true;
            }
            long key = KeyFor(root, map);
            BeautyRec rec;
            if (beautyCache.TryGetValue(key, out rec) && Find.TickManager.TicksGame - rec.tick < BeautyTtlTicks)
            {
                __result = rec.value;
                BeautyCacheHits++;
                __state = true; // served from cache - postfix must not re-store
                return false;
            }
            return true;
        }

        public static void AverageBeautyPerceptible_Postfix(IntVec3 root, Map map, float __result, bool __state)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.roomStatCache || map == null || !root.IsValid || __state)
            {
                return;
            }
            if (ConflictGuard.SuppressRoomStats && !ConflictGuard.Ov)
            {
                return;
            }
            if (beautyCache.Count > 4096)
            {
                beautyCache.Clear();
            }
            BeautyRec rec;
            rec.tick = Find.TickManager.TicksGame;
            rec.value = __result;
            beautyCache[KeyFor(root, map)] = rec;
        }

        // ---- room stat recompute rate limit ----
        private static readonly Dictionary<Room, int> lastRoomCompute = new Dictionary<Room, int>();
        private const int RoomStatMinIntervalTicks = 250;

        public static long RoomRecomputesSkipped;

        public static bool UpdateRoomStatsAndRole_Prefix(Room __instance)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.roomStatCache)
            {
                return true;
            }
            if (ConflictGuard.SuppressRoomStats && !ConflictGuard.Ov)
            {
                return true;
            }
            TickManager tm = Find.TickManager;
            if (tm == null)
            {
                return true;
            }
            int now = tm.TicksGame;
            int last;
            if (lastRoomCompute.TryGetValue(__instance, out last) && now - last < RoomStatMinIntervalTicks)
            {
                RoomRecomputesSkipped++;
                return false; // keep last values; dirty flag stays set, so the
                              // real recompute happens once the interval passes
            }
            if (lastRoomCompute.Count > 3000)
            {
                lastRoomCompute.Clear();
            }
            lastRoomCompute[__instance] = now;
            return true;
        }

        public static void ClearCaches()
        {
            beautyCache.Clear();
            lastRoomCompute.Clear();
        }
    }
}
