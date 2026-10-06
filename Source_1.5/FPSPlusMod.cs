using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    public class FPSPlusSettings : ModSettings
    {
        public bool alertThrottle = true;
        public bool inspectCache = true;
        public bool heightCache = true;
        public bool overlayCache = true;
        public bool tooltipNearMouse = true;
        public int overlayRefreshFrames = 10;
        public bool textCache = true;
        public bool slowSpecialScans = true;

        // gameplay-affecting options (clearly separated in the settings UI)
        public bool animalWanderThrottle = false;
        public int animalWanderMult = 4;
        public bool junkCleanup = false;
        public bool cleanFilth = false;
        public bool cleanFilthEverywhere = false;
        public bool cleanChunks = false;
        public bool cleanCorpses = false;
        public bool workScanCooldown = false;
        public int workScanCooldownTicks = 120;
        public bool capRaids = false;
        public float raidPointsCap = 4000f;
        public bool wildlifeReduce = false;
        public float wildlifeMult = 0.5f;
        public bool zoomDetail = true;
        public bool worldPawnCleanup = false;
        public bool designationBatch = true;
        public bool haulStorageMemory = true;
        public bool wealthStretch = true;
        public bool roomStatCache = true;
        public bool offMapSleep = true;
        public bool componentProfiler = true;
        public bool autoTuneAlerts = true;
        public bool masterEnabled = true;
        // A4 复核：FactionManagerTick/2 **默认关闭**。它不是"只有定时器"——
        //   * Faction.CheckReachNaturalGoodwill 用 `naturalGoodwillTimer++`（阈值 3,000,000）⇒ 速率减半；
        //   * KidnappedPawnsTracker.KidnappedPawnsTrackerTick 用 `TicksGame % 15051 != 0`（15051 是奇数）
        //     ⇒ "只放行偶数 tick"会把一半的赎回判定机会直接丢掉 ⇒ 速率减半。
        // IdeoManagerTick/2 经复核**保留默认开启**：无每 tick 累积量（Precept_Role 只是幂等 Recache，
        // 仪式义务触发里唯一精确 tick 的 RitualObligationTrigger_Date 已由 IdeoPatches 用 2-tick 窗口补偿）。
        public bool factionThrottle = false;
        public bool ideoThrottle = true;
        public bool particleCap = true;
        public int particleCapPerFrame = 150;
        public bool afkSaver = true;
        public bool showFpsCounter = false;
        public bool showTpsCounter = false;
        public int counterCorner = 0; // 0 TL, 1 TR, 2 BL, 3 BR
        public float overlayX = -1f; // custom drag position (screen fraction); -1 = use corner
        public float overlayY = -1f;
        public bool colonistBarCache = true;
        public bool reachCache = true;  // B: 已开启（原为 experimental off）
        public bool showFpsGraph = false;
        public bool showTpsGraph = false;
        public bool quickSettingsButton = true;
        public bool welcomeShown = false;
        public Dictionary<string, bool> throttledComponents = new Dictionary<string, bool>();
        // A2 修复：默认 **false** = 尊重 ConflictGuard 的检测结果，冲突时自动让位（只停用冲突的那一项）。
        // 旧默认值 true 让 ConflictGuard 形同虚设：消费点全是 `if (SuppressX && !Ov)`，
        // 而 Ov 直接返回本字段 ⇒ 默认配置下即使检测到别的 mod 打了同一个方法，功能也永不退让。
        // 想强制打开的用户可在 FPS+ 设置 > Advanced 手动勾选（文案保留"可手动覆盖"说明）。
        public bool ignoreConflictGuard = false;

        public float throttleStrength = 1f;
        public int maxIntervalFrames = 600;
        public int criticalMaxIntervalFrames = 90;
        public float frameBudgetMs = 0.2f;
        public int pausedMinIntervalFrames = 120;
        public float inspectTtlMs = 150f;

        public Dictionary<string, bool> disabledAlerts = new Dictionary<string, bool>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref alertThrottle, "alertThrottle", true);
            Scribe_Values.Look(ref inspectCache, "inspectCache", true);
            Scribe_Values.Look(ref heightCache, "heightCache", true);
            Scribe_Values.Look(ref overlayCache, "overlayCache", true);
            Scribe_Values.Look(ref tooltipNearMouse, "tooltipNearMouse", true);
            Scribe_Values.Look(ref overlayRefreshFrames, "overlayRefreshFrames", 10);
            Scribe_Values.Look(ref textCache, "textCache", true);
            Scribe_Values.Look(ref slowSpecialScans, "slowSpecialScans", true);
            Scribe_Values.Look(ref worldPawnCleanup, "worldPawnCleanup", false);
            Scribe_Values.Look(ref designationBatch, "designationBatch", true);
            Scribe_Values.Look(ref haulStorageMemory, "haulStorageMemory", true);
            Scribe_Values.Look(ref wealthStretch, "wealthStretch", true);
            Scribe_Values.Look(ref roomStatCache, "roomStatCache", true);
            Scribe_Values.Look(ref offMapSleep, "offMapSleep", true);
            Scribe_Values.Look(ref componentProfiler, "componentProfiler", true);
            Scribe_Values.Look(ref autoTuneAlerts, "autoTuneAlerts", true);
            Scribe_Values.Look(ref masterEnabled, "masterEnabled", true);
            Scribe_Values.Look(ref factionThrottle, "factionThrottle", false);
            Scribe_Values.Look(ref ideoThrottle, "ideoThrottle", true);
            Scribe_Values.Look(ref particleCap, "particleCap", true);
            Scribe_Values.Look(ref particleCapPerFrame, "particleCapPerFrame", 150);
            Scribe_Values.Look(ref afkSaver, "afkSaver", true);
            Scribe_Values.Look(ref showFpsCounter, "showFpsCounter", false);
            Scribe_Values.Look(ref showTpsCounter, "showTpsCounter", false);
            Scribe_Values.Look(ref counterCorner, "counterCorner", 0);
            Scribe_Values.Look(ref overlayX, "overlayX", -1f);
            Scribe_Values.Look(ref overlayY, "overlayY", -1f);
            Scribe_Values.Look(ref colonistBarCache, "colonistBarCache", true);
            Scribe_Values.Look(ref reachCache, "reachCache", false);
            Scribe_Values.Look(ref showFpsGraph, "showFpsGraph", false);
            Scribe_Values.Look(ref showTpsGraph, "showTpsGraph", false);
            Scribe_Values.Look(ref quickSettingsButton, "quickSettingsButton", true);
            Scribe_Values.Look(ref welcomeShown, "welcomeShown", false);
            Scribe_Collections.Look(ref throttledComponents, "throttledComponents", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && throttledComponents == null)
            {
                throttledComponents = new Dictionary<string, bool>();
            }
            Scribe_Values.Look(ref ignoreConflictGuard, "ignoreConflictGuard", false);
            Scribe_Values.Look(ref animalWanderThrottle, "animalWanderThrottle", false);
            Scribe_Values.Look(ref animalWanderMult, "animalWanderMult", 4);
            Scribe_Values.Look(ref junkCleanup, "junkCleanup", false);
            Scribe_Values.Look(ref cleanFilth, "cleanFilth", false);
            Scribe_Values.Look(ref cleanFilthEverywhere, "cleanFilthEverywhere", false);
            Scribe_Values.Look(ref cleanChunks, "cleanChunksV2", false);
            Scribe_Values.Look(ref cleanCorpses, "cleanCorpsesV2", false);
            Scribe_Values.Look(ref workScanCooldown, "workScanCooldown", false);
            Scribe_Values.Look(ref workScanCooldownTicks, "workScanCooldownTicks", 120);
            Scribe_Values.Look(ref capRaids, "capRaids", false);
            Scribe_Values.Look(ref raidPointsCap, "raidPointsCap", 4000f);
            Scribe_Values.Look(ref wildlifeReduce, "wildlifeReduce", false);
            Scribe_Values.Look(ref wildlifeMult, "wildlifeMult", 0.5f);
            Scribe_Values.Look(ref zoomDetail, "zoomDetail", true);
            Scribe_Values.Look(ref throttleStrength, "throttleStrength", 1f);
            Scribe_Values.Look(ref maxIntervalFrames, "maxIntervalFrames", 600);
            Scribe_Values.Look(ref criticalMaxIntervalFrames, "criticalMaxIntervalFrames", 90);
            Scribe_Values.Look(ref frameBudgetMs, "frameBudgetMs", 0.2f);
            Scribe_Values.Look(ref pausedMinIntervalFrames, "pausedMinIntervalFrames", 120);
            Scribe_Values.Look(ref inspectTtlMs, "inspectTtlMs", 150f);
            Scribe_Collections.Look(ref disabledAlerts, "disabledAlerts", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disabledAlerts == null)
            {
                disabledAlerts = new Dictionary<string, bool>();
            }
        }
    }

