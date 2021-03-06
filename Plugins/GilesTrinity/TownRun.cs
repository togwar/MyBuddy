﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using GilesTrinity.Notifications;
using GilesTrinity.Settings.Loot;
using GilesTrinity.Technicals;
using Zeta;
using Zeta.Common;
using Zeta.CommonBot;
using Zeta.CommonBot.Profile;
using Zeta.CommonBot.Profile.Common;
using Zeta.Internals.Actors;
using NotificationManager = GilesTrinity.Notifications.NotificationManager;

namespace GilesTrinity
{
    internal class TownRun
    {
        // Whether salvage/sell run should go to a middle-waypoint first to help prevent stucks

        internal static bool bLastTownRunCheckResult = false;
        // Random variables used during item handling and town-runs

        private static int itemDelayLoopLimit = 0;

        private static bool loggedAnythingThisStash = false;

        private static bool loggedJunkThisStash = false;
        internal static string ValueItemStatString = "";
        internal static string junkItemStatString = "";
        internal static bool testingBackpack = false;
        // Safety pauses to make sure we aren't still coming through the portal or selling

        internal static bool bPreStashPauseDone = false;


        internal static HashSet<GilesCachedACDItem> hashGilesCachedKeepItems = new HashSet<GilesCachedACDItem>();

        internal static HashSet<GilesCachedACDItem> hashGilesCachedSalvageItems = new HashSet<GilesCachedACDItem>();

        internal static HashSet<GilesCachedACDItem> hashGilesCachedSellItems = new HashSet<GilesCachedACDItem>();
        // Stash mapper - it's an array representing every slot in your stash, true or false dictating if the slot is free or not

        private static bool[,] StashSlotBlocked = new bool[7, 30];

        internal static float iLowestDurabilityFound = -1;

        internal static bool bNeedsEquipmentRepairs = false;
        // DateTime check to prevent inventory-check spam when looking for repairs being needed
        internal static DateTime timeLastAttemptedTownRun = DateTime.Now;

        internal static bool ShouldUseWalk = false;

        internal static bool bReachedDestination = false;
        // The distance last loop, so we can compare to current distance to work out if we moved

        internal static float lastDistance = 0f;
        // This dictionary stores attempted stash counts on items, to help detect any stash stucks on the same item etc.

        internal static Dictionary<int, int> _dictItemStashAttempted = new Dictionary<int, int>();

