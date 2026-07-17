using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Two simulation-side savers:
    //
    // 1. STORAGE MEMORY: when an item finds no storage, every hauler re-asks
    //    "where can I store this?" over and over - a full-map stockpile scan per
    //    item per pawn, forever, when stockpiles are full. We remember "nowhere"
    //    for ~4 seconds per item. The memory is wiped instantly when the player
    //    changes any storage settings or zone, so pawns react to real changes.
    //
    // 2. WEALTH STRETCH: the game recounts EVERYTHING the colony owns in one
    //    frame every ~1.4 in-game hours - a visible hiccup on big maps. We let
    //    that happen every ~4 hours instead. Raid sizing reacts slightly slower
    //    to wealth changes; nothing else uses the delta.
    public static class HaulWealthPatches
    {
        // ---- 1. storage memory ----
        private struct NoStoreRec
        {
            public int expireTick;
            public byte priority;
        }

        private static readonly Dictionary<Thing, NoStoreRec> noStorage = new Dictionary<Thing, NoStoreRec>();
        private const int MemoryTicks = 250;

        public static long HaulLookupsSkipped;

        public static bool TryFindBestBetterStorageFor_Prefix(Thing t, StoragePriority currentPriority, ref IntVec3 foundCell, ref IHaulDestination haulDestination, ref bool __result, out bool __state)
        {
            __state = false;
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.haulStorageMemory || t == null)
            {
                return true;
            }
            if (ConflictGuard.SuppressHaulCache && !ConflictGuard.Ov)
            {
                return true;
            }
            NoStoreRec rec;
            if (noStorage.TryGetValue(t, out rec) && rec.priority == (byte)currentPriority && Find.TickManager.TicksGame < rec.expireTick)
            {
                foundCell = IntVec3.Invalid;
                haulDestination = null;
                __result = false;
                HaulLookupsSkipped++;
                __state = true; // served from memory - postfix must not re-arm
                return false;
            }
            return true;
        }

        public static void TryFindBestBetterStorageFor_Postfix(Thing t, StoragePriority currentPriority, bool __result, bool __state)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.haulStorageMemory || t == null || __state)
            {
                return;
            }
            if (ConflictGuard.SuppressHaulCache && !ConflictGuard.Ov)
            {
                return;
            }
            if (__result)
            {
                noStorage.Remove(t);
                return;
            }
            if (noStorage.Count > 4000)
            {
                noStorage.Clear();
            }
            NoStoreRec rec;
            rec.expireTick = Find.TickManager.TicksGame + MemoryTicks;
            rec.priority = (byte)currentPriority;
            noStorage[t] = rec;
        }

        // player changed storage settings / zone shape: forget everything so
        // pawns react instantly
        public static void StorageChanged_Postfix()
        {
            if (noStorage.Count > 0)
            {
                noStorage.Clear();
            }
        }

        public static void ClearStorageMemory()
        {
            noStorage.Clear();
        }

        // ---- 2. wealth recount stretch ----
        private static readonly FieldInfo LastCountTickField = AccessTools.Field(typeof(WealthWatcher), "lastCountTick");
        private const float StretchTicks = 15000f;

        public static long WealthRecountsSkipped;

        public static bool RecountIfNeeded_Prefix(WealthWatcher __instance)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.wealthStretch || LastCountTickField == null)
            {
                return true;
            }
            if (ConflictGuard.SuppressWealth && !ConflictGuard.Ov)
            {
                return true;
            }
            float last = (float)LastCountTickField.GetValue(__instance);
            float now = Find.TickManager.TicksGame;
            if (now - last > 5000f && now - last <= StretchTicks)
            {
                WealthRecountsSkipped++;
                return false; // vanilla would recount now; wait for the stretch
            }
            return true; // not due yet (vanilla no-ops) or overdue (vanilla recounts)
        }
    }
}
