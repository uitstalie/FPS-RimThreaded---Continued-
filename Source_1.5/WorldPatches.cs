using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FPSPlus
{
    // Faction throttle. FactionManagerTick runs EVERY tick: the goodwill
    // manager, settlement-proximity checks, and a full FactionTick for every
    // faction (45+ in big modlists) - almost all of it internal timers that
    // fire on even tick intervals (250, 2500...).
    //
    // We run it only on EVEN ticks (1-in-2). Because vanilla's interval checks
    // use even numbers, they all still fire exactly on schedule - we only skip
    // the odd-tick passes where nothing was going to happen anyway. Half the
    // cost, same behavior. (World objects are NOT touched here: RimWorld 1.6
    // already batches their heavy work at 1-in-15 natively.)
    public static class WorldPatches
    {
        public static long FactionTicksSkipped;

        public static bool FactionManagerTick_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.factionThrottle)
            {
                return true;
            }
            if (ConflictGuard.SuppressFactions && !ConflictGuard.Ov)
            {
                return true;
            }
            TickManager tm = Find.TickManager;
            if (tm == null || (tm.TicksGame & 1) == 0)
            {
                return true; // even ticks: run vanilla, all timers fire on schedule
            }
            FactionTicksSkipped++;
            return false;
        }
    }
}
