﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GilesTrinity.ItemRules;
using GilesTrinity.Settings.Loot;
using GilesTrinity.Technicals;
using Zeta;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.CommonBot;
using Zeta.Internals.Actors;
using System.Xml.Serialization;

namespace GilesTrinity
{
    public partial class GilesTrinity : IPlugin
    {
        public class PersistentStats
        {
            public bool IsReset;            // true between reset and next report output
            public DateTime WhenStartedSession; // when did current session start, helps identify item increments
            [XmlIgnore]
            public TimeSpan TotalRunningTime;
            public int TotalDeaths;
            public int TotalLeaveGames;
            public int TotalJoinGames;
            public int TotalProfileRecycles;
            public int TotalXp;
            public int LastXp;
            public int NextLvXp;
            public GItemStats ItemsDropped;
            public GItemStats ItemsPicked;
            public long TotalRunningTimeTicks
            {
                get { return TotalRunningTime.Ticks; }
                set { TotalRunningTime = new TimeSpan(value); }
            }

            public PersistentStats()
            {
                ItemsDropped = new GItemStats(0, new double[4], new double[64], new double[4, 64], 0, new double[64], 0, new double[4], new double[64], new double[4, 64], 0);
                ItemsPicked = new GItemStats(0, new double[4], new double[64], new double[4, 64], 0, new double[64], 0, new double[4], new double[64], new double[4, 64], 0);

                Reset();
            }

            public void Reset()
            {
                IsReset = true;
                TotalRunningTime = TimeSpan.Zero;
                TotalDeaths = 0;
                TotalLeaveGames = 0;
                TotalJoinGames = 0;
                TotalProfileRecycles = 0;
                TotalXp = 0;
                LastXp = 0;
                NextLvXp = 0;

                ItemsDropped.Total = 0;
                Array.Clear(ItemsDropped.TotalPerQuality, 0, ItemsDropped.TotalPerQuality.Length);
                Array.Clear(ItemsDropped.TotalPerLevel, 0, ItemsDropped.TotalPerLevel.Length);
                Array.Clear(ItemsDropped.TotalPerQPerL, 0, ItemsDropped.TotalPerQPerL.Length);
                ItemsDropped.TotalPotions = 0;
                Array.Clear(ItemsDropped.PotionsPerLevel, 0, ItemsDropped.PotionsPerLevel.Length);
                ItemsDropped.TotalGems = 0;
                Array.Clear(ItemsDropped.GemsPerType, 0, ItemsDropped.GemsPerType.Length);
                Array.Clear(ItemsDropped.GemsPerLevel, 0, ItemsDropped.GemsPerLevel.Length);
                Array.Clear(ItemsDropped.GemsPerTPerL, 0, ItemsDropped.GemsPerTPerL.Length);
                ItemsDropped.TotalInfernalKeys = 0;

                ItemsPicked.Total = 0;
                Array.Clear(ItemsPicked.TotalPerQuality, 0, ItemsPicked.TotalPerQuality.Length);
                Array.Clear(ItemsPicked.TotalPerLevel, 0, ItemsPicked.TotalPerLevel.Length);
                Array.Clear(ItemsPicked.TotalPerQPerL, 0, ItemsPicked.TotalPerQPerL.Length);
                ItemsPicked.TotalPotions = 0;
                Array.Clear(ItemsPicked.PotionsPerLevel, 0, ItemsPicked.PotionsPerLevel.Length);
                ItemsPicked.TotalGems = 0;
                Array.Clear(ItemsPicked.GemsPerType, 0, ItemsPicked.GemsPerType.Length);
                Array.Clear(ItemsPicked.GemsPerLevel, 0, ItemsPicked.GemsPerLevel.Length);
                Array.Clear(ItemsPicked.GemsPerTPerL, 0, ItemsPicked.GemsPerTPerL.Length);
                ItemsPicked.TotalInfernalKeys = 0;
            }

