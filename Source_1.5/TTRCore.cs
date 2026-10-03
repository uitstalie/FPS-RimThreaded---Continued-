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
                string stamp = "?";
                try
                {
                    // Assembly.Location 在 RimWorld/Mono 下常为空 ⇒ 退回 mod 目录里的实际 DLL 文件时间
                    string dll = System.IO.Path.Combine(TTRMod.Instance.Content.RootDir, "1.6", "Assemblies", "RimThreadedTTR.dll");
                    if (System.IO.File.Exists(dll)) stamp = System.IO.File.GetLastWriteTime(dll).ToString("MM-dd HH:mm");
                }
                catch { }
                Log.Message("[RimThreadedTTR] TTR BUILD " + stamp + " (assembly mtime, auto)");
                try
            {
                TickListParallel.SkipPawnBuilding = System.IO.File.Exists("/tmp/ttr-skip-pawn");
                if (TickListParallel.SkipPawnBuilding) Log.Warning("[RimThreadedTTR] ⚠ 天花板实验：跳过 Pawn/Building tick（仅测量，游戏状态会被破坏）");
            }
            catch { }
            TTRSettings settings = TTRMod.Instance.settings;
                Harmony harmony = new Harmony("boksu.rimthreadedttr");

                if (settings.threadSafeRand)
                {
                    PatchRand(harmony);
                }
                if (settings.marshalSounds)
                {
                    // S1 第二半：Sustainer 收尾仅主线程执行
                    System.Reflection.MethodInfo sus = AccessTools.Method(typeof(Verse.Sound.SustainerManager), "UpdateAllSustainerScopes", Type.EmptyTypes);
                    if (sus != null)
                    {
                        harmony.Patch(sus, new HarmonyMethod(AccessTools.Method(typeof(SoundPatches), "UpdateAllSustainerScopesPrefix")), null, null, null);
                        Log.Message("[RimThreadedTTR] S1b 音频编组：SustainerManager.UpdateAllSustainerScopes 仅主线程执行");
                    }
                }
                if (false)
                {
                    PatchSounds(harmony);
                }
                ThrottlePatches.Apply(harmony, settings);
                if (settings.probeDoTick) DoTickProbe.Apply(harmony);
                if (settings.parallelTickList && TickListParallel.Init())
                {
                    TickListParallel.Enabled = true;
                    TickListParallel.SettingsDefault = true;
                    TickListParallel.DefaultWorkers = TickListParallel.Workers;
                    TickListParallel.DefaultKeepPawnBuildingSerial = TickListParallel.KeepPawnBuildingSerial;
                    TickListParallel.Workers = settings.MaxThreadsClamped;
                    TickListParallel.MinItems = settings.tickListMinItems;
                    TickListParallel.KeepPawnBuildingSerial = settings.tickListKeepPawnBuildingSerial;
                    MethodInfo target = AccessTools.Method(typeof(TickList), "Tick", Type.EmptyTypes);
                    if (target != null)
                    {
                        harmony.Patch(target,
                            new HarmonyMethod(AccessTools.Method(typeof(TickListParallel), "Tick_Prefix")), null, null, null);
                        Log.Message("[RimThreadedTTR] P2 TickList 并行已启用（切片循环，Pawn/Building 串行="
                            + TickListParallel.KeepPawnBuildingSerial + "）");
                    }
                }

                // P-1 结论（2026-10-02 实测）：**自建轻量派发器被否决**。
                //   LiteParallel 1053.4 µs/次  vs  GenThreading.ParallelFor 26.1 µs/次（512 项空任务·14 线程）
                // 14 个 worker 的事件唤醒风暴约 1 ms/次；而原版派发只要 26 µs ⇒ 无需替换。
                // 保留 LiteParallel.cs 作为记录，默认不启用；后续阶段统一使用 GenThreading.ParallelFor。
                if (settings.parallelPawnTicks && PawnParallel.Init())
                {
                    PawnParallel.Enabled = true;
                    PawnParallel.Workers = settings.MaxThreadsClamped;
                    PawnParallel.IncludeHealth = settings.parallelPawnHealth;
                    PatchPawnParallel(harmony);
                }
                // 设置文件读取路径存在不确定性 ⇒ 同时支持 flag 文件强制开启（与 /tmp/ttr-p2-all 同机制）
                bool hediffFlag = false;
                try { hediffFlag = System.IO.File.Exists("/tmp/ttr-hediff"); } catch { }
                bool regionFlag = false;
                try { regionFlag = System.IO.File.Exists("/tmp/ttr-region"); } catch { }
                if (settings.hediffLock || hediffFlag)
                {
                    HediffLock.Apply(harmony);
                    if (regionFlag) RegionLock.Apply(harmony);   // 热点 #2：区域系统锁
                    MainThreadGuardBypass.Apply(harmony);        // 热点 #3：单线程断言宽松化
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
