using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FPSPlus
{
    // OPTIONAL gameplay-affecting optimizations. Unlike the UI-side features,
    // these CAN change game behavior - each has its own settings switch.
    public static class GameplayPatches
    {
        public static long WorkScansSkipped;

        // ---- 1. Animal wander throttling ----
        // Idle animals burn CPU re-running their think tree and pathfinding a new
        // wander destination every few seconds. Stretching wander/wait job length
        // means fewer re-thinks; animals behave the same, just change activity
        // less often.
        public static void WanderTryGiveJob_Postfix(Pawn pawn, ref Job __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.animalWanderThrottle || __result == null || pawn == null)
            {
                return;
            }
            if (ConflictGuard.SuppressWander && !ConflictGuard.Ov)
            {
                return;
            }
            RaceProperties race = pawn.RaceProps;
            if (race == null || !race.Animal)
            {
                return;
            }
            int expiry = __result.expiryInterval;
            if (expiry > 0 && expiry < 2500)
            {
                int stretched = expiry * s.animalWanderMult;
                if (stretched > 2500)
                {
                    stretched = 2500;
                }
                __result.expiryInterval = stretched;
            }
        }

        // ---- 3. Work scan cooldown ----
        // A jobless colonist re-scans every WorkGiver against every thing on the
        // map over and over. After a scan that found nothing, skip re-scans for a
        // short cooldown. Emergency work (firefighting, prioritized) and direct
        // player orders are never delayed.
        private static readonly Dictionary<Pawn, int> nextWorkScanTick = new Dictionary<Pawn, int>();

        public static bool WorkTryIssueJobPackage_Prefix(JobGiver_Work __instance, Pawn pawn, ref ThinkResult __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.workScanCooldown || pawn == null || __instance.emergency)
            {
                return true;
            }
            if (ConflictGuard.SuppressWorkScan && !ConflictGuard.Ov)
            {
                return true;
            }
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                return true;
            }
            int now = Find.TickManager.TicksGame;
            int next;
            if (nextWorkScanTick.TryGetValue(pawn, out next) && now < next)
            {
                WorkScansSkipped++;
                __result = ThinkResult.NoJob;
                return false;
            }
            return true;
        }

        public static void WorkTryIssueJobPackage_Postfix(JobGiver_Work __instance, Pawn pawn, ThinkResult __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.workScanCooldown || pawn == null || __instance.emergency)
            {
                return;
            }
            if (ConflictGuard.SuppressWorkScan && !ConflictGuard.Ov)
            {
                return;
            }
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            int stored;
            if (nextWorkScanTick.TryGetValue(pawn, out stored) && now < stored)
            {
                return; // this call was skipped by the cooldown - keep it as is
            }
            if (__result.Job == null)
            {
                if (nextWorkScanTick.Count > 400)
                {
                    nextWorkScanTick.Clear();
                }
                nextWorkScanTick[pawn] = now + s.workScanCooldownTicks;
            }
            else
            {
                nextWorkScanTick.Remove(pawn);
            }
        }

        // ---- 4. Population caps ----
        public static void ThreatPoints_Postfix(ref float __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.capRaids && __result > s.raidPointsCap
                && (!ConflictGuard.SuppressThreat || ConflictGuard.Ov))
            {
                __result = s.raidPointsCap;
            }
        }

        public static void AnimalDensity_Postfix(ref float __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.wildlifeReduce
                && (!ConflictGuard.SuppressDensity || ConflictGuard.Ov))
            {
                __result *= s.wildlifeMult;
            }
        }

        // ---- 5. Less detail when zoomed out (visual only) ----
        private static bool ZoomedOut()
        {
            CameraDriver cd = Find.CameraDriver;
            if (cd == null)
            {
                return false;
            }
            CameraZoomRange z = cd.CurrentZoom;
            return z == CameraZoomRange.Far || z == CameraZoomRange.Furthest;
        }

        public static bool ShadowDrawWorker_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.zoomDetail && (!ConflictGuard.SuppressShadow || ConflictGuard.Ov) && ZoomedOut())
            {
                return false; // skip dynamic shadows - invisible at this zoom anyway
            }
            return true;
        }

        public static bool DrawAllWeather_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.zoomDetail && (!ConflictGuard.SuppressWeather || ConflictGuard.Ov) && ZoomedOut())
            {
                return false; // skip rain/snow particle drawing when zoomed out
            }
            return true;
        }

        public static bool PlantWindSway_Prefix(ref bool __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.zoomDetail && (!ConflictGuard.SuppressSway || ConflictGuard.Ov) && ZoomedOut())
            {
                __result = false; // plants stop swaying while zoomed out
                return false;
            }
            return true;
        }
    }

    // ---- 2. Junk cleanup ----
    // Once per in-game hour, despawns clutter OUTSIDE the home area:
    // filth (always when enabled), rock chunks not in any zone (sub-option),
    // dessicated non-colonist corpses (sub-option). Capped per pass to avoid
    // hitches.
    public class JunkCleaner : GameComponent
    {
        public static long TotalCleaned;

        private int nextPassTick;
        private static readonly List<Thing> tmp = new List<Thing>();

        public JunkCleaner(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (FPSPlusInit.StandaloneActive)
            {
                return;
            }
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.junkCleanup)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now < nextPassTick)
            {
                return;
            }
            nextPassTick = now + 2500;
            int budget = 500;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count && budget > 0; m++)
            {
                Map map = maps[m];
                Area home = map.areaManager.Home;
                if (home == null)
                {
                    continue;
                }
                if (s.cleanFilth)
                {
                    budget = CleanPass(map.listerThings.ThingsInGroup(ThingRequestGroup.Filth), home, map, budget, 0);
                }
                if (s.cleanChunks && budget > 0)
                {
                    budget = CleanPass(map.listerThings.ThingsInGroup(ThingRequestGroup.Chunk), home, map, budget, 1);
                }
                if (s.cleanCorpses && budget > 0)
                {
                    budget = CleanPass(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse), home, map, budget, 2);
                }
            }
            SwapSeen();
        }

        // Age tracking: a candidate must stay a candidate for the whole delay
        // before it despawns - fresh battle blood, lootable corpses and freshly
        // mined chunks all get a grace window first.
        private static Dictionary<int, int> firstSeen = new Dictionary<int, int>();
        private static Dictionary<int, int> nextSeen = new Dictionary<int, int>();
        private const int DespawnDelayTicks = 18000; // ~5 real minutes at normal speed

        private static int CleanPass(List<Thing> source, Area home, Map map, int budget, int kind)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            int now = Find.TickManager.TicksGame;
            tmp.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                Thing t = source[i];
                if (!t.Spawned)
                {
                    continue;
                }
                bool aged = false;
                if (kind == 0)
                {
                    // filth: outside home = clean right away (nobody will ever
                    // mop it); inside home = only after the delay, and only if
                    // the everywhere-option is on
                    if (home[t.Position])
                    {
                        if (!s.cleanFilthEverywhere)
                        {
                            continue;
                        }
                        aged = true;
                    }
                }
                else
                {
                    // chunks and corpses: never touch anything in a stockpile
                    // or on a storage shelf, never touch designated things
                    if (t.Position.GetSlotGroup(map) != null)
                    {
                        continue;
                    }
                    if (map.designationManager.DesignationOn(t) != null)
                    {
                        continue;
                    }
                    if (kind == 2)
                    {
                        Corpse c = t as Corpse;
                        if (c == null || c.InnerPawn == null)
                        {
                            continue;
                        }
                        if (c.InnerPawn.Faction != null && c.InnerPawn.Faction.IsPlayer)
                        {
                            continue; // never colonist/colony animal corpses
                        }
                        if (c.questTags != null && c.questTags.Count > 0)
                        {
                            continue; // never quest corpses
                        }
                    }
                    aged = true;
                }
                if (aged)
                {
                    int seen;
                    if (firstSeen.TryGetValue(t.thingIDNumber, out seen))
                    {
                        if (now - seen < DespawnDelayTicks)
                        {
                            nextSeen[t.thingIDNumber] = seen; // still waiting
                            continue;
                        }
                    }
                    else
                    {
                        nextSeen[t.thingIDNumber] = now; // start the clock
                        continue;
                    }
                }
                if (tmp.Count < budget)
                {
                    tmp.Add(t);
                }
                else if (aged)
                {
                    // over budget this pass - keep it ripe for the next one
                    nextSeen[t.thingIDNumber] = now - DespawnDelayTicks;
                }
            }
            for (int i = 0; i < tmp.Count; i++)
            {
                try
                {
                    tmp[i].Destroy(DestroyMode.Vanish);
                    TotalCleaned++;
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimThreadedTTR] FPS+ junk cleanup could not destroy " + tmp[i] + ": " + ex.Message);
                }
            }
            int cleaned = tmp.Count;
            tmp.Clear();
            return budget - cleaned;
        }

        // called after all maps each pass: forget the clocks of things that are
        // no longer candidates (mopped by pawns, hauled to storage, despawned)
        private static void SwapSeen()
        {
            Dictionary<int, int> old = firstSeen;
            firstSeen = nextSeen;
            nextSeen = old;
            nextSeen.Clear();
        }
    }

    // World pawn cleaner: old saves silently carry every pawn that ever existed
    // (dead raiders' relatives, traders from year one...). Vanilla's GC keeps
    // anything with any story link. This removes only the provably-invisible
    // set: DEAD humanlikes the player has NEVER SEEN, with no relation to any
    // player pawn, no quest reservation, not force-kept, corpse gone.
    public class WorldPawnCleaner : GameComponent
    {
        public static long TotalRemoved;

        private int nextPassTick;

        public WorldPawnCleaner(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (FPSPlusInit.StandaloneActive)
            {
                return;
            }
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.worldPawnCleanup)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (now < nextPassTick)
            {
                return;
            }
            nextPassTick = now + 240000; // every 4 in-game days
            TotalRemoved += Clean(200);
        }

        public static int Clean(int max)
        {
            WorldPawns wp = Find.WorldPawns;
            if (wp == null)
            {
                return 0;
            }
            wp.gc.CancelGCPass(); // don't fight vanilla's incremental GC pass
            List<Pawn> dead = new List<Pawn>(wp.AllPawnsDead);
            int removed = 0;
            for (int i = 0; i < dead.Count; i++)
            {
                Pawn p = dead[i];
                if (p == null || p.Discarded || p.RaceProps == null || !p.RaceProps.Humanlike)
                {
                    continue;
                }
                if (p.relations != null && p.relations.everSeenByPlayer)
                {
                    continue; // the player knows this pawn - not our business
                }
                if (PawnUtility.EverBeenColonistOrTameAnimal(p))
                {
                    continue;
                }
                if (wp.ForcefullyKeptPawns.Contains(p))
                {
                    continue; // a mod or quest pinned this pawn on purpose
                }
                if (QuestUtility.IsReservedByQuestOrQuestBeingGenerated(p))
                {
                    continue;
                }
                if (!p.Corpse.DestroyedOrNull())
                {
                    continue; // corpse still exists somewhere
                }
                bool relatedToPlayer = false;
                if (p.relations != null)
                {
                    foreach (Pawn rel in p.relations.RelatedPawns)
                    {
                        if (rel != null && !rel.Discarded && rel.Faction != null && rel.Faction.IsPlayer)
                        {
                            relatedToPlayer = true;
                            break;
                        }
                    }
                }
                if (relatedToPlayer)
                {
                    continue;
                }
                try
                {
                    wp.RemoveAndDiscardPawnViaGC(p);
                    removed++;
                }
                catch (Exception ex)
                {
                    Log.Warning("[RimThreadedTTR] FPS+ world pawn cleanup skipped " + p + ": " + ex.Message);
                }
                if (removed >= max)
                {
                    break;
                }
            }
            return removed;
        }
    }
}
