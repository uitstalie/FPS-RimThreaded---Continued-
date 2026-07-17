using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FPSPlus
{
    // Two whole-map per-frame scans replaced with narrow lookups. Both are pure
    // rendering-side: what gets drawn is still computed live every frame, only
    // the "which things are relevant" scan is reduced.
    public static class UiScanPatches
    {
        // ---- ThingOverlays: cache the on-screen overlay set ----
        // Vanilla iterates every HasGUIOverlay thing on the map every Repaint
        // (thousands of forbidden items in a big base) just to find the ones
        // inside the camera rect. We cache that subset and refresh it when the
        // camera rect changes, the source list count changes, or every N frames.
        private static Map overlayMap;
        private static CellRect overlayRect;
        private static int overlaySourceCount = -1;
        private static int overlayCacheFrame = -1000000;
        private static readonly List<Thing> overlayVisible = new List<Thing>();

        public static long OverlayRefreshes;
        public static long OverlayCachedFrames;

        public static bool ThingOverlaysOnGUI_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.overlayCache)
            {
                return true;
            }
            if (ConflictGuard.SuppressOverlay && !ConflictGuard.Ov)
            {
                return true;
            }
            if (Event.current.type != EventType.Repaint)
            {
                return false; // vanilla early-out
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return false;
            }
            CellRect viewRect = Find.CameraDriver.CurrentViewRect;
            List<Thing> src = map.listerThings.ThingsInGroup(ThingRequestGroup.HasGUIOverlay);
            int frame = Time.frameCount;
            bool refresh = map != overlayMap
                || viewRect.minX != overlayRect.minX || viewRect.maxX != overlayRect.maxX
                || viewRect.minZ != overlayRect.minZ || viewRect.maxZ != overlayRect.maxZ
                || src.Count != overlaySourceCount
                || frame - overlayCacheFrame >= s.overlayRefreshFrames;
            if (refresh)
            {
                overlayMap = map;
                overlayRect = viewRect;
                overlaySourceCount = src.Count;
                overlayCacheFrame = frame;
                overlayVisible.Clear();
                for (int i = 0; i < src.Count; i++)
                {
                    Thing t = src[i];
                    if (viewRect.Contains(t.Position) && !map.fogGrid.IsFogged(t.Position))
                    {
                        overlayVisible.Add(t);
                    }
                }
                OverlayRefreshes++;
            }
            else
            {
                OverlayCachedFrames++;
            }
            for (int i = 0; i < overlayVisible.Count; i++)
            {
                Thing t = overlayVisible[i];
                if (t.Destroyed || !t.Spawned || t.Map != map)
                {
                    continue; // despawned since last refresh; will drop out on next one
                }
                try
                {
                    t.DrawGUIOverlay();
                }
                catch (Exception ex)
                {
                    Log.Error("Exception drawing ThingOverlay for " + t + ": " + ex);
                }
            }
            return false;
        }

        public static void ClearOverlayCache()
        {
            overlayMap = null;
            overlayVisible.Clear();
            overlaySourceCount = -1;
        }

        // ---- TooltipGiverList: near-mouse lookup ----
        // Vanilla iterates every tooltip-giving thing on the map every Repaint to
        // find the one under the mouse. The tooltip rect is one cell centered on
        // the thing's DrawPos, so only things registered within one cell of the
        // mouse can ever match - look those up in the thing grid instead.
        private static readonly HashSet<Thing> tooltipSeen = new HashSet<Thing>();

        public static bool DispenseAllThingTooltips_Prefix()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.tooltipNearMouse)
            {
                return true;
            }
            if (ConflictGuard.SuppressTooltip && !ConflictGuard.Ov)
            {
                return true;
            }
            if (Event.current.type != EventType.Repaint
                || Find.WindowStack.FloatMenu != null
                || (Find.Targeter.IsTargeting && Find.Targeter.targetingSource != null && Find.Targeter.targetingSource.HidePawnTooltips))
            {
                return false; // vanilla early-outs
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return false;
            }
            CellRect currentViewRect = Find.CameraDriver.CurrentViewRect;
            float cellSizePixels = Find.CameraDriver.CellSizePixels;
            Vector2 cellSize = new Vector2(cellSizePixels, cellSizePixels);
            Rect rect = new Rect(0f, 0f, cellSize.x, cellSize.y);
            IntVec3 mouseCell = UI.MouseCell();
            tooltipSeen.Clear();
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    IntVec3 c = new IntVec3(mouseCell.x + dx, 0, mouseCell.z + dz);
                    if (!c.InBounds(map))
                    {
                        continue;
                    }
                    List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing thing = things[i];
                        if (!tooltipSeen.Add(thing))
                        {
                            continue;
                        }
                        bool giver = thing.def.hasTooltip || thing is Hive || thing is IAttackTarget;
                        if (!giver)
                        {
                            continue;
                        }
                        if (!currentViewRect.Contains(thing.Position) || thing.Position.Fogged(thing.Map))
                        {
                            continue;
                        }
                        Vector2 ui = thing.DrawPos.MapToUIPosition();
                        rect.x = ui.x - cellSize.x / 2f;
                        rect.y = ui.y - cellSize.y / 2f;
                        if (!rect.Contains(Event.current.mousePosition))
                        {
                            continue;
                        }
                        // giver == vanilla's ShouldShowShotReport for everything
                        // that reaches this point, so always compute the string
                        string shotText = TooltipUtility.ShotCalculationTipString(thing);
                        if (thing.def.hasTooltip || !shotText.NullOrEmpty())
                        {
                            Pawn pawn = thing as Pawn;
                            if (pawn != null && pawn.IsHiddenFromPlayer())
                            {
                                return false; // vanilla breaks the loop here
                            }
                            TipSignal tooltip = thing.GetTooltip();
                            if (!shotText.NullOrEmpty())
                            {
                                tooltip.text = tooltip.text + "\n\n" + shotText;
                            }
                            TooltipHandler.TipRegion(rect, tooltip);
                        }
                    }
                }
            }
            return false;
        }
    }
}
