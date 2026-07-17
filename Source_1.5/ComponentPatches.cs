using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    // "TPS thieves" panel. Every mod can install invisible background workers
    // (map/world/game components) that run EVERY tick. Nobody sees what they
    // cost. We take over the three loops that run them, stopwatch each worker,
    // show a live cost list in settings, and let the player throttle any of
    // them to 1-in-4 ticks with a checkbox (all OFF by default - measuring is
    // free, throttling is the player's informed choice).
    public class CompRec
    {
        public double emaMs;
        public long runs;
        public string typeName;
        public string shortName;
        public string modName;
    }

    public static class ComponentPatches
    {
        public static readonly Dictionary<Type, CompRec> Recs = new Dictionary<Type, CompRec>();
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        public static long ThrottleSkips;

        private static CompRec RecFor(Type t)
        {
            CompRec rec;
            if (!Recs.TryGetValue(t, out rec))
            {
                rec = new CompRec();
                rec.typeName = t.FullName;
                rec.shortName = t.Name;
                try
                {
                    rec.modName = t.Assembly.GetName().Name;
                }
                catch (Exception)
                {
                    rec.modName = "?";
                }
                Recs[t] = rec;
            }
            return rec;
        }

        public static bool IsThrottled(CompRec rec)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            bool on;
            if (s != null && s.throttledComponents != null && s.throttledComponents.TryGetValue(rec.typeName, out on))
            {
                return on;
            }
            return false;
        }

        // our own components must never be throttleable
        public static bool CanThrottle(CompRec rec)
        {
            return !rec.typeName.StartsWith("FPSPlus") && !rec.typeName.StartsWith("RimThreadedTTR");
        }

        private static bool ShouldSkip(Type t, CompRec rec)
        {
            if (!IsThrottled(rec) || !CanThrottle(rec))
            {
                return false;
            }
            // throttled: run once every 4 ticks, offset by type so work spreads
            if ((Find.TickManager.TicksGame + (t.GetHashCode() & 0x7FFFFFFF)) % 4 == 0)
            {
                return false;
            }
            ThrottleSkips++;
            return true;
        }

        private static bool Active()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.componentProfiler)
            {
                return false;
            }
            if (ConflictGuard.SuppressComponents && !ConflictGuard.Ov)
            {
                return false;
            }
            return true;
        }

        public static bool MapComponentTick_Prefix(Map map)
        {
            if (!Active())
            {
                return true;
            }
            List<MapComponent> components = map.components;
            for (int i = 0; i < components.Count; i++)
            {
                MapComponent c = components[i];
                Type t = c.GetType();
                CompRec rec = RecFor(t);
                if (ShouldSkip(t, rec))
                {
                    continue;
                }
                long start = Stopwatch.GetTimestamp();
                try
                {
                    c.MapComponentTick();
                }
                catch (Exception ex)
                {
                    Log.Error(ex.ToString());
                }
                Record(rec, start);
            }
            return false;
        }

        public static bool WorldComponentTick_Prefix(World world)
        {
            if (!Active())
            {
                return true;
            }
            List<WorldComponent> components = world.components;
            for (int i = 0; i < components.Count; i++)
            {
                WorldComponent c = components[i];
                Type t = c.GetType();
                CompRec rec = RecFor(t);
                if (ShouldSkip(t, rec))
                {
                    continue;
                }
                long start = Stopwatch.GetTimestamp();
                try
                {
                    c.WorldComponentTick();
                }
                catch (Exception ex)
                {
                    Log.Error(ex.ToString());
                }
                Record(rec, start);
            }
            return false;
        }

        public static bool GameComponentTick_Prefix()
        {
            if (!Active())
            {
                return true;
            }
            Game game = Current.Game;
            if (game == null)
            {
                return true;
            }
            List<GameComponent> components = game.components;
            for (int i = 0; i < components.Count; i++)
            {
                GameComponent c = components[i];
                Type t = c.GetType();
                CompRec rec = RecFor(t);
                if (ShouldSkip(t, rec))
                {
                    continue;
                }
                long start = Stopwatch.GetTimestamp();
                try
                {
                    c.GameComponentTick();
                }
                catch (Exception ex)
                {
                    Log.Error(ex.ToString());
                }
                Record(rec, start);
            }
            return false;
        }

        private static void Record(CompRec rec, long start)
        {
            double ms = (Stopwatch.GetTimestamp() - start) * MsPerTick;
            rec.runs++;
            if (rec.runs == 1)
            {
                rec.emaMs = ms;
            }
            else
            {
                rec.emaMs = rec.emaMs * 0.95 + ms * 0.05;
            }
        }

        public static List<CompRec> SnapshotSorted()
        {
            List<CompRec> rows = new List<CompRec>(Recs.Count);
            foreach (KeyValuePair<Type, CompRec> kv in Recs)
            {
                rows.Add(kv.Value);
            }
            rows.Sort(delegate(CompRec a, CompRec b) { return b.emaMs.CompareTo(a.emaMs); });
            return rows;
        }

        public static string DebugSummary()
        {
            List<CompRec> rows = SnapshotSorted();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("components=").Append(rows.Count).Append(" top=[");
            int n = Math.Min(5, rows.Count);
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(rows[i].shortName).Append(":").Append((rows[i].emaMs * 1000.0).ToString("F0")).Append("us");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
