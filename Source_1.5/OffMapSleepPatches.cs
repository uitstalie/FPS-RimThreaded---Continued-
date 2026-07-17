using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FPSPlus
{
    // Off-map pawn sleep. The game "mothballs" off-map pawns (they tick ~100x
    // less), but ANY hediff that isn't specifically whitelisted blocks the
    // sleep - and with a large modlist almost every pawn carries some modded
    // hediff (gene effects, race markers, "idle while despawned"...). Result:
    // hundreds of invisible pawns ticking at full price forever.
    //
    // This patch lets off-map pawns sleep despite those hediffs. Caravan
    // members and transport-pod travelers still never sleep (vanilla rule
    // kept). Cost: an off-map pawn's wounds/diseases pause instead of
    // progressing - they resume the moment the pawn matters again.
    public static class OffMapSleepPatches
    {
        public static long ForcedSleeps;

        public static void ShouldMothball_Postfix(Pawn p, ref bool __result)
        {
            if (__result)
            {
                return;
            }
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.offMapSleep || p == null)
            {
                return;
            }
            if (ConflictGuard.SuppressOffMapSleep && !ConflictGuard.Ov)
            {
                return;
            }
            // keep vanilla's hard exclusions - only bypass the hediff blockers
            if (p.IsCaravanMember() || PawnUtility.IsTravelingInTransportPodWorldObject(p))
            {
                return;
            }
            __result = true;
            ForcedSleeps++;
        }
    }
}
