using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace RimThreadedTTR
{
    /// <summary>
    /// Combat-TPS A/B benchmark. Inert unless launched with -ttrcombatbench.
    /// Spawns two large hostile gunner squads that hold position and fight,
    /// then measures TPS over a fixed window at Ultrafast. Pass -ttrnopar to
    /// force parallel targeting OFF for the run, so the same scenario can be
    /// measured with and without the feature.
    /// </summary>
    public class TTRCombatBench : GameComponent
    {
        private const int SquadSize = 60;
        private const int StabilizeTicks = 150;
        private const int MeasureMs = 15000;

        private readonly bool active;
        private int startTick = -1;
        private bool spawned;
        private bool measuring;
        private bool done;
        private int ticksInWindow;
        private Stopwatch watch;

        public TTRCombatBench(Game game)
        {
            active = GenCommandLine.CommandLineArgPassed("ttrcombatbench");
        }

        public override void GameComponentTick()
        {
            if (!active || done)
            {
                return;
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            }
            if (startTick < 0)
            {
                startTick = Find.TickManager.TicksGame;
                if (GenCommandLine.CommandLineArgPassed("ttrnopar"))
                {
                    // A3：原为 TTRSettings.parallelTargeting（死设置），现为 TargetingPatches 静态字段
                    TargetingPatches.ParallelTargetingEnabled = false;
                    TargetingPatches.runtimeDisabled = true;
                }
                Log.Message("[TTRCombatBench] started. Parallel targeting: "
                    + TargetingPatches.ParallelTargetingEnabled + ".");
            }
            int elapsed = Find.TickManager.TicksGame - startTick;

            if (elapsed >= 30 && !spawned)
            {
                spawned = true;
                SpawnFight(map);
            }
            if (elapsed == StabilizeTicks)
            {
                measuring = true;
                ticksInWindow = 0;
                watch = Stopwatch.StartNew();
            }
            if (measuring && watch.ElapsedMilliseconds >= MeasureMs)
            {
                done = true;
                double tps = ticksInWindow * 1000.0 / watch.ElapsedMilliseconds;
                Log.Message(string.Format(
                    "[TTRCombatBench] RESULT parallelTargeting={0} tps={1:F1} targetingBatches={2} disabled={3}",
                    TargetingPatches.ParallelTargetingEnabled, tps,
                    TargetingPatches.parallelRunCount, TargetingPatches.runtimeDisabled));
                Application.Quit();
            }
            if (measuring)
            {
                ticksInWindow++;
            }
        }

        private void SpawnFight(Map map)
        {
            List<Faction> hostiles = new List<Faction>();
            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (!f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer))
                {
                    hostiles.Add(f);
                }
            }
            if (hostiles.Count == 0)
            {
                Log.Warning("[TTRCombatBench] no hostile faction found.");
                return;
            }
            Faction f1 = hostiles[0];
            PawnKindDef gunner = DefDatabase<PawnKindDef>.GetNamed("Mercenary_Gunner");
            PawnKindDef warg = DefDatabase<PawnKindDef>.GetNamedSilentFail("Warg");
            if (warg == null)
            {
                warg = DefDatabase<PawnKindDef>.GetNamed("Bear_Grizzly");
            }
            IntVec3 left = map.Center + new IntVec3(-18, 0, 0);
            IntVec3 right = map.Center + new IntVec3(18, 0, 0);
            // Ranged shooters (do the heavy target search) held in place by a
            // defend lord, vs a permanent-manhunter animal pack that charges
            // them - a sustained ranged firefight that stresses target scoring.
            List<Pawn> gunners = SpawnSquad(map, gunner, f1, left, SquadSize);
            List<Pawn> pack = SpawnSquad(map, warg, null, right, SquadSize);
            for (int i = 0; i < pack.Count; i++)
            {
                if (pack[i].mindState != null)
                {
                    pack[i].mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, null, forced: true);
                }
            }
            if (gunners.Count > 0)
            {
                LordMaker.MakeNewLord(f1, new LordJob_DefendPoint(left), map, gunners);
            }
            Log.Message("[TTRCombatBench] spawned " + gunners.Count + " gunners vs " + pack.Count + " manhunter " + warg.label + "s.");
        }

        private List<Pawn> SpawnSquad(Map map, PawnKindDef kind, Faction faction, IntVec3 around, int count)
        {
            List<Pawn> list = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell;
                Predicate<IntVec3> validator = delegate(IntVec3 c)
                {
                    return c.Standable(map) && !c.Fogged(map);
                };
                if (!CellFinder.TryFindRandomCellNear(around, map, 10, validator, out cell))
                {
                    continue;
                }
                Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(pawn, cell, map);
                list.Add(pawn);
            }
            return list;
        }
    }
}
