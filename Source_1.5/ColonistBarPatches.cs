using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // Colonist bar icon cache. Vanilla rebuilds each colonist's status icon
    // list (sleeping, attacking, mental state...) EVERY frame, per colonist -
    // including fresh Translate() lookups for every tooltip, every frame.
    // We rebuild a pawn's list at most every 15 frames (~1/4 second) and just
    // draw the cached list in between. Looks identical; a status icon can
    // appear at most 1/4 second late.
    public static class ColonistBarPatches
    {
        public static long IconRebuildsSaved;

        private struct IconEntry
        {
            public Texture2D tex;
            public string tip;
            public bool hasColor;
            public Color color;
        }

        private class PawnIcons
        {
            public int frame;
            public List<IconEntry> icons = new List<IconEntry>();
        }

        private const int RefreshFrames = 15;

        private static readonly Dictionary<int, PawnIcons> Cache = new Dictionary<int, PawnIcons>();

        // vanilla's private static icon textures, fetched once
        private static readonly Texture2D IconFormingCaravan = Tex("Icon_FormingCaravan");
        private static readonly Texture2D IconMentalStateAggro = Tex("Icon_MentalStateAggro");
        private static readonly Texture2D IconMentalStateNonAggro = Tex("Icon_MentalStateNonAggro");
        private static readonly Texture2D IconMedicalRest = Tex("Icon_MedicalRest");
        private static readonly Texture2D IconSleeping = Tex("Icon_Sleeping");
        private static readonly Texture2D IconFleeing = Tex("Icon_Fleeing");
        private static readonly Texture2D IconAttacking = Tex("Icon_Attacking");
        private static readonly Texture2D IconIdle = Tex("Icon_Idle");
        private static readonly Texture2D IconBurning = Tex("Icon_Burning");
        private static readonly Texture2D IconInspired = Tex("Icon_Inspired");

        // tooltips translated once instead of every frame
        private static string tipFormingCaravan;
        private static string tipMedicalRest;
        private static string tipSleeping;
        private static string tipFleeing;
        private static string tipAttacking;
        private static string tipIdle;
        private static string tipBurning;
        private static bool tipsReady;

        private static Texture2D Tex(string fieldName)
        {
            try
            {
                FieldInfo f = AccessTools.Field(typeof(ColonistBarColonistDrawer), fieldName);
                return f != null ? (Texture2D)f.GetValue(null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void EnsureTips()
        {
            if (tipsReady)
            {
                return;
            }
            tipsReady = true;
            tipFormingCaravan = "ActivityIconFormingCaravan".Translate();
            tipMedicalRest = "ActivityIconMedicalRest".Translate();
            tipSleeping = "ActivityIconSleeping".Translate();
            tipFleeing = "ActivityIconFleeing".Translate();
            tipAttacking = "ActivityIconAttacking".Translate();
            tipIdle = "ActivityIconIdle".Translate();
            tipBurning = "ActivityIconBurning".Translate();
        }

        public static bool DrawIcons_Prefix(Rect rect, Pawn colonist)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.colonistBarCache)
            {
                return true;
            }
            if (ConflictGuard.SuppressColonistBar && !ConflictGuard.Ov)
            {
                return true;
            }
            if (colonist == null || colonist.Dead)
            {
                return false; // vanilla also draws nothing for dead pawns
            }
            if (IconMentalStateAggro == null)
            {
                return true; // texture lookup failed - stay vanilla
            }

            try
            {
                PawnIcons rec;
                if (!Cache.TryGetValue(colonist.thingIDNumber, out rec))
                {
                    if (Cache.Count > 400)
                    {
                        Cache.Clear();
                    }
                    rec = new PawnIcons();
                    rec.frame = -RefreshFrames;
                    Cache[colonist.thingIDNumber] = rec;
                }
                int now = Time.frameCount;
                if (now - rec.frame >= RefreshFrames)
                {
                    rec.frame = now;
                    Rebuild(colonist, rec.icons);
                }
                else
                {
                    IconRebuildsSaved++;
                }

                // draw exactly like vanilla's tail
                if (rec.icons.Count > 0)
                {
                    float size = Mathf.Min(ColonistBarColonistDrawer.PawnTextureSize.x / (float)rec.icons.Count, 20f) * Find.ColonistBar.Scale;
                    Vector2 pos = new Vector2(rect.x + 1f, rect.yMax - size - 1f);
                    for (int i = 0; i < rec.icons.Count; i++)
                    {
                        IconEntry e = rec.icons[i];
                        GUI.color = e.hasColor ? e.color : Color.white;
                        Rect r = new Rect(pos.x, pos.y, size, size);
                        GUI.DrawTexture(r, e.tex);
                        if (e.tip != null)
                        {
                            TooltipHandler.TipRegion(r, e.tip);
                        }
                        pos.x += size;
                    }
                    GUI.color = Color.white;
                }
                return false;
            }
            catch (Exception)
            {
                return true; // any surprise: fall back to vanilla for this frame
            }
        }

        private static void Add(List<IconEntry> list, Texture2D tex, string tip)
        {
            IconEntry e = default(IconEntry);
            e.tex = tex;
            e.tip = tip;
            list.Add(e);
        }

        private static void AddColored(List<IconEntry> list, Texture2D tex, Color color)
        {
            IconEntry e = default(IconEntry);
            e.tex = tex;
            e.hasColor = true;
            e.color = color;
            list.Add(e);
        }

        // Faithful copy of vanilla DrawIcons' decision tree (RimWorld 1.6).
        private static void Rebuild(Pawn colonist, List<IconEntry> list)
        {
            EnsureTips();
            list.Clear();

            bool attacking = false;
            if (colonist.CurJob != null)
            {
                JobDef def = colonist.CurJob.def;
                if (def == JobDefOf.AttackMelee || def == JobDefOf.AttackStatic)
                {
                    attacking = true;
                }
                else if (def == JobDefOf.Wait_Combat)
                {
                    Stance_Busy busy = colonist.stances.curStance as Stance_Busy;
                    if (busy != null && busy.focusTarg.IsValid)
                    {
                        attacking = true;
                    }
                }
            }

            if (colonist.IsFormingCaravan())
            {
                Add(list, IconFormingCaravan, tipFormingCaravan);
            }
            if (colonist.InAggroMentalState)
            {
                Add(list, IconMentalStateAggro, colonist.MentalStateDef.LabelCap);
            }
            else if (colonist.InMentalState)
            {
                Add(list, IconMentalStateNonAggro, colonist.MentalStateDef.LabelCap);
            }
            else if (colonist.InBed() && colonist.CurrentBed().Medical)
            {
                Add(list, IconMedicalRest, tipMedicalRest);
            }
            else if (colonist.CurJob != null && colonist.jobs.curDriver != null && colonist.jobs.curDriver.asleep)
            {
                Add(list, IconSleeping, tipSleeping);
            }
            else if (colonist.GetCaravan() != null && colonist.needs != null && colonist.needs.rest != null && colonist.needs.rest.Resting)
            {
                Add(list, IconSleeping, tipSleeping);
            }
            else if (colonist.CurJob != null && colonist.CurJob.def == JobDefOf.FleeAndCower)
            {
                Add(list, IconFleeing, tipFleeing);
            }
            else if (attacking)
            {
                Add(list, IconAttacking, tipAttacking);
            }
            else if (colonist.mindState.IsIdle && GenDate.DaysPassed >= 1)
            {
                Add(list, IconIdle, tipIdle);
            }

            if (colonist.IsBurning())
            {
                Add(list, IconBurning, tipBurning);
            }
            if (colonist.Inspired)
            {
                Add(list, IconInspired, colonist.InspirationDef.LabelCap);
            }
            if (colonist.IsSlaveOfColony)
            {
                Add(list, colonist.guest.GetIcon(), null);
            }
            else
            {
                bool roleDrawn = false;
                if (colonist.Ideo != null)
                {
                    Ideo ideo = colonist.Ideo;
                    Precept_Role role = ideo.GetRole(colonist);
                    if (role != null)
                    {
                        AddColored(list, role.Icon, ideo.Color);
                        roleDrawn = true;
                    }
                }
                if (!roleDrawn)
                {
                    Faction faction = null;
                    if (colonist.HasExtraMiniFaction((Quest)null))
                    {
                        faction = colonist.GetExtraMiniFaction((Quest)null);
                    }
                    else if (colonist.HasExtraHomeFaction((Quest)null))
                    {
                        faction = colonist.GetExtraHomeFaction((Quest)null);
                    }
                    if (faction != null)
                    {
                        AddColored(list, faction.def.FactionIcon, faction.Color);
                    }
                }
            }
        }
    }
}
