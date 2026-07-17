using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FPSPlus
{
    public class AlertRec
    {
        public double emaMs;
        public long count;
        public int lastRecalcFrame = -1000000;
        public int lastSeenFrame;
        public string lastLabel = "";
        public string typeName;
        public string shortName;
    }

    public static class AlertPatches
    {
        public static readonly Dictionary<Alert, AlertRec> Recs = new Dictionary<Alert, AlertRec>();

        private static readonly FieldInfo ActiveAlertsField = AccessTools.Field(typeof(AlertsReadout), "activeAlerts");

        private static readonly double MsPerTimestampTick = 1000.0 / Stopwatch.Frequency;

        // an alert at or below this EMA cost keeps the vanilla 24-frame cycle
        private const double CheapMs = 0.02;

        // per-frame recalc budget tracking
        private static int budgetFrame = -1;
        private static double budgetUsedMs;

        // rolling per-second stats
        private static int statSecond = -1;
        private static double msAccum;
        private static long ranAccum;
        public static double MsLastSecond;
        public static long RanLastSecond;

        // lifetime counters
        public static long TotalRan;
        public static long TotalGated;
        public static long TotalDeferred;
        public static long TotalDisabledSkips;

        public static AlertRec GetRec(Alert alert)
        {
            AlertRec rec;
            if (!Recs.TryGetValue(alert, out rec))
            {
                if (Recs.Count > 2000)
                {
                    PruneStale();
                }
                rec = new AlertRec();
                Type t = alert.GetType();
                rec.typeName = t.FullName;
                rec.shortName = t.Name;
                Recs[alert] = rec;
            }
            return rec;
        }

        private static void PruneStale()
        {
            int frame = Time.frameCount;
            List<Alert> stale = new List<Alert>();
            foreach (KeyValuePair<Alert, AlertRec> kv in Recs)
            {
                if (frame - kv.Value.lastSeenFrame > 20000)
                {
                    stale.Add(kv.Key);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                Recs.Remove(stale[i]);
            }
        }

        public static bool IsDisabled(AlertRec rec)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            bool disabled;
            if (s != null && s.disabledAlerts != null && s.disabledAlerts.TryGetValue(rec.typeName, out disabled))
            {
                return disabled;
            }
            return false;
        }

        public static void ResetScheduleFor(string typeName)
        {
            foreach (KeyValuePair<Alert, AlertRec> kv in Recs)
            {
                if (kv.Value.typeName == typeName)
                {
                    kv.Value.lastRecalcFrame = -1000000;
                }
            }
        }

        // Auto-tuning: when FPS drops, throttle alerts harder (up to 3x the
        // user's chosen strength); when FPS is healthy, use the slider as-is.
        // Bounded and only affects re-check frequency - never correctness.
        private static float fpsEma = 60f;
        private static int lastFpsFrame = -1;

        public static float AutoFactor
        {
            get
            {
                int f = Time.frameCount;
                if (f != lastFpsFrame)
                {
                    lastFpsFrame = f;
                    float dt = Time.unscaledDeltaTime;
                    if (dt > 0.0001f && dt < 1f)
                    {
                        fpsEma = fpsEma * 0.95f + (1f / dt) * 0.05f;
                    }
                }
                if (fpsEma >= 60f)
                {
                    return 1f;
                }
                if (fpsEma <= 25f)
                {
                    return 3f;
                }
                return 1f + (60f - fpsEma) * (2f / 35f);
            }
        }

        private static int IntervalFor(Alert alert, AlertRec rec, FPSPlusSettings s)
        {
            double mult = rec.emaMs / CheapMs;
            if (mult < 1.0)
            {
                mult = 1.0;
            }
            double strength = s.throttleStrength;
            if (s.autoTuneAlerts)
            {
                strength *= AutoFactor;
            }
            double interval = 24.0 * mult * strength;
            TickManager tm = Find.TickManager;
            if (tm != null && tm.Paused && interval < (double)s.pausedMinIntervalFrames)
            {
                interval = s.pausedMinIntervalFrames;
            }
            int max = s.maxIntervalFrames;
            if (alert.Priority == AlertPriority.Critical && max > s.criticalMaxIntervalFrames)
            {
                max = s.criticalMaxIntervalFrames;
            }
            if (interval > (double)max)
            {
                interval = max;
            }
            if (interval < 24.0)
            {
                interval = 24.0;
            }
            return (int)interval;
        }

        private static bool BudgetExceeded(FPSPlusSettings s)
        {
            if (budgetFrame != Time.frameCount)
            {
                budgetFrame = Time.frameCount;
                budgetUsedMs = 0.0;
            }
            return budgetUsedMs >= (double)s.frameBudgetMs;
        }

        private static void RemoveFromActive(AlertsReadout readout, Alert alert)
        {
            if (ActiveAlertsField == null || readout == null)
            {
                return;
            }
            List<Alert> list = ActiveAlertsField.GetValue(readout) as List<Alert>;
            if (list != null)
            {
                list.Remove(alert);
            }
        }

        // Gate on AlertsReadout.CheckAddOrRemoveAlert: decides whether an alert is
        // due for recalculation this frame. forceRemove calls always pass through
        // (they clean up stale quest/precept/scenario alerts).
        public static bool CheckAddOrRemoveAlert_Prefix(AlertsReadout __instance, Alert alert, bool forceRemove)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.alertThrottle || alert == null || forceRemove)
            {
                return true;
            }
            if (ConflictGuard.SuppressAlerts && !ConflictGuard.Ov)
            {
                return true;
            }
            AlertRec rec = GetRec(alert);
            rec.lastSeenFrame = Time.frameCount;
            if (IsDisabled(rec))
            {
                TotalDisabledSkips++;
                RemoveFromActive(__instance, alert);
                return false;
            }
            if (rec.count == 0)
            {
                return true; // never measured: run now to get a cost sample and correct state
            }
            int since = Time.frameCount - rec.lastRecalcFrame;
            int interval = IntervalFor(alert, rec, s);
            if (since < interval)
            {
                TotalGated++;
                return false;
            }
            if (BudgetExceeded(s) && since < s.maxIntervalFrames)
            {
                TotalDeferred++;
                return false;
            }
            return true;
        }

        // Timing wrap on Alert.Recalculate: measures real cost per alert instance.
        public static void Recalculate_Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        public static void Recalculate_Postfix(Alert __instance, long __state)
        {
            double ms = (Stopwatch.GetTimestamp() - __state) * MsPerTimestampTick;
            AlertRec rec = GetRec(__instance);
            int frame = Time.frameCount;
            rec.count++;
            rec.lastRecalcFrame = frame;
            rec.lastSeenFrame = frame;
            if (rec.count == 1)
            {
                rec.emaMs = ms;
            }
            else
            {
                rec.emaMs = rec.emaMs * 0.8 + ms * 0.2;
            }
            string label = __instance.Label;
            if (!label.NullOrEmpty())
            {
                rec.lastLabel = label;
            }
            if (budgetFrame != frame)
            {
                budgetFrame = frame;
                budgetUsedMs = 0.0;
            }
            budgetUsedMs += ms;
            TotalRan++;
            int sec = (int)Time.realtimeSinceStartup;
            if (sec != statSecond)
            {
                statSecond = sec;
                MsLastSecond = msAccum;
                RanLastSecond = ranAccum;
                msAccum = 0.0;
                ranAccum = 0;
            }
            msAccum += ms;
            ranAccum++;
        }

        // Cache on the Alert.Height getter: vanilla runs Text.CalcHeight on every
        // active alert every frame (twice - AlertsHeight and DrawAt).
        private static readonly Dictionary<string, float> HeightCache = new Dictionary<string, float>();

        public static bool Height_Prefix(Alert __instance, ref float __result)
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s == null || !s.heightCache)
            {
                return true;
            }
            if (ConflictGuard.SuppressHeight && !ConflictGuard.Ov)
            {
                return true;
            }
            Text.Font = GameFont.Small; // vanilla getter sets this; DrawAt relies on it
            string label = __instance.Label;
            if (label == null)
            {
                label = "";
            }
            float h;
            if (HeightCache.TryGetValue(label, out h))
            {
                __result = h;
                return false;
            }
            h = Text.CalcHeight(label, 148f);
            if (HeightCache.Count > 512)
            {
                HeightCache.Clear();
            }
            HeightCache[label] = h;
            __result = h;
            return false;
        }

        // Vanilla scans quests/precepts/scenario/signal alerts every 20 frames
        // (hardcoded). The transpiler swaps that constant for this call, so the
        // interval follows the setting live: 60 frames when enabled, vanilla 20
        // when not. Worst case a brand-new alert appears ~0.7s later.
        public static int SpecialScanInterval()
        {
            FPSPlusSettings s = FPSPlusMod.Settings;
            if (s != null && s.slowSpecialScans && (!ConflictGuard.SuppressAlerts || ConflictGuard.Ov))
            {
                return 60;
            }
            return 20;
        }

        public static IEnumerable<CodeInstruction> AlertsUpdate_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo interval = AccessTools.Method(typeof(AlertPatches), "SpecialScanInterval");
            foreach (CodeInstruction ins in instructions)
            {
                bool is20 = false;
                if (ins.opcode == OpCodes.Ldc_I4_S)
                {
                    object op = ins.operand;
                    if (op is sbyte)
                    {
                        is20 = (sbyte)op == 20;
                    }
                    else if (op is byte)
                    {
                        is20 = (byte)op == 20;
                    }
                    else if (op is int)
                    {
                        is20 = (int)op == 20;
                    }
                }
                if (is20)
                {
                    yield return new CodeInstruction(OpCodes.Call, interval);
                }
                else
                {
                    yield return ins;
                }
            }
        }

        public static void ResetStats()
        {
            TotalRan = 0;
            TotalGated = 0;
            TotalDeferred = 0;
            TotalDisabledSkips = 0;
            MsLastSecond = 0.0;
            RanLastSecond = 0;
            msAccum = 0.0;
            ranAccum = 0;
        }

        // What the same alert set would cost per second at the vanilla 24-frame cycle.
        public static double EstimatedVanillaMsPerSecond()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt < 0.0001f)
            {
                dt = 0.0001f;
            }
            double fps = 1.0 / (double)dt;
            if (fps > 240.0)
            {
                fps = 240.0;
            }
            double sum = 0.0;
            int frame = Time.frameCount;
            foreach (KeyValuePair<Alert, AlertRec> kv in Recs)
            {
                if (frame - kv.Value.lastSeenFrame < 3000 && !IsDisabled(kv.Value))
                {
                    sum += kv.Value.emaMs;
                }
            }
            return sum * fps / 24.0;
        }

        public static string DebugSummary()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("tracked=").Append(Recs.Count);
            sb.Append(" ran=").Append(TotalRan);
            sb.Append(" gated=").Append(TotalGated);
            sb.Append(" deferred=").Append(TotalDeferred);
            sb.Append(" alertMsLastSec=").Append(MsLastSecond.ToString("F2"));
            sb.Append(" estVanillaMsPerSec=").Append(EstimatedVanillaMsPerSecond().ToString("F2"));
            List<AlertRec> top = new List<AlertRec>();
            foreach (KeyValuePair<Alert, AlertRec> kv in Recs)
            {
                top.Add(kv.Value);
            }
            top.Sort(delegate(AlertRec a, AlertRec b) { return b.emaMs.CompareTo(a.emaMs); });
            int n = Math.Min(5, top.Count);
            sb.Append(" top=[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(top[i].shortName).Append(":").Append((top[i].emaMs * 1000.0).ToString("F0")).Append("us");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
