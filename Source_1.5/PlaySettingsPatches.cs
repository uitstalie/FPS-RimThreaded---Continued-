using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Quick settings button in the bottom-right play settings row (next to
    // home area, auto rebuild...). One click opens the mod settings window.
    // Postfix only adds an icon - stacks safely with other mods' buttons.
    public static class PlaySettingsPatches
    {
        private static Texture2D icon;
        private static bool iconTried;

        public static void DoPlaySettingsGlobalControls_Postfix(WidgetRow row, bool worldView)
        {
            // Raw, not Settings: the button must stay reachable while the
            // master switch is off, so people can turn things back on fast.
            FPSPlusSettings s = FPSPlusMod.Raw;
            if (s == null || !s.quickSettingsButton || row == null)
            {
                return;
            }
            if (!iconTried)
            {
                iconTried = true;
                icon = ContentFinder<Texture2D>.Get("FPSPlus/QuickIcon", false);
            }
            if (icon == null)
            {
                return;
            }
            if (row.ButtonIcon(icon, "FPS+ | RimThreaded settings"))
            {
                try
                {
#if TTR_MERGED
                    Mod mod = LoadedModManager.GetMod(typeof(RimThreadedTTR.TTRMod));
#else
                    Mod mod = LoadedModManager.GetMod(typeof(FPSPlusMod));
#endif
                    if (mod != null)
                    {
                        Find.WindowStack.Add(new Dialog_ModSettings(mod));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[FPS+] could not open settings window: " + ex);
                }
            }
        }
    }
}
