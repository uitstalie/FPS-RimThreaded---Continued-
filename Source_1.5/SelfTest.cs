using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Runs only when the game is launched with -fpsplustest: force-unpauses,
    // waits for alerts to start (tick 600), collects 1200 frames of data,
    // logs a summary, quits.
    public class FPSPlusSelfTest : GameComponent
    {
        private static readonly bool Armed = GenCommandLine.CommandLineArgPassed("fpsplustest");

        private int frames;
        private bool done;
        private bool announced;
        private bool unpausedOnce;
        private int uiPass;
        private bool uiDone;

        public FPSPlusSelfTest(Game game)
        {
        }

        // Draw each settings page once during the run so a layout bug shows
        // up in the test log instead of on a player's screen.
        public override void GameComponentOnGUI()
        {
            if (!Armed || uiDone || FPSPlusInit.StandaloneActive || frames < 10)
            {
                return;
            }
            try
            {
                SettingsUI.DebugDrawTab(new Rect(0f, 0f, 850f, 560f), uiPass);
                uiPass++;
                if (uiPass >= 6)
                {
                    uiDone = true;
                    Log.Message("[RimThreadedTTR] FPS+ SELFTEST settings UI drew all settings pages (incl. FastLoad) OK.");
                }
            }
            catch (Exception ex)
            {
                uiDone = true;
                Log.Error("[RimThreadedTTR] FPS+ SELFTEST settings UI draw FAILED: " + ex);
            }
        }

        public override void GameComponentUpdate()
        {
            if (!Armed || done || FPSPlusInit.StandaloneActive)
            {
                return;
            }
            if (!announced)
            {
                announced = true;
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST armed - waiting for tick 700.");
            }
            TickManager tm = Find.TickManager;
            if (tm == null)
            {
                return;
            }
            if (tm.TicksGame < 700)
            {
                if (tm.CurTimeSpeed == TimeSpeed.Paused)
                {
                    tm.CurTimeSpeed = TimeSpeed.Fast;
                    if (!unpausedOnce)
                    {
                        unpausedOnce = true;
                        Log.Message("[RimThreadedTTR] FPS+ SELFTEST unpaused the game (tick " + tm.TicksGame + ").");
                    }
                }
                return;
            }
            frames++;
            if (frames == 1)
            {
                CreateTestDesignations();
            }
            if (frames == 600)
            {
                VerifyHaulAndWealth();
                VerifyReachCache();
                SaveDoctor.Scan();
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST SaveDoctor scan: worldPawns=" + SaveDoctor.WorldPawnsAlive + "+" + SaveDoctor.WorldPawnsDead
                    + " tales=" + SaveDoctor.TalesTotal + "/" + SaveDoctor.TalesRemovable
                    + " archive=" + SaveDoctor.ArchiveTotal + "/" + SaveDoctor.ArchiveRemovable
                    + " quests=" + SaveDoctor.QuestsTotal + "/" + SaveDoctor.QuestsRemovable
                    + " filth=" + SaveDoctor.FilthTotal
                    + " filthCleaned=" + SaveDoctor.CleanFilth());
            }
            if (frames % 300 == 0 && frames < 1200)
            {
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST progress " + frames + "/1200 frames, tick " + tm.TicksGame + ".");
            }
            if (frames >= 1200)
            {
                done = true;
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST " + AlertPatches.DebugSummary()
                    + " inspectComputes=" + InspectPatches.Computes
                    + " inspectHits=" + InspectPatches.Hits
                    + " overlayRefreshes=" + UiScanPatches.OverlayRefreshes
                    + " overlayCachedFrames=" + UiScanPatches.OverlayCachedFrames
                    + " workScansSkipped=" + GameplayPatches.WorkScansSkipped
                    + " junkCleaned=" + JunkCleaner.TotalCleaned
                    + " textCacheHits=" + TextCachePatches.Hits
                    + " worldPawnsRemoved=" + WorldPawnCleaner.TotalRemoved
                    + " desInstancedCalls=" + DesignationPatches.InstancedDrawCalls
                    + " desIndividualDraws=" + DesignationPatches.IndividualDraws
                    + " haulLookupsSkipped=" + HaulWealthPatches.HaulLookupsSkipped
                    + " wealthRecountsSkipped=" + HaulWealthPatches.WealthRecountsSkipped
                    + " beautyCacheHits=" + RoomBeautyPatches.BeautyCacheHits
                    + " roomRecomputesSkipped=" + RoomBeautyPatches.RoomRecomputesSkipped
                    + " forcedSleeps=" + OffMapSleepPatches.ForcedSleeps
                    + " factionTicksSkipped=" + WorldPatches.FactionTicksSkipped
                    + " ideoTicksSkipped=" + IdeoPatches.IdeoTicksSkipped
                    + " flecksCapped=" + ParticleCapPatches.FlecksSkipped
                    + " barIconRebuildsSaved=" + ColonistBarPatches.IconRebuildsSaved
                    + " reachChecksSkipped=" + ReachCachePatches.ReachChecksSkipped
                    + " saveDoctorCleaned=" + SaveDoctor.TotalCleaned);
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST workers: " + ComponentPatches.DebugSummary());
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST COMPLETE - shutting down.");
                Root.Shutdown();
            }
        }

        // Directly exercise the storage-memory and wealth-stretch code paths,
        // since a short test window never hits them naturally.
        private static void VerifyHaulAndWealth()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return;
                }
                // storage memory: two identical lookups back to back - if the
                // first finds nothing, the second must be served from memory
                Pawn colonist = null;
                System.Collections.Generic.List<Pawn> pawns = map.mapPawns.FreeColonists;
                if (pawns.Count > 0)
                {
                    colonist = pawns[0];
                }
                System.Collections.Generic.List<Thing> haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways);
                Thing t = null;
                for (int i = 0; i < haulables.Count; i++)
                {
                    if (haulables[i].Spawned)
                    {
                        t = haulables[i];
                        break;
                    }
                }
                if (colonist != null && t != null)
                {
                    IntVec3 cell;
                    IHaulDestination dest;
                    long before = HaulWealthPatches.HaulLookupsSkipped;
                    bool first = StoreUtility.TryFindBestBetterStorageFor(t, colonist, map, StoreUtility.CurrentStoragePriorityOf(t), colonist.Faction, out cell, out dest, true);
                    StoreUtility.TryFindBestBetterStorageFor(t, colonist, map, StoreUtility.CurrentStoragePriorityOf(t), colonist.Faction, out cell, out dest, true);
                    long delta = HaulWealthPatches.HaulLookupsSkipped - before;
                    Log.Message("[RimThreadedTTR] FPS+ SELFTEST storage-memory check: firstLookupFound=" + first + " secondLookupSkipped=" + (delta > 0 ? "YES" : "no"));
                }
                // wealth stretch: pretend the last recount was 6000 ticks ago
                // (vanilla would recount; our stretch must skip it)
                WealthWatcher ww = map.wealthWatcher;
                System.Reflection.FieldInfo lastTick = HarmonyLib.AccessTools.Field(typeof(WealthWatcher), "lastCountTick");
                System.Reflection.MethodInfo recount = HarmonyLib.AccessTools.Method(typeof(WealthWatcher), "RecountIfNeeded");
                if (ww != null && lastTick != null && recount != null)
                {
                    lastTick.SetValue(ww, (float)(Find.TickManager.TicksGame - 6000));
                    long wBefore = HaulWealthPatches.WealthRecountsSkipped;
                    recount.Invoke(ww, null);
                    long wDelta = HaulWealthPatches.WealthRecountsSkipped - wBefore;
                    lastTick.SetValue(ww, (float)Find.TickManager.TicksGame);
                    Log.Message("[RimThreadedTTR] FPS+ SELFTEST wealth-stretch check: recountSkipped=" + (wDelta > 0 ? "YES" : "no"));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ SELFTEST haul/wealth verification failed: " + ex);
            }
        }

        // Two identical can-I-reach checks against solid rock: the first is a
        // real "no", the second must be served from the reach cache.
        private static void VerifyReachCache()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return;
                }
                System.Collections.Generic.List<Pawn> pawns = map.mapPawns.FreeColonists;
                if (pawns.Count == 0)
                {
                    return;
                }
                Pawn colonist = pawns[0];
                IntVec3 target = IntVec3.Invalid;
                foreach (IntVec3 c in map.AllCells)
                {
                    if (c.Impassable(map))
                    {
                        target = c;
                        break;
                    }
                }
                if (!target.IsValid)
                {
                    Log.Message("[RimThreadedTTR] FPS+ SELFTEST reach-cache check skipped: no impassable cell on this map.");
                    return;
                }
                FPSPlusSettings s = FPSPlusMod.Raw;
                bool old = s.reachCache;
                s.reachCache = true;
                ReachCachePatches.ClearAll();
                long before = ReachCachePatches.ReachChecksSkipped;
                bool first = map.reachability.CanReach(colonist.Position, target, Verse.AI.PathEndMode.OnCell, TraverseParms.For(colonist));
                map.reachability.CanReach(colonist.Position, target, Verse.AI.PathEndMode.OnCell, TraverseParms.For(colonist));
                long delta = ReachCachePatches.ReachChecksSkipped - before;
                s.reachCache = old;
                ReachCachePatches.ClearAll();
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST reach-cache check: firstAnswerNo=" + (!first) + " secondFromCache=" + (delta > 0 ? "YES" : "no"));
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ SELFTEST reach-cache verification failed: " + ex);
            }
        }

        // Exercise designation draw batching: designate rocks to mine and plants
        // to cut, like a player mass-selecting with drag tools.
        private static void CreateTestDesignations()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    return;
                }
                int mines = 0;
                foreach (IntVec3 c in map.AllCells)
                {
                    if (mines >= 150)
                    {
                        break;
                    }
                    if (c.GetFirstMineable(map) != null && map.designationManager.DesignationAt(c, DesignationDefOf.Mine) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(c, DesignationDefOf.Mine));
                        mines++;
                    }
                }
                int cuts = 0;
                System.Collections.Generic.List<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
                for (int i = 0; i < plants.Count && cuts < 150; i++)
                {
                    Thing p = plants[i];
                    if (p.Spawned && map.designationManager.DesignationOn(p, DesignationDefOf.CutPlant) == null)
                    {
                        map.designationManager.AddDesignation(new Designation(p, DesignationDefOf.CutPlant));
                        cuts++;
                    }
                }
                Log.Message("[RimThreadedTTR] FPS+ SELFTEST designated " + mines + " mine cells + " + cuts + " plants for draw batching test.");
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ SELFTEST could not create test designations: " + ex);
            }
        }
    }
}