            public void AddItemsDroppedStats(
                GItemStats Last, GItemStats New)
            {
                ItemsDropped.Total += New.Total - Last.Total;
                ItemsDropped.TotalPotions += New.TotalPotions - Last.TotalPotions;
                ItemsDropped.TotalGems += New.TotalGems - Last.TotalGems;
                ItemsDropped.TotalInfernalKeys += New.TotalInfernalKeys - Last.TotalInfernalKeys;

                AddArray(ItemsDropped.TotalPerQuality, Last.TotalPerQuality, New.TotalPerQuality);
                AddArray(ItemsDropped.TotalPerLevel, Last.TotalPerLevel, New.TotalPerLevel);
                AddArray(ItemsDropped.TotalPerQPerL, Last.TotalPerQPerL, New.TotalPerQPerL);
                AddArray(ItemsDropped.PotionsPerLevel, Last.PotionsPerLevel, New.PotionsPerLevel);
                AddArray(ItemsDropped.GemsPerType, Last.GemsPerType, New.GemsPerType);
                AddArray(ItemsDropped.GemsPerLevel, Last.GemsPerLevel, New.GemsPerLevel);
                AddArray(ItemsDropped.GemsPerTPerL, Last.GemsPerTPerL, New.GemsPerTPerL);
            }

            public void AddItemsPickedStats(
                GItemStats Last, GItemStats New)
            {
                ItemsPicked.Total += New.Total - Last.Total;
                ItemsPicked.TotalPotions += New.TotalPotions - Last.TotalPotions;
                ItemsPicked.TotalGems += New.TotalGems - Last.TotalGems;
                ItemsPicked.TotalInfernalKeys += New.TotalInfernalKeys - Last.TotalInfernalKeys;

                AddArray(ItemsPicked.TotalPerQuality, Last.TotalPerQuality, New.TotalPerQuality);
                AddArray(ItemsPicked.TotalPerLevel, Last.TotalPerLevel, New.TotalPerLevel);
                AddArray(ItemsPicked.TotalPerQPerL, Last.TotalPerQPerL, New.TotalPerQPerL);
                AddArray(ItemsPicked.PotionsPerLevel, Last.PotionsPerLevel, New.PotionsPerLevel);
                AddArray(ItemsPicked.GemsPerType, Last.GemsPerType, New.GemsPerType);
                AddArray(ItemsPicked.GemsPerLevel, Last.GemsPerLevel, New.GemsPerLevel);
                AddArray(ItemsPicked.GemsPerTPerL, Last.GemsPerTPerL, New.GemsPerTPerL);
            }

            static public void UpdateItemsDroppedStats(
                GItemStats Last, GItemStats New)
            {
                Last.Total = New.Total;
                Last.TotalPotions = New.TotalPotions;
                Last.TotalGems = New.TotalGems;
                Last.TotalInfernalKeys = New.TotalInfernalKeys;

                CopyArray(Last.TotalPerQuality, New.TotalPerQuality);
                CopyArray(Last.TotalPerLevel, New.TotalPerLevel);
                CopyArray(Last.TotalPerQPerL, New.TotalPerQPerL);
                CopyArray(Last.PotionsPerLevel, New.PotionsPerLevel);
                CopyArray(Last.GemsPerType, New.GemsPerType);
                CopyArray(Last.GemsPerLevel, New.GemsPerLevel);
                CopyArray(Last.GemsPerTPerL, New.GemsPerTPerL);
            }

            static public void UpdateItemsPickedStats(
                GItemStats Last, GItemStats New)
            {
                Last.Total = New.Total;
                Last.TotalPotions = New.TotalPotions;
                Last.TotalGems = New.TotalGems;
                Last.TotalInfernalKeys = New.TotalInfernalKeys;

                CopyArray(Last.TotalPerQuality, New.TotalPerQuality);
                CopyArray(Last.TotalPerLevel, New.TotalPerLevel);
                CopyArray(Last.TotalPerQPerL, New.TotalPerQPerL);
                CopyArray(Last.PotionsPerLevel, New.PotionsPerLevel);
                CopyArray(Last.GemsPerType, New.GemsPerType);
                CopyArray(Last.GemsPerLevel, New.GemsPerLevel);
                CopyArray(Last.GemsPerTPerL, New.GemsPerTPerL);
            }

            static private void AddArray(double[] target, double[] last, double[] src)
            {
                for (int i = 0; i < target.GetLength(0); i++)
                {
                    target[i] += src[i] - last[i];
                }
            }
            static private void AddArray(double[,] target, double[,] last, double[,] src)
            {
                for (int i = 0; i < target.GetLength(0); i++)
                    for (int j = 0; j < target.GetLength(1); j++)
                    {
                        target[i, j] += src[i, j] - last[i, j];
                    }
            }