        /// <summary>
        /// TownRunCheckOverlord - determine if we should do a town-run or not
        /// </summary>
        /// <param name="ret"></param>
        /// <returns></returns>
        internal static bool TownRunCanRun(object ret)
        {
            using (new PerformanceLogger("TownRunOverlord"))
            {
                GilesTrinity.IsReadyToTownRun = false;

                if (GilesTrinity.BossLevelAreaIDs.Contains(GilesTrinity.PlayerStatus.LevelAreaId))
                    return false;

                if (GilesTrinity.IsReadyToTownRun && GilesTrinity.CurrentTarget != null)
                {
                    TownRunCheckTimer.Reset();
                    return false;
                }

                // Check if we should be forcing a town-run
                if (GilesTrinity.ForceVendorRunASAP || Zeta.CommonBot.Logic.BrainBehavior.IsVendoring)
                {
                    if (!TownRun.bLastTownRunCheckResult)
                    {
                        bPreStashPauseDone = false;
                        if (Zeta.CommonBot.Logic.BrainBehavior.IsVendoring)
                        {
                            DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Looks like we are being asked to force a town-run by a profile/plugin/new DB feature, now doing so.");
                        }
                    }
                    GilesTrinity.IsReadyToTownRun = true;
                }

                // Time safety switch for more advanced town-run checking to prevent CPU spam
                else if (DateTime.Now.Subtract(timeLastAttemptedTownRun).TotalSeconds > 6)
                {
                    timeLastAttemptedTownRun = DateTime.Now;

                    // Check for no space in backpack
                    Vector2 ValidLocation = GilesTrinity.FindValidBackpackLocation(true);
                    if (ValidLocation.X < 0 || ValidLocation.Y < 0)
                    {
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "No more space to pickup a 2-slot item, now running town-run routine.");
                        if (!bLastTownRunCheckResult)
                        {
                            bPreStashPauseDone = false;
                            bLastTownRunCheckResult = true;
                        }
                        GilesTrinity.IsReadyToTownRun = true;
                    }

                    // Check durability percentages
                    foreach (ACDItem tempitem in ZetaDia.Me.Inventory.Equipped)
                    {
                        if (tempitem.BaseAddress != IntPtr.Zero)
                        {
                            if (tempitem.DurabilityPercent <= Zeta.CommonBot.Settings.CharacterSettings.Instance.RepairWhenDurabilityBelow)
                            {
                                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Items may need repair, now running town-run routine.");
                                if (!bLastTownRunCheckResult)
                                {
                                    bPreStashPauseDone = false;
                                }
                                GilesTrinity.IsReadyToTownRun = true;
                            }
                        }
                    }
                }

                if (Zeta.CommonBot.ErrorDialog.IsVisible)
                {
                    GilesTrinity.IsReadyToTownRun = false;
                }

                bLastTownRunCheckResult = GilesTrinity.IsReadyToTownRun;

                // Clear blacklists to triple check any potential targets
                if (GilesTrinity.IsReadyToTownRun)
                {
                    GilesTrinity.hashRGUIDBlacklist3 = new HashSet<int>();
                    GilesTrinity.hashRGUIDBlacklist15 = new HashSet<int>();
                    GilesTrinity.hashRGUIDBlacklist60 = new HashSet<int>();
                    GilesTrinity.hashRGUIDBlacklist90 = new HashSet<int>();
                }

                // Fix for A1 new game with bags full
                if (GilesTrinity.PlayerStatus.LevelAreaId == 19947 && ZetaDia.CurrentQuest.QuestSNO == 87700 && (ZetaDia.CurrentQuest.StepId == -1 || ZetaDia.CurrentQuest.StepId == 42))
                {
                    GilesTrinity.IsReadyToTownRun = false;
                }

                // check for navigation obstacles (never TP near demonic forges, etc)
                if (GilesTrinity.hashNavigationObstacleCache.Any(o => Vector3.Distance(o.Location, GilesTrinity.PlayerStatus.CurrentPosition) < 90f))
                {
                    GilesTrinity.IsReadyToTownRun = false;
                }

                if (GilesTrinity.IsReadyToTownRun && !(Zeta.CommonBot.Logic.BrainBehavior.IsVendoring || GilesTrinity.PlayerStatus.IsInTown))
                {
                    string cantUseTPreason = String.Empty;
                    if (!ZetaDia.Me.CanUseTownPortal(out cantUseTPreason))
                    {
                        DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.UserInformation, "It appears we need to town run but can't: {0}", cantUseTPreason);
                        GilesTrinity.IsReadyToTownRun = false;
                    }
                }


