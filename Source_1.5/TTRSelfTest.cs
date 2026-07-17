using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimThreadedTTR
{
    /// <summary>
    /// Diagnostic harness, inert unless the game is started with the
    /// -ttrselftest command line argument. Spawns a burst of flecks shortly
    /// after map load and then reports whether the parallel simulation path
    /// actually ran. Used to validate the mod after game updates.
    /// </summary>
    public class TTRSelfTest : GameComponent
    {
        private readonly bool active;
        private int startTick = -1;
        private bool spawnedFlecks;
        private bool reported;

        public TTRSelfTest(Game game)
        {
            active = GenCommandLine.CommandLineArgPassed("ttrselftest");
        }

        public override void GameComponentTick()
        {
            if (!active || reported)
            {
                return;
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
            }
            int elapsed = Find.TickManager.TicksGame - startTick;
            if (elapsed >= 60 && !spawnedFlecks)
            {
                spawnedFlecks = true;
                Vector3 center = map.Center.ToVector3Shifted();
                for (int i = 0; i < 800; i++)
                {
                    Vector3 offset = new Vector3(Rand.Range(-15f, 15f), 0f, Rand.Range(-15f, 15f));
                    FleckMaker.ThrowSmoke(center + offset, map, 1.5f);
                    FleckMaker.ThrowDustPuff(center + offset, map, 1.2f);
                }
                Log.Message("[RimThreadedTTR] Self-test: spawned 1600 flecks at map center.");
            }
            if (elapsed >= 500)
            {
                reported = true;
                bool pass = FleckRegistry.parallelRunCount > 0
                    && !FleckRegistry.runtimeDisabled
                    && !FleckRegistry.drawRuntimeDisabled;
                Log.Message("[RimThreadedTTR] Self-test result: parallel sim batches: "
                    + FleckRegistry.parallelRunCount
                    + ", parallel draw batches: " + FleckRegistry.drawParallelRunCount
                    + ", sim disabled: " + FleckRegistry.runtimeDisabled
                    + ", draw disabled: " + FleckRegistry.drawRuntimeDisabled
                    + (pass ? " -> PASS" : " -> FAIL"));
            }
        }
    }

    /// <summary>
    /// Combat self-test, inert unless launched with -ttrcombattest. Spawns two
    /// mutually hostile squads of gunners and reports whether the parallel
    /// targeting path ran during the resulting firefight.
    /// </summary>
    public class TTRCombatSelfTest : GameComponent
    {
        private readonly bool active;
        private int startTick = -1;
        private bool spawned;
        private bool reported;

        public TTRCombatSelfTest(Game game)
        {
            active = GenCommandLine.CommandLineArgPassed("ttrcombattest");
        }

        public override void GameComponentTick()
        {
            if (!active || reported)
            {
                return;
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
            }
            int elapsed = Find.TickManager.TicksGame - startTick;
            if (elapsed >= 60 && !spawned)
            {
                spawned = true;
                SpawnSquads(map);
            }
            if (elapsed >= 1500)
            {
                reported = true;
                bool pass = TargetingPatches.parallelRunCount > 0
                    && !TargetingPatches.runtimeDisabled
                    && TargetingPatches.verifyMismatches == 0;
                Log.Message("[RimThreadedTTR] Combat self-test result: parallel targeting batches: "
                    + TargetingPatches.parallelRunCount
                    + " (prefix called " + TargetingPatches.prefixCallCount + "x, max candidates " + TargetingPatches.maxCandidatesSeen + ")"
                    + ", verify: " + TargetingPatches.verifyMismatches + " mismatches / " + TargetingPatches.verifyChecks + " checks"
                    + ", targeting disabled: " + TargetingPatches.runtimeDisabled
                    + (pass ? " -> PASS" : " -> FAIL"));
            }
        }

        private void SpawnSquads(Map map)
        {
            Faction f1 = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate);
            if (f1 == null)
            {
                List<Faction> all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    Faction f = all[i];
                    if (!f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer))
                    {
                        f1 = f;
                        break;
                    }
                }
            }
            if (f1 == null)
            {
                Log.Warning("[RimThreadedTTR] Combat self-test: no hostile humanlike faction found.");
                return;
            }
            // Gunners on one side, a permanent-manhunter warg pack on the other:
            // manhunters attack everyone, so a fight is guaranteed regardless of
            // world faction relations.
            PawnKindDef gunner = DefDatabase<PawnKindDef>.GetNamed("Mercenary_Gunner");
            PawnKindDef warg = DefDatabase<PawnKindDef>.GetNamedSilentFail("Warg");
            if (warg == null)
            {
                warg = DefDatabase<PawnKindDef>.GetNamed("Bear_Grizzly");
            }
            List<Pawn> gunners = SpawnSquad(map, gunner, f1, map.Center + new IntVec3(-12, 0, 0), 20, manhunter: false);
            SpawnSquad(map, warg, null, map.Center + new IntVec3(12, 0, 0), 25, manhunter: true);
            // Without a lord, hostile NPCs try to leave the map; make them hold
            // position and fight instead.
            if (gunners.Count > 0)
            {
                Verse.AI.Group.LordMaker.MakeNewLord(f1, new Verse.AI.Group.LordJob_DefendPoint(map.Center + new IntVec3(-12, 0, 0)), map, gunners);
            }
            Log.Message("[RimThreadedTTR] Combat self-test: spawned " + gunners.Count + " gunners (" + f1.Name + ") vs 25 manhunter " + warg.label + "s.");
        }

        private List<Pawn> SpawnSquad(Map map, PawnKindDef kind, Faction faction, IntVec3 around, int count, bool manhunter)
        {
            List<Pawn> spawnedPawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell;
                Predicate<IntVec3> validator = delegate(IntVec3 c)
                {
                    return c.Standable(map) && !c.Fogged(map);
                };
                if (!CellFinder.TryFindRandomCellNear(around, map, 8, validator, out cell))
                {
                    continue;
                }
                Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(pawn, cell, map);
                spawnedPawns.Add(pawn);
                if (manhunter && pawn.mindState != null)
                {
                    pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, null, forced: true);
                }
            }
            return spawnedPawns;
        }
    }
}
