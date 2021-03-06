﻿using GilesTrinity.Settings.Combat;
using GilesTrinity.Technicals;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Zeta;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.CommonBot;
using Zeta.Internals.Actors;
using Zeta.Internals.SNO;
namespace GilesTrinity
{
    public partial class GilesTrinity : IPlugin
    {
        /// <summary>
        /// For backwards compatability
        /// </summary>
        public static void RefreshDiaObjects()
        {
            // Framelock should happen in the MainLoop, where we read all the actual ACD's
            RefreshDiaObjectCache();
        }

        /// <summary>
        /// This method will add and update necessary information about all available actors. Determines GilesObjectType, sets ranges, updates blacklists, determines avoidance, kiting, target weighting
        /// and the result is we will have a new target for the Target Handler. Returns true if the cache was refreshed.
        /// </summary>
        /// <returns>True if the cache was updated</returns>
        public static bool RefreshDiaObjectCache(bool forceUpdate = false)
        {
            using (new PerformanceLogger("RefreshDiaObjectCache"))
            {
                if (DateTime.Now.Subtract(LastRefreshedCache).TotalMilliseconds < Settings.Advanced.CacheRefreshRate && !forceUpdate)
                {
                    if (!UpdateCurrentTarget())
                        return false;
                }
                LastRefreshedCache = DateTime.Now;

                using (new PerformanceLogger("RefreshDiaObjectCache.UpdateBlock"))
                {
                    GenericCache.MaintainCache();
                    GenericBlacklist.MaintainBlacklist();

                    using (ZetaDia.Memory.AcquireFrame())
                    {
                        // Update player-data cache, including buffs
                        GilesPlayerCache.UpdateCachedPlayerData();

                        if (PlayerStatus.CurrentHealthPct <= 0)
                        {
                            return false;
                        }

                        if (Settings.Combat.Misc.UseNavMeshTargeting && !gp.CanStandAt(gp.WorldToGrid(PlayerStatus.CurrentPosition.ToVector2())))
                        {
                            NavHelper.UpdateSearchGridProvider();
                        }


                        RefreshCacheInit();

                        // Now pull up all the data and store anything we want to handle in the super special cache list
                        // Also use many cache dictionaries to minimize DB<->D3 memory hits, and speed everything up a lot
                        RefreshCacheMainLoop();
                    }
                }

                // Reduce ignore-for-loops counter
                if (IgnoreTargetForLoops > 0)
                    IgnoreTargetForLoops--;
                // If we have an avoidance under our feet, then create a new object which contains a safety point to move to
                // But only if we aren't force-cancelling avoidance for XX time
                bool bFoundSafeSpot = false;

                using (new PerformanceLogger("RefreshDiaObjectCache.AvoidanceCheck"))
                {
                    // Note that if treasure goblin level is set to kamikaze, even avoidance moves are disabled to reach the goblin!
                    if (StandingInAvoidance && (!AnyTreasureGoblinsPresent || Settings.Combat.Misc.GoblinPriority <= GoblinPriority.Prioritize) &&
                        DateTime.Now.Subtract(timeCancelledEmergencyMove).TotalMilliseconds >= cancelledEmergencyMoveForMilliseconds)
                    {
                        Vector3 vAnySafePoint = NavHelper.FindSafeZone(false, 1, PlayerStatus.CurrentPosition, true);
                        // Ignore avoidance stuff if we're incapacitated or didn't find a safe spot we could reach
                        if (vAnySafePoint != vNullLocation)
                        {
                            if (Settings.Advanced.LogCategories.HasFlag(LogCategory.Movement))
                            {
                                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Kiting Avoidance: {0} Distance: {1:0} Direction: {2:0}, Health%={3:0.00}, KiteDistance: {4:0}",
                                    vAnySafePoint, vAnySafePoint.Distance(Me.Position), MathUtil.GetHeading(MathUtil.FindDirectionDegree(Me.Position, vAnySafePoint)),
                                    PlayerStatus.CurrentHealthPct, PlayerKiteDistance);
                            }

                            bFoundSafeSpot = true;
                            CurrentTarget = new GilesObject()
                                {
                                    Position = vAnySafePoint,
                                    Type = GObjectType.Avoidance,
                                    Weight = 20000,
                                    CentreDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vAnySafePoint),
                                    RadiusDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vAnySafePoint),
                                    InternalName = "GilesSafePoint"
                                }; ;
                        }
                        //else
                        //{
                        //    // Didn't find any safe spot we could reach, so don't look for any more safe spots for at least 2.8 seconds
                        //    cancelledEmergencyMoveForMilliseconds = 2800;
                        //    timeCancelledEmergencyMove = DateTime.Now;
                        //    DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Unable to find kite location, canceling emergency movement for {0}ms", cancelledEmergencyMoveForMilliseconds);
                        //}
                    }
                }
                /*
                 * Give weights to objects
                 */
                // Special flag for special whirlwind circumstances
                bAnyNonWWIgnoreMobsInRange = false;
                // Now give each object a weight *IF* we aren't skipping direcly to a safe-spot
                if (!bFoundSafeSpot)
                {
                    RefreshDiaGetWeights();
                    RefreshSetKiting(ref vKitePointAvoid, NeedToKite, ref TryToKite);
                }
                // Not heading straight for a safe-spot?
                // No valid targets but we were told to stay put?
                if (CurrentTarget == null && ShouldStayPutDuringAvoidance && !StandingInAvoidance)
                {
                    CurrentTarget = new GilesObject()
                                        {
                                            Position = PlayerStatus.CurrentPosition,
                                            Type = GObjectType.Avoidance,
                                            Weight = 20000,
                                            CentreDistance = 2f,
                                            RadiusDistance = 2f,
                                            InternalName = "GilesStayPutPoint"
                                        };
                    DbHelper.Log(TrinityLogLevel.Debug, LogCategory.CacheManagement, "Staying Put During Avoidance");
                }
                using (new PerformanceLogger("RefreshDiaObjectCache.FinalChecks"))
                {
                    // force to stay put if we want to town run and there's no target
                    if (CurrentTarget == null && ForceVendorRunASAP)
                    {
                        bDontMoveMeIAmDoingShit = true;
                    }

                    // Still no target, let's see if we should backtrack or wait for wrath to come off cooldown...
                    if (CurrentTarget == null)
                    {
                        RefreshDoBackTrack();
                    }
                    // Still no target, let's end it all!
                    if (CurrentTarget == null)
                    {
                        return true;
                    }
                    // Ok record the time we last saw any unit at all
                    if (CurrentTarget.Type == GObjectType.Unit)
                    {
                        lastHadUnitInSights = DateTime.Now;
                        // And record when we last saw any form of elite
                        if (CurrentTarget.IsBoss || CurrentTarget.IsEliteRareUnique || CurrentTarget.IsTreasureGoblin)
                            lastHadEliteUnitInSights = DateTime.Now;
                    }
                    // Record the last time our target changed
                    if (CurrentTargetRactorGUID != CurrentTarget.RActorGuid)
                    {
                        RecordTargetHistory();
                        DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Weight, "Found New Target - {0} CurrentTargetRactorGUID: {1} CurrentTarget.RActorGuid: {2}",
                                        DateTime.Now, CurrentTargetRactorGUID, CurrentTarget.RActorGuid);
                        dateSincePickedTarget = DateTime.Now;
                        iTargetLastHealth = 0f;
                    }
                    else
                    {
                        // We're sticking to the same target, so update the target's health cache to check for stucks
                        if (CurrentTarget.Type == GObjectType.Unit)
                        {
                            // Check if the health has changed, if so update the target-pick time before we blacklist them again
                            if (CurrentTarget.HitPointsPct != iTargetLastHealth)
                            {
                                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Weight, "Keeping Target {0} - CurrentTarget.iHitPoints: {1:0.00}  iTargetLastHealth: {2:0.00} ",
                                                CurrentTarget.RActorGuid, CurrentTarget.HitPointsPct, iTargetLastHealth);
                                dateSincePickedTarget = DateTime.Now;
                            }
                            // Now store the target's last-known health
                            iTargetLastHealth = CurrentTarget.HitPointsPct;
                        }
                    }
                }
                // We have a target and the cached was refreshed
                return true;
            }
        }

        private static bool UpdateCurrentTarget()
        {
            // Return true if we need to refresh objects and get a new target
            bool forceUpdate = false;
            try
            {
                PlayerStatus.CurrentPosition = ZetaDia.Me.Position;
                PlayerStatus.CurrentHealthPct = ZetaDia.Me.HitpointsCurrentPct;

                if (CurrentTarget != null && CurrentTarget.Type == GObjectType.Unit && CurrentTarget.Unit != null && CurrentTarget.Unit.IsValid)
                {
                    DiaUnit unit = CurrentTarget.Unit;
                    if (unit.IsDead)
                    {
                        DbHelper.Log(TrinityLogLevel.Debug, LogCategory.Behavior, "CurrentTarget is dead, setting null");
                        CurrentTarget = null;
                        forceUpdate = true;
                    }
                    else
                    {
                        CurrentTarget.Position = unit.Position;
                        CurrentTarget.HitPointsPct = unit.HitpointsCurrentPct;
                        CurrentTarget.HitPoints = unit.HitpointsCurrent;
                        DbHelper.Log(TrinityLogLevel.Debug, LogCategory.Behavior, "Updated CurrentTarget HitPoints={0:0.00} & Position={1}", CurrentTarget.HitPointsPct, CurrentTarget.Position);
                    }
                }
                else if (CurrentTarget != null && CurrentTarget.Type == GObjectType.Unit)
                {
                    DbHelper.Log(TrinityLogLevel.Debug, LogCategory.Behavior, "CurrentTarget is invalid, setting null");
                    CurrentTarget = null;
                    forceUpdate = true;
                }
            }
            catch
            {
                DbHelper.Log(TrinityLogLevel.Debug, LogCategory.Behavior, "Error updating current target information");
                CurrentTarget = null;
                forceUpdate = true;
            }
            return forceUpdate;
        }
        // Refresh object list from Diablo 3 memory RefreshDiaObjects()
        private static void RefreshCacheInit()
        {
            using (new PerformanceLogger("RefreshDiaObjectCache.CacheInit"))
            {

                // Update when we last refreshed with current time
                LastRefreshedCache = DateTime.Now;

                // Blank current/last/next targets
                LastPrimaryTargetPosition = CurrentTarget != null ? CurrentTarget.Position : vNullLocation;
                vKitePointAvoid = vNullLocation;
                // store current target GUID
                CurrentTargetRactorGUID = CurrentTarget != null ? CurrentTarget.RActorGuid : -1;
                //reset current target
                CurrentTarget = null;
                // Reset all variables for target-weight finding
                AnyTreasureGoblinsPresent = false;
                CurrentBotKillRange = Math.Min((float)(Settings.Combat.Misc.NonEliteRange), Zeta.CommonBot.Settings.CharacterSettings.Instance.KillRadius);

                CurrentBotLootRange = Zeta.CommonBot.Settings.CharacterSettings.Instance.LootRadius;
                ShouldStayPutDuringAvoidance = false;
                // Set up the fake object for the target handler
                FakeObject = null;

                // Always have a minimum kill radius, so we're never getting whacked without retaliating
                if (CurrentBotKillRange < 10)
                    CurrentBotKillRange = 10;

                // Not allowed to kill monsters due to profile/routine/combat targeting settings - just set the kill range to a third
                if (!ProfileManager.CurrentProfile.KillMonsters || !CombatTargeting.Instance.AllowedToKillMonsters)
                {
                    CurrentBotKillRange = 0;
                }

                // Not allowed to loots due to profile/routine/loot targeting settings - just set range to a quarter
                if (!ProfileManager.CurrentProfile.PickupLoot || !LootTargeting.Instance.AllowedToLoot)
                {
                    CurrentBotLootRange = 0;
                }

                if (PlayerStatus.ActorClass == ActorClass.Barbarian && Hotbar.Contains(SNOPower.Barbarian_WrathOfTheBerserker) && GetHasBuff(SNOPower.Barbarian_WrathOfTheBerserker))
                { //!sp - keep looking for kills while WOTB is up
                    iKeepKillRadiusExtendedFor = Math.Max(3, iKeepKillRadiusExtendedFor);
                    timeKeepKillRadiusExtendedUntil = DateTime.Now.AddSeconds(iKeepKillRadiusExtendedFor);
                }
                // Counter for how many cycles we extend or reduce our attack/kill radius, and our loot radius, after a last kill
                if (iKeepKillRadiusExtendedFor > 0)
                {
                    TimeSpan diffResult = DateTime.Now.Subtract(timeKeepKillRadiusExtendedUntil);
                    iKeepKillRadiusExtendedFor = (int)diffResult.Seconds;
                    //DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Moving, "Kill Radius remaining " + diffResult.Seconds + "s");
                    if (timeKeepKillRadiusExtendedUntil <= DateTime.Now)
                    {
                        iKeepKillRadiusExtendedFor = 0;
                    }
                }
                if (iKeepLootRadiusExtendedFor > 0)
                    iKeepLootRadiusExtendedFor--;

                // Clear forcing close-range priority on mobs after XX period of time
                if (ForceCloseRangeTarget && DateTime.Now.Subtract(lastForcedKeepCloseRange).TotalMilliseconds > ForceCloseRangeForMilliseconds)
                {
                    ForceCloseRangeTarget = false;
                }
                // Bunch of variables used throughout
                hashMonsterObstacleCache = new HashSet<GilesObstacle>();
                hashAvoidanceObstacleCache = new HashSet<GilesObstacle>();
                hashNavigationObstacleCache = new HashSet<GilesObstacle>();
                AnyElitesPresent = false;
                AnyMobsInRange = false;
                TownRun.lastDistance = 0f;
                IsAvoidingProjectiles = false;
                // Every 15 seconds, clear the "blackspots" where avoidance failed, so we can re-check them
                if (DateTime.Now.Subtract(lastClearedAvoidanceBlackspots).TotalSeconds > 15)
                {
                    lastClearedAvoidanceBlackspots = DateTime.Now;
                    hashAvoidanceBlackspot = new HashSet<GilesObstacle>();
                }
                // Clear our very short-term destructible blacklist within 3 seconds of last attacking a destructible
                if (bNeedClearDestructibles && DateTime.Now.Subtract(lastDestroyedDestructible).TotalMilliseconds > 2500)
                {
                    bNeedClearDestructibles = false;
                    hashRGUIDDestructible3SecBlacklist = new HashSet<int>();
                }
                // Clear our very short-term ignore-monster blacklist (from not being able to raycast on them or already dead units)
                if (NeedToClearBlacklist3 && DateTime.Now.Subtract(dateSinceBlacklist3Clear).TotalMilliseconds > 3000)
                {
                    NeedToClearBlacklist3 = false;
                    hashRGUIDBlacklist3 = new HashSet<int>();
                }

                // Reset the counters for player-owned things
                iPlayerOwnedMysticAlly = 0;
                iPlayerOwnedGargantuan = 0;
                iPlayerOwnedZombieDog = 0;
                iPlayerOwnedDHPets = 0;
                // Reset the counters for monsters at various ranges
                ElitesWithinRange = new int[] { 0, 0, 0, 0, 0, 0, 0, 0 };
                AnythingWithinRange = new int[] { 0, 0, 0, 0, 0, 0, 0, 0 };
                NonRendedTargets_9 = 0;
                anyBossesInRange = false;
                // Flag for if we should search for an avoidance spot or not
                StandingInAvoidance = false;
                // Highest weight found as we progress through, so we can pick the best target at the end (the one with the highest weight)
                w_HighestWeightFound = 0;
                // Here's the list we'll use to store each object
                GilesObjectCache = new List<GilesObject>();
                hashDoneThisRactor = new HashSet<int>();
            }
        }

        private static void ClearCachesOnGameChange(object sender, EventArgs e)
        {
            dictGilesVectorCache = new Dictionary<int, Vector3>();
            dictGilesObjectTypeCache = new Dictionary<int, GObjectType>();
            dictGilesActorSNOCache = new Dictionary<int, int>();
            dictGilesACDGUIDCache = new Dictionary<int, int>();
            dictGilesLastHealthCache = new Dictionary<int, double>();
            dictGilesLastHealthChecked = new Dictionary<int, int>();
            dictGilesMonsterAffixCache = new Dictionary<int, MonsterAffixes>();
            dictGilesMaxHealthCache = new Dictionary<int, double>();
            dictionaryStoredMonsterTypes = new Dictionary<int, MonsterType>();
            dictionaryStoredMonsterSizes = new Dictionary<int, MonsterSize>();
            dictGilesBurrowedCache = new Dictionary<int, bool>();
            dictSummonedByID = new Dictionary<int, int>();
            dictGilesInternalNameCache = new Dictionary<int, string>();
            dictGilesGoldAmountCache = new Dictionary<int, int>();
            dictGilesGameBalanceIDCache = new Dictionary<int, int>();
            dictGilesDynamicIDCache = new Dictionary<int, int>();
            dictGilesQualityCache = new Dictionary<int, ItemQuality>();
            dictGilesPickupItem = new Dictionary<int, bool>();
            dictHasBeenRayCastedCache = new Dictionary<int, bool>();
            dictHasBeenNavigableCache = new Dictionary<int, bool>();
            dictHasBeenInLoSCache = new Dictionary<int, bool>();
        }

        private static HashSet<string> ignoreNames = new HashSet<string>
        {
            "MarkerLocation", "Generic_Proxy", "Hireling", "Start_Location", "SphereTrigger", "Checkpoint", "ConductorProxyMaster", "BoxTrigger", "SavePoint", "TriggerSphere", 
            "minimapicon",
        };

        private static void RefreshCacheMainLoop()
        {
            using (new PerformanceLogger("CacheManagement.RefreshCacheMainLoop"))
            {
                IEnumerable<DiaObject> refreshSource;

                if (Settings.Advanced.LogCategories.HasFlag(LogCategory.CacheManagement))
                {
                    refreshSource = ReadDebugActorsFromMemory();
                }
                else
                {
                    refreshSource = ReadActorsFromMemory();
                }
                Stopwatch t1 = new Stopwatch();

                foreach (DiaObject currentObject in refreshSource)
                {
                    try
                    {
                        bool AddToCache = false;

                        if (!Settings.Advanced.LogCategories.HasFlag(LogCategory.CacheManagement))
                        {
                            /*
                             *  Main Cache Function
                             */
                            AddToCache = CacheDiaObject(currentObject);
                        }
                        else
                        {
                            // We're debugging, slightly slower, calculate performance metrics and dump debugging to log 
                            t1.Reset();
                            t1.Start();

                            /*
                             *  Main Cache Function
                             */
                            AddToCache = CacheDiaObject(currentObject);

                            if (t1.IsRunning)
                                t1.Stop();

                            double duration = t1.Elapsed.TotalMilliseconds;

                            if ((Settings.Advanced.LogCategories.HasFlag(LogCategory.Performance) && duration > 1 || !Settings.Advanced.LogCategories.HasFlag(LogCategory.Performance)))
                            {
                                string unitExtras = "";

                                if (c_ObjectType == GObjectType.Unit)
                                {
                                    if (c_unit_IsElite)
                                        unitExtras += " IsElite";

                                    if (c_unit_IsShielded)
                                        unitExtras += " IsShielded";

                                    if (c_HasDotDPS)
                                        unitExtras += " HasDotDPS";

                                    if (c_HasBeenInLoS)
                                        unitExtras += " HasBeenInLoS";                                    

                                    unitExtras += " HP=" + c_HitPoints.ToString("0") + " (" + c_HitPointsPct.ToString("0.00") + ")";
                                }
                                DbHelper.Log(TrinityLogLevel.Debug, LogCategory.CacheManagement,
                                    "Cache: [{0:0000.0000}ms] {1} {2} Type: {3} ({4}) Name: {5} ({6}) {7} {8} Dist2Mid: {9:0} Dist2Rad: {10:0} ZDiff: {11:0} Radius: {12:0} RAGuid: {13} {14}",
                                    duration,
                                    (AddToCache ? "Added " : "Ignored"),
                                    (!AddToCache ? (" By: " + (c_IgnoreReason != "None" ? c_IgnoreReason + "." : "") + c_IgnoreSubStep) : ""),
                                    c_diaObject.ActorType,
                                    c_ObjectType,
                                    c_InternalName,
                                    c_ActorSNO,
                                    (c_unit_IsBoss ? " IsBoss" : ""),
                                    (c_CurrentAnimation != SNOAnim.Invalid ? " Anim: " + c_CurrentAnimation : ""),
                                    c_CentreDistance,
                                    c_RadiusDistance,
                                    c_ZDiff,
                                    c_Radius,
                                    c_RActorGuid,
                                    unitExtras);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DbHelper.Log(TrinityLogLevel.Debug, LogCategory.CacheManagement, "Error while refreshing DiaObject ActorSNO: {0} Name: {1} Type: {2} Distance: {3:0}",
                                currentObject.ActorSNO, currentObject.Name, currentObject.ActorType, currentObject.Distance);
                        DbHelper.Log(TrinityLogLevel.Debug, LogCategory.CacheManagement, "{0}", ex);

                    }
                }

            }
        }

        private static IOrderedEnumerable<DiaObject> ReadDebugActorsFromMemory()
        {
            return from o in ZetaDia.Actors.GetActorsOfType<DiaObject>(true, false)
                   orderby o.Distance
                   select o;
        }

        private static IEnumerable<DiaObject> ReadActorsFromMemory()
        {
            return from o in ZetaDia.Actors.GetActorsOfType<DiaObject>(true, false)
                   select o;
        }

        private static bool RefreshItemStats(GItemBaseType tempbasetype)
        {
            bool isNewLogItem = false;

            c_ItemMd5Hash = HashGenerator.GenerateItemHash(c_Position, c_ActorSNO, c_InternalName, CurrentWorldDynamicId, c_ItemQuality, c_ItemLevel);

            if (!GenericCache.ContainsKey(c_ItemMd5Hash))
            {
                GenericCache.AddToCache(new GenericCacheObject(c_ItemMd5Hash, null, new TimeSpan(1, 0, 0)));

                isNewLogItem = true;
                if (tempbasetype == GItemBaseType.Armor || tempbasetype == GItemBaseType.WeaponOneHand || tempbasetype == GItemBaseType.WeaponTwoHand ||
                    tempbasetype == GItemBaseType.WeaponRange || tempbasetype == GItemBaseType.Jewelry || tempbasetype == GItemBaseType.FollowerItem ||
                    tempbasetype == GItemBaseType.Offhand)
                {
                    int iThisQuality;
                    ItemsDroppedStats.Total++;
                    if (c_ItemQuality >= ItemQuality.Legendary)
                        iThisQuality = QUALITYORANGE;
                    else if (c_ItemQuality >= ItemQuality.Rare4)
                        iThisQuality = QUALITYYELLOW;
                    else if (c_ItemQuality >= ItemQuality.Magic1)
                        iThisQuality = QUALITYBLUE;
                    else
                        iThisQuality = QUALITYWHITE;
                    ItemsDroppedStats.TotalPerQuality[iThisQuality]++;
                    ItemsDroppedStats.TotalPerLevel[c_ItemLevel]++;
                    ItemsDroppedStats.TotalPerQPerL[iThisQuality, c_ItemLevel]++;
                }
                else if (tempbasetype == GItemBaseType.Gem)
                {
                    int iThisGemType = 0;
                    ItemsDroppedStats.TotalGems++;
                    if (c_item_GItemType == GItemType.Topaz)
                        iThisGemType = GEMTOPAZ;
                    if (c_item_GItemType == GItemType.Ruby)
                        iThisGemType = GEMRUBY;
                    if (c_item_GItemType == GItemType.Emerald)
                        iThisGemType = GEMEMERALD;
                    if (c_item_GItemType == GItemType.Amethyst)
                        iThisGemType = GEMAMETHYST;
                    ItemsDroppedStats.GemsPerType[iThisGemType]++;
                    ItemsDroppedStats.GemsPerLevel[c_ItemLevel]++;
                    ItemsDroppedStats.GemsPerTPerL[iThisGemType, c_ItemLevel]++;
                }
                else if (c_item_GItemType == GItemType.HealthPotion)
                {
                    ItemsDroppedStats.TotalPotions++;
                    ItemsDroppedStats.PotionsPerLevel[c_ItemLevel]++;
                }
                else if (c_item_GItemType == GItemType.InfernalKey)
                {
                    ItemsDroppedStats.TotalInfernalKeys++;
                }
                // See if we should update the stats file
                if (DateTime.Now.Subtract(ItemStatsLastPostedReport).TotalSeconds > 10)
                {
                    ItemStatsLastPostedReport = DateTime.Now;
                    OutputReport();
                }
            }
            return isNewLogItem;
        }
        private static void RefreshDoBackTrack()
        {
            // See if we should wait for [playersetting] milliseconds for possible loot drops before continuing run
            if (DateTime.Now.Subtract(lastHadUnitInSights).TotalMilliseconds <= Settings.Combat.Misc.DelayAfterKill || DateTime.Now.Subtract(lastHadEliteUnitInSights).TotalMilliseconds <= Settings.Combat.Misc.DelayAfterKill)
            {
                CurrentTarget = new GilesObject()
                                    {
                                        Position = PlayerStatus.CurrentPosition,
                                        Type = GObjectType.Avoidance,
                                        Weight = 20000,
                                        CentreDistance = 2f,
                                        RadiusDistance = 2f,
                                        InternalName = "GilesWaitForLootDrops"
                                    };
                DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Behavior, "Waiting for loot to drop, delay: {0}ms", Settings.Combat.Misc.DelayAfterKill);
            }
            // Now see if we need to do any backtracking
            if (CurrentTarget == null && iTotalBacktracks >= 2 && Settings.Combat.Misc.AllowBacktracking && !PlayerStatus.IsInTown)
            // Never bother with the 1st backtrack position nor if we are in town
            {
                // See if we're already within 18 feet of our start position first
                if (Vector3.Distance(PlayerStatus.CurrentPosition, vBacktrackList[1]) <= 18f)
                {
                    vBacktrackList = new SortedList<int, Vector3>();
                    iTotalBacktracks = 0;
                }
                // See if we can raytrace to the final location and it's within 25 feet
                if (iTotalBacktracks >= 2 && Vector3.Distance(PlayerStatus.CurrentPosition, vBacktrackList[1]) <= 25f &&
                    NavHelper.CanRayCast(PlayerStatus.CurrentPosition, vBacktrackList[1]))
                {
                    vBacktrackList = new SortedList<int, Vector3>();
                    iTotalBacktracks = 0;
                }
                if (iTotalBacktracks >= 2)
                {
                    // See if we can skip to the next backtracker location first
                    if (iTotalBacktracks >= 3)
                    {
                        if (Vector3.Distance(PlayerStatus.CurrentPosition, vBacktrackList[iTotalBacktracks - 1]) <= 10f)
                        {
                            vBacktrackList.Remove(iTotalBacktracks);
                            iTotalBacktracks--;
                        }
                    }
                    CurrentTarget = new GilesObject()
                                        {
                                            Position = vBacktrackList[iTotalBacktracks],
                                            Type = GObjectType.Backtrack,
                                            Weight = 20000,
                                            CentreDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vBacktrackList[iTotalBacktracks]),
                                            RadiusDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vBacktrackList[iTotalBacktracks]),
                                            InternalName = "GilesBacktrack"
                                        };
                }
            }
            else
            {
                vBacktrackList = new SortedList<int, Vector3>();
                iTotalBacktracks = 0;
            }
            // End of backtracking check
            //TODO : If this code is obselete remove it (Check that) 
            // Finally, a special check for waiting for wrath of the berserker cooldown before engaging Azmodan
            if (CurrentTarget == null && Hotbar.Contains(SNOPower.Barbarian_WrathOfTheBerserker) && Settings.Combat.Barbarian.WaitWOTB && !GilesUseTimer(SNOPower.Barbarian_WrathOfTheBerserker) &&
                ZetaDia.CurrentWorldId == 121214 &&
                (Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(711.25f, 716.25f, 80.13903f)) <= 40f || Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(546.8467f, 551.7733f, 1.576313f)) <= 40f))
            {
                bDontSpamOutofCombat = true;
                Logging.Write("[Trinity] Waiting for Wrath Of The Berserker cooldown before continuing to Azmodan.");
                CurrentTarget = new GilesObject()
                                    {
                                        Position = PlayerStatus.CurrentPosition,
                                        Type = GObjectType.Avoidance,
                                        Weight = 20000,
                                        CentreDistance = 2f,
                                        RadiusDistance = 2f,
                                        InternalName = "GilesWaitForWrath"
                                    };
            }
            // And a special check for wizard archon
            if (CurrentTarget == null && Hotbar.Contains(SNOPower.Wizard_Archon) && !GilesUseTimer(SNOPower.Wizard_Archon) && Settings.Combat.Wizard.WaitArchon && ZetaDia.CurrentWorldId == 121214 &&
                (Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(711.25f, 716.25f, 80.13903f)) <= 40f || Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(546.8467f, 551.7733f, 1.576313f)) <= 40f))
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Waiting for Wizard Archon cooldown before continuing to Azmodan.");
                CurrentTarget = new GilesObject()
                                    {
                                        Position = PlayerStatus.CurrentPosition,
                                        Type = GObjectType.Avoidance,
                                        Weight = 20000,
                                        CentreDistance = 2f,
                                        RadiusDistance = 2f,
                                        InternalName = "GilesWaitForArchon"
                                    };
            }
            // And a very sexy special check for WD BigBadVoodoo
            if (CurrentTarget == null && Hotbar.Contains(SNOPower.Witchdoctor_BigBadVoodoo) && !PowerManager.CanCast(SNOPower.Witchdoctor_BigBadVoodoo) && ZetaDia.CurrentWorldId == 121214 &&
                (Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(711.25f, 716.25f, 80.13903f)) <= 40f || Vector3.Distance(PlayerStatus.CurrentPosition, new Vector3(546.8467f, 551.7733f, 1.576313f)) <= 40f))
            {
                DbHelper.Log(TrinityLogLevel.Normal, LogCategory.UserInformation, "Waiting for WD BigBadVoodoo cooldown before continuing to Azmodan.");
                CurrentTarget = new GilesObject()
                                    {
                                        Position = PlayerStatus.CurrentPosition,
                                        Type = GObjectType.Avoidance,
                                        Weight = 20000,
                                        CentreDistance = 2f,
                                        RadiusDistance = 2f,
                                        InternalName = "GilesWaitForVoodooo"
                                    };
            }
        }

        internal sealed class KitePosition
        {
            public DateTime PositionFoundTime { get; set; }
            public Vector3 Position { get; set; }
            public float Distance { get; set; }
            public KitePosition() { }
        }
        internal static KitePosition LastKitePosition = null;

        private static void RefreshSetKiting(ref Vector3 vKitePointAvoid, bool NeedToKite, ref bool TryToKite)
        {
            using (new PerformanceLogger("RefreshDiaObjectCache.Kiting"))
            {

                TryToKite = false;

                var monsterList = from m in GilesObjectCache
                                  where m.Type == GObjectType.Unit &&
                                  m.Weight > 0 &&
                                  m.RadiusDistance <= PlayerKiteDistance &&
                                  (m.IsBossOrEliteRareUnique ||
                                   ((m.HitPointsPct >= .15 || m.MonsterStyle != MonsterSize.Swarm) && !m.IsBossOrEliteRareUnique)
                                   )
                                  select m;

                if (CurrentTarget != null && CurrentTarget.Type == GObjectType.Unit && PlayerKiteDistance > 0 && CurrentTarget.RadiusDistance <= PlayerKiteDistance)
                {
                    TryToKite = true;
                    vKitePointAvoid = PlayerStatus.CurrentPosition;
                }

                if (monsterList.Count() > 0 && (PlayerStatus.ActorClass != ActorClass.Wizard || IsWizardShouldKite()))
                {
                    TryToKite = true;
                    vKitePointAvoid = PlayerStatus.CurrentPosition;
                }

                // Note that if treasure goblin level is set to kamikaze, even avoidance moves are disabled to reach the goblin!
                bool shouldKamikazeTreasureGoblins = (!AnyTreasureGoblinsPresent || Settings.Combat.Misc.GoblinPriority <= GoblinPriority.Prioritize);

                double msCancelledEmergency = DateTime.Now.Subtract(timeCancelledEmergencyMove).TotalMilliseconds;
                bool shouldEmergencyMove = msCancelledEmergency >= cancelledEmergencyMoveForMilliseconds && NeedToKite;

                double msCancelledKite = DateTime.Now.Subtract(timeCancelledKiteMove).TotalMilliseconds;
                bool shouldKite = msCancelledKite >= cancelledKiteMoveForMilliseconds && TryToKite;

                if (shouldKamikazeTreasureGoblins && (shouldEmergencyMove || shouldKite))
                {
                    Vector3 vAnySafePoint = NavHelper.FindSafeZone(false, 1, vKitePointAvoid, true, monsterList);

                    if (LastKitePosition == null)
                    {
                        LastKitePosition = new KitePosition()
                        {
                            PositionFoundTime = DateTime.Now,
                            Position = vAnySafePoint,
                            Distance = vAnySafePoint.Distance(PlayerStatus.CurrentPosition)
                        };
                    }

                    if (vAnySafePoint != Vector3.Zero && vAnySafePoint.Distance(PlayerStatus.CurrentPosition) >= 1)
                    {

                        if ((DateTime.Now.Subtract(LastKitePosition.PositionFoundTime).TotalMilliseconds > 3000 && LastKitePosition.Position == vAnySafePoint) ||
                            (CurrentTarget != null && DateTime.Now.Subtract(lastGlobalCooldownUse).TotalMilliseconds > 1500 && TryToKite))
                        {
                            timeCancelledKiteMove = DateTime.Now;
                            cancelledKiteMoveForMilliseconds = 1500;
                            DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Kite movement failed, cancelling for {0:0}ms", cancelledKiteMoveForMilliseconds);
                            return;
                        }
                        else
                        {
                            LastKitePosition = new KitePosition()
                            {
                                PositionFoundTime = DateTime.Now,
                                Position = vAnySafePoint,
                                Distance = vAnySafePoint.Distance(PlayerStatus.CurrentPosition)
                            };
                        }

                        if (Settings.Advanced.LogCategories.HasFlag(LogCategory.Movement))
                        {
                            DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Kiting to: {0} Distance: {1:0} Direction: {2:0}, Health%={3:0.00}, KiteDistance: {4:0}, Nearby Monsters: {5:0} NeedToKite: {6} TryToKite: {7}",
                                vAnySafePoint, vAnySafePoint.Distance(PlayerStatus.CurrentPosition), MathUtil.GetHeading(MathUtil.FindDirectionDegree(Me.Position, vAnySafePoint)),
                                PlayerStatus.CurrentHealthPct, PlayerKiteDistance, monsterList.Count(),
                                NeedToKite, TryToKite);
                        }
                        CurrentTarget = new GilesObject()
                                            {
                                                Position = vAnySafePoint,
                                                Type = GObjectType.Avoidance,
                                                Weight = 90000,
                                                CentreDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vAnySafePoint),
                                                RadiusDistance = Vector3.Distance(PlayerStatus.CurrentPosition, vAnySafePoint),
                                                InternalName = "KitePoint"
                                            };

                        //timeCancelledKiteMove = DateTime.Now;
                        //cancelledKiteMoveForMilliseconds = 100;

                        // Try forcing a target update with each kiting
                        //bForceTargetUpdate = true;
                    }
                    //else
                    //{
                    //    // Didn't find any kiting we could reach, so don't look for any more kite spots for at least 1.5 seconds
                    //    timeCancelledKiteMove = DateTime.Now;
                    //    cancelledKiteMoveForMilliseconds = 500;
                    //}
                }
                else if (!shouldEmergencyMove && NeedToKite)
                {
                    DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Emergency movement cancelled for {0:0}ms", DateTime.Now.Subtract(timeCancelledEmergencyMove).TotalMilliseconds - cancelledKiteMoveForMilliseconds);
                }
                else if (!shouldKite && TryToKite)
                {
                    DbHelper.Log(TrinityLogLevel.Verbose, LogCategory.Movement, "Kite movement cancelled for {0:0}ms", DateTime.Now.Subtract(timeCancelledKiteMove).TotalMilliseconds - cancelledKiteMoveForMilliseconds);
                }
            }
        }

        private static bool IsWizardShouldKite()
        {
            if (PlayerStatus.ActorClass == ActorClass.Wizard)
            {
                if (Settings.Combat.Wizard.KiteOption == WizardKiteOption.Anytime)
                    return true;

                if (GetHasBuff(SNOPower.Wizard_Archon) && Settings.Combat.Wizard.KiteOption == WizardKiteOption.ArchonOnly)
                    return true;
                if (!GetHasBuff(SNOPower.Wizard_Archon) && Settings.Combat.Wizard.KiteOption == WizardKiteOption.NormalOnly)
                    return true;

                return false;

            }
            else
                return false;
        }
    }
}