#if TTR_MERGED
    // Built into RimThreaded - Continued: no separate mod entry in the settings
    // menu. TTRMod owns the settings object (stored inside TTRSettings) and
    // shows the FPS+ page as a tab in its own settings window.
    // Settings returns null while the master switch is off, which cleanly
    // disables every feature gate at once (they all null-check Settings).
    public static class FPSPlusMod
    {
        public static FPSPlusSettings Raw;

        public static FPSPlusSettings Settings
        {
            get
            {
                FPSPlusSettings r = Raw;
                if (r == null || !r.masterEnabled)
                {
                    return null;
                }
                return r;
            }
        }
    }
#else
    public class FPSPlusMod : Mod
    {
        public static FPSPlusSettings Raw;

        public static FPSPlusSettings Settings
        {
            get
            {
                FPSPlusSettings r = Raw;
                if (r == null || !r.masterEnabled)
                {
                    return null;
                }
                return r;
            }
        }

        public FPSPlusMod(ModContentPack content) : base(content)
        {
            Raw = GetSettings<FPSPlusSettings>();
        }

        public override string SettingsCategory()
        {
            return "FPS+";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            SettingsUI.Draw(inRect);
        }
    }
#endif

    [StaticConstructorOnStartup]
    public static class FPSPlusInit
    {
        // True only in the copy embedded in RimThreaded - Continued when the
        // standalone FPS+ mod is ALSO active: the standalone wins and this
        // copy goes fully inert (no patches, no components).
        public static bool StandaloneActive;

        static FPSPlusInit()
        {
#if TTR_MERGED
            if (ModLister.GetActiveModWithIdentifier("boksu.fpsplus") != null)
            {
                StandaloneActive = true;
                Log.Message("[RimThreadedTTR] Standalone FPS+ mod detected - built-in FPS+ module disabled to avoid double patching.");
                return;
            }
#endif
            Harmony harmony = new Harmony("boksu.fpsplus");
            int count = 0;

            MethodInfo checkMethod = AccessTools.Method(typeof(AlertsReadout), "CheckAddOrRemoveAlert");
            if (checkMethod != null)
            {
                harmony.Patch(checkMethod, new HarmonyMethod(typeof(AlertPatches), "CheckAddOrRemoveAlert_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ AlertsReadout.CheckAddOrRemoveAlert not found - alert throttling inactive.");
            }

            MethodInfo recalcMethod = AccessTools.Method(typeof(Alert), "Recalculate");
            if (recalcMethod != null)
            {
                harmony.Patch(recalcMethod,
                    new HarmonyMethod(typeof(AlertPatches), "Recalculate_Prefix"),
                    new HarmonyMethod(typeof(AlertPatches), "Recalculate_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Alert.Recalculate not found - alert cost measurement inactive.");
            }

            MethodInfo heightGetter = AccessTools.PropertyGetter(typeof(Alert), "Height");
            if (heightGetter != null)
            {
                harmony.Patch(heightGetter, new HarmonyMethod(typeof(AlertPatches), "Height_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Alert.Height getter not found - height caching inactive.");
            }

            MethodInfo drawInspect = AccessTools.Method(typeof(InspectPaneFiller), "DrawInspectStringFor");
            if (drawInspect != null)
            {
                harmony.Patch(drawInspect, new HarmonyMethod(typeof(InspectPatches), "DrawInspectStringFor_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ InspectPaneFiller.DrawInspectStringFor not found - inspect caching inactive.");
            }

            MethodInfo overlays = AccessTools.Method(typeof(ThingOverlays), "ThingOverlaysOnGUI");
            if (overlays != null)
            {
                harmony.Patch(overlays, new HarmonyMethod(typeof(UiScanPatches), "ThingOverlaysOnGUI_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ ThingOverlays.ThingOverlaysOnGUI not found - overlay caching inactive.");
            }

            MethodInfo tooltips = AccessTools.Method(typeof(TooltipGiverList), "DispenseAllThingTooltips");
            if (tooltips != null)
            {
                harmony.Patch(tooltips, new HarmonyMethod(typeof(UiScanPatches), "DispenseAllThingTooltips_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ TooltipGiverList.DispenseAllThingTooltips not found - tooltip lookup inactive.");
            }

            MethodInfo drawDes = AccessTools.Method(typeof(DesignationManager), "DrawDesignations");
            if (drawDes != null)
            {
                harmony.Patch(drawDes, new HarmonyMethod(typeof(DesignationPatches), "DrawDesignations_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ DesignationManager.DrawDesignations not found - designation batching inactive.");
            }

            MethodInfo storageFor = AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStorageFor");
            if (storageFor != null)
            {
                harmony.Patch(storageFor,
                    new HarmonyMethod(typeof(HaulWealthPatches), "TryFindBestBetterStorageFor_Prefix"),
                    new HarmonyMethod(typeof(HaulWealthPatches), "TryFindBestBetterStorageFor_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ StoreUtility.TryFindBestBetterStorageFor not found - storage memory inactive.");
            }

            // storage memory invalidation hooks (best effort - expiry covers gaps)
            MethodInfo notifyChanged = AccessTools.Method(typeof(StorageSettings), "TryNotifyChanged");
            if (notifyChanged != null)
            {
                harmony.Patch(notifyChanged, null, new HarmonyMethod(typeof(HaulWealthPatches), "StorageChanged_Postfix"), null, null);
                count++;
            }
            MethodInfo zoneAdd = AccessTools.Method(typeof(Zone), "AddCell");
            if (zoneAdd != null)
            {
                harmony.Patch(zoneAdd, null, new HarmonyMethod(typeof(HaulWealthPatches), "StorageChanged_Postfix"), null, null);
                count++;
            }
            MethodInfo zoneRemove = AccessTools.Method(typeof(Zone), "RemoveCell");
            if (zoneRemove != null)
            {
                harmony.Patch(zoneRemove, null, new HarmonyMethod(typeof(HaulWealthPatches), "StorageChanged_Postfix"), null, null);
                count++;
            }

            MethodInfo recount = AccessTools.Method(typeof(WealthWatcher), "RecountIfNeeded");
            if (recount != null)
            {
                harmony.Patch(recount, new HarmonyMethod(typeof(HaulWealthPatches), "RecountIfNeeded_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ WealthWatcher.RecountIfNeeded not found - wealth stretch inactive.");
            }

            MethodInfo beauty = AccessTools.Method(typeof(BeautyUtility), "AverageBeautyPerceptible");
            if (beauty != null)
            {
                harmony.Patch(beauty,
                    new HarmonyMethod(typeof(RoomBeautyPatches), "AverageBeautyPerceptible_Prefix"),
                    new HarmonyMethod(typeof(RoomBeautyPatches), "AverageBeautyPerceptible_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ BeautyUtility.AverageBeautyPerceptible not found - beauty caching inactive.");
            }

            MethodInfo roomStats = AccessTools.Method(typeof(Room), "UpdateRoomStatsAndRole");
            if (roomStats != null)
            {
                harmony.Patch(roomStats, new HarmonyMethod(typeof(RoomBeautyPatches), "UpdateRoomStatsAndRole_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Room.UpdateRoomStatsAndRole not found - room stat caching inactive.");
            }

            MethodInfo mothball = AccessTools.Method(typeof(RimWorld.Planet.WorldPawns), "ShouldMothball");
            if (mothball != null)
            {
                harmony.Patch(mothball, null, new HarmonyMethod(typeof(OffMapSleepPatches), "ShouldMothball_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ WorldPawns.ShouldMothball not found - off-map sleep inactive.");
            }

            MethodInfo factionTick = AccessTools.Method(typeof(FactionManager), "FactionManagerTick");
            if (factionTick != null)
            {
                harmony.Patch(factionTick, new HarmonyMethod(typeof(WorldPatches), "FactionManagerTick_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ FactionManager.FactionManagerTick not found - faction throttle inactive.");
            }

            MethodInfo ideoTick = AccessTools.Method(typeof(IdeoManager), "IdeoManagerTick");
            MethodInfo dateTrigger = AccessTools.Method(typeof(RitualObligationTrigger_Date), "Tick");
            if (ideoTick != null && dateTrigger != null)
            {
                harmony.Patch(ideoTick, new HarmonyMethod(typeof(IdeoPatches), "IdeoManagerTick_Prefix"), null, null, null);
                harmony.Patch(dateTrigger, new HarmonyMethod(typeof(IdeoPatches), "DateTriggerTick_Prefix"), null, null, null);
                count += 2;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ IdeoManagerTick/date trigger not found - ideology throttle inactive.");
            }

            MethodInfo createFleck = AccessTools.Method(typeof(FleckManager), "CreateFleck");
            if (createFleck != null)
            {
                harmony.Patch(createFleck, new HarmonyMethod(typeof(ParticleCapPatches), "CreateFleck_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ FleckManager.CreateFleck not found - particle cap inactive.");
            }

            MethodInfo drawIcons = AccessTools.Method(typeof(ColonistBarColonistDrawer), "DrawIcons");
            if (drawIcons != null)
            {
                harmony.Patch(drawIcons, new HarmonyMethod(typeof(ColonistBarPatches), "DrawIcons_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ ColonistBarColonistDrawer.DrawIcons not found - colonist bar cache inactive.");
            }

            MethodInfo canReach = AccessTools.Method(typeof(Reachability), "CanReach",
                new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Verse.AI.PathEndMode), typeof(TraverseParms) });
            MethodInfo dirtyWalk = AccessTools.Method(typeof(RegionDirtyer), "Notify_WalkabilityChanged",
                new Type[] { typeof(IntVec3), typeof(bool) });
            MethodInfo dirtySpawn = AccessTools.Method(typeof(RegionDirtyer), "Notify_ThingAffectingRegionsSpawned",
                new Type[] { typeof(Thing) });
            MethodInfo dirtyDespawn = AccessTools.Method(typeof(RegionDirtyer), "Notify_ThingAffectingRegionsDespawned",
                new Type[] { typeof(Thing) });
            MethodInfo dirtyAll = AccessTools.Method(typeof(RegionDirtyer), "SetAllDirty", Type.EmptyTypes);
            if (canReach != null && dirtyWalk != null && dirtySpawn != null && dirtyDespawn != null && dirtyAll != null)
            {
                harmony.Patch(canReach, new HarmonyMethod(typeof(ReachCachePatches), "CanReach_Prefix"),
                    new HarmonyMethod(typeof(ReachCachePatches), "CanReach_Postfix"), null, null);
                harmony.Patch(dirtyWalk, null, new HarmonyMethod(typeof(ReachCachePatches), "RegionsChanged_Postfix"), null, null);
                harmony.Patch(dirtySpawn, null, new HarmonyMethod(typeof(ReachCachePatches), "RegionsChanged_Postfix"), null, null);
                harmony.Patch(dirtyDespawn, null, new HarmonyMethod(typeof(ReachCachePatches), "RegionsChanged_Postfix"), null, null);
                harmony.Patch(dirtyAll, null, new HarmonyMethod(typeof(ReachCachePatches), "RegionsChanged_Postfix"), null, null);
                count += 5;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Reachability/RegionDirtyer methods not found - reach cache inactive.");
            }

            MethodInfo playSettings = AccessTools.Method(typeof(PlaySettings), "DoPlaySettingsGlobalControls");
            if (playSettings != null)
            {
                harmony.Patch(playSettings, null, new HarmonyMethod(typeof(PlaySettingsPatches), "DoPlaySettingsGlobalControls_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ PlaySettings.DoPlaySettingsGlobalControls not found - quick settings button inactive.");
            }

            MethodInfo mapCompTick = AccessTools.Method(typeof(MapComponentUtility), "MapComponentTick");
            MethodInfo worldCompTick = AccessTools.Method(typeof(RimWorld.Planet.WorldComponentUtility), "WorldComponentTick");
            MethodInfo gameCompTick = AccessTools.Method(typeof(GameComponentUtility), "GameComponentTick");
            if (mapCompTick != null && worldCompTick != null && gameCompTick != null)
            {
                harmony.Patch(mapCompTick, new HarmonyMethod(typeof(ComponentPatches), "MapComponentTick_Prefix"), null, null, null);
                harmony.Patch(worldCompTick, new HarmonyMethod(typeof(ComponentPatches), "WorldComponentTick_Prefix"), null, null, null);
                harmony.Patch(gameCompTick, new HarmonyMethod(typeof(ComponentPatches), "GameComponentTick_Prefix"), null, null, null);
                count += 3;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ component tick utilities not found - background worker panel inactive.");
            }

            // gameplay-affecting optimizations (each gated by its own setting)
            MethodInfo wander = AccessTools.Method(typeof(Verse.AI.JobGiver_Wander), "TryGiveJob");
            if (wander != null)
            {
                harmony.Patch(wander, null, new HarmonyMethod(typeof(GameplayPatches), "WanderTryGiveJob_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ JobGiver_Wander.TryGiveJob not found - animal wander throttling inactive.");
            }

            MethodInfo workScan = AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage",
                new Type[] { typeof(Pawn), typeof(Verse.AI.JobIssueParams) });
            if (workScan != null)
            {
                harmony.Patch(workScan,
                    new HarmonyMethod(typeof(GameplayPatches), "WorkTryIssueJobPackage_Prefix"),
                    new HarmonyMethod(typeof(GameplayPatches), "WorkTryIssueJobPackage_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ JobGiver_Work.TryIssueJobPackage not found - work scan cooldown inactive.");
            }

            MethodInfo threat = AccessTools.Method(typeof(StorytellerUtility), "DefaultThreatPointsNow");
            if (threat != null)
            {
                harmony.Patch(threat, null, new HarmonyMethod(typeof(GameplayPatches), "ThreatPoints_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ StorytellerUtility.DefaultThreatPointsNow not found - raid cap inactive.");
            }

            MethodInfo density = AccessTools.PropertyGetter(typeof(WildAnimalSpawner), "DesiredAnimalDensity");
            if (density != null)
            {
                harmony.Patch(density, null, new HarmonyMethod(typeof(GameplayPatches), "AnimalDensity_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ WildAnimalSpawner.DesiredAnimalDensity not found - wildlife reduction inactive.");
            }

            MethodInfo shadow = AccessTools.Method(typeof(Graphic_Shadow), "DrawWorker");
            if (shadow != null)
            {
                harmony.Patch(shadow, new HarmonyMethod(typeof(GameplayPatches), "ShadowDrawWorker_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Graphic_Shadow.DrawWorker not found - zoom shadow skip inactive.");
            }

            MethodInfo weather = AccessTools.Method(typeof(WeatherManager), "DrawAllWeather");
            if (weather != null)
            {
                harmony.Patch(weather, new HarmonyMethod(typeof(GameplayPatches), "DrawAllWeather_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ WeatherManager.DrawAllWeather not found - zoom weather skip inactive.");
            }

            MethodInfo sway = AccessTools.PropertyGetter(typeof(Prefs), "PlantWindSway");
            if (sway != null)
            {
                harmony.Patch(sway, new HarmonyMethod(typeof(GameplayPatches), "PlantWindSway_Prefix"), null, null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Prefs.PlantWindSway not found - zoom sway skip inactive.");
            }

            MethodInfo calcHeight = AccessTools.Method(typeof(Text), "CalcHeight");
            if (calcHeight != null)
            {
                harmony.Patch(calcHeight,
                    new HarmonyMethod(typeof(TextCachePatches), "CalcHeight_Prefix"),
                    new HarmonyMethod(typeof(TextCachePatches), "CalcHeight_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Text.CalcHeight not found - text cache inactive.");
            }

            MethodInfo calcSize = AccessTools.Method(typeof(Text), "CalcSize");
            if (calcSize != null)
            {
                harmony.Patch(calcSize,
                    new HarmonyMethod(typeof(TextCachePatches), "CalcSize_Prefix"),
                    new HarmonyMethod(typeof(TextCachePatches), "CalcSize_Postfix"), null, null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ Text.CalcSize not found - text cache inactive.");
            }

            MethodInfo alertsUpdate = AccessTools.Method(typeof(AlertsReadout), "AlertsReadoutUpdate");
            if (alertsUpdate != null)
            {
                harmony.Patch(alertsUpdate, null, null, new HarmonyMethod(typeof(AlertPatches), "AlertsUpdate_Transpiler"), null);
                count++;
            }
            else
            {
                Log.Error("[RimThreadedTTR] FPS+ AlertsReadoutUpdate not found - slow special scans inactive.");
            }

            Log.Message("[RimThreadedTTR] FPS+ module ready, " + count + "/39 methods patched (13 UI-side + 16 gameplay-optional + 7 cache-invalidation hooks + 3 background-worker hooks).");
        }
    }
}
