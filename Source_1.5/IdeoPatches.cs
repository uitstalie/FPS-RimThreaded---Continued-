using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FPSPlus
{
    // Ideology throttle. IdeoManagerTick runs every tick and ticks every
    // precept of every ideo - ideology-heavy saves carry 1000+ ritual
    // precepts. We run it only on EVEN ticks (1-in-2), same trick as the
    // faction throttle.
    //
    // The one thing that is NOT safe to skip blindly: ritual date triggers
    // (RitualObligationTrigger_Date.Tick) fire on ONE exact tick per year -
    // skip that tick and the ritual silently never happens. So while the
    // throttle is active we replace that check with a 2-tick window: each
    // executed even tick also covers the skipped odd tick before it. Every
    // date still fires exactly once, at most 1 tick late (1/60 second).
    public static class IdeoPatches
    {
        public static long IdeoTicksSkipped;

        private static readonly FieldInfo MustBePlayerIdeoField =
            AccessTools.Field(typeof(RitualObligationTrigger), "mustBePlayerIdeo");

        private static bool ThrottleOn()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.ideoThrottle)
            {
                return false;
            }
            if (ConflictGuard.SuppressIdeo && !ConflictGuard.Ov)
            {
                return false;
            }
            return true;
        }

        public static bool IdeoManagerTick_Prefix()
        {
            if (!ThrottleOn())
            {
                return true;
            }
            TickManager tm = Find.TickManager;
            if (tm == null || (tm.TicksGame & 1) == 0)
            {
                return true; // even ticks run vanilla
            }
            IdeoTicksSkipped++;
            return false;
        }

        // Replicates RitualObligationTrigger_Date.Tick with the window.
        public static bool DateTriggerTick_Prefix(RitualObligationTrigger_Date __instance)
        {
            if (!ThrottleOn())
            {
                return true; // vanilla exact-tick check
            }
            try
            {
                Precept_Ritual rit = __instance.ritual;
                if (rit == null || rit.isAnytime)
                {
                    return false;
                }
                bool mustBePlayer = MustBePlayerIdeoField != null && (bool)MustBePlayerIdeoField.GetValue(__instance);
                if (mustBePlayer && !Faction.OfPlayer.ideos.Has(rit.ideo))
                {
                    return false;
                }
                int cur = __instance.CurrentTickRelative();
                int occ = __instance.OccursOnTick();
                if (cur == occ || cur - 1 == occ)
                {
                    rit.AddObligation(new RitualObligation(rit));
                }
                return false;
            }
            catch (Exception)
            {
                return true; // anything odd: fall back to vanilla behavior
            }
        }
    }
}
