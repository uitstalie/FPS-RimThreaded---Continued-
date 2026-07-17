using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Replaces InspectPaneFiller.DrawInspectStringFor with a cached version.
    // Vanilla rebuilds the inspect text (sel.GetInspectString(), often 0.5-2 ms on
    // modded pawns) on every GUI pass - several times per frame while anything is
    // selected. We reuse the composed text for a short TTL instead.
    public static class InspectPatches
    {
        private static ISelectable cachedSel;
        private static string cachedText;
        private static float cachedAt = -1000f;
        private static bool exceptionErrored;

        public static long Computes;
        public static long Hits;

        public static bool DrawInspectStringFor_Prefix(ISelectable sel, Rect rect)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.inspectCache || sel == null)
            {
                return true;
            }
            if (ConflictGuard.SuppressInspect && !ConflictGuard.Ov)
            {
                return true;
            }
            float now = Time.realtimeSinceStartup;
            if (object.ReferenceEquals(sel, cachedSel) && cachedText != null && (now - cachedAt) * 1000f < s.inspectTtlMs)
            {
                Hits++;
                InspectPaneFiller.DrawInspectString(cachedText, rect);
                return false;
            }
            string text;
            try
            {
                text = sel.GetInspectString();
                Thing thing = sel as Thing;
                if (thing != null)
                {
                    string low = thing.GetInspectStringLowPriority();
                    if (!low.NullOrEmpty())
                    {
                        if (!text.NullOrEmpty())
                        {
                            text = text.TrimEndNewlines() + "\n";
                        }
                        text += low;
                    }
                }
            }
            catch (Exception ex)
            {
                text = string.Concat("GetInspectString exception on ", sel.ToString(), ":\n", ex);
                if (!exceptionErrored)
                {
                    Log.Error(text);
                    exceptionErrored = true;
                }
            }
            if (!text.NullOrEmpty() && GenText.ContainsEmptyLines(text))
            {
                Log.ErrorOnce(string.Concat("Inspect string for ", sel.ToString(), " contains empty lines.\n\nSTART\n", text, "\nEND"), 837163521);
            }
            cachedSel = sel;
            cachedText = text;
            cachedAt = now;
            Computes++;
            InspectPaneFiller.DrawInspectString(text, rect);
            return false;
        }
    }
}