            static private void CopyArray(double[] last, double[] src)
            {
                for (int i = 0; i < last.GetLength(0); i++)
                {
                    last[i] = src[i];
                }
            }
            static private void CopyArray(double[,] last, double[,] src)
            {
                for (int i = 0; i < last.GetLength(0); i++)
                    for (int j = 0; j < last.GetLength(1); j++)
                    {
                        last[i, j] = src[i, j];
                    }
            }
        }

        private static PersistentStats PersistentTotalStats = new PersistentStats();
        private static PersistentStats PersistentLastSaved = new PersistentStats();
        private static Dictionary<int, PersistentStats> worldStatsDictionary =
            new Dictionary<int, PersistentStats>();

        private static PersistentStats PersistentUpdateOne(String aFilename)
        {
            PersistentStats updated = new PersistentStats();
            // Load from file
            var xml = new XmlSerializer(updated.GetType());

            if (File.Exists(aFilename))
            {
                using (var reader = new StreamReader(aFilename))
                {
                    updated = xml.Deserialize(reader) as PersistentStats;
                    if (updated.IsReset)
                        updated.Reset();
                }
            }
            else
            {
                updated.Reset();
            }

            // If we are in new session (started times don't match) we clear PersistentLastSaved,
            // because we want to add all we now have.
            // However, after reset, we don't want to do that, because we are (hopefully) curious
            // only about new stuff
            if (updated.WhenStartedSession != ItemStatsWhenStartedBot && !updated.IsReset)
            {
                PersistentLastSaved.Reset();
            }

            // Add the differences
            TimeSpan TotalRunningTime = DateTime.Now.Subtract(ItemStatsWhenStartedBot);

            updated.IsReset = false;
            updated.WhenStartedSession = ItemStatsWhenStartedBot;
            updated.TotalRunningTime += TotalRunningTime - PersistentLastSaved.TotalRunningTime;
            updated.TotalDeaths += iTotalDeaths - PersistentLastSaved.TotalDeaths;
            updated.TotalLeaveGames += TotalLeaveGames - PersistentLastSaved.TotalLeaveGames;
            updated.TotalJoinGames += iTotalJoinGames - PersistentLastSaved.TotalJoinGames;
            updated.TotalProfileRecycles += TotalProfileRecycles - PersistentLastSaved.TotalProfileRecycles;
            updated.TotalXp += iTotalXp - PersistentLastSaved.TotalXp;
            updated.LastXp += iLastXp - PersistentLastSaved.LastXp;
            updated.NextLvXp += iNextLvXp - PersistentLastSaved.NextLvXp;
            // Adds difference between now and LastSaved, and set LastSaved to now
            updated.AddItemsDroppedStats(PersistentLastSaved.ItemsDropped, ItemsDroppedStats);
            updated.AddItemsPickedStats(PersistentLastSaved.ItemsPicked, ItemsPickedStats);

            // Write the PersistentTotalStats
            using (var writer = new StreamWriter(aFilename))
            {
                xml.Serialize(writer, updated);
                writer.Flush();
            }

            return updated;
        }

        private static void PersistentUpdateStats()
        {
            // Total stats
            string filename = Path.Combine(FileManager.LoggingPath, String.Format("FullStats - {0}.xml", PlayerStatus.ActorClass));
            PersistentTotalStats = PersistentUpdateOne(filename);

            // World ID stats
            filename = Path.Combine(FileManager.LoggingPath, String.Format("WorldStats {1} - {0}.xml", PlayerStatus.ActorClass, cachedStaticWorldId));
            if (!worldStatsDictionary.ContainsKey(cachedStaticWorldId))
                worldStatsDictionary.Add(cachedStaticWorldId, new PersistentStats());
            worldStatsDictionary[cachedStaticWorldId] = PersistentUpdateOne(filename);

            // Sets LastSaved to now for the rest of the things
            TimeSpan TotalRunningTime = DateTime.Now.Subtract(ItemStatsWhenStartedBot);
            PersistentLastSaved.TotalRunningTime = TotalRunningTime;
            PersistentLastSaved.TotalDeaths = iTotalDeaths;
            PersistentLastSaved.TotalLeaveGames = TotalLeaveGames;
            PersistentLastSaved.TotalJoinGames = iTotalJoinGames;
            PersistentLastSaved.TotalProfileRecycles = TotalProfileRecycles;
            PersistentLastSaved.TotalXp = iTotalXp;
            PersistentLastSaved.LastXp = iLastXp;
            PersistentLastSaved.NextLvXp = iNextLvXp;

            PersistentStats.UpdateItemsDroppedStats(PersistentLastSaved.ItemsDropped, ItemsDroppedStats);
            PersistentStats.UpdateItemsPickedStats(PersistentLastSaved.ItemsPicked, ItemsPickedStats);
        }

