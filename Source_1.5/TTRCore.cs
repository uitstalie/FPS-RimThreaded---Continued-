using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimThreadedTTR
{
    /// <summary>
    /// Patch bootstrap. Runs after all defs are loaded (StaticConstructorOnStartup),
    /// which is required because fleck system registration reads DefDatabase.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TTRCore
    {
        private static void PatchPawnParallel(Harmony harmony)
        {
            int ok = 0;
            MethodInfo finalizer = AccessTools.Method(typeof(PawnParallel), "Flush");
            ok += TryPatchPawn(harmony, typeof(Pawn_EquipmentTracker), "EquipmentTrackerTick",
                AccessTools.Method(typeof(PawnParallel), "Collect_Equip"));
            ok += TryPatchPawn(harmony, typeof(Pawn_NativeVerbs), "NativeVerbsTick",
                AccessTools.Method(typeof(PawnParallel), "Collect_Native"));
            if (PawnParallel.IncludeHealth)
            {
                ok += TryPatchPawn(harmony, typeof(Pawn_HealthTracker), "HealthTick",
                    AccessTools.Method(typeof(PawnParallel), "Collect_Health"));
            }
            // 结算点：MapPostTick 之后
            MethodInfo post = AccessTools.Method(typeof(Map), "MapPostTick", Type.EmptyTypes);
            if (post != null)
            {
                harmony.Patch(post, null, new HarmonyMethod(finalizer), null, null);
                ok++;
            }
            Log.Message("[RimThreadedTTR] Pawn 并行补丁：" + ok + " 项生效（Health=" + PawnParallel.IncludeHealth
                + " 线程=" + PawnParallel.Workers + "）");
        }

        private static int TryPatchPawn(Harmony harmony, Type type, string method, MethodInfo prefix)
        {
            MethodInfo target = AccessTools.Method(type, method, Type.EmptyTypes);
            if (target == null || prefix == null) return 0;
            harmony.Patch(target, new HarmonyMethod(prefix), null, null, null);
            return 1;
        }

        static TTRCore()
        {
            try
            {
                TTRSettings settings = TTRMod.Instance.settings;
                Harmony harmony = new Harmony("boksu.rimthreadedttr");

                if (settings.threadSafeRand)
                {
                    PatchRand(harmony);
                }
                if (settings.marshalSounds)
                {
                    PatchSounds(harmony);
                }
                ThrottlePatches.Apply(harmony, settings);
                if (settings.parallelPawnTicks && PawnParallel.Init())
                {
                    PawnParallel.Enabled = true;
                    PawnParallel.Workers = settings.MaxThreadsClamped;
                    PawnParallel.IncludeHealth = settings.parallelPawnHealth;
                    PatchPawnParallel(harmony);
                }
                FleckRegistry.RegisterAndPatchAll(harmony);
                // Parallel combat targeting was prototyped and benchmarked here
                // (TargetingPatches). Controlled micro-benchmark on 1.6.4871
                // measured 0.98x - a slight regression - because only the
                // line-of-sight filter can be threaded safely while the
                // expensive friendly-fire scoring cannot (it uses Verb's shared
                // static buffers), and fork-join overhead exceeds the small LOS
                // gain at realistic candidate counts. Left unwired on purpose;
                // see PORTING_NOTES.md.
                PatchDrainHook(harmony);

                Log.Message("[RimThreadedTTR] Initialized. Worker threads: " + settings.MaxThreadsClamped
                    + ", thread-safe Rand: " + settings.threadSafeRand
                    + ", sound marshaling: " + settings.marshalSounds
                    + ", parallel flecks: " + settings.parallelFlecks + ".");
            }
            catch (Exception ex)
            {
                Log.Error("[RimThreadedTTR] Initialization failed: " + ex);
            }
        }

        private static void PatchRand(Harmony harmony)
        {
            Type rand = typeof(Rand);
            Type patches = typeof(RandPatches);

            harmony.Patch(AccessTools.PropertyGetter(rand, "Value"),
                new HarmonyMethod(patches.GetMethod("ValuePrefix")), null, null, null);
            harmony.Patch(AccessTools.PropertyGetter(rand, "Int"),
                new HarmonyMethod(patches.GetMethod("IntPrefix")), null, null, null);
            harmony.Patch(AccessTools.PropertySetter(rand, "Seed"),
                new HarmonyMethod(patches.GetMethod("SeedPrefix")), null, null, null);
            harmony.Patch(AccessTools.Method(rand, "PushState", Type.EmptyTypes),
                new HarmonyMethod(patches.GetMethod("PushStatePrefix")), null, null, null);
            harmony.Patch(AccessTools.Method(rand, "PushState", new Type[] { typeof(int) }),
                new HarmonyMethod(patches.GetMethod("PushStateSeedPrefix")), null, null, null);
            harmony.Patch(AccessTools.Method(rand, "PopState", Type.EmptyTypes),
                new HarmonyMethod(patches.GetMethod("PopStatePrefix")), null, null, null);
        }

        private static void PatchSounds(Harmony harmony)
        {
            Type starter = typeof(SoundStarter);
            Type patches = typeof(SoundPatches);

            harmony.Patch(AccessTools.Method(starter, "PlayOneShot"),
                new HarmonyMethod(patches.GetMethod("PlayOneShotPrefix")), null, null, null);
            harmony.Patch(AccessTools.Method(starter, "PlayOneShotOnCamera"),
                new HarmonyMethod(patches.GetMethod("PlayOneShotOnCameraPrefix")), null, null, null);
        }

        private static void PatchDrainHook(Harmony harmony)
        {
            // Safety net: drain the main-thread queue once per frame during play,
            // in case something was queued outside our own parallel sections.
            harmony.Patch(AccessTools.Method(typeof(TickManager), "TickManagerUpdate"),
                null, new HarmonyMethod(typeof(TTRCore).GetMethod("DrainPostfix")), null, null);
        }

        public static void DrainPostfix()
        {
            MainThreadQueue.Drain();
        }
    }
}
