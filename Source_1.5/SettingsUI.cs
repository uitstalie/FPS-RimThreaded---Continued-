using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FPSPlus
{
    // Settings front-end: five small pages (Main / Interface / Gameplay /
    // Cleanup / Advanced) instead of one giant scroll wall. Everything the
    // player usually needs lives on Main; per-alert and per-worker control
    // stays available under Advanced.
    public static class SettingsUI
    {
        private static int tab;
        private static Vector2 scrollMain;
        private static Vector2 scrollUi;
        private static Vector2 scrollGame;
        private static Vector2 scrollClean;
        private static Vector2 scrollAdv;
        private static Vector2 scrollDoctor;
        private static Vector2 scrollFast;

        private static string[] TabNames
        {
            get
            {
                return new string[]
                {
                    T("FPP_TabMain", "Main"), T("FPP_TabInterface", "Interface"), T("FPP_TabGameplay", "Gameplay"),
                    T("FPP_TabCleanup", "Cleanup"), T("FPP_TabDoctor", "Doctor"), T("FPP_TabAdvanced", "Advanced"),
                    T("FPP_TabFastLoad", "FastLoad")
                };
            }
        }

        private static readonly Color Good = new Color(0.5f, 0.95f, 0.5f);
        private static readonly Color Warn = new Color(1f, 0.85f, 0.4f);
        private static readonly Color Bad = new Color(1f, 0.55f, 0.5f);
        private static readonly Color Dim = new Color(0.72f, 0.72f, 0.72f);

        private static Texture2D bgTex;
        private static bool bgTried;

        // Translation with English fallback: a missing key can never show a
        // broken label, it just shows English.
        internal static string T(string key, string fallback)
        {
            TaggedString result;
            if (key.TryTranslate(out result))
            {
                return result;
            }
            return fallback;
        }

        internal static string TF(string key, string fallback, params object[] args)
        {
            TaggedString result;
            string pattern = key.TryTranslate(out result) ? result.ToString() : fallback;
            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException)
            {
                return string.Format(fallback, args);
            }
        }

        // Starfield backdrop behind the whole settings window. Missing texture
        // (e.g. old install without the Textures folder) just skips it.
        public static void DrawBackground(Rect inRect)
        {
            if (!bgTried)
            {
                bgTried = true;
                bgTex = ContentFinder<Texture2D>.Get("FPSPlus/SettingsBg", false);
                Log.Message("[RimThreadedTTR] FPS+ settings background " + (bgTex != null ? "loaded." : "not found - using plain background."));
            }
            if (bgTex == null)
            {
                return;
            }
            Rect big = inRect.ExpandedBy(6f);
            GUI.DrawTexture(big, bgTex, ScaleMode.ScaleAndCrop);
            Widgets.DrawBoxSolid(big, new Color(0f, 0f, 0f, 0.25f));
        }

        // For the Threading page, which lives in TTRSettings.
        public static void GlassPanelPublic(Rect r)
        {
            GlassPanel(r);
        }

        // Dark translucent panel so text stays readable over the starfield.
        private static void GlassPanel(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.05f, 0.07f, 0.11f, 0.78f));
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawBox(r);
            GUI.color = Color.white;
        }

        public static void Draw(Rect inRect)
        {
            // 合并后 FastLoad 页也必须能打开（即使独立 FPS+ 在跑、FPS+ 设置被让位）
            bool fastLoadTab = tab == TabNames.Length - 1;
            if (FPSPlusInit.StandaloneActive && !fastLoadTab)
            {
                Widgets.Label(inRect, T("FPP_StandaloneActive", "The standalone FPS+ mod is active - configure FPS+ in its own settings. This built-in copy is disabled."));
                return;
            }
            // Raw, not Settings: the page must keep drawing while the master
            // switch is off, or the user could never turn it back on.
            FPSPlusSettings s = FPSPlusMod.Raw;
            if (s == null && !fastLoadTab)
            {
                return;
            }

            float gap = 4f;
            float bw = (inRect.width - gap * (TabNames.Length - 1)) / TabNames.Length;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Rect r = new Rect(inRect.x + i * (bw + gap), inRect.y, bw, 30f);
                Widgets.DrawOptionBackground(r, tab == i);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, TabNames[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(r))
                {
                    tab = i;
                    SoundDefOf.Click.PlayOneShotOnCamera(null);
                }
            }
            Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            if (tab == 0)
            {
                DrawMain(body, s);
            }
            else if (tab == 1)
            {
                DrawInterface(body, s);
            }
            else if (tab == 2)
            {
                DrawGameplay(body, s);
            }
            else if (tab == 3)
            {
                DrawCleanup(body, s);
            }
            else if (tab == 4)
            {
                DrawDoctor(body, s);
            }
            else if (tab == 5)
            {
                DrawAdvanced(body, s);
            }
            else
            {
                DrawFastLoad(body);
            }
        }

        // Used by the self-test to exercise every page without clicks.
        public static void DebugDrawTab(Rect inRect, int which)
        {
            int old = tab;
            tab = which % TabNames.Length;
            DrawBackground(inRect);
            Draw(inRect);
            tab = old;
        }

        // ------------------------------------------------------------------
        // MAIN - master switch, live status, one-click presets, tools
        // ------------------------------------------------------------------
        private static void DrawMain(Rect body, FPSPlusSettings s)
        {
            bool playing = Current.ProgramState == ProgramState.Playing;
            float contentH = playing ? 710f : 640f;
            Rect view = new Rect(0f, 0f, body.width - 20f, contentH);
            Widgets.BeginScrollView(body, ref scrollMain, view);
            float w = view.width;
            float y = 0f;

            // master switch panel
            Rect p1 = new Rect(0f, y, w, 72f);
            GlassPanel(p1);
            Rect cb = new Rect(12f, y + 8f, w - 24f, 30f);
            Text.Font = GameFont.Medium;
            Widgets.CheckboxLabeled(cb, T("FPP_MasterSwitch", "FPS+ master switch"), ref s.masterEnabled);
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(cb, T("FPP_MasterSwitchTip", "Turn EVERYTHING off or on with one click. Useful for testing: if a problem disappears with this off, it came from FPS+. If it stays, it is another mod."));
            int on;
            int total;
            CountFeatures(s, out on, out total);
            if (s.masterEnabled)
            {
                Line(12f, y + 42f, w - 24f, TF("FPP_StatusActive", "ACTIVE - {0} of {1} boosts turned on.", on, total), Good);
            }
            else
            {
                Line(12f, y + 42f, w - 24f, T("FPP_StatusOff", "EVERYTHING OFF - the game runs pure vanilla."), Bad);
            }
            y += 80f;

            // live status panel (with FPS graph while playing)
            float p2h = playing ? 216f : 152f;
            Rect p2 = new Rect(0f, y, w, p2h);
            GlassPanel(p2);
            float ly = y + 8f;
            ly = Line(12f, ly, w - 24f, T("FPP_LiveStatus", "LIVE STATUS"), Dim);
            if (!ConflictGuard.Checked)
            {
                ly = Line(12f, ly, w - 24f, T("FPP_ConflictWait", "Conflict check runs when a game is loaded."), Dim);
            }
            else if (ConflictGuard.Report.Count == 0)
            {
                ly = Line(12f, ly, w - 24f, T("FPP_NoConflicts", "No mod conflicts - every feature is free to run."), Good);
            }
            else
            {
                ly = Line(12f, ly, w - 24f, TF("FPP_ConflictsFound", "{0} feature(s) auto-disabled to avoid a mod conflict - details on the Advanced page.", ConflictGuard.Report.Count), Warn);
            }
            if (playing)
            {
                ly = Line(12f, ly, w - 24f, TF("FPP_AlertCpuRow", "Alert CPU last second: {0} ms (vanilla rate would be ~{1} ms)", AlertPatches.MsLastSecond.ToString("F2"), AlertPatches.EstimatedVanillaMsPerSecond().ToString("F0")), Color.white);
                ly = Line(12f, ly, w - 24f, TF("FPP_AlertSkippedRow", "Alert re-checks skipped: {0}   text cache hits: {1}", AlertPatches.TotalGated + AlertPatches.TotalDeferred, TextCachePatches.Hits), Color.white);
                ly = Line(12f, ly, w - 24f, TF("FPP_JunkStatRow", "Junk despawned: {0}   world pawns removed: {1}", JunkCleaner.TotalCleaned, WorldPawnCleaner.TotalRemoved), Color.white);
                ly = Line(12f, ly, w - 24f, TF("FPP_AutoTuneNow", "Auto-tune boost right now: {0}x", AlertPatches.AutoFactor.ToString("F1")), Color.white);
                PerfHistory.DrawGraph(new Rect(12f, ly + 4f, w - 24f, 54f), PerfHistory.Fps, new Color(0.35f, 0.86f, 0.31f), T("FPP_FpsGraphLabel", "FPS - last 60 seconds"));
            }
            else
            {
                ly = Line(12f, ly, w - 24f, T("FPP_LoadForNumbers", "Load a game to see live numbers here."), Dim);
            }
            y += p2h + 8f;

            // quick setup panel
            Rect p3 = new Rect(0f, y, w, 134f);
            GlassPanel(p3);
            float qy = y + 8f;
            qy = Line(12f, qy, w - 24f, T("FPP_QuickSetup", "QUICK SETUP"), Dim);
            float half = (w - 36f) / 2f;
            Rect bRec = new Rect(12f, qy + 2f, half, 30f);
            if (Widgets.ButtonText(bRec, T("FPP_Recommended", "Recommended")))
            {
                ApplyRecommended(s);
                Note(T("FPP_NoteRecommended", "FPS+ recommended setup applied."));
            }
            TooltipHandler.TipRegion(bRec, T("FPP_RecommendedTip", "Every safe boost ON. Difficulty changers (raid cap, wildlife) and item deleters (chunks, corpses) stay OFF."));
            Rect bUi = new Rect(24f + half, qy + 2f, half, 30f);
            if (Widgets.ButtonText(bUi, T("FPP_UiOnly", "UI only")))
            {
                ApplyUiOnly(s);
                Note(T("FPP_NoteUiOnly", "FPS+ set to UI-only - gameplay is pure vanilla."));
            }
            TooltipHandler.TipRegion(bUi, T("FPP_UiOnlyTip", "Only interface and rendering boosts. Nothing that happens in the game changes at all."));
            GUI.color = Dim;
            Widgets.Label(new Rect(12f, qy + 38f, w - 24f, 66f),
                T("FPP_PresetNote", "Recommended = every boost ON except raid cap, wildlife reduction and chunk/corpse deleting.\nUI only = interface boosts only - 100% vanilla gameplay."));
            GUI.color = Color.white;
            y += 142f;

            // tools panel
            float p4h = playing ? 150f : 112f;
            Rect p4 = new Rect(0f, y, w, p4h);
            GlassPanel(p4);
            float ty = y + 8f;
            ty = Line(12f, ty, w - 24f, T("FPP_Tools", "TOOLS"), Dim);
            Rect bCopy = new Rect(12f, ty + 2f, half, 30f);
            if (Widgets.ButtonText(bCopy, T("FPP_CopyReport", "Copy performance report")))
            {
                GUIUtility.systemCopyBuffer = BuildReport(s);
                Note(T("FPP_NoteReportCopied", "FPS+ report copied - paste it anywhere."));
            }
            TooltipHandler.TipRegion(bCopy, T("FPP_CopyReportTip", "Copies a full diagnostic summary to the clipboard - perfect for bug reports and Workshop comments."));
            Rect bReset = new Rect(24f + half, ty + 2f, half, 30f);
            if (Widgets.ButtonText(bReset, T("FPP_ResetStats", "Reset statistics")))
            {
                AlertPatches.ResetStats();
                GameplayPatches.WorkScansSkipped = 0;
                JunkCleaner.TotalCleaned = 0;
            }
            ty += 36f;
            if (playing)
            {
                Rect bClean = new Rect(12f, ty + 2f, w - 24f, 30f);
                if (Widgets.ButtonText(bClean, T("FPP_CleanWorldPawnsNow", "Clean world pawns now")))
                {
                    int removed = WorldPawnCleaner.Clean(5000);
                    WorldPawnCleaner.TotalRemoved += removed;
                    Note(TF("FPP_NoteRemovedPawns", "[FPS+] removed {0} forgotten world pawns.", removed));
                }
                TooltipHandler.TipRegion(bClean, T("FPP_CleanWorldPawnsTip", "Instantly forget dead strangers your save drags around: never seen by you, no relation to your colonists, no quest, no corpse. Great for old saves."));
                ty += 36f;
                Line(12f, ty + 2f, w - 24f, T("FPP_CleanupNeverTouches", "Cleanup never touches pawns with any link to your colony."), Dim);
            }
            else
            {
                Line(12f, ty + 2f, w - 24f, T("FPP_LoadToUnlock", "Load a game to unlock the world pawn cleanup button."), Dim);
            }
            y += p4h + 10f;

            Line(0f, y, w, "FPS+ | RimThreaded Continued", Dim);

            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // INTERFACE - pure UI/render boosts, never change gameplay
        // ------------------------------------------------------------------
        private static void DrawInterface(Rect body, FPSPlusSettings s)
        {
            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            float contentH = 970f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollUi, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_InterfaceHeader", "Interface boosts"), T("FPP_InterfaceSub", "Pure UI and rendering. These never change what happens in the game."));

            l.CheckboxLabeled(T("FPP_AlertThrottle", "Adaptive alert throttling"), ref s.alertThrottle,
                T("FPP_AlertThrottleTip", "Re-check expensive alerts less often. Cost is measured live per alert; cheap alerts keep the vanilla rate and critical alerts keep a fast lane."));
            l.CheckboxLabeled(T("FPP_InspectCache", "Inspect pane caching"), ref s.inspectCache,
                T("FPP_InspectCacheTip", "Cache the inspect panel text briefly instead of rebuilding it several times every frame while something is selected."));
            l.CheckboxLabeled(T("FPP_HeightCache", "Alert height caching"), ref s.heightCache,
                T("FPP_HeightCacheTip", "Cache text height calculations for alert labels."));
            bool overlayWas = s.overlayCache;
            l.CheckboxLabeled(T("FPP_OverlayCache", "Thing overlay caching"), ref s.overlayCache,
                T("FPP_OverlayCacheTip", "Cache which forbidden/blueprint overlay icons are on screen instead of scanning every item on the map every frame."));
            if (overlayWas && !s.overlayCache)
            {
                UiScanPatches.ClearOverlayCache();
            }
            l.CheckboxLabeled(T("FPP_TooltipNearMouse", "Near-mouse tooltip lookup"), ref s.tooltipNearMouse,
                T("FPP_TooltipNearMouseTip", "Only check things next to the mouse cursor for tooltips instead of scanning every thing on the map every frame."));
            bool textWas = s.textCache;
            l.CheckboxLabeled(T("FPP_TextCache", "Text size caching"), ref s.textCache,
                T("FPP_TextCacheTip", "The game measures the same text sizes hundreds of times per frame across all UI (including other mods). Remember the results instead. Zero visual change."));
            if (textWas && !s.textCache)
            {
                TextCachePatches.ClearCaches();
            }
            l.CheckboxLabeled(T("FPP_SlowSpecialScans", "Slower special alert scans"), ref s.slowSpecialScans,
                T("FPP_SlowSpecialScansTip", "Check for new quest/ideology/scenario alerts once per second instead of three times per second. A new alert appears at most ~0.7s later."));
            bool desWas = s.designationBatch;
            l.CheckboxLabeled(T("FPP_DesignationBatch", "Designation draw batching"), ref s.designationBatch,
                T("FPP_DesignationBatchTip", "Mass-designated icons (haul, chop, hunt...) are drawn in one batch per icon type instead of one draw call each, and the on-screen scan is cached. Hundreds of designations stop costing FPS. Looks identical."));
            if (desWas && !s.designationBatch)
            {
                DesignationPatches.ClearCache();
            }
            l.CheckboxLabeled(T("FPP_ZoomDetail", "Less detail when zoomed out"), ref s.zoomDetail,
                T("FPP_ZoomDetailTip", "Skip dynamic shadows, weather particles and plant swaying while zoomed far out. Purely visual - nothing in the game changes."));
            l.CheckboxLabeled(T("FPP_ParticleCap", "Particle cap in heavy scenes"), ref s.particleCap,
                T("FPP_ParticleCapTip", "Big fights can request hundreds of new effects (smoke, sparks) in one frame - exactly when FPS matters most. This caps how many NEW effects can appear per frame; quiet moments never reach the cap. Visual only."));
            if (s.particleCap)
            {
                l.Label("    " + TF("FPP_ParticleCapMax", "Max new effects per frame: {0}", s.particleCapPerFrame));
                s.particleCapPerFrame = (int)l.Slider((float)s.particleCapPerFrame, 30f, 400f);
            }
            l.CheckboxLabeled(T("FPP_ColonistBarCache", "Colonist bar caching"), ref s.colonistBarCache,
                T("FPP_ColonistBarCacheTip", "The bar with colonist faces rebuilds every status icon (sleeping, attacking, mental state...) every frame for every colonist. This remembers the icons for 1/4 second instead. Looks identical."));
            l.CheckboxLabeled(T("FPP_AfkSaver", "AFK saver"), ref s.afkSaver,
                T("FPP_AfkSaverTip", "When the game window is not focused (you alt-tab away), rendering drops to 15 fps - nobody is watching those frames anyway. Cooler PC, quieter fans. The colony keeps running normally; full speed returns the instant you come back."));

            l.Gap(8f);
            SectionHeader(l, T("FPP_CounterHeader", "On-screen counter"), null);
            l.CheckboxLabeled(T("FPP_ShowFps", "Show FPS"), ref s.showFpsCounter,
                T("FPP_ShowFpsTip", "Small live FPS number on screen while playing."));
            l.CheckboxLabeled(T("FPP_ShowTps", "Show TPS"), ref s.showTpsCounter,
                T("FPP_ShowTpsTip", "Small live TPS number (game ticks per second) on screen while playing. At normal speed a healthy game shows 60."));
            l.CheckboxLabeled(T("FPP_ShowFpsGraph", "Show FPS graph"), ref s.showFpsGraph,
                T("FPP_ShowFpsGraphTip", "Small live graph of your FPS over the last 60 seconds, on screen while playing."));
            l.CheckboxLabeled(T("FPP_ShowTpsGraph", "Show TPS graph"), ref s.showTpsGraph,
                T("FPP_ShowTpsGraphTip", "Small live graph of your TPS over the last 60 seconds, on screen while playing."));
            if (s.showFpsCounter || s.showTpsCounter || s.showFpsGraph || s.showTpsGraph)
            {
                string[] corners = { T("FPP_CornerTL", "Top left"), T("FPP_CornerTR", "Top right"), T("FPP_CornerBL", "Bottom left"), T("FPP_CornerBR", "Bottom right") };
                if (l.ButtonText(T("FPP_Corner", "Corner") + ": " + corners[s.counterCorner]))
                {
                    s.counterCorner = (s.counterCorner + 1) % 4;
                    s.overlayX = -1f; // corner preset overrides a dragged position
                    s.overlayY = -1f;
                }
                GUI.color = Dim;
                l.Label(T("FPP_DragHint", "Tip: you can also DRAG the counter with the mouse to any spot on screen."));
                GUI.color = Color.white;
            }
            l.CheckboxLabeled(T("FPP_QuickButton", "Quick settings button"), ref s.quickSettingsButton,
                T("FPP_QuickButtonTip", "Small gauge icon in the bottom-right icon row. One click opens these settings from inside the game."));

            l.Gap(8f);
            SectionHeader(l, T("FPP_FineTuning", "Fine tuning"), null);
            l.Label(TF("FPP_ThrottleStrength", "Throttle strength: {0}x  (higher = fewer alert re-checks)", s.throttleStrength.ToString("F1")));
            s.throttleStrength = l.Slider(s.throttleStrength, 0.5f, 4f);
            l.CheckboxLabeled(TF("FPP_AutoTune", "Auto-tune alert throttling (current boost: {0}x)", AlertPatches.AutoFactor.ToString("F1")), ref s.autoTuneAlerts,
                T("FPP_AutoTuneTip", "When FPS drops, alerts are re-checked less often (up to 3x your chosen strength). When FPS is healthy, your slider is used as-is. Only changes how often alerts refresh, nothing else."));
            l.Label(TF("FPP_MaxDelay", "Max delay between checks of one alert: {0} frames", s.maxIntervalFrames));
            s.maxIntervalFrames = (int)l.Slider((float)s.maxIntervalFrames, 120f, 1800f);
            l.Label(TF("FPP_InspectTtl", "Inspect cache lifetime: {0} ms", (int)s.inspectTtlMs));
            s.inspectTtlMs = l.Slider(s.inspectTtlMs, 50f, 500f);

            l.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // GAMEPLAY - boosts that can change behavior, each says how
        // ------------------------------------------------------------------
        private static void DrawGameplay(Rect body, FPSPlusSettings s)
        {
            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            float contentH = 700f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollGame, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_GameplayHeader", "Gameplay boosts"), T("FPP_GameplaySub", "These can change game behavior a little - every tooltip says exactly what."));

            l.CheckboxLabeled(T("FPP_AnimalWander", "Animal wander throttling"), ref s.animalWanderThrottle,
                T("FPP_AnimalWanderTip", "Idle animals decide where to wander less often (fewer AI re-thinks and pathfinds). They act the same, just change activity less frequently."));
            if (s.animalWanderThrottle)
            {
                l.Label("    " + TF("FPP_WanderStretch", "Wander stretch: {0}x", s.animalWanderMult));
                s.animalWanderMult = (int)l.Slider((float)s.animalWanderMult, 2f, 8f);
            }
            l.CheckboxLabeled(T("FPP_WorkScan", "Work scan cooldown"), ref s.workScanCooldown,
                T("FPP_WorkScanTip", "A colonist who found no work waits a moment before re-scanning all jobs. Emergencies and your direct orders are never delayed."));
            if (s.workScanCooldown)
            {
                l.Label("    " + TF("FPP_Cooldown", "Cooldown: {0} ticks ({1}s)", s.workScanCooldownTicks, (s.workScanCooldownTicks / 60f).ToString("F1")));
                s.workScanCooldownTicks = (int)l.Slider((float)s.workScanCooldownTicks, 60f, 600f);
            }
            bool haulWas = s.haulStorageMemory;
            l.CheckboxLabeled(T("FPP_StorageMemory", "Storage memory"), ref s.haulStorageMemory,
                T("FPP_StorageMemoryTip", "When an item has nowhere to be stored, remember that for ~4 seconds instead of re-scanning all storage for it non-stop. Resets instantly when you change any stockpile or storage settings. Stops full stockpiles from eating TPS."));
            if (haulWas && !s.haulStorageMemory)
            {
                HaulWealthPatches.ClearStorageMemory();
            }
            l.CheckboxLabeled(T("FPP_WealthStretch", "Wealth recount stretch"), ref s.wealthStretch,
                T("FPP_WealthStretchTip", "The colony wealth recount (a small periodic hiccup on big maps) runs every ~4 in-game hours instead of ~1.4. Raid sizing reacts slightly slower to wealth changes."));
            l.CheckboxLabeled(T("FPP_OffMapSleep", "Off-map pawn sleep"), ref s.offMapSleep,
                T("FPP_OffMapSleepTip", "Off-map pawns (trader stock, mercenary pools, faction NPCs) are allowed to fully sleep even when modded health markers would keep them awake. They wake the moment they matter. Their wounds/diseases pause while asleep."));
            l.CheckboxLabeled(T("FPP_FactionThrottle", "Faction throttle (OFF by default - changes rates)"), ref s.factionThrottle,
                T("FPP_FactionThrottleTip", "Runs the faction system 1-in-2 ticks. Warning: it is NOT a pure timer - Faction.naturalGoodwillTimer is a per-tick counter (rate halved) and KidnappedPawnsTracker uses an odd modulus (%15051) so half of its ransom checks are lost. Off by default; only enable if you accept those rate changes."));
            l.CheckboxLabeled(T("FPP_IdeoThrottle", "Ideology throttle"), ref s.ideoThrottle,
                T("FPP_IdeoThrottleTip", "Belief systems tick every ritual and precept every tick - ideology-heavy saves carry 1000+ of them. This runs the system 1-in-2 ticks. Ritual dates are specially protected: every scheduled ritual still fires, at most 1/60 second late."));
            bool roomWas = s.roomStatCache;
            l.CheckboxLabeled(T("FPP_RoomCache", "Room & beauty caching"), ref s.roomStatCache,
                T("FPP_RoomCacheTip", "Big storage rooms stop being lag bombs: room stats recompute at most every ~4 seconds instead of on every hauled item, and pawn beauty sampling is cached briefly. Mood and room readouts react a few seconds later."));
            if (roomWas && !s.roomStatCache)
            {
                RoomBeautyPatches.ClearCaches();
            }

            l.Gap(8f);
            SectionHeaderColored(l, T("FPP_ExperimentalHeader", "Experimental"), T("FPP_ExperimentalSub", "New and still being battle-tested. OFF by default - turn on only if you want to help test."), Warn);
            l.CheckboxLabeled(T("FPP_ReachCache", "Reach cache (EXPERIMENTAL)"), ref s.reachCache,
                T("FPP_ReachCacheTip", "Pawns ask 'can I walk to X?' constantly - for blocked things the answer is no every time, and each answer costs a map scan. This remembers a NO for 1 second. The cache is wiped instantly when any structure changes, and checks against other pawns are never cached. Worst case: a pawn ignores a newly reachable item for 1 second. Big TPS on maps with unreachable items."));

            l.Gap(8f);
            SectionHeaderColored(l, T("FPP_DifficultyHeader", "Difficulty changers"), T("FPP_DifficultySub", "These make the game easier - kept separate on purpose. OFF is the honest default."), Warn);
            l.CheckboxLabeled(T("FPP_CapRaids", "Cap raid size"), ref s.capRaids,
                T("FPP_CapRaidsTip", "Limits raid strength (threat points). Makes late game easier AND faster - this changes difficulty!"));
            if (s.capRaids)
            {
                l.Label("    " + TF("FPP_MaxThreat", "Max threat points: {0}", (int)s.raidPointsCap));
                s.raidPointsCap = l.Slider(s.raidPointsCap, 1000f, 10000f);
            }
            l.CheckboxLabeled(T("FPP_Wildlife", "Reduce wildlife"), ref s.wildlifeReduce,
                T("FPP_WildlifeTip", "Fewer wild animals spawn on the map. Less hunting available - this changes gameplay!"));
            if (s.wildlifeReduce)
            {
                l.Label("    " + TF("FPP_WildlifeAmount", "Wildlife amount: {0}%", (int)(s.wildlifeMult * 100f)));
                s.wildlifeMult = l.Slider(s.wildlifeMult, 0.1f, 1f);
            }

            l.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // CLEANUP - despawn clutter, forget dead strangers
        // ------------------------------------------------------------------
        private static void DrawCleanup(Rect body, FPSPlusSettings s)
        {
            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            float contentH = 460f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollClean, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_CleanupHeader", "Cleanup"), T("FPP_CleanupSub", "Remove things the game keeps simulating for no reason. Fewer things = faster ticks."));

            l.CheckboxLabeled(T("FPP_JunkCleanup", "Junk cleanup"), ref s.junkCleanup,
                T("FPP_JunkCleanupTip", "Once per in-game hour, despawn clutter. Fewer things on the map = faster everything. Anything in a stockpile or on a shelf is never touched."));
            if (s.junkCleanup)
            {
                l.CheckboxLabeled("    " + T("FPP_CleanFilth", "Clean filth outside home area"), ref s.cleanFilth,
                    T("FPP_CleanFilthTip", "Dirt, blood, vomit etc. outside the home area - nobody would ever mop those."));
                l.CheckboxLabeled("    " + T("FPP_CleanFilthEverywhere", "Clean filth everywhere after ~5 min"), ref s.cleanFilthEverywhere,
                    T("FPP_CleanFilthEverywhereTip", "Blood and dirt inside your base also disappear if they sit for ~5 minutes. Cleaners still mop fresh messes first."));
                l.CheckboxLabeled("    " + T("FPP_CleanChunks", "Clean loose rock chunks after ~5 min"), ref s.cleanChunks,
                    T("FPP_CleanChunksTip", "Chunks not in any stockpile/storage and not designated disappear after ~5 minutes. Store or designate the ones you want to keep!"));
                l.CheckboxLabeled("    " + T("FPP_CleanCorpses", "Clean loose corpses after ~5 min"), ref s.cleanCorpses,
                    T("FPP_CleanCorpsesTip", "Non-colonist corpses not in any storage disappear after ~5 minutes. Never touches colonist/colony animal corpses, quest corpses, stored corpses or anything designated. Strip raiders within the window if you want their gear!"));
            }

            l.Gap(8f);
            l.CheckboxLabeled(T("FPP_WorldPawnCleanup", "World pawn cleanup"), ref s.worldPawnCleanup,
                T("FPP_WorldPawnCleanupTip", "Every 4 days, forget dead strangers your save drags around: never seen by you, no relation to your colonists, no quest, no corpse. Old saves get faster and smaller."));
            if (Current.ProgramState == ProgramState.Playing)
            {
                if (l.ButtonText(T("FPP_CleanWorldPawnsNow", "Clean world pawns now")))
                {
                    int removed = WorldPawnCleaner.Clean(5000);
                    WorldPawnCleaner.TotalRemoved += removed;
                    Note(TF("FPP_NoteRemovedPawns", "[FPS+] removed {0} forgotten world pawns.", removed));
                }
            }
            else
            {
                GUI.color = Dim;
                l.Label(T("FPP_LoadForButton", "Load a game to use the instant world pawn cleanup button (also on the Main page)."));
                GUI.color = Color.white;
            }

            l.Gap(8f);
            GUI.color = Dim;
            l.Label(TF("FPP_JunkSoFar", "Junk despawned so far: {0}   world pawns removed: {1}", JunkCleaner.TotalCleaned, WorldPawnCleaner.TotalRemoved)); 
            GUI.color = Color.white;

            l.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // DOCTOR - scan the save for dead weight, clean it category by category
        // ------------------------------------------------------------------
        private static void DrawDoctor(Rect body, FPSPlusSettings s)
        {
            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            float contentH = 560f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollDoctor, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_DoctorHeader", "Save Doctor"), T("FPP_DoctorSub", "Old saves quietly carry thousands of dead entries. Scan first, then clean what you want. Nothing here runs on its own."));

            if (Current.ProgramState != ProgramState.Playing)
            {
                GUI.color = Dim;
                l.Label(T("FPP_LoadForDoctor", "Load a game to use the Save Doctor."));
                GUI.color = Color.white;
            }
            else
            {
                if (l.ButtonText(T("FPP_ScanNow", "Scan my save now")))
                {
                    SaveDoctor.Scan();
                }
                l.Gap(6f);
                if (!SaveDoctor.Scanned)
                {
                    GUI.color = Dim;
                    l.Label(T("FPP_PressScan", "Press Scan to see what your save is carrying."));
                    GUI.color = Color.white;
                }
                else
                {
                    l.Label(TF("FPP_WorldPawnsRow", "World pawns carried in save: {0} alive, {1} dead", SaveDoctor.WorldPawnsAlive, SaveDoctor.WorldPawnsDead));
                    if (l.ButtonText(T("FPP_CleanStrangers", "Clean forgotten dead strangers")))
                    {
                        int removed = WorldPawnCleaner.Clean(5000);
                        WorldPawnCleaner.TotalRemoved += removed;
                        SaveDoctor.TotalCleaned += removed;
                        Note(TF("FPP_NoteRemovedPawns", "[FPS+] removed {0} forgotten world pawns.", removed));
                        SaveDoctor.Scan();
                    }
                    l.Gap(6f);
                    l.Label(TF("FPP_TalesRow", "Stories (tales): {0} total, {1} old and unused", SaveDoctor.TalesTotal, SaveDoctor.TalesRemovable));
                    if (SaveDoctor.TalesRemovable > 0 && l.ButtonText(T("FPP_CleanTales", "Clean old unused stories")))
                    {
                        int removed = SaveDoctor.CleanTales();
                        Note(TF("FPP_NoteRemovedTales", "[FPS+] removed {0} old unused tales.", removed));
                        SaveDoctor.Scan();
                    }
                    l.Gap(6f);
                    l.Label(TF("FPP_LettersRow", "Letters and messages in history: {0} total, {1} older than 1 year", SaveDoctor.ArchiveTotal, SaveDoctor.ArchiveRemovable));
                    if (SaveDoctor.ArchiveRemovable > 0 && l.ButtonText(T("FPP_CleanLetters", "Clean old letters and messages")))
                    {
                        int removed = SaveDoctor.CleanArchive();
                        Note(TF("FPP_NoteRemovedLetters", "[FPS+] removed {0} old letters/messages.", removed));
                        SaveDoctor.Scan();
                    }
                    l.Gap(6f);
                    l.Label(TF("FPP_QuestsRow", "Quests: {0} total, {1} finished over a year ago", SaveDoctor.QuestsTotal, SaveDoctor.QuestsRemovable));
                    if (SaveDoctor.QuestsRemovable > 0 && l.ButtonText(T("FPP_CleanQuests", "Clean old finished quests")))
                    {
                        int removed = SaveDoctor.CleanQuests();
                        Note(TF("FPP_NoteRemovedQuests", "[FPS+] removed {0} old finished quests.", removed));
                        SaveDoctor.Scan();
                    }
                    l.Gap(6f);
                    l.Label(TF("FPP_FilthRow", "Filth on all maps: {0}", SaveDoctor.FilthTotal));
                    if (SaveDoctor.FilthTotal > 0 && l.ButtonText(T("FPP_CleanFilthNow", "Remove ALL filth now (instant mop)")))
                    {
                        int removed = SaveDoctor.CleanFilth();
                        Note(TF("FPP_NoteRemovedFilth", "[FPS+] removed {0} filth.", removed));
                        SaveDoctor.Scan();
                    }
                    l.Gap(10f);
                    GUI.color = Dim;
                    l.Label(TF("FPP_TotalCleaned", "Total cleaned by Save Doctor: {0}", SaveDoctor.TotalCleaned));
                    GUI.color = Color.white;
                }
            }
            l.Gap(8f);
            GUI.color = Warn;
            l.Label(T("FPP_BackupWarning", "Cleaning changes your save when you next save the game. Keep a backup save - always."));
            GUI.color = Color.white;
            l.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // ADVANCED - conflicts, raw counters, per-alert / per-worker control
        // ------------------------------------------------------------------
        private static void DrawAdvanced(Rect body, FPSPlusSettings s)
        {
            List<AlertRec> rows = SnapshotSorted();
            List<CompRec> allComps = ComponentPatches.SnapshotSorted();
            List<CompRec> compRows = new List<CompRec>();
            for (int ci = 0; ci < allComps.Count && compRows.Count < 15; ci++)
            {
                compRows.Add(allComps[ci]);
            }

            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            float contentH = 420f + ConflictGuard.Report.Count * 24f + compRows.Count * 26f + rows.Count * 26f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollAdv, view);

            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_AdvancedHeader", "Advanced"), T("FPP_AdvancedSub", "Conflict details, raw counters and per-item control."));

            if (ConflictGuard.Checked)
            {
                if (ConflictGuard.Report.Count == 0)
                {
                    GUI.color = Good;
                    l.Label(T("FPP_ConflictNone", "Conflict check: no other mod fights any FPS+ feature - everything active."));
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = Warn;
                    l.Label(TF("FPP_ConflictSome", "Conflict check: {0} feature(s) auto-disabled (another mod patches the same code):", ConflictGuard.Report.Count));
                    for (int i = 0; i < ConflictGuard.Report.Count; i++)
                    {
                        l.Label("    " + ConflictGuard.Report[i]);
                    }
                    GUI.color = Color.white;
                    l.CheckboxLabeled(T("FPP_ForceOn", "Manually force these features ON anyway (default: OFF - we auto-yield to the other mod)"), ref s.ignoreConflictGuard,
                        T("FPP_ForceOnTip", "Default is OFF: when another mod patches the same method we switch only that feature off, so the two never fight over the same code. Tick this to override that and run both anyway - only use it if you know what you are doing."));
                }
            }
            else
            {
                GUI.color = Dim;
                l.Label(T("FPP_ConflictWait", "Conflict check runs when a game is loaded."));
                GUI.color = Color.white;
            }

            l.Gap(8f);
            GUI.color = Dim;
            l.Label(T("FPP_RawCounters", "RAW COUNTERS"));
            GUI.color = Color.white;
            l.Label(TF("FPP_RawAlerts", "Alert re-checks run: {0}   skipped: {1}   inspect cache hits: {2}", AlertPatches.TotalRan, AlertPatches.TotalGated + AlertPatches.TotalDeferred, InspectPatches.Hits));
            l.Label(TF("FPP_RawWork", "Work scans skipped: {0}   junk despawned: {1}   text cache hits: {2}", GameplayPatches.WorkScansSkipped, JunkCleaner.TotalCleaned, TextCachePatches.Hits));
            l.Label(TF("FPP_RawStorage", "Storage lookups skipped: {0}   wealth recounts skipped: {1}   beauty cache hits: {2}", HaulWealthPatches.HaulLookupsSkipped, HaulWealthPatches.WealthRecountsSkipped, RoomBeautyPatches.BeautyCacheHits));
            l.Label(TF("FPP_RawRoom", "Room recomputes skipped: {0}   faction ticks skipped: {1}   forced sleeps: {2}", RoomBeautyPatches.RoomRecomputesSkipped, WorldPatches.FactionTicksSkipped, OffMapSleepPatches.ForcedSleeps));
            l.Label(TF("FPP_RawWorld", "World pawns removed: {0}   batched designation draws: {1}   auto-tune factor: {2}", WorldPawnCleaner.TotalRemoved, DesignationPatches.InstancedDrawCalls, AlertPatches.AutoFactor.ToString("F1") + "x"));

            float y = l.CurHeight + 10f;
            l.End();

            Widgets.Label(new Rect(0f, y, view.width, 24f), T("FPP_WorkersByCost", "Background workers by cost (invisible mod systems) - tick to slow one to 1-in-4 ticks:"));
            y += 26f;
            if (compRows.Count == 0)
            {
                GUI.color = Dim;
                Widgets.Label(new Rect(0f, y, view.width, 26f), T("FPP_NoMeasurements", "No measurements yet - load a game and let it run for a few seconds."));
                GUI.color = Color.white;
                y += 26f;
            }
            for (int i = 0; i < compRows.Count; i++)
            {
                CompRec crec = compRows[i];
                Rect crow = new Rect(0f, y, view.width, 26f);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(crow);
                }
                string cname = crec.shortName + "  [" + crec.modName + "]";
                if (cname.Length > 70)
                {
                    cname = cname.Substring(0, 70);
                }
                if (ComponentPatches.CanThrottle(crec))
                {
                    bool thr = ComponentPatches.IsThrottled(crec);
                    bool thrWas = thr;
                    Widgets.CheckboxLabeled(new Rect(0f, y, view.width - 90f, 26f), cname, ref thr);
                    if (thr != thrWas)
                    {
                        s.throttledComponents[crec.typeName] = thr;
                    }
                }
                else
                {
                    Widgets.Label(new Rect(24f, y, view.width - 114f, 26f), cname);
                }
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(view.width - 88f, y, 84f, 26f), (crec.emaMs * 1000.0).ToString("F0") + " us");
                Text.Anchor = TextAnchor.UpperLeft;
                y += 26f;
            }
            y += 12f;

            Widgets.Label(new Rect(0f, y, view.width, 24f), T("FPP_AlertsByCost", "Alerts by measured cost - untick to disable an alert entirely:"));
            y += 26f;
            if (rows.Count == 0)
            {
                GUI.color = Dim;
                Widgets.Label(new Rect(0f, y, view.width, 30f), T("FPP_NoMeasurements", "No measurements yet - load a game and let it run for a few seconds."));
                GUI.color = Color.white;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                AlertRec rec = rows[i];
                Rect row = new Rect(0f, y, view.width, 26f);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(row);
                }
                bool enabled = !AlertPatches.IsDisabled(rec);
                bool was = enabled;
                string name = rec.shortName;
                if (!rec.lastLabel.NullOrEmpty())
                {
                    name = name + "  (" + rec.lastLabel + ")";
                }
                if (name.Length > 70)
                {
                    name = name.Substring(0, 70);
                }
                Widgets.CheckboxLabeled(new Rect(0f, y, view.width - 90f, 26f), name, ref enabled);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(view.width - 88f, y, 84f, 26f), (rec.emaMs * 1000.0).ToString("F0") + " us");
                Text.Anchor = TextAnchor.UpperLeft;
                if (enabled != was)
                {
                    s.disabledAlerts[rec.typeName] = !enabled;
                    if (enabled)
                    {
                        AlertPatches.ResetScheduleFor(rec.typeName);
                    }
                }
                y += 26f;
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // FASTLOAD - 原 FastLoad mod 的全部开关（合并进本 mod 后仍可在此控制）
        // ------------------------------------------------------------------
        private static void DrawFastLoad(Rect body)
        {
            FastLoad.FastLoadSettings fl = FastLoad.FastLoadMod.Settings;
            GlassPanel(body);
            Rect inner = body.ContractedBy(10f);
            if (fl == null)
            {
                Widgets.Label(inner, T("FPP_FastLoadNotReady", "FastLoad module is not initialized yet. Restart the game."));
                return;
            }
            float contentH = 560f;
            Rect view = new Rect(0f, 0f, inner.width - 20f, contentH);
            Widgets.BeginScrollView(inner, ref scrollFast, view);

            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0f, 0f, view.width, contentH));

            SectionHeader(l, T("FPP_FastLoadHeader", "FastLoad - startup / loading speed and timing"),
                T("FPP_FastLoadSub", "Startup timing report, XPath fast path, static-constructor timing and runtime attribution. The report is written to <config folder>/FastLoad-Startup.txt."));

            GUI.color = Warn;
            l.Label(T("FPP_FastLoadRestartNote", "Timing / attribution / throttle switches are read while the game starts, so they apply after a restart. The last three (forced speed, auto-load, patch audit) apply immediately."));
            GUI.color = Color.white;
            l.Gap(6f);

            l.CheckboxLabeled(T("FPP_FL_ProfileEnabled", "Startup timing report"), ref fl.profileEnabled,
                T("FPP_FL_ProfileEnabledTip", "Time every loading phase and every mod; the report is written to FastLoad-Startup.txt in the config folder (overwritten each run). Turns all startup hooks off when unchecked."));
            l.CheckboxLabeled(T("FPP_FL_XPathFastPath", "XPath fast path (faster XML patching)"), ref fl.xpathFastPath,
                T("FPP_FL_XPathFastPathTip", "Turns Defs/<type>[defName=\"X\"] into an index lookup and caches compiled XPath expressions. Semantically equivalent; turn it off if patching behaves strangely."));
            l.CheckboxLabeled(T("FPP_FL_StaticCtorTiming", "Static-constructor timing (per mod)"), ref fl.staticCtorTiming,
                T("FPP_FL_StaticCtorTimingTip", "Hooks RuntimeHelpers.RunClassConstructor to measure each mod's [StaticConstructorOnStartup] time."));

            l.Gap(8f);
            GUI.color = Dim;
            l.Label(T("FPP_FL_RuntimeHeader", "RUNTIME ATTRIBUTION - main-thread tick chain, accumulated per mod/type"));
            GUI.color = Color.white;
            l.CheckboxLabeled(T("FPP_FL_RuntimeProfiling", "Runtime attribution (enabled)"), ref fl.runtimeProfiling,
                T("FPP_FL_RuntimeProfilingTip", "Hooks TickManager / Map / MapComponent / GameComponent / WorldComponent / JobDriver / JobTracker and accumulates main-thread time per mod and type into the report. Small overhead; turn it off to run at full speed."));
            l.CheckboxLabeled(T("FPP_FL_RuntimeProfilingMinimal", "Minimal TPS meter (only totals + TPS)"), ref fl.runtimeProfilingMinimal,
                T("FPP_FL_RuntimeProfilingMinimalTip", "Only two hooks (DoSingleTick / TickManagerUpdate) - near-zero overhead way to get a measured TPS number for A/B tests."));
            l.CheckboxLabeled(T("FPP_FL_RuntimeProfilingMapPost", "Probe: MapPostTick subsystems"), ref fl.runtimeProfilingMapPost,
                T("FPP_FL_RuntimeProfilingMapPostTip", "Only hooks the four MapPostTick suspects (FireWatcher / MapComponentUtility / TileMutatorWorker / WaterBodyTracker), once per tick."));
            l.CheckboxLabeled(T("FPP_FL_RuntimeProfilingFrames", "Probe: per-frame entries"), ref fl.runtimeProfilingFrames,
                T("FPP_FL_RuntimeProfilingFramesTip", "Hooks the once-per-frame entries (MapUpdate / UI root / colonist bar / alerts / map interface / selector / map drawer) to locate frame time."));
            l.CheckboxLabeled(T("FPP_FL_RuntimeProfilingHotThings", "Hot path: Thing/ThingComp per tick (sampled)"), ref fl.runtimeProfilingHotThings,
                T("FPP_FL_RuntimeProfilingHotThingsTip", "Also hooks ThingWithComps.Tick / Thing.Tick / ThingComp.CompTick*, sampled 1 in 100. Huge call volume - only enable when digging deep."));
            l.Label(TF("FPP_FL_SampleRate", "  Hot-path sample rate (1 = exact, 100 = 1 in 100): {0}", fl.runtimeProfilingSampleRate));
            fl.runtimeProfilingSampleRate = (int)l.Slider((float)fl.runtimeProfilingSampleRate, 1f, 200f);

            l.Gap(8f);
            GUI.color = Dim;
            l.Label(T("FPP_FL_TpsHeader", "TPS OPTIMISATION - automatic throttling"));
            GUI.color = Color.white;
            l.CheckboxLabeled(T("FPP_FL_TpsOptimizeVisual", "Visual throttling (Effecter / pawn effects -> every 2 ticks)"), ref fl.tpsOptimizeVisual,
                T("FPP_FL_TpsOptimizeVisualTip", "Purely visual, no gameplay change. Saves roughly 1.5% of tick time."));
            l.CheckboxLabeled(T("FPP_FL_TpsOptimizeSimulation", "Simulation throttling (wind -> 4 ticks; gas / haulables -> 2 ticks)"), ref fl.tpsOptimizeSimulation,
                T("FPP_FL_TpsOptimizeSimulationTip", "Wind is pure flavour (turbine readouts update slower); gas diffusion and haulable list refresh at half rate - a slight gameplay effect, can be turned off anytime."));
            l.CheckboxLabeled(T("FPP_FL_TpsParallelPawnTick", "Parallel tick pilot (equipment / native verbs)"), ref fl.tpsParallelPawnTick,
                T("FPP_FL_TpsParallelPawnTickTip", "Collects EquipmentTrackerTick / NativeVerbsTick and runs them in parallel at the end of the tick. Measured as a net loss on small batches - keep it off unless benchmarking."));

            l.Gap(8f);
            GUI.color = Dim;
            l.Label(T("FPP_FL_DebugHeader", "TEST / DEBUG HELPERS - applied immediately"));
            GUI.color = Color.white;
            l.CheckboxLabeled(T("FPP_FL_ForceUltrafast", "Force speed level (Superfast / Ultrafast)"), ref fl.forceUltrafast,
                T("FPP_FL_ForceUltrafastTip", "Benchmark helper: forces the time speed to the fastest level once per second so every A/B run uses the same speed. Also lets you test with Smart Speed."));
            l.CheckboxLabeled(T("FPP_FL_AutoLoadSave", "Auto-load a save from the main menu"), ref fl.autoLoadSave,
                T("FPP_FL_AutoLoadSaveTip", "Debug helper: requires the flag file /tmp/ttr-loadsave. Loads the newest save (or the name/path written in that file) automatically. Leave off for normal play."));
            l.CheckboxLabeled(T("FPP_FL_AuditPatches", "Patch audit (find 'sleeping' optimisations)"), ref fl.auditPatches,
                T("FPP_FL_AuditPatchesTip", "Once per session, lists how many methods are patched, by which Harmony owner, and whether each key optimisation target really got patched."));

            l.Gap(10f);
            GUI.color = Dim;
            l.Label(T("FPP_FL_ReportPath", "Report: <config folder>/FastLoad-Startup.txt"));
            GUI.color = Color.white;

            l.End();
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        private static float Line(float x, float y, float w, string text, Color c)
        {
            GUI.color = c;
            Widgets.Label(new Rect(x, y, w, 22f), text);
            GUI.color = Color.white;
            return y + 22f;
        }

        private static void SectionHeader(Listing_Standard l, string title, string sub)
        {
            SectionHeaderColored(l, title, sub, Color.white);
        }

        private static void SectionHeaderColored(Listing_Standard l, string title, string sub, Color c)
        {
            Text.Font = GameFont.Medium;
            GUI.color = c;
            l.Label(title);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (!sub.NullOrEmpty())
            {
                GUI.color = Dim;
                l.Label(sub);
                GUI.color = Color.white;
            }
            l.GapLine(6f);
        }

        private static void CountFeatures(FPSPlusSettings s, out int on, out int total)
        {
            bool[] f =
            {
                s.alertThrottle, s.inspectCache, s.heightCache, s.overlayCache,
                s.tooltipNearMouse, s.textCache, s.slowSpecialScans, s.designationBatch,
                s.zoomDetail, s.animalWanderThrottle, s.junkCleanup, s.workScanCooldown,
                s.haulStorageMemory, s.wealthStretch, s.offMapSleep, s.factionThrottle,
                s.ideoThrottle, s.particleCap, s.afkSaver, s.colonistBarCache,
                s.roomStatCache, s.capRaids, s.wildlifeReduce, s.worldPawnCleanup
            };
            total = f.Length;
            on = 0;
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i])
                {
                    on++;
                }
            }
        }

        // Every safe boost ON; difficulty changers and item deleters OFF.
        private static void ApplyRecommended(FPSPlusSettings s)
        {
            s.masterEnabled = true;
            s.alertThrottle = true;
            s.inspectCache = true;
            s.heightCache = true;
            s.overlayCache = true;
            s.tooltipNearMouse = true;
            s.textCache = true;
            s.slowSpecialScans = true;
            s.designationBatch = true;
            s.zoomDetail = true;
            s.autoTuneAlerts = true;
            s.animalWanderThrottle = true;
            s.junkCleanup = true;
            s.cleanFilth = true;
            s.cleanFilthEverywhere = false;
            s.cleanChunks = false;
            s.cleanCorpses = false;
            s.workScanCooldown = true;
            s.haulStorageMemory = true;
            s.wealthStretch = true;
            s.offMapSleep = true;
            s.factionThrottle = true;
            s.ideoThrottle = true;
            s.particleCap = true;
            s.afkSaver = true;
            s.colonistBarCache = true;
            s.roomStatCache = true;
            s.worldPawnCleanup = true;
            s.capRaids = false;
            s.wildlifeReduce = false;
            s.ignoreConflictGuard = false;
        }

        // Interface boosts only - gameplay stays 100% vanilla.
        private static void ApplyUiOnly(FPSPlusSettings s)
        {
            s.masterEnabled = true;
            s.alertThrottle = true;
            s.inspectCache = true;
            s.heightCache = true;
            s.overlayCache = true;
            s.tooltipNearMouse = true;
            s.textCache = true;
            s.slowSpecialScans = true;
            s.designationBatch = true;
            s.zoomDetail = true;
            s.autoTuneAlerts = true;
            s.animalWanderThrottle = false;
            s.junkCleanup = false;
            s.cleanChunks = false;
            s.cleanCorpses = false;
            s.workScanCooldown = false;
            s.haulStorageMemory = false;
            s.wealthStretch = false;
            s.offMapSleep = false;
            s.factionThrottle = false;
            s.ideoThrottle = false;
            s.particleCap = true;
            s.afkSaver = true;
            s.colonistBarCache = true;
            s.roomStatCache = false;
            s.capRaids = false;
            s.wildlifeReduce = false;
            s.worldPawnCleanup = false;
            s.ignoreConflictGuard = false;
            HaulWealthPatches.ClearStorageMemory();
            RoomBeautyPatches.ClearCaches();
        }

        private static void Note(string msg)
        {
            if (Current.ProgramState == ProgramState.Playing)
            {
                Messages.Message(msg, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        // One-click diagnostic summary the player can paste into a bug report.
        private static string BuildReport(FPSPlusSettings s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== FPS+ | RimThreaded Continued - performance report ===");
            sb.AppendLine("Master switch: " + (s.masterEnabled ? "ON" : "OFF"));
            if (ConflictGuard.Checked)
            {
                if (ConflictGuard.Report.Count == 0)
                {
                    sb.AppendLine("Conflicts: none detected");
                }
                else
                {
                    sb.AppendLine("Conflicts (auto-disabled features):");
                    for (int i = 0; i < ConflictGuard.Report.Count; i++)
                    {
                        sb.AppendLine("  - " + ConflictGuard.Report[i]);
                    }
                }
            }
            else
            {
                sb.AppendLine("Conflicts: not checked yet (load a game)");
            }
            sb.AppendLine("Alerts: " + AlertPatches.DebugSummary());
            sb.AppendLine("Background workers: " + ComponentPatches.DebugSummary());
            sb.AppendLine("Counters: workScansSkipped=" + GameplayPatches.WorkScansSkipped
                + " junkCleaned=" + JunkCleaner.TotalCleaned
                + " textCacheHits=" + TextCachePatches.Hits
                + " inspectHits=" + InspectPatches.Hits
                + " haulLookupsSkipped=" + HaulWealthPatches.HaulLookupsSkipped
                + " wealthRecountsSkipped=" + HaulWealthPatches.WealthRecountsSkipped
                + " beautyCacheHits=" + RoomBeautyPatches.BeautyCacheHits
                + " roomRecomputesSkipped=" + RoomBeautyPatches.RoomRecomputesSkipped
                + " worldPawnsRemoved=" + WorldPawnCleaner.TotalRemoved
                + " forcedSleeps=" + OffMapSleepPatches.ForcedSleeps
                + " factionTicksSkipped=" + WorldPatches.FactionTicksSkipped
                + " ideoTicksSkipped=" + IdeoPatches.IdeoTicksSkipped
                + " flecksCapped=" + ParticleCapPatches.FlecksSkipped
                + " barIconRebuildsSaved=" + ColonistBarPatches.IconRebuildsSaved
                + " reachChecksSkipped=" + ReachCachePatches.ReachChecksSkipped
                + " saveDoctorCleaned=" + SaveDoctor.TotalCleaned
                + " desInstancedCalls=" + DesignationPatches.InstancedDrawCalls);
            sb.AppendLine("Auto-tune factor: " + AlertPatches.AutoFactor.ToString("F1") + "x");
            return sb.ToString();
        }

        // One row per alert type (quest/precept alerts can have many instances of
        // the same type; Alert_Custom quest alerts are throttled but not listed,
        // since disabling them by type would silently kill quest alerts).
        private static List<AlertRec> SnapshotSorted()
        {
            Dictionary<string, AlertRec> byType = new Dictionary<string, AlertRec>();
            foreach (KeyValuePair<Alert, AlertRec> kv in AlertPatches.Recs)
            {
                if (kv.Key is Alert_Custom || kv.Key is Alert_CustomCritical)
                {
                    continue;
                }
                AlertRec r = kv.Value;
                AlertRec agg;
                if (byType.TryGetValue(r.typeName, out agg))
                {
                    if (r.emaMs > agg.emaMs)
                    {
                        agg.emaMs = r.emaMs;
                        agg.lastLabel = r.lastLabel;
                    }
                }
                else
                {
                    agg = new AlertRec();
                    agg.emaMs = r.emaMs;
                    agg.lastLabel = r.lastLabel;
                    agg.typeName = r.typeName;
                    agg.shortName = r.shortName;
                    byType[r.typeName] = agg;
                }
            }
            List<AlertRec> rows = new List<AlertRec>(byType.Values);
            rows.Sort(delegate(AlertRec a, AlertRec b) { return b.emaMs.CompareTo(a.emaMs); });
            return rows;
        }
    }
}