        /// <summary>
        /// Full Output Of Item Stats
        /// </summary>
        internal static void PersistentOutputReport()
        {
            try
            {
                PersistentUpdateStats();
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "PersistentOutputReport exception: {0}", ex.ToString());
            }

            // Full Stats
            try
            {
                var fullStatsPath = Path.Combine(FileManager.LoggingPath, String.Format("FullStats - {0}.log", PlayerStatus.ActorClass));

                using (FileStream LogStream =
                    File.Open(fullStatsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    LogStats(LogStream, PersistentTotalStats);
                }
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "FullStats exception: {0}", ex.ToString());
            }

            // Current World Stats

            try
            {
                var worldStatsPath = Path.Combine(FileManager.LoggingPath, String.Format("WorldStats {1} - {0}.log", PlayerStatus.ActorClass, cachedStaticWorldId));

                using (FileStream LogStream = File.Open(worldStatsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    LogStats(LogStream, worldStatsDictionary[cachedStaticWorldId]);
                }
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "WorldStats exception: {0}", ex.ToString());
            }

            // AggregateWorldStats
            try
            {
                var aggregateWorldStatsPath = Path.Combine(FileManager.LoggingPath, String.Format("AgregateWorldStats - {0}.log", PlayerStatus.ActorClass));

                using (FileStream LogStream = File.Open(aggregateWorldStatsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    LogWorldStats(LogStream, worldStatsDictionary);
                }
            }
            catch (Exception ex)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "AggregateWorldStats exception: {0}", ex.ToString());
            }

        }

        internal static void LogWorldStats(FileStream LogStream, Dictionary<int, PersistentStats> aWorldStats)
        {
            using (StreamWriter LogWriter = new StreamWriter(LogStream))
            {
                LogWriter.WriteLine("=== Per World agregate stats ===");
                LogWriter.WriteLine("The format is: worldid <tab> stat");
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== Time spent in WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.TotalRunningTime.TotalHours);
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== Total items + iph dropped per WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.ItemsDropped.Total + "\t" + Math.Round(v.Value.ItemsDropped.Total / v.Value.TotalRunningTime.TotalHours, 2).ToString("0.00"));
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== Total rares + iph dropped per WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.ItemsDropped.TotalPerQuality[2] + "\t" + Math.Round(v.Value.ItemsDropped.TotalPerQuality[2] / v.Value.TotalRunningTime.TotalHours, 2).ToString("0.00"));
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== ilvl63 rares + iph dropped per WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.ItemsDropped.TotalPerQPerL[2, 63] + "\t" + Math.Round(v.Value.ItemsDropped.TotalPerQPerL[2, 63] / v.Value.TotalRunningTime.TotalHours, 2).ToString("0.00"));
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== Total legendaries + iph dropped per WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.ItemsDropped.TotalPerQuality[3] + "\t" + Math.Round(v.Value.ItemsDropped.TotalPerQuality[3] / v.Value.TotalRunningTime.TotalHours, 2).ToString("0.00"));
                LogWriter.WriteLine();
                LogWriter.WriteLine("=== Total items + iph picked per WorldID ===");
                foreach (var v in aWorldStats)
                    LogWriter.WriteLine(v.Key + "\t" + v.Value.ItemsPicked.Total + "\t" + Math.Round(v.Value.ItemsPicked.Total / v.Value.TotalRunningTime.TotalHours, 2).ToString("0.00"));

                LogWriter.Flush();
                LogStream.Flush();
            }
        }

        internal static void LogStats(FileStream LogStream, PersistentStats aPersistentStats)
        {
            var ts = aPersistentStats;
            // Create whole new file
            using (StreamWriter LogWriter = new StreamWriter(LogStream))
            {
                LogWriter.WriteLine("===== Misc Statistics =====");
                LogWriter.WriteLine("Total tracking time: " + ((int)ts.TotalRunningTime.TotalHours).ToString() + "h " + ts.TotalRunningTime.Minutes.ToString("0.00") +
                    "m " + ts.TotalRunningTime.Seconds.ToString() + "s");
                LogWriter.WriteLine("Total deaths: " + ts.TotalDeaths.ToString() + " [" + Math.Round(ts.TotalDeaths / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                LogWriter.WriteLine("Total games (approx): " + ts.TotalLeaveGames.ToString() + " [" + Math.Round(ts.TotalLeaveGames / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                if (ts.TotalLeaveGames == 0 && ts.TotalJoinGames > 0)
                {
                    if (ts.TotalJoinGames == 1 && ts.TotalProfileRecycles > 1)
                    {
                        LogWriter.WriteLine("(a profile manager/death handler is interfering with join/leave game events, attempting to guess total runs based on profile-loops)");
                        LogWriter.WriteLine("Total full profile cycles: " + ts.TotalProfileRecycles.ToString() + " [" + Math.Round(ts.TotalProfileRecycles / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    }
                    else
                    {
                        LogWriter.WriteLine("(your games left value may be bugged @ 0 due to profile managers/routines etc., now showing games joined instead:)");
                        LogWriter.WriteLine("Total games joined: " + ts.TotalJoinGames.ToString() + " [" + Math.Round(ts.TotalJoinGames / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    }
                }
                LogWriter.WriteLine("Total XP gained: " + Math.Round(ts.TotalXp / (float)1000000, 2).ToString("0.00") + " million [" + Math.Round(ts.TotalXp / ts.TotalRunningTime.TotalHours / 1000000, 2).ToString("0.00") + " million per hour]");
                LogWriter.WriteLine("");
                LogWriter.WriteLine("===== Item DROP Statistics =====");

                // Item stats
                if (ts.ItemsDropped.Total > 0)
                {
                    LogWriter.WriteLine("Items:");
                    LogWriter.WriteLine("Total items dropped: " + ts.ItemsDropped.Total.ToString() + " [" +
                        Math.Round(ts.ItemsDropped.Total / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    LogWriter.WriteLine("Items dropped by ilvl: ");
                    for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                        if (ts.ItemsDropped.TotalPerLevel[itemLevel] > 0)
                            LogWriter.WriteLine("- ilvl" + itemLevel.ToString() + ": " + ts.ItemsDropped.TotalPerLevel[itemLevel].ToString() + " [" +
                                Math.Round(ts.ItemsDropped.TotalPerLevel[itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" +
                                Math.Round((ts.ItemsDropped.TotalPerLevel[itemLevel] / ts.ItemsDropped.Total) * 100, 2).ToString("0.00") + " %}");
                    LogWriter.WriteLine("");
                    LogWriter.WriteLine("Items dropped by quality: ");
                    for (int iThisQuality = 0; iThisQuality <= 3; iThisQuality++)
                    {
                        if (ts.ItemsDropped.TotalPerQuality[iThisQuality] > 0)
                        {
                            LogWriter.WriteLine("- " + sQualityString[iThisQuality] + ": " + ts.ItemsDropped.TotalPerQuality[iThisQuality].ToString() + " [" + Math.Round(ts.ItemsDropped.TotalPerQuality[iThisQuality] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsDropped.TotalPerQuality[iThisQuality] / ts.ItemsDropped.Total) * 100, 2).ToString("0.00") + " %}");
                            for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                                if (ts.ItemsDropped.TotalPerQPerL[iThisQuality, itemLevel] > 0)
                                    LogWriter.WriteLine("--- ilvl " + itemLevel.ToString() + " " + sQualityString[iThisQuality] + ": " + ts.ItemsDropped.TotalPerQPerL[iThisQuality, itemLevel].ToString() + " [" + Math.Round(ts.ItemsDropped.TotalPerQPerL[iThisQuality, itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsDropped.TotalPerQPerL[iThisQuality, itemLevel] / ts.ItemsDropped.Total) * 100, 2).ToString("0.00") + " %}");
                        }

                        // Any at all this quality?
                    }

                    // For loop on quality
                    LogWriter.WriteLine("");
                }

                // End of item stats

                // Potion stats
                if (ts.ItemsDropped.TotalPotions > 0)
                {
                    LogWriter.WriteLine("Potion Drops:");
                    LogWriter.WriteLine("Total potions: " + ts.ItemsDropped.TotalPotions.ToString() + " [" + Math.Round(ts.ItemsDropped.TotalPotions / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    for (int itemLevel = 1; itemLevel <= 63; itemLevel++) if (ts.ItemsDropped.PotionsPerLevel[itemLevel] > 0)
                            LogWriter.WriteLine("- ilvl " + itemLevel.ToString() + ": " + ts.ItemsDropped.PotionsPerLevel[itemLevel].ToString() + " [" + Math.Round(ts.ItemsDropped.PotionsPerLevel[itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsDropped.PotionsPerLevel[itemLevel] / ts.ItemsDropped.TotalPotions) * 100, 2).ToString("0.00") + " %}");
                    LogWriter.WriteLine("");
                }

                // End of potion stats

                // Gem stats
                if (ts.ItemsDropped.TotalGems > 0)
                {
                    LogWriter.WriteLine("Gem Drops:");
                    LogWriter.WriteLine("Total gems: " + ts.ItemsDropped.TotalGems.ToString() + " [" + Math.Round(ts.ItemsDropped.TotalGems / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    for (int iThisGemType = 0; iThisGemType <= 3; iThisGemType++)
                    {
                        if (ts.ItemsDropped.GemsPerType[iThisGemType] > 0)
                        {
                            LogWriter.WriteLine("- " + sGemString[iThisGemType] + ": " + ts.ItemsDropped.GemsPerType[iThisGemType].ToString() + " [" + Math.Round(ts.ItemsDropped.GemsPerType[iThisGemType] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsDropped.GemsPerType[iThisGemType] / ts.ItemsDropped.TotalGems) * 100, 2).ToString("0.00") + " %}");
                            for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                                if (ts.ItemsDropped.GemsPerTPerL[iThisGemType, itemLevel] > 0)
                                    LogWriter.WriteLine("--- ilvl " + itemLevel.ToString() + " " + sGemString[iThisGemType] + ": " + ts.ItemsDropped.GemsPerTPerL[iThisGemType, itemLevel].ToString() + " [" + Math.Round(ts.ItemsDropped.GemsPerTPerL[iThisGemType, itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsDropped.GemsPerTPerL[iThisGemType, itemLevel] / ts.ItemsDropped.TotalGems) * 100, 2).ToString("0.00") + " %}");
                        }

                        // Any at all this quality?
                    }

                    // For loop on quality
                }

                // End of gem stats

                // Key stats
                if (ts.ItemsDropped.TotalInfernalKeys > 0)
                {
                    LogWriter.WriteLine("Infernal Key Drops:");
                    LogWriter.WriteLine("Total Keys: " + ts.ItemsDropped.TotalInfernalKeys.ToString() + " [" + Math.Round(ts.ItemsDropped.TotalInfernalKeys / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                }

                // End of key stats
                LogWriter.WriteLine("");
                LogWriter.WriteLine("");
                LogWriter.WriteLine("===== Item PICKUP Statistics =====");

                // Item stats
                if (ts.ItemsPicked.Total > 0)
                {
                    LogWriter.WriteLine("Items:");
                    LogWriter.WriteLine("Total items picked up: " + ts.ItemsPicked.Total.ToString() + " [" + Math.Round(ts.ItemsPicked.Total / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    LogWriter.WriteLine("Item picked up by ilvl: ");
                    for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                        if (ts.ItemsPicked.TotalPerLevel[itemLevel] > 0)
                            LogWriter.WriteLine("- ilvl" + itemLevel.ToString() + ": " + ts.ItemsPicked.TotalPerLevel[itemLevel].ToString() + " [" + Math.Round(ts.ItemsPicked.TotalPerLevel[itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.TotalPerLevel[itemLevel] / ts.ItemsPicked.Total) * 100, 2).ToString("0.00") + " %}");
                    LogWriter.WriteLine("");
                    LogWriter.WriteLine("Items picked up by quality: ");
                    for (int iThisQuality = 0; iThisQuality <= 3; iThisQuality++)
                    {
                        if (ts.ItemsPicked.TotalPerQuality[iThisQuality] > 0)
                        {
                            LogWriter.WriteLine("- " + sQualityString[iThisQuality] + ": " + ts.ItemsPicked.TotalPerQuality[iThisQuality].ToString() + " [" + Math.Round(ts.ItemsPicked.TotalPerQuality[iThisQuality] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.TotalPerQuality[iThisQuality] / ts.ItemsPicked.Total) * 100, 2).ToString("0.00") + " %}");
                            for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                                if (ts.ItemsPicked.TotalPerQPerL[iThisQuality, itemLevel] > 0)
                                    LogWriter.WriteLine("--- ilvl " + itemLevel.ToString() + " " + sQualityString[iThisQuality] + ": " + ts.ItemsPicked.TotalPerQPerL[iThisQuality, itemLevel].ToString() + " [" + Math.Round(ts.ItemsPicked.TotalPerQPerL[iThisQuality, itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.TotalPerQPerL[iThisQuality, itemLevel] / ts.ItemsPicked.Total) * 100, 2).ToString("0.00") + " %}");
                        }

                        // Any at all this quality?
                    }

                    // For loop on quality
                    LogWriter.WriteLine("");
                    if (totalFollowerItemsIgnored > 0)
                    {
                        LogWriter.WriteLine("  (note: " + totalFollowerItemsIgnored.ToString() + " follower items ignored for being ilvl <60 or blue)");
                    }
                }

                // End of item stats

                // Potion stats
                if (ts.ItemsPicked.TotalPotions > 0)
                {
                    LogWriter.WriteLine("Potion Pickups:");
                    LogWriter.WriteLine("Total potions: " + ts.ItemsPicked.TotalPotions.ToString() + " [" + Math.Round(ts.ItemsPicked.TotalPotions / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    for (int itemLevel = 1; itemLevel <= 63; itemLevel++) if (ts.ItemsPicked.PotionsPerLevel[itemLevel] > 0)
                            LogWriter.WriteLine("- ilvl " + itemLevel.ToString() + ": " + ts.ItemsPicked.PotionsPerLevel[itemLevel].ToString() + " [" + Math.Round(ts.ItemsPicked.PotionsPerLevel[itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.PotionsPerLevel[itemLevel] / ts.ItemsPicked.TotalPotions) * 100, 2).ToString("0.00") + " %}");
                    LogWriter.WriteLine("");
                }

                // End of potion stats

                // Gem stats
                if (ts.ItemsPicked.TotalGems > 0)
                {
                    LogWriter.WriteLine("Gem Pickups:");
                    LogWriter.WriteLine("Total gems: " + ts.ItemsPicked.TotalGems.ToString() + " [" + Math.Round(ts.ItemsPicked.TotalGems / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                    for (int iThisGemType = 0; iThisGemType <= 3; iThisGemType++)
                    {
                        if (ts.ItemsPicked.GemsPerType[iThisGemType] > 0)
                        {
                            LogWriter.WriteLine("- " + sGemString[iThisGemType] + ": " + ts.ItemsPicked.GemsPerType[iThisGemType].ToString() + " [" + Math.Round(ts.ItemsPicked.GemsPerType[iThisGemType] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.GemsPerType[iThisGemType] / ts.ItemsPicked.TotalGems) * 100, 2).ToString("0.00") + " %}");
                            for (int itemLevel = 1; itemLevel <= 63; itemLevel++)
                                if (ts.ItemsPicked.GemsPerTPerL[iThisGemType, itemLevel] > 0)
                                    LogWriter.WriteLine("--- ilvl " + itemLevel.ToString() + " " + sGemString[iThisGemType] + ": " + ts.ItemsPicked.GemsPerTPerL[iThisGemType, itemLevel].ToString() + " [" + Math.Round(ts.ItemsPicked.GemsPerTPerL[iThisGemType, itemLevel] / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour] {" + Math.Round((ts.ItemsPicked.GemsPerTPerL[iThisGemType, itemLevel] / ts.ItemsPicked.TotalGems) * 100, 2).ToString("0.00") + " %}");
                        }

                        // Any at all this quality?
                    }

                    // For loop on quality
                }

                // End of gem stats

                // Key stats
                if (ts.ItemsPicked.TotalInfernalKeys > 0)
                {
                    LogWriter.WriteLine("Infernal Key Pickups:");
                    LogWriter.WriteLine("Total Keys: " + ts.ItemsPicked.TotalInfernalKeys.ToString() + " [" + Math.Round(ts.ItemsPicked.TotalInfernalKeys / ts.TotalRunningTime.TotalHours, 2).ToString("0.00") + " per hour]");
                }

                // End of key stats
                LogWriter.WriteLine("===== End Of Report =====");
                LogWriter.Flush();
                LogStream.Flush();
            }
        }
    }
}