                if ((GilesTrinity.IsReadyToTownRun && TownRunTimerFinished()) || Zeta.CommonBot.Logic.BrainBehavior.IsVendoring)
                    return true;
                else if (GilesTrinity.IsReadyToTownRun && !TownRunCheckTimer.IsRunning)
                {
                    TownRunCheckTimer.Start();
                    loggedAnythingThisStash = false;
                    loggedJunkThisStash = false;
                }
                return false;
            }
        }

        internal static bool TownRunTimerFinished()
        {
            return TownRunCheckTimer.IsRunning && TownRunCheckTimer.ElapsedMilliseconds > 2000;
        }

        internal static bool TownRunTimerRunning()
        {
            return TownRunCheckTimer.IsRunning && TownRunCheckTimer.ElapsedMilliseconds < 2000;
        }

        private static bool lastTownPortalCheckResult = false;
        private static DateTime lastTownPortalCheckTime = DateTime.MinValue;

        /// <summary>
        /// Returns if we're trying to TownRun or if profile tag is UseTownPortalTag
        /// </summary>
        /// <returns></returns>
        internal static bool IsTryingToTownPortal()
        {
            if (DateTime.Now.Subtract(lastTownPortalCheckTime).TotalMilliseconds < GilesTrinity.Settings.Advanced.CacheRefreshRate)
                return lastTownPortalCheckResult;

            bool result = false;

            if (GilesTrinity.IsReadyToTownRun)
                result = true;

            if (GilesTrinity.ForceVendorRunASAP)
                result = true;

            if (TownRunCheckTimer.IsRunning)
                result = true;

            ProfileBehavior CurrentProfileBehavior = null;

            try
            {
                if (ProfileManager.CurrentProfileBehavior != null)
                    CurrentProfileBehavior = ProfileManager.CurrentProfileBehavior;
            }
            catch (Exception ex)
            {
                DbHelper.Log(LogCategory.UserInformation, "Exception while checking for TownPortal!");
                DbHelper.Log(LogCategory.GlobalHandler, ex.ToString());
            }
            if (ProfileManager.CurrentProfileBehavior != null)
            {
                Type profileBehaviortype = CurrentProfileBehavior.GetType();
                if (profileBehaviortype != null && (profileBehaviortype == typeof(UseTownPortalTag) || profileBehaviortype == typeof(WaitTimerTag) || profileBehaviortype == typeof(XmlTags.TrinityTownRun)))
                {
                    result = true;
                }
            }

            if (Zeta.CommonBot.Logic.BrainBehavior.IsVendoring)
                result = true;


            lastTownPortalCheckTime = DateTime.Now;
            lastTownPortalCheckResult = result;
            return result;
        }

        /// <summary>
        /// Randomize the timer between stashing/salvaging etc.
        /// </summary>
        internal static void RandomizeTheTimer()
        {
            Random rndNum = new Random(int.Parse(Guid.NewGuid().ToString().Substring(0, 8), NumberStyles.HexNumber));
            int rnd = rndNum.Next(7);
            itemDelayLoopLimit = 4 + rnd + ((int)Math.Floor(((double)(BotMain.TicksPerSecond / 2))));
        }

        internal static Stopwatch randomTimer = new Stopwatch();
        internal static Random timerRandomizer = new Random();
        internal static int randomTimerVal = -1;

        internal static void SetStartRandomTimer()
        {
            if (!randomTimer.IsRunning)
            {
                randomTimerVal = timerRandomizer.Next(500, 1500);
                randomTimer.Start();
            }
        }

        internal static void StopRandomTimer()
        {
            randomTimer.Reset();
        }

        internal static bool RandomTimerIsDone()
        {
            return (randomTimer.IsRunning && randomTimer.ElapsedMilliseconds >= randomTimerVal);
        }

        internal static bool RandomTimerIsNotDone()
        {
            return (randomTimer.IsRunning && randomTimer.ElapsedMilliseconds < randomTimerVal);
        }

        /// <summary>
        /// Sell Validation - Determines what should or should not be sold to vendor
        /// </summary>
        /// <param name="thisinternalname"></param>
        /// <param name="thislevel"></param>
        /// <param name="thisquality"></param>
        /// <param name="thisdbitemtype"></param>
        /// <param name="thisfollowertype"></param>
        /// <returns></returns>

        internal static bool SellValidation(GilesCachedACDItem cItem)
        {
            // Check this isn't something we want to salvage
            if (SalvageValidation(cItem))
                return false;

            if (StashValidation(cItem, cItem.AcdItem))
                return false;

            if (ItemManager.Current.ItemIsProtected(cItem.AcdItem))
            {
                return false;
            }

            GItemType thisGilesItemType = GilesTrinity.DetermineItemType(cItem.InternalName, cItem.DBItemType, cItem.FollowerType);
            GItemBaseType thisGilesBaseType = GilesTrinity.DetermineBaseType(thisGilesItemType);
            switch (thisGilesBaseType)
            {
                case GItemBaseType.WeaponRange:
                case GItemBaseType.WeaponOneHand:
                case GItemBaseType.WeaponTwoHand:
                case GItemBaseType.Armor:
                case GItemBaseType.Offhand:
                case GItemBaseType.Jewelry:
                case GItemBaseType.FollowerItem:
                    return true;
                case GItemBaseType.Gem:
                case GItemBaseType.Misc:
                    if (thisGilesItemType == GItemType.CraftingPlan)
                        return true;
                    else
                        return false;
                case GItemBaseType.Unknown:
                    return false;
            }

            // Switch giles base item type
            return false;
        }

        /// <summary>
        /// Salvage Validation - Determines what should or should not be salvaged
        /// </summary>
        /// <param name="thisinternalname"></param>
        /// <param name="thislevel"></param>
        /// <param name="thisquality"></param>
        /// <param name="thisdbitemtype"></param>
        /// <param name="thisfollowertype"></param>
        /// <returns></returns>

        internal static bool SalvageValidation(GilesCachedACDItem cItem)
        {
            GItemType thisGilesItemType = GilesTrinity.DetermineItemType(cItem.InternalName, cItem.DBItemType, cItem.FollowerType);
            GItemBaseType thisGilesBaseType = GilesTrinity.DetermineBaseType(thisGilesItemType);

            // Take Salvage Option corresponding to ItemLevel
            SalvageOption salvageOption = GetSalvageOption(cItem.Quality);

            if (ItemManager.Current.ItemIsProtected(cItem.AcdItem))
            {
                return false;
            }

            if (cItem.Quality >= ItemQuality.Legendary && salvageOption == SalvageOption.InfernoOnly && cItem.Level >= 60)
                return true;

            switch (thisGilesBaseType)
            {
                case GItemBaseType.WeaponRange:
                case GItemBaseType.WeaponOneHand:
                case GItemBaseType.WeaponTwoHand:
                case GItemBaseType.Armor:
                case GItemBaseType.Offhand:
                    return ((cItem.Level >= 61 && salvageOption == SalvageOption.InfernoOnly) || salvageOption == SalvageOption.All);
                case GItemBaseType.Jewelry:
                    return ((cItem.Level >= 59 && salvageOption == SalvageOption.InfernoOnly) || salvageOption == SalvageOption.All);
                case GItemBaseType.FollowerItem:
                    return ((cItem.Level >= 60 && salvageOption == SalvageOption.InfernoOnly) || salvageOption == SalvageOption.All);
                case GItemBaseType.Gem:
                case GItemBaseType.Misc:
                case GItemBaseType.Unknown:
                    return false;
            }

            // Switch giles base item type
            return false;
        }


        internal static SalvageOption GetSalvageOption(ItemQuality thisquality)
        {
            if (thisquality >= ItemQuality.Magic1 && thisquality <= ItemQuality.Magic3)
            {
                return GilesTrinity.Settings.Loot.TownRun.SalvageBlueItemOption;
            }
            else if (thisquality >= ItemQuality.Rare4 && thisquality <= ItemQuality.Rare6)
            {
                return GilesTrinity.Settings.Loot.TownRun.SalvageYellowItemOption;
            }
            else if (thisquality >= ItemQuality.Legendary)
            {
                return GilesTrinity.Settings.Loot.TownRun.SalvageLegendaryItemOption;
            }
            return SalvageOption.None;
        }

        internal static Stopwatch TownRunCheckTimer = new Stopwatch();


        internal static bool StashValidation(GilesCachedACDItem cItem)
        {
            return StashValidation(cItem, cItem.AcdItem);
        }

        /// <summary>
        /// Determines if we should stash this item or not
        /// </summary>
        /// <param name="cItem"></param>
        /// <param name="item"></param>
        /// <returns></returns>

        internal static bool StashValidation(GilesCachedACDItem cItem, ACDItem item)
        {
            if (ItemManager.Current.ItemIsProtected(cItem.AcdItem))
            {
                return false;
            }

            bool shouldStashItem = GilesTrinity.ShouldWeStashThis(cItem, item);
            return shouldStashItem;
        }

        /// <summary>
        /// Pre Stash prepares stuff for our stash run
        /// </summary>
        /// <param name="ret"></param>
        /// <returns></returns>

        internal static void SendEmailNotification()
        {
            if (GilesTrinity.Settings.Notification.MailEnabled && NotificationManager.EmailMessage.Length > 0)
                NotificationManager.SendEmail(
                    GilesTrinity.Settings.Notification.EmailAddress,
                    GilesTrinity.Settings.Notification.EmailAddress,
                    "New DB stash loot - " + ZetaDia.Service.CurrentHero.BattleTagName,
                    NotificationManager.EmailMessage.ToString(),
                    NotificationManager.SmtpServer,
                    GilesTrinity.Settings.Notification.EmailPassword);
            NotificationManager.EmailMessage.Clear();
        }

        internal static void SendMobileNotifications()
        {
            while (NotificationManager.pushQueue.Count > 0)
            {
                NotificationManager.SendNotification(NotificationManager.pushQueue.Dequeue());
            }
        }


        /// <summary>
        /// Log the nice items we found and stashed
        /// </summary>
        /// <param name="thisgooditem"></param>
        /// <param name="thisgilesbaseitemtype"></param>
        /// <param name="thisgilesitemtype"></param>
        /// <param name="ithisitemvalue"></param>
        internal static void LogGoodItems(GilesCachedACDItem thisgooditem, GItemBaseType thisgilesbaseitemtype, GItemType thisgilesitemtype, double ithisitemvalue)
        {
            FileStream LogStream = null;
            try
            {
                LogStream = File.Open(Path.Combine(FileManager.LoggingPath, "StashLog - " + ZetaDia.Me.ActorClass.ToString() + ".log"), FileMode.Append, FileAccess.Write, FileShare.Read);

                //TODO : Change File Log writing
                using (StreamWriter LogWriter = new StreamWriter(LogStream))
                {
                    if (!loggedAnythingThisStash)
                    {
                        loggedAnythingThisStash = true;
                        LogWriter.WriteLine(DateTime.Now.ToString() + ":");
                        LogWriter.WriteLine("====================");
                    }
                    string sLegendaryString = "";
                    bool bShouldNotify = false;
                    if (thisgooditem.Quality >= ItemQuality.Legendary)
                    {
                        if (!GilesTrinity.Settings.Notification.LegendaryScoring)
                            bShouldNotify = true;
                        else if (GilesTrinity.Settings.Notification.LegendaryScoring && GilesTrinity.CheckScoreForNotification(thisgilesbaseitemtype, ithisitemvalue))
                            bShouldNotify = true;
                        if (bShouldNotify)
                            NotificationManager.AddNotificationToQueue(thisgooditem.RealName + " [" + thisgilesitemtype.ToString() +
                                "] (Score=" + ithisitemvalue.ToString() + ". " + ValueItemStatString + ")",
                                ZetaDia.Service.CurrentHero.Name + " new legendary!", ProwlNotificationPriority.Emergency);
                        sLegendaryString = " {legendary item}";

                        // Change made by bombastic
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "+=+=+=+=+=+=+=+=+ LEGENDARIO ENCONTRADO +=+=+=+=+=+=+=+=+");
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "+  Nome:       {0} ({1})", thisgooditem.RealName, thisgilesitemtype);
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "+  Pontos:       {0:0}", ithisitemvalue);
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "+  Atributos: {0}", ValueItemStatString);
                        DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+");
                    }
                    else
                    {

                        // Check for non-legendary notifications
                        bShouldNotify = GilesTrinity.CheckScoreForNotification(thisgilesbaseitemtype, ithisitemvalue);
                        if (bShouldNotify)
                            NotificationManager.AddNotificationToQueue(thisgooditem.RealName + " [" + thisgilesitemtype.ToString() + "] (Score=" + ithisitemvalue.ToString() + ". " + ValueItemStatString + ")", ZetaDia.Service.CurrentHero.Name + " new item!", ProwlNotificationPriority.Emergency);
                    }
                    if (bShouldNotify)
                    {
                        NotificationManager.EmailMessage.AppendLine(thisgilesbaseitemtype.ToString() + " - " + thisgilesitemtype.ToString() + " '" + thisgooditem.RealName + "'. Score = " + Math.Round(ithisitemvalue).ToString() + sLegendaryString)
                            .AppendLine("  " + ValueItemStatString)
                            .AppendLine();
                    }
                    LogWriter.WriteLine(thisgilesbaseitemtype.ToString() + " - " + thisgilesitemtype.ToString() + " '" + thisgooditem.RealName + "'. Score = " + Math.Round(ithisitemvalue).ToString() + sLegendaryString);
                    LogWriter.WriteLine("  " + ValueItemStatString);
                    LogWriter.WriteLine("");
                }
                LogStream.Close();
            }
            catch (IOException)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Fatal Error: File access error for stash log file.");
                if (LogStream != null)
                    LogStream.Close();
            }
        }

        /// <summary>
        /// Log the rubbish junk items we salvaged or sold
        /// </summary>
        /// <param name="thisgooditem"></param>
        /// <param name="thisgilesbaseitemtype"></param>
        /// <param name="thisgilesitemtype"></param>
        /// <param name="ithisitemvalue"></param>
        internal static void LogJunkItems(GilesCachedACDItem thisgooditem, GItemBaseType thisgilesbaseitemtype, GItemType thisgilesitemtype, double ithisitemvalue)
        {
            FileStream LogStream = null;
            try
            {
                LogStream = File.Open(Path.Combine(FileManager.LoggingPath, "JunkLog - " + ZetaDia.Me.ActorClass.ToString() + ".log"), FileMode.Append, FileAccess.Write, FileShare.Read);
                using (StreamWriter LogWriter = new StreamWriter(LogStream))
                {
                    if (!loggedJunkThisStash)
                    {
                        loggedJunkThisStash = true;
                        LogWriter.WriteLine(DateTime.Now.ToString() + ":");
                        LogWriter.WriteLine("====================");
                    }
                    string sLegendaryString = "";
                    if (thisgooditem.Quality >= ItemQuality.Legendary)
                        sLegendaryString = " {legendary item}";
                    LogWriter.WriteLine(thisgilesbaseitemtype.ToString() + " - " + thisgilesitemtype.ToString() + " '" + thisgooditem.RealName + "'. Score = " + ithisitemvalue.ToString("0") + sLegendaryString);
                    if (junkItemStatString != "")
                        LogWriter.WriteLine("  " + junkItemStatString);
                    else
                        LogWriter.WriteLine("  (no scorable attributes)");
                    LogWriter.WriteLine("");
                }
                LogStream.Close();
            }
            catch (IOException)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Fatal Error: File access error for junk log file.");
                if (LogStream != null)
                    LogStream.Close();
            }
        }


        internal static Vector2 SortingFindLocationStash(bool isOriginalTwoSlot, bool endOfStash = false)
        {
            int iPointX = -1;
            int iPointY = -1;
            for (int iRow = 0; iRow <= 29; iRow++)
            {
                for (int iColumn = 0; iColumn <= 6; iColumn++)
                {
                    if (!StashSlotBlocked[iColumn, iRow])
                    {
                        bool bNotEnoughSpace = false;
                        if (iRow != 9 && iRow != 19 && iRow != 29)
                        {
                            bNotEnoughSpace = (isOriginalTwoSlot && StashSlotBlocked[iColumn, iRow + 1]);
                        }
                        else
                        {
                            if (isOriginalTwoSlot)
                                bNotEnoughSpace = true;
                        }
                        if (!bNotEnoughSpace)
                        {
                            iPointX = iColumn;
                            iPointY = iRow;
                            if (!endOfStash)
                                goto FoundStashLocation;
                        }
                    }
                }
            }
        FoundStashLocation:
            if ((iPointX < 0) || (iPointY < 0))
            {
                return new Vector2(-1, -1);
            }
            return new Vector2(iPointX, iPointY);
        }

        /// <summary>
        /// Sorts the stash
        /// </summary>
        internal static void SortStash()
        {

            // Try and update the player-data
            //ZetaDia.Actors.Update();

            // Check we can get the player dynamic ID
            int iPlayerDynamicID = -1;
            try
            {
                iPlayerDynamicID = GilesTrinity.PlayerStatus.MyDynamicID;
            }
            catch
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Failure getting your player data from DemonBuddy, abandoning the sort!");
                return;
            }
            if (iPlayerDynamicID == -1)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Failure getting your player data, abandoning the sort!");
                return;
            }

            // List used for all the sorting
            List<GilesStashSort> listSortMyStash = new List<GilesStashSort>();

            // Map out the backpack free slots
            for (int iRow = 0; iRow <= 5; iRow++)
                for (int iColumn = 0; iColumn <= 9; iColumn++)
                    GilesTrinity.BackpackSlotBlocked[iColumn, iRow] = false;

            foreach (ACDItem item in ZetaDia.Me.Inventory.Backpack)
            {
                int inventoryRow = item.InventoryRow;
                int inventoryColumn = item.InventoryColumn;

                // Mark this slot as not-free
                GilesTrinity.BackpackSlotBlocked[inventoryColumn, inventoryRow] = true;

                // Try and reliably find out if this is a two slot item or not
                if (item.IsTwoSquareItem && inventoryRow < 5)
                {
                    GilesTrinity.BackpackSlotBlocked[inventoryColumn, inventoryRow + 1] = true;
                }
            }

            // Map out the stash free slots
            for (int iRow = 0; iRow <= 29; iRow++)
                for (int iColumn = 0; iColumn <= 6; iColumn++)
                    StashSlotBlocked[iColumn, iRow] = false;

            // Block off the entire of any "protected stash pages"
            foreach (int iProtPage in Zeta.CommonBot.Settings.CharacterSettings.Instance.ProtectedStashPages)
                for (int iProtRow = 0; iProtRow <= 9; iProtRow++)
                    for (int iProtColumn = 0; iProtColumn <= 6; iProtColumn++)
                        StashSlotBlocked[iProtColumn, iProtRow + (iProtPage * 10)] = true;

            // Remove rows we don't have
            for (int iRow = (ZetaDia.Me.NumSharedStashSlots / 7); iRow <= 29; iRow++)
                for (int iColumn = 0; iColumn <= 6; iColumn++)
                    StashSlotBlocked[iColumn, iRow] = true;

            // Map out all the items already in the stash and store their scores if appropriate
            foreach (ACDItem item in ZetaDia.Me.Inventory.StashItems)
            {
                int inventoryRow = item.InventoryRow;
                int inventoryColumn = item.InventoryColumn;

                // Mark this slot as not-free
                StashSlotBlocked[inventoryColumn, inventoryRow] = true;

                // Try and reliably find out if this is a two slot item or not
                GItemType itemType = GilesTrinity.DetermineItemType(item.InternalName, item.ItemType, item.FollowerSpecialType);

                if (item.IsTwoSquareItem && inventoryRow != 19 && inventoryRow != 9 && inventoryRow != 29)
                {
                    StashSlotBlocked[inventoryColumn, inventoryRow + 1] = true;
                }
                else if (item.IsTwoSquareItem && (inventoryRow == 19 || inventoryRow == 9 || inventoryRow == 29))
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "WARNING: There was an error reading your stash, abandoning the process.");
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Always make sure you empty your backpack, open the stash, then RESTART DEMONBUDDY before sorting!");
                    return;
                }
                GilesCachedACDItem thiscacheditem = new GilesCachedACDItem(item, item.InternalName, item.Name, item.Level, item.ItemQualityLevel, item.Gold, item.GameBalanceId,
                    item.DynamicId, item.Stats.WeaponDamagePerSecond, item.IsOneHand, item.IsTwoHand, item.DyeType, item.ItemType, item.ItemBaseType, item.FollowerSpecialType,
                    item.IsUnidentified, item.ItemStackQuantity, item.Stats);

                double ItemValue = ItemValuation.ValueThisItem(thiscacheditem, itemType);
                double NeedScore = GilesTrinity.ScoreNeeded(item.ItemBaseType);


                // Ignore stackable items
                // TODO check if item.MaxStackCount is 0 on non stackable items or 1
                if (!(item.MaxStackCount > 1) && itemType != GItemType.StaffOfHerding)
                {
                    listSortMyStash.Add(new GilesStashSort(((ItemValue / NeedScore) * 1000), 1, inventoryColumn, inventoryRow, item.DynamicId, item.IsTwoSquareItem));
                }
            }


            // Sort the items in the stash by their row number, lowest to highest
            listSortMyStash.Sort((p1, p2) => p1.InventoryRow.CompareTo(p2.InventoryRow));

            // Now move items into your backpack until full, then into the END of the stash
            Vector2 vFreeSlot;

            // Loop through all stash items
            foreach (GilesStashSort thisstashsort in listSortMyStash)
            {
                vFreeSlot = GilesTrinity.SortingFindLocationBackpack(thisstashsort.bIsTwoSlot);
                int iStashOrPack = 1;
                if (vFreeSlot.X == -1 || vFreeSlot.Y == -1)
                {
                    vFreeSlot = SortingFindLocationStash(thisstashsort.bIsTwoSlot, true);
                    if (vFreeSlot.X == -1 || vFreeSlot.Y == -1)
                        continue;
                    iStashOrPack = 2;
                }
                if (iStashOrPack == 1)
                {
                    ZetaDia.Me.Inventory.MoveItem(thisstashsort.iDynamicID, iPlayerDynamicID, InventorySlot.PlayerBackpack, (int)vFreeSlot.X, (int)vFreeSlot.Y);
                    StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow] = false;
                    if (thisstashsort.bIsTwoSlot)
                        StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow + 1] = false;
                    GilesTrinity.BackpackSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y] = true;
                    if (thisstashsort.bIsTwoSlot)
                        GilesTrinity.BackpackSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y + 1] = true;
                    thisstashsort.iInventoryColumn = (int)vFreeSlot.X;
                    thisstashsort.InventoryRow = (int)vFreeSlot.Y;
                    thisstashsort.iStashOrPack = 2;
                }
                else
                {
                    ZetaDia.Me.Inventory.MoveItem(thisstashsort.iDynamicID, iPlayerDynamicID, InventorySlot.PlayerSharedStash, (int)vFreeSlot.X, (int)vFreeSlot.Y);
                    StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow] = false;
                    if (thisstashsort.bIsTwoSlot)
                        StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow + 1] = false;
                    StashSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y] = true;
                    if (thisstashsort.bIsTwoSlot)
                        StashSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y + 1] = true;
                    thisstashsort.iInventoryColumn = (int)vFreeSlot.X;
                    thisstashsort.InventoryRow = (int)vFreeSlot.Y;
                    thisstashsort.iStashOrPack = 1;
                }
                Thread.Sleep(150);
            }

            // Now sort the items by their score, highest to lowest
            listSortMyStash.Sort((p1, p2) => p1.dStashScore.CompareTo(p2.dStashScore));
            listSortMyStash.Reverse();

            // Now fill the stash in ordered-order
            foreach (GilesStashSort thisstashsort in listSortMyStash)
            {
                vFreeSlot = SortingFindLocationStash(thisstashsort.bIsTwoSlot, false);
                if (vFreeSlot.X == -1 || vFreeSlot.Y == -1)
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Failure trying to put things back into stash, no stash slots free? Abandoning...");
                    return;
                }
                ZetaDia.Me.Inventory.MoveItem(thisstashsort.iDynamicID, iPlayerDynamicID, InventorySlot.PlayerSharedStash, (int)vFreeSlot.X, (int)vFreeSlot.Y);
                if (thisstashsort.iStashOrPack == 1)
                {
                    StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow] = false;
                    if (thisstashsort.bIsTwoSlot)
                        StashSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow + 1] = false;
                }
                else
                {
                    GilesTrinity.BackpackSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow] = false;
                    if (thisstashsort.bIsTwoSlot)
                        GilesTrinity.BackpackSlotBlocked[thisstashsort.iInventoryColumn, thisstashsort.InventoryRow + 1] = false;
                }
                StashSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y] = true;
                if (thisstashsort.bIsTwoSlot)
                    StashSlotBlocked[(int)vFreeSlot.X, (int)vFreeSlot.Y + 1] = true;
                thisstashsort.iStashOrPack = 1;
                thisstashsort.InventoryRow = (int)vFreeSlot.Y;
                thisstashsort.iInventoryColumn = (int)vFreeSlot.X;
                Thread.Sleep(150);
            }
            DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Stash sorted!");
        }

    }
}
