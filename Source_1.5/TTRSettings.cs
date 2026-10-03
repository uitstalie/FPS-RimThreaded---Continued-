using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimThreadedTTR
{
    public class TTRSettings : ModSettings
    {
        // -1 means "auto" (processor count - 2, min 1)
        public int maxThreads = -1;  // A: 回到 auto=14（实测 1.207 vs 8 worker 的 1.310 ms/tick）；FPS/TPS 平衡待同会话 A/B
        public int maxThreadsAutoReserve = 0;

        public bool parallelFlecks = true;
        public bool parallelFleckDraw = true;
        public int fleckThreshold = 200;

        // Per-system opt-in for particle systems added by other mods,
        // keyed by system type full name. Missing key = OFF (safe default).
        public Dictionary<string, bool> moddedFleckSystems = new Dictionary<string, bool>();

        public bool IsModdedSystemEnabled(string key)
        {
            bool enabled;
            if (moddedFleckSystems != null && moddedFleckSystems.TryGetValue(key, out enabled))
            {
                return enabled;
            }
            return false;
        }

        public void SetModdedSystemEnabled(string key, bool value)
        {
            if (moddedFleckSystems == null)
            {
                moddedFleckSystems = new Dictionary<string, bool>();
            }
            moddedFleckSystems[key] = value;
        }

        public bool threadSafeRand = true;
        public bool marshalSounds = true;

        public bool parallelTargeting = true;
        public int targetingThreshold = 8;

        // ── 降频（实测约 −200 µs/tick；默认只开"不影响数值"的部分）──
        /// <summary>风（纯风味/涡轮读数）每 4 tick；气体扩散、搬运清单每 2 tick。</summary>
        public bool throttleSimulation = true;
        /// <summary>纯视觉：Effecter 维护与小人特效每 2 tick。</summary>
        public bool throttleVisual = true;
        /// <summary>心情需求每 2 次结算（**改玩法节奏**，默认关）。</summary>
        public bool throttleMood = true;   // 5) 实测 98 µs/tick（改心情节奏，可在设置里关）
        /// <summary>门每 2 tick（实测 46 µs/tick）。</summary>
        public bool throttleDoor = true;
        /// <summary>睡觉小人的 JobDriver 每 2 tick（实测 41 µs/tick）。</summary>
        public bool throttleLayDown = true;
        /// <summary>故事叙述者每 2 tick（实测 55 µs/tick）。</summary>
        public bool throttleStoryteller = true;
        /// <summary>3) Mugirl.CorporateNetwork（GameComponent，单次 40 µs）每 4 tick。</summary>
        public bool throttleMugirl = true;
        /// <summary>6) 野生动物（无派系动物）的 AI/寻路每 2 tick（需求/健康仍每 tick）。</summary>
        public bool throttleWildAnimals = true;
        /// <summary>A：所有小人的 AI（MindState + Pather）每 2 tick（需求/健康仍每 tick）。默认关。</summary>
        public bool throttlePawnAI = false;
        /// <summary>探针：统计每 tick 的 Thing.DoTick 次数（P2 决策用，测量后应关闭）。</summary>
        public bool probeDoTick = false;

        /// <summary>C：实验性 —— 在 Mono 上也给 Fleck 闭合泛型基类打补丁（风险：Mono 原生 abort）。</summary>
        public bool experimentalFleckOnMono = true;   // C: 测试中

        // ── P2：TickList 主循环切片并行 ──
        /// <summary>TickList 主循环循环级并行（零逐调用拦截）。</summary>
        public bool parallelTickList = true;
        /// <summary>小于该数量的小批直接串行（避免小批次负收益）。</summary>
        public int tickListMinItems = 32;
        /// <summary>Pawn/Building 保持串行（改地图风险最高；属 P3）。</summary>
        public bool tickListKeepPawnBuildingSerial = true;

        // ── Pawn 子系统并行 ──
        /// <summary>并行结算每小人的 Equipment/NativeVerbs 等自包含子系统（每 tick 单次大批量派发）。</summary>
        public bool parallelPawnTicks = false;
        /// <summary>是否把 HealthTick 也并行（**损伤可致死会改地图**，默认关）。</summary>
        public bool parallelPawnHealth = false;

#if TTR_MERGED
        // v1.2: the FPS+ module's settings live inside this mod's settings,
        // persisted in the same file. One mod, one settings entry.
        public FPSPlus.FPSPlusSettings fpsSettings = new FPSPlus.FPSPlusSettings();
#endif

        public int MaxThreadsClamped
        {
            get
            {
                if (maxThreads <= 0)
                {
                    // 用 **P 核线程数**（不是逻辑核总数）：混核 CPU（如 i7-14700K：16 P 线程 + 12 E 线程）
                    // 上多出来的 E 核慢约 1.7 倍，把它们拉进 worker 只会让最慢的那条决定整批耗时。
                    return Math.Max(1, Math.Min(TTRPlatform.PerformanceThreadCount - 2, 16));
                }
                return Math.Max(1, Math.Min(maxThreads, 64));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref maxThreads, "maxThreads", -1);
            Scribe_Values.Look(ref parallelFlecks, "parallelFlecks", true);
            Scribe_Values.Look(ref parallelFleckDraw, "parallelFleckDraw", true);
            Scribe_Values.Look(ref fleckThreshold, "fleckThreshold", 200);
            Scribe_Collections.Look(ref moddedFleckSystems, "moddedFleckSystems", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && moddedFleckSystems == null)
            {
                moddedFleckSystems = new Dictionary<string, bool>();
            }
            Scribe_Values.Look(ref threadSafeRand, "threadSafeRand", true);
            Scribe_Values.Look(ref marshalSounds, "marshalSounds", true);
            Scribe_Values.Look(ref parallelTargeting, "parallelTargeting", true);
            Scribe_Values.Look(ref throttleSimulation, "throttleSimulation", true);
            Scribe_Values.Look(ref throttleVisual, "throttleVisual", true);
            Scribe_Values.Look(ref throttleMood, "throttleMood", true);
            Scribe_Values.Look(ref throttleDoor, "throttleDoor", true);
            Scribe_Values.Look(ref throttleLayDown, "throttleLayDown", true);
            Scribe_Values.Look(ref throttleStoryteller, "throttleStoryteller", true);
            Scribe_Values.Look(ref throttleMugirl, "throttleMugirl", true);
            Scribe_Values.Look(ref throttleWildAnimals, "throttleWildAnimals", true);
            Scribe_Values.Look(ref throttlePawnAI, "throttlePawnAI", false);
            Scribe_Values.Look(ref parallelPawnTicks, "parallelPawnTicks", false);
            Scribe_Values.Look(ref parallelPawnHealth, "parallelPawnHealth", false);
            Scribe_Values.Look(ref targetingThreshold, "targetingThreshold", 8);
#if TTR_MERGED
            if (fpsSettings == null)
            {
                fpsSettings = new FPSPlus.FPSPlusSettings();
            }
            fpsSettings.ExposeData();
#endif
        }
    }

    public class TTRMod : Mod
    {
        public static TTRMod Instance;
        public TTRSettings settings;

        public TTRMod(ModContentPack content)
            : base(content)
        {
            Instance = this;
            settings = GetSettings<TTRSettings>();
#if TTR_MERGED
            // hand the embedded FPS+ module its settings object
            FPSPlus.FPSPlusMod.Raw = settings.fpsSettings;
#endif
        }

        public override string SettingsCategory()
        {
            return "FPS+ | RimThreaded";
        }

#if TTR_MERGED
        private static int settingsTab; // 0 = threading, 1 = FPS+
#endif

        public override void DoSettingsWindowContents(Rect inRect)
        {
#if TTR_MERGED
            FPSPlus.SettingsUI.DrawBackground(inRect);
            string[] topTabs = { "Threading", "FPS+ (performance)" };
            for (int i = 0; i < topTabs.Length; i++)
            {
                Rect tr = new Rect(inRect.x + i * 230f, inRect.y, 224f, 30f);
                Widgets.DrawOptionBackground(tr, settingsTab == i);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(tr, topTabs[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(tr))
                {
                    settingsTab = i;
                }
            }
            Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            if (settingsTab == 1)
            {
                FPSPlus.SettingsUI.Draw(body);
                base.DoSettingsWindowContents(inRect);
                return;
            }
            FPSPlus.SettingsUI.GlassPanelPublic(body);
            inRect = body.ContractedBy(10f);
#endif
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("Worker threads: " + (settings.maxThreads <= 0 ? ("Auto (" + settings.MaxThreadsClamped + ")") : settings.maxThreads.ToString()));
            settings.maxThreads = (int)listing.Slider(settings.maxThreads <= 0 ? 0f : (float)settings.maxThreads, 0f, 16f);
            if (settings.maxThreads == 0)
            {
                settings.maxThreads = -1;
            }
            listing.GapLine();

            listing.CheckboxLabeled("Parallel fleck simulation", ref settings.parallelFlecks,
                "Simulate visual particles (flecks: rain splashes, smoke, sparks...) on multiple threads when there are many of them.");
            listing.CheckboxLabeled("Parallel fleck drawing", ref settings.parallelFleckDraw,
                "Draw static particles (rain splashes, impacts...) on multiple threads. Vanilla already does this for thrown particles like smoke; this extends it to the rest.");
            if (settings.parallelFlecks || settings.parallelFleckDraw)
            {
                listing.Label("  Minimum fleck count before going parallel: " + settings.fleckThreshold);
                settings.fleckThreshold = (int)listing.Slider((float)settings.fleckThreshold, 50f, 2000f);

                listing.Gap();
                listing.Label("Particle systems from other mods (OFF by default - enable one by one, at your own risk):");
                if (FleckRegistry.moddedSystems.Count == 0)
                {
                    listing.Label("  (none detected in your mod list)");
                }
                else
                {
                    for (int i = 0; i < FleckRegistry.moddedSystems.Count; i++)
                    {
                        ModdedFleckSystemEntry entry = FleckRegistry.moddedSystems[i];
                        bool enabled = settings.IsModdedSystemEnabled(entry.settingsKey);
                        bool before = enabled;
                        listing.CheckboxLabeled("  " + entry.label, ref enabled,
                            "Run this mod's particles in parallel too. Its code was not written for threading - if problems appear, turn this off. Applies instantly, no restart needed.");
                        if (enabled != before)
                        {
                            settings.SetModdedSystemEnabled(entry.settingsKey, enabled);
                        }
                    }
                }
            }
            listing.GapLine();

            listing.CheckboxLabeled("Thread-safe random numbers (restart required)", ref settings.threadSafeRand,
                "Gives every background thread its own random number stream so parallel code cannot corrupt the game's main random state. Needed by parallel flecks; also protects other mods that use background threads.");
            listing.CheckboxLabeled("Redirect off-thread sounds to main thread (restart required)", ref settings.marshalSounds,
                "If any code tries to play a sound from a background thread, queue it to play safely on the main thread instead of crashing Unity's audio.");

            listing.GapLine();
            listing.Label("Changes to the last two options apply after restarting the game.");

            listing.GapLine();
            listing.Label("— TickList 并行（P2，我们加的）—");
            listing.CheckboxLabeled("TickList 主循环切片并行", ref settings.parallelTickList,
                "把 TickList.Tick 的主循环切片并行（实测同会话 +23% TPS）。只改并行度，不改 tick 速率。");
            listing.Label("  最小批大小（小于此值走串行）: " + settings.tickListMinItems);
            settings.tickListMinItems = (int)listing.Slider((float)settings.tickListMinItems, 8f, 512f);
            listing.CheckboxLabeled("Pawn/Building 保持串行（推荐）", ref settings.tickListKeepPawnBuildingSerial,
                "取消勾选 = 让 Pawn/Building 也并行。实测会立刻出现 Collection was modified（需 RimThreaded 级线程安全改造）。");
            listing.Label("  本会话：批次 " + TickListParallel.Batches + " · 对象 " + TickListParallel.Items
                + (TickListParallel.DisabledByErrors ? " · ⚠ 已因错误自动停用" : ""));

            listing.GapLine();
            listing.Label("— 降频（Performance-Optimizer 风格）—");
            listing.CheckboxLabeled("模拟类：风 /4 · 气体 /2 · 搬运清单 /2", ref settings.throttleSimulation,
                "实测省约 190 µs/tick。风是纯风味；气体扩散与搬运清单刷新频率减半（轻微玩法影响）。");
            listing.CheckboxLabeled("纯视觉：Effecter / 小人特效 每 2 tick", ref settings.throttleVisual,
                "实测省约 19 µs/tick，不影响数值。");
            listing.CheckboxLabeled("心情需求每 2 次结算（改玩法节奏，默认关）", ref settings.throttleMood,
                "省约 47 µs/tick，但会改变心情变化节奏。");

            listing.GapLine();
            listing.CheckboxLabeled("实验：Mono 上也启用 Fleck 闭合泛型补丁", ref settings.experimentalFleckOnMono,
                "实测在 Linux/Mono 上可用（12 方法已打、真实存档 11 分钟 0 崩溃），理论上仍有 Mono 原生终止风险。");

            listing.GapLine();
            string simStatus = FleckRegistry.runtimeDisabled ? "OFF (safety switch)" : "OK";
            string drawStatus = FleckRegistry.drawRuntimeDisabled ? "OFF (safety switch)" : "OK";
            listing.Label("This session: " + FleckRegistry.parallelRunCount + " parallel simulation batches ("
                + simStatus + "), " + FleckRegistry.drawParallelRunCount + " parallel draw batches (" + drawStatus + ").");
            listing.Label("Counters grow during storms, fires and big fights - that is the mod working.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
