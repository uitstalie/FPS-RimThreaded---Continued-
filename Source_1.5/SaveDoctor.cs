using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FPSPlus
{
    // Save Doctor. Old saves quietly carry thousands of dead entries: tales
    // of long-gone pawns, ancient letters, forgotten world pawns, filth.
    // Scan counts them; each Clean button removes one category. Everything
    // is manual - nothing runs on its own.
    public static class SaveDoctor
    {
        public static bool Scanned;
        public static int WorldPawnsAlive;
        public static int WorldPawnsDead;
        public static int TalesTotal;
        public static int TalesRemovable;
        public static int ArchiveTotal;
        public static int ArchiveRemovable;
        public static int FilthTotal;
        public static int QuestsTotal;
        public static int QuestsRemovable;
        public static long TotalCleaned;

        private const int TaleOldTicks = 7200000;    // 2 in-game years
        private const int LetterOldTicks = 3600000;  // 1 in-game year

        private static readonly FieldInfo TaleDateField = AccessTools.Field(typeof(Tale), "date");
        private static readonly MethodInfo RemoveTaleMethod = AccessTools.Method(typeof(TaleManager), "RemoveTale");

        public static void Scan()
        {
            Scanned = true;
            WorldPawnsAlive = 0;
            WorldPawnsDead = 0;
            TalesTotal = 0;
            TalesRemovable = 0;
            ArchiveTotal = 0;
            ArchiveRemovable = 0;
            FilthTotal = 0;
            try
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (p.Dead)
                    {
                        WorldPawnsDead++;
                    }
                    else
                    {
                        WorldPawnsAlive++;
                    }
                }
                List<Tale> tales = Find.TaleManager.AllTalesListForReading;
                TalesTotal = tales.Count;
                for (int i = 0; i < tales.Count; i++)
                {
                    if (TaleRemovable(tales[i]))
                    {
                        TalesRemovable++;
                    }
                }
                List<IArchivable> arch = Find.Archive.ArchivablesListForReading;
                ArchiveTotal = arch.Count;
                for (int i = 0; i < arch.Count; i++)
                {
                    if (ArchivableRemovable(arch[i]))
                    {
                        ArchiveRemovable++;
                    }
                }
                List<Map> maps = Find.Maps;
                for (int i = 0; i < maps.Count; i++)
                {
                    FilthTotal += maps[i].listerThings.ThingsInGroup(ThingRequestGroup.Filth).Count;
                }
                List<Quest> quests = Find.QuestManager.QuestsListForReading;
                QuestsTotal = quests.Count;
                for (int i = 0; i < quests.Count; i++)
                {
                    if (QuestRemovable(quests[i]))
                    {
                        QuestsRemovable++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ Save Doctor scan failed: " + ex);
            }
        }

        // Old tales nothing points at anymore. Tales referenced by art are
        // never touched (Unused is false for those).
        private static bool TaleRemovable(Tale t)
        {
            if (t == null || !t.Unused)
            {
                return false;
            }
            if (TaleDateField == null)
            {
                return false;
            }
            int date = (int)TaleDateField.GetValue(t);
            return Find.TickManager.TicksGame - date > TaleOldTicks;
        }

        // Old letters/messages: never pinned ones, never quest-locked ones.
        private static bool ArchivableRemovable(IArchivable a)
        {
            if (a == null || !a.CanCullArchivedNow)
            {
                return false;
            }
            if (Find.Archive.IsPinned(a))
            {
                return false;
            }
            return Find.TickManager.TicksGame - a.CreatedTicksGame > LetterOldTicks;
        }

        // Only quests that are fully OVER (ended and internally cleaned up)
        // and have been over for more than a year. Active or offered quests
        // are never touched.
        private static bool QuestRemovable(Quest q)
        {
            if (q == null || !q.Historical)
            {
                return false;
            }
            int endedAt = q.cleanupTick > 0 ? q.cleanupTick : q.appearanceTick;
            if (endedAt <= 0)
            {
                return false;
            }
            return Find.TickManager.TicksGame - endedAt > LetterOldTicks;
        }

        public static int CleanQuests()
        {
            int removed = 0;
            try
            {
                List<Quest> quests = new List<Quest>(Find.QuestManager.QuestsListForReading);
                for (int i = 0; i < quests.Count; i++)
                {
                    if (QuestRemovable(quests[i]))
                    {
                        Find.QuestManager.Remove(quests[i]);
                        removed++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ Save Doctor quest cleanup failed: " + ex);
            }
            TotalCleaned += removed;
            return removed;
        }

        public static int CleanTales()
        {
            int removed = 0;
            try
            {
                if (RemoveTaleMethod == null)
                {
                    return 0;
                }
                List<Tale> tales = new List<Tale>(Find.TaleManager.AllTalesListForReading);
                for (int i = 0; i < tales.Count; i++)
                {
                    if (TaleRemovable(tales[i]))
                    {
                        RemoveTaleMethod.Invoke(Find.TaleManager, new object[] { tales[i] });
                        removed++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ Save Doctor tale cleanup failed: " + ex);
            }
            TotalCleaned += removed;
            return removed;
        }

        public static int CleanArchive()
        {
            int removed = 0;
            try
            {
                List<IArchivable> arch = new List<IArchivable>(Find.Archive.ArchivablesListForReading);
                for (int i = 0; i < arch.Count; i++)
                {
                    if (ArchivableRemovable(arch[i]) && Find.Archive.Remove(arch[i]))
                    {
                        removed++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ Save Doctor archive cleanup failed: " + ex);
            }
            TotalCleaned += removed;
            return removed;
        }

        public static int CleanFilth()
        {
            int removed = 0;
            try
            {
                List<Map> maps = Find.Maps;
                for (int m = 0; m < maps.Count; m++)
                {
                    List<Thing> filth = new List<Thing>(maps[m].listerThings.ThingsInGroup(ThingRequestGroup.Filth));
                    for (int i = 0; i < filth.Count; i++)
                    {
                        Thing f = filth[i];
                        if (f != null && f.Spawned && !f.Destroyed)
                        {
                            f.Destroy(DestroyMode.Vanish);
                            removed++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RimThreadedTTR] FPS+ Save Doctor filth cleanup failed: " + ex);
            }
            TotalCleaned += removed;
            return removed;
        }
    }
}
