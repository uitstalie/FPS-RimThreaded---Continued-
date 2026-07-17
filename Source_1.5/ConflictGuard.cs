using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Per-feature conflict auto-guard. After a game loads (when every mod has
    // finished patching), inspect each of our patch targets: if another mod has
    // a prefix or transpiler on the same method, switch off ONLY that feature
    // and report it in the settings UI. Foreign postfixes are allowed - they
    // stack safely. The user can force-override in settings.
    public static class ConflictGuard
    {
        public static bool Checked;

        public static bool SuppressAlerts;        // alert throttle + slower scans
        public static bool SuppressHeight;
        public static bool SuppressInspect;
        public static bool SuppressOverlay;
        public static bool SuppressTooltip;
        public static bool SuppressText;
        public static bool SuppressWander;
        public static bool SuppressWorkScan;
        public static bool SuppressThreat;
        public static bool SuppressDensity;
        public static bool SuppressShadow;
        public static bool SuppressWeather;
        public static bool SuppressSway;
        public static bool SuppressDesignations;
        public static bool SuppressHaulCache;
        public static bool SuppressWealth;
        public static bool SuppressRoomStats;
        public static bool SuppressOffMapSleep;
        public static bool SuppressFactions;
        public static bool SuppressIdeo;
        public static bool SuppressFleckCap;
        public static bool SuppressColonistBar;
        public static bool SuppressReach;
        public static bool SuppressComponents;

        public static readonly List<string> Report = new List<string>();

        // settings override: user forces features on despite detected conflicts
        public static bool Ov
        {
            get
            {
                FPSPlusSettings s = FPSPlusMod.Settings;
                return s != null && s.ignoreConflictGuard;
            }
        }

        public static void RunCheck()
        {
            if (Checked)
            {
                return;
            }
            Checked = true;
            Report.Clear();

            SuppressAlerts = Foreign("alert throttling",
                AccessTools.Method(typeof(AlertsReadout), "CheckAddOrRemoveAlert"),
                AccessTools.Method(typeof(AlertsReadout), "AlertsReadoutUpdate"));
            SuppressHeight = Foreign("alert height cache",
                AccessTools.PropertyGetter(typeof(Alert), "Height"));
            SuppressInspect = Foreign("inspect pane cache",
                AccessTools.Method(typeof(InspectPaneFiller), "DrawInspectStringFor"));
            SuppressOverlay = Foreign("thing overlay cache",
                AccessTools.Method(typeof(ThingOverlays), "ThingOverlaysOnGUI"));
            SuppressTooltip = Foreign("tooltip lookup",
                AccessTools.Method(typeof(TooltipGiverList), "DispenseAllThingTooltips"));
            SuppressText = Foreign("text size cache",
                AccessTools.Method(typeof(Text), "CalcHeight"),
                AccessTools.Method(typeof(Text), "CalcSize"));
            SuppressWander = Foreign("animal wander throttle",
                AccessTools.Method(typeof(Verse.AI.JobGiver_Wander), "TryGiveJob"));
            SuppressWorkScan = Foreign("work scan cooldown",
                AccessTools.Method(typeof(JobGiver_Work), "TryIssueJobPackage"));
            SuppressThreat = Foreign("raid cap",
                AccessTools.Method(typeof(StorytellerUtility), "DefaultThreatPointsNow"));
            SuppressDensity = Foreign("wildlife reduction",
                AccessTools.PropertyGetter(typeof(WildAnimalSpawner), "DesiredAnimalDensity"));
            SuppressShadow = Foreign("zoomed-out shadows",
                AccessTools.Method(typeof(Graphic_Shadow), "DrawWorker"));
            SuppressWeather = Foreign("zoomed-out weather",
                AccessTools.Method(typeof(WeatherManager), "DrawAllWeather"));
            SuppressSway = Foreign("zoomed-out plant sway",
                AccessTools.PropertyGetter(typeof(Prefs), "PlantWindSway"));
            SuppressDesignations = Foreign("designation draw batching",
                AccessTools.Method(typeof(DesignationManager), "DrawDesignations"));
            SuppressHaulCache = Foreign("storage memory",
                AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStorageFor"));
            SuppressWealth = Foreign("wealth recount stretch",
                AccessTools.Method(typeof(WealthWatcher), "RecountIfNeeded"));
            SuppressRoomStats = Foreign("room & beauty cache",
                AccessTools.Method(typeof(BeautyUtility), "AverageBeautyPerceptible"),
                AccessTools.Method(typeof(Room), "UpdateRoomStatsAndRole"));
            SuppressOffMapSleep = Foreign("off-map pawn sleep",
                AccessTools.Method(typeof(RimWorld.Planet.WorldPawns), "ShouldMothball"));
            SuppressFactions = Foreign("faction throttle",
                AccessTools.Method(typeof(FactionManager), "FactionManagerTick"));
            SuppressIdeo = Foreign("ideology throttle",
                AccessTools.Method(typeof(IdeoManager), "IdeoManagerTick"),
                AccessTools.Method(typeof(RitualObligationTrigger_Date), "Tick"));
            SuppressFleckCap = Foreign("particle cap",
                AccessTools.Method(typeof(FleckManager), "CreateFleck"));
            SuppressColonistBar = Foreign("colonist bar cache",
                AccessTools.Method(typeof(ColonistBarColonistDrawer), "DrawIcons"));
            SuppressReach = Foreign("reach cache",
                AccessTools.Method(typeof(Reachability), "CanReach",
                    new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Verse.AI.PathEndMode), typeof(TraverseParms) }));
            SuppressComponents = Foreign("background worker panel",
                AccessTools.Method(typeof(MapComponentUtility), "MapComponentTick"),
                AccessTools.Method(typeof(RimWorld.Planet.WorldComponentUtility), "WorldComponentTick"),
                AccessTools.Method(typeof(GameComponentUtility), "GameComponentTick"));

            if (Report.Count == 0)
            {
                Log.Message("[FPS+] conflict check: no other mod touches our patch targets - all features active.");
            }
            else
            {
                Log.Message("[FPS+] conflict check: " + Report.Count
                    + " feature(s) auto-disabled to avoid fighting another mod:\n  - "
                    + string.Join("\n  - ", Report.ToArray())
                    + "\n  (override available in FPS+ settings)");
            }
        }

        private static bool Foreign(string featureName, params MethodBase[] targets)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                MethodBase target = targets[i];
                if (target == null)
                {
                    continue;
                }
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null)
                {
                    continue;
                }
                string owner = FirstForeignOwner(info.Prefixes);
                if (owner == null)
                {
                    owner = FirstForeignOwner(info.Transpilers);
                }
                if (owner != null)
                {
                    Report.Add(featureName + "  (conflicts with: " + owner + " on " + target.Name + ")");
                    return true;
                }
            }
            return false;
        }

        private static string FirstForeignOwner(IList<Patch> patches)
        {
            if (patches == null)
            {
                return null;
            }
            for (int i = 0; i < patches.Count; i++)
            {
                string owner = patches[i].owner;
                if (owner != "boksu.fpsplus" && owner != "boksu.rimthreadedttr")
                {
                    return owner;
                }
            }
            return null;
        }
    }

    public class ConflictGuardComponent : GameComponent
    {
        public ConflictGuardComponent(Game game)
        {
        }

        public override void FinalizeInit()
        {
            if (FPSPlusInit.StandaloneActive)
            {
                return;
            }
            try
            {
                ConflictGuard.RunCheck();
            }
            catch (Exception ex)
            {
                Log.Warning("[FPS+] conflict check failed (features stay on): " + ex);
            }
        }
    }
}
