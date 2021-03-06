﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using GilesTrinity.DbProvider;
using GilesTrinity.Technicals;
using Zeta;
using Zeta.CommonBot;
using Zeta.CommonBot.Profile;
using Zeta.CommonBot.Profile.Common;
using Zeta.TreeSharp;

namespace GilesTrinity
{
    public class GoldInactivity
    {
        private static int lastKnowCoin = 0;
        private static DateTime lastCheckBag = DateTime.MinValue;
        private static DateTime lastRefreshCoin = DateTime.MinValue;

        /// <summary>
        /// Resets the gold inactivity timer
        /// </summary>
        internal static void ResetCheckGold()
        {
            lastCheckBag = DateTime.Now;
            lastRefreshCoin = DateTime.Now;
            lastKnowCoin = 0;
        }

        /// <summary>
        /// Determines whether or not to leave the game based on the gold inactivity timer
        /// </summary>
        /// <returns></returns>
        internal static bool GoldInactive()
        {
            if (!GilesTrinity.Settings.Advanced.GoldInactivityEnabled)
            {
                // timer isn't enabled so move along!
                ResetCheckGold();
                return false;
            }
            try
            {
                if (!ZetaDia.IsInGame)
                {
                    ResetCheckGold(); //If not in game, reset the timer
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Not in game, gold inactivity reset", 0);
                    return false;
                }
                if (ZetaDia.IsLoadingWorld)
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Loading world, gold inactivity reset", 0);
                    return false;
                }
                if ((DateTime.Now.Subtract(lastCheckBag).TotalSeconds < 5))
                {
                    return false;
                }

                // sometimes bosses take a LONG time
                if (GilesTrinity.CurrentTarget != null && GilesTrinity.CurrentTarget.IsBoss)
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Current target is boss, gold inactivity reset", 0);
                    ResetCheckGold();
                    return false;
                }

                if (TownRun.IsTryingToTownPortal())
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Trying to town portal, gold inactivity reset", 0);
                    ResetCheckGold();
                    return false;
                }
                // Don't go inactive on WaitTimer tags
                ProfileBehavior c = null;
                try
                {
                    if (ProfileManager.CurrentProfileBehavior != null)
                        c = ProfileManager.CurrentProfileBehavior;
                }
                catch { }
                if (c != null && c.GetType() == typeof(WaitTimerTag))
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Wait timer tag, gold inactivity reset", 0);
                    ResetCheckGold();
                    return false;
                }



                lastCheckBag = DateTime.Now;
                int currentcoin = GilesTrinity.PlayerStatus.Coinage;

                if (currentcoin != lastKnowCoin && currentcoin != 0)
                {
                    lastRefreshCoin = DateTime.Now;
                    lastKnowCoin = currentcoin;
                }
                int notpickupgoldsec = Convert.ToInt32(DateTime.Now.Subtract(lastRefreshCoin).TotalSeconds);
                if (notpickupgoldsec >= GilesTrinity.Settings.Advanced.GoldInactivityTimer)
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Gold inactivity after {0}s. Sending abort.", notpickupgoldsec);
                    lastRefreshCoin = DateTime.Now;
                    lastKnowCoin = currentcoin;
                    notpickupgoldsec = 0;
                    return true;
                }
                else if (notpickupgoldsec > 0)
                {
                    DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Gold unchanged for {0}s", notpickupgoldsec);
                }
            }
            catch (Exception e)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, e.Message);
            }
            DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "Gold inactivity error - no result", 0);
            return false;
        }

        private static bool isLeavingGame = false;
        private static bool leaveGameInitiated = false;

        private static Stopwatch leaveGameTimer = new Stopwatch();

        /// <summary>
        /// Leaves the game if gold inactivity timer is tripped
        /// </summary>
        internal static bool GoldInactiveLeaveGame()
        {
            if (leaveGameTimer.IsRunning && leaveGameTimer.ElapsedMilliseconds < 12000)
            {
                return true;
            }

            // Fixes a race condition crash. Zomg!
            Thread.Sleep(5000);

            if (!ZetaDia.IsInGame || !ZetaDia.Me.IsValid || ZetaDia.IsLoadingWorld)
            {
                isLeavingGame = false;
                leaveGameInitiated = false;
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "GoldInactiveLeaveGame called but not in game!");
                return false;
            }

            if (!BotMain.IsRunning)
            {
                return false;
            }

            if (!isLeavingGame && !leaveGameInitiated)
            {
                // Exit the game and reload the profile
                PlayerMover.timeLastRestartedGame = DateTime.Now;
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Gold Inactivity timer tripped - Anti-stuck measures exiting current game.");
                // Reload this profile
                ProfileManager.Load(Zeta.CommonBot.ProfileManager.CurrentProfile.Path);
                GilesTrinity.ResetEverythingNewGame();
                isLeavingGame = true;
                return true;
            }

            if (!leaveGameInitiated && isLeavingGame)
            {
                leaveGameTimer.Start();
                ZetaDia.Service.Party.LeaveGame();
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "GoldInactiveLeaveGame initiated LeaveGame");
                return true;
            }            

            if (DateTime.Now.Subtract(PlayerMover.timeLastRestartedGame).TotalSeconds <= 12)
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "GoldInactiveLeaveGame waiting for LeaveGame");
                return true;
            }

            isLeavingGame = false;
            leaveGameInitiated = false;
            DbHelper.Log(TrinityLogLevel.Normal, LogCategory.GlobalHandler, "GoldInactiveLeaveGame finished");

            return false;
        }

    }
}
