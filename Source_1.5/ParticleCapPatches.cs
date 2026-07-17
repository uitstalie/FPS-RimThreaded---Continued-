using System;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Particle cap. Big fights can request hundreds of new flecks (smoke,
    // sparks, flashes) in a single frame - exactly when FPS matters most.
    // We cap how many NEW flecks may spawn per frame; requests over the cap
    // are simply skipped. Quiet moments never reach the cap, so nothing
    // changes outside heavy scenes. Visual only.
    public static class ParticleCapPatches
    {
        public static long FlecksSkipped;

        private static int frame = -1;
        private static int spawnedThisFrame;

        public static bool CreateFleck_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.particleCap)
            {
                return true;
            }
            if (ConflictGuard.SuppressFleckCap && !ConflictGuard.Ov)
            {
                return true;
            }
            int f = Time.frameCount;
            if (f != frame)
            {
                frame = f;
                spawnedThisFrame = 0;
            }
            if (spawnedThisFrame >= s.particleCapPerFrame)
            {
                FlecksSkipped++;
                return false;
            }
            spawnedThisFrame++;
            return true;
        }
    }
}
