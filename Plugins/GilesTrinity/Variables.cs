﻿using GilesTrinity.ItemRules;
using GilesTrinity.Settings;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using Zeta;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.Internals.Actors;
using Zeta.Internals.SNO;
using Zeta.Pathfinding;
using Zeta.Navigation;

namespace GilesTrinity
{
    public partial class GilesTrinity : IPlugin
    {
        private static TrinitySetting _Settings = new TrinitySetting();
        /// <summary>
        /// Settings of the plugin
        /// </summary>
        public static TrinitySetting Settings
        {
            get
            {
                return _Settings;
            }
        }


        /* I create so many variables that it's a pain in the arse to categorize them
         * So I just throw them all here for quick searching, reference etc.
         * I've tried to make most variable names be pretty damned obvious what they are for!
         * I've also commented a lot of variables/sections of variables to explain what they are for, incase you are trying to work them all out!
         */
        /// <summary>
        /// Used for letting noobs know they started the bot without Trinity enabled in the plugins tab.
        /// </summary>
        private static bool IsPluginEnabled = false;

        /// <summary>
        /// A flag to say whether any NON-hashActorSNOWhirlwindIgnore things are around
        /// </summary>
        private static bool bAnyNonWWIgnoreMobsInRange = false;

        /// <summary>
        /// A null location, may shave off the tiniest fraction of CPU time, but probably not. Still, Giles liked using this variable apparently.
        /// </summary>
        private static readonly Vector3 vNullLocation = Vector3.Zero;

        /// <summary>
        /// Used for a global bot-pause
        /// </summary>
        private static bool bMainBotPaused = false;

        /// <summary>
        /// Used to force-refresh dia objects at least once every XX milliseconds
        /// </summary>
        public static DateTime LastRefreshedCache = DateTime.MinValue;

        /// <summary>
        /// This object is used for the main handling - the "current target" etc. as selected by the target-selecter, whether it be a unit, an item, a shrine, anything. 
        /// It's cached data using my own class, so I never need to hit D3 memory to "re-check" the data or to call an interact request or anything
        /// </summary>
        internal static GilesObject CurrentTarget = null;

        /// <summary>
        /// A flag to indicate whether we have a new target from the overlord (decorator) or not, in which case don't refresh targets again this first loop
        /// </summary>
        private static bool IsWholeNewTarget = false;

        /// <summary>
        /// A flag to indicate if we should pick a new power/ability to use or not
        /// </summary>
        private static bool ShouldPickNewAbilities = false;

        /// <summary>
        /// Flag used to indicate if we are simply waiting for a power to go off - so don't do any new target checking or anything
        /// </summary>
        private static bool IsWaitingForPower = false;

        /// <summary>
        /// A special post power use pause, causes targetHandler to wait on any new decisions
        /// </summary>
        private static bool IsWaitingAfterPower = false;

        /// <summary>
        /// If TargetHandle is waiting waiting before popping a potion - we won't refresh cache/change targets/unstuck/etc
        /// </summary>
        private static bool IsWaitingForPotion = false;

        /// <summary>
        /// Status text for DB main window status
        /// </summary>
        private static string sStatusText = "";

        /// <summary>
        /// A flag to indicate if we just entered or just left archon form (and so to force-update the hotbar)
        /// </summary>
        internal static bool HasHadArchonbuff = false;

        /// <summary>
        /// A flag to see if we need to refresh hot bar abilities
        /// </summary>
        internal static bool ShouldRefreshHotbarAbilities = false;

        /// <summary>
        /// A "fake" object to send to target provider for stuck handlers etc.
        /// </summary>
        public static DiaObject FakeObject;

        /// <summary>
        /// Timestamp of when our position was last measured as changed
        /// </summary>
        private static DateTime lastMovedDuringCombat = DateTime.Today;

        // The following three dictionaries are special caches - they start empty but fill up with data that gets re-used while the bot runs, saving on D3 memory hits

        /// <summary>
        /// Special cache for monster types {ActorSNO, MonsterType}
        /// </summary>
        private static Dictionary<int, MonsterType> dictionaryStoredMonsterTypes = new Dictionary<int, MonsterType>();

        /// <summary>
        /// Special cache for Monster sizes {ActorSNO, MonsterSize}
        /// </summary>
        private static Dictionary<int, MonsterSize> dictionaryStoredMonsterSizes = new Dictionary<int, MonsterSize>();

        /// <summary>
        /// Used to ignore a specific RActor for <see cref="IgnoreTargetForLoops"/> ticks
        /// </summary>
        private static int IgnoreRactorGUID = 0;

        /// <summary>
        /// Ignore <see cref=" IgnoreRactorGUID"/> for this many ticks
        /// </summary>
        private static int IgnoreTargetForLoops = 0;

        /// <summary>
        /// Holds all of the player's current info handily cached, updated once per loop with a minimum timer on updates to save D3 memory hits
        /// </summary>
        public static GilesPlayerCache PlayerStatus = new GilesPlayerCache(DateTime.Today, false, false, false, 0d, 0d, 0d, 0d, 0d, vNullLocation, false, 0, 1, ActorClass.Invalid, String.Empty);

        [Obsolete("This property is obsolete; use PlayerStatus instead")]
        public static GilesPlayerCache playerStatus { get { return PlayerStatus; } }

        /// <summary>
        /// Obstacle cache, things we can't or shouldn't move through
        /// </summary>
        internal static HashSet<GilesObstacle> hashNavigationObstacleCache = new HashSet<GilesObstacle>();

        // Related to the profile reloaded when restarting games, to pick the FIRST profile.

        // Also storing a list of all profiles, for experimental reasons/incase I want to use them down the line
        public static List<string> listProfilesLoaded = new List<string>();
        public static string CurrentProfile = "";
        public static string FirstProfile = "";

        // A list of small areas covering zones we move through while fighting to help our custom move-handler skip ahead waypoints
        internal static HashSet<GilesObstacle> hashSkipAheadAreaCache = new HashSet<GilesObstacle>();
        public static DateTime lastAddedLocationCache = DateTime.Today;
        public static Vector3 vLastRecordedLocationCache = Vector3.Zero;
        public static bool bSkipAheadAGo = false;

        /// <summary>
        /// A list of all monsters and their positions, so we don't try to walk through them during avoidance
        /// </summary>
        internal static HashSet<GilesObstacle> hashMonsterObstacleCache = new HashSet<GilesObstacle>();

        /// <summary>
        /// A list of all current obstacles, to help avoid running through them when picking targets
        /// </summary>
        internal static HashSet<GilesObstacle> hashAvoidanceObstacleCache = new HashSet<GilesObstacle>();

        /// <summary>
        /// Blacklist avoidance spots we failed to reach in time, for a period of time
        /// </summary>
        private static HashSet<GilesObstacle> hashAvoidanceBlackspot = new HashSet<GilesObstacle>();
        private static DateTime lastClearedAvoidanceBlackspots = DateTime.Today;

        // A count for player mystic ally, gargantuans, and zombie dogs
        private static int iPlayerOwnedMysticAlly = 0;
        public static int iPlayerOwnedGargantuan = 0;
        public static int iPlayerOwnedZombieDog = 0;
        private static int iPlayerOwnedDHPets = 0;

        // These are a bunch of safety counters for how many times in a row we register having *NO* ability to select when we need one (eg all off cooldown)

        // After so many, give the player a friendly warning to check their skill/build setup
        private static int iNoAbilitiesAvailableInARow = 0;
        private static DateTime lastRemindedAboutAbilities = DateTime.Today;

        // Last had any mob in range, for loot-waiting
        internal static DateTime lastHadUnitInSights = DateTime.Today;

        // When we last saw a boss/elite etc.
        internal static DateTime lastHadEliteUnitInSights = DateTime.Today;

        // Do we need to reset the debug bar after combat handling?
        private static bool bResetStatusText = false;

        // A list of "useonceonly" tags that have been triggered this xml profile
        public static HashSet<int> hashUseOnceID = new HashSet<int>();
        public static Dictionary<int, int> dictUseOnceID = new Dictionary<int, int>();

        // For the random ID tag
        public static Dictionary<int, int> dictRandomID = new Dictionary<int, int>();

        // Death counts
        public static int iMaxDeathsAllowed = 0;
        public static int iDeathsThisRun = 0;

        // Force a target update after certain interactions
        private static bool ForceTargetUpdate = false;

        /// <summary>
        /// This holds whether or not we want to prioritize a close-target, used when we might be body-blocked by monsters
        /// </summary>
        private static bool ForceCloseRangeTarget = false;

        // How many times a movement fails because of being "blocked"
        private static int TimesBlockedMoving = 0;

        // how long to force close-range targets for
        private static int ForceCloseRangeForMilliseconds = 0;

        // Date time we were last told to stick to close range targets
        private static DateTime lastForcedKeepCloseRange = DateTime.Today;


        // Caching of the current primary target's health, to detect if we AREN'T damaging it for a period of time
        private static double iTargetLastHealth = 0f;

        // This is used so we don't use certain skills until we "top up" our primary resource by enough
        internal static double MinEnergyReserve = 0d;

        /// <summary>
        /// Store the date-time when we *FIRST* picked this target, so we can blacklist after X period of time targeting
        /// </summary>
        private static DateTime dateSincePickedTarget = DateTime.Today;

        // These values below are set on a per-class basis later on, so don't bother changing them here! These are the old default values
        private static double PlayerEmergencyHealthPotionLimit = 0.46;
        private static double PlayerEmergencyHealthGlobeLimit = 0.6;

        /// <summary>
        /// Distance to kite, read settings (class independant)
        /// </summary>
        internal static int PlayerKiteDistance = 0;

        /*
         *  Blacklists
         */
        internal static bool NeedToClearBlacklist3 = false;
        internal static DateTime dateSinceBlacklist3Clear = DateTime.Today;
        internal static DateTime dateSinceBlacklist15Clear = DateTime.Today;
        internal static DateTime dateSinceBlacklist60Clear = DateTime.Today;
        internal static DateTime dateSinceBlacklist90Clear = DateTime.Today;

        /// <summary>
        /// Use RActorGUID to blacklist an object/monster for 3 seconds
        /// </summary>
        internal static HashSet<int> hashRGUIDBlacklist3 = new HashSet<int>();
        /// <summary>
        /// Use RActorGUID to blacklist an object/monster for 15 seconds
        /// </summary>
        internal static HashSet<int> hashRGUIDBlacklist15 = new HashSet<int>();
        /// <summary>
        /// Use RActorGUID to blacklist an object/monster for 60 seconds
        /// </summary>
        internal static HashSet<int> hashRGUIDBlacklist60 = new HashSet<int>();
        /// <summary>
        /// Use RActorGUID to blacklist an object/monster for 90 seconds
        /// </summary>
        internal static HashSet<int> hashRGUIDBlacklist90 = new HashSet<int>();

        // This is a blacklist that is cleared within 3 seconds of last attacking a destructible
        private static HashSet<int> hashRGUIDDestructible3SecBlacklist = new HashSet<int>();
        private static DateTime lastDestroyedDestructible = DateTime.Today;
        private static bool bNeedClearDestructibles = false;

        // An ordered list of all of the backtrack locations to navigate through once we finish our current activities
        public static SortedList<int, Vector3> vBacktrackList = new SortedList<int, Vector3>();
        public static int iTotalBacktracks = 0;

        // The number of loops to extend kill range for after a fight to try to maximize kill bonus exp etc.
        private static int iKeepKillRadiusExtendedFor = 0;
        private static DateTime timeKeepKillRadiusExtendedUntil = DateTime.Today;

        // The number of loops to extend loot range for after a fight to try to stop missing loot
        private static int iKeepLootRadiusExtendedFor = 0;

        // Some avoidance related variables

        /// <summary>
        /// Whether or not we need avoidance this target-search-loop
        /// </summary>
        private static bool StandingInAvoidance = false;

        /// <summary>
        /// Whether or not there are projectiles to avoid
        /// </summary>
        private static bool IsAvoidingProjectiles = false;


        /// <summary>
        /// This lets us know if there is a target but it's in avoidance so we can just "stay put" until avoidance goes
        /// </summary>
        private static bool ShouldStayPutDuringAvoidance = false;

        /// <summary>
        /// This force-prevents avoidance for XX loops incase we get stuck trying to avoid stuff
        /// </summary>
        private static DateTime timeCancelledEmergencyMove = DateTime.MinValue;
        private static int cancelledEmergencyMoveForMilliseconds = 0;

        /// <summary>
        /// Prevent spam-kiting too much - allow fighting between each kite movement
        /// </summary>
        private static DateTime timeCancelledKiteMove = DateTime.Now;
        private static int cancelledKiteMoveForMilliseconds = 0;

        // How many follower items were ignored, purely for item stat tracking
        private static int totalFollowerItemsIgnored = 0;

        // Variable to let us force new target creations immediately after a root
        private static bool wasRootedLastTick = false;

        // Variables used to actually hold powers the power-selector has picked to use, for buffing and main power use
        private static TrinityPower powerBuff;
        private static TrinityPower CurrentPower;
        internal static SNOPower LastPowerUsed = SNOPower.None;

        // Two variables to stop DB from attempting any navigator movement mid-combat/mid-backtrack
        public static bool bDontMoveMeIAmDoingShit = false;
        public static bool bDontSpamOutofCombat = false;
        public static bool bOnlyTarget = false;

        // Target provider and core routine variables
        private static bool AnyElitesPresent = false;
        private static bool AnyTreasureGoblinsPresent = false;
        private static bool AnyMobsInRange = false;
        private static float CurrentBotKillRange = 0f;
        private static float CurrentBotLootRange = 0f;
        internal static bool MaintainTempestRush = false;

        /// <summary>
        /// Use Beserker Only with "Hard" elites (specific affixes)
        /// </summary>
        private static bool shouldUseBerserkerPower = false;
        /// <summary>
        /// Are we waiting for a special? Don't waste mana/rage/disc/hate etc.
        /// </summary>
        private static bool IsWaitingForSpecial = false;

        // Goblinney things
        private static int iTotalNumberGoblins = 0;
        private static DateTime lastGoblinTime = DateTime.Today;

        internal static DateTime SweepWindSpam = DateTime.Today;

        // Variables relating to quick-reference of monsters within sepcific ranges (if anyone has suggestion for similar functionality with reduced CPU use, lemme know, but this is fast atm!)
        private static int[] ElitesWithinRange = new int[] { 0, 0, 0, 0, 0, 0, 0 };
        private static int[] AnythingWithinRange = new int[] { 0, 0, 0, 0, 0, 0, 0 };
        private static bool anyBossesInRange = false;
        private const int RANGE_50 = 0;
        private const int RANGE_40 = 1;
        private const int RANGE_30 = 2;
        private const int RANGE_25 = 3;
        private const int RANGE_20 = 4;
        private const int RANGE_15 = 5;
        private const int RANGE_12 = 6;
        private const int RANGE_6 = 7;
        private static int iWithinRangeLastRend = 0;
        private static int NonRendedTargets_9 = 0;

        // Unique ID of mob last targetting when using rend
        private static int iACDGUIDLastRend = 0;

        // Unique ID of mob last targetting when using whirlwind
        private static int iACDGUIDLastWhirlwind = 0;
        private static bool IsAlreadyMoving = false;
        private static Vector3 vLastMoveToTarget;
        private static float fLastDistanceFromTarget;
        private static DateTime lastMovementCommand = DateTime.Today;

        // Actual combat function variables
        internal static bool HasMappedPlayerAbilities = false;

        // Contains our apparent *CURRENT* hotbar abilities, cached in a fast hash
        public static HashSet<SNOPower> Hotbar = new HashSet<SNOPower>();

        // Contains a hash of our LAST hotbar abilities before we transformed into archon (for quick and safe hotbar restoration)
        internal static HashSet<SNOPower> hashCachedPowerHotbarAbilities = new HashSet<SNOPower>();

        // A list and a dictionary for quick buff checking and buff references
        internal static List<Buff> listCachedBuffs = new List<Buff>();
        internal static Dictionary<int, int> dictCachedBuffs = new Dictionary<int, int>();

        // For "position-shifting" to navigate around obstacle SNO's
        private static Vector3 vShiftedPosition = Vector3.Zero;
        private static DateTime lastShiftedPosition = DateTime.Today;
        private static int iShiftPositionFor = 0;
        private static Vector3 vCurrentDestination;
        private static Vector3 vSideToSideTarget;
        private static DateTime lastChangedZigZag = DateTime.Today;
        private static Vector3 vPositionLastZigZagCheck = Vector3.Zero;
        public static int CurrentWorldDynamicId = -1;
        public static int cachedStaticWorldId = -1; // worldId from profiles, used in persistent stats
        public static GameDifficulty iCurrentGameDifficulty = GameDifficulty.Invalid;


        /// <summary>
        /// Do not wait for animation after using, spam the power (false)
        /// </summary>
        private const bool NO_WAIT_ANIM = false;
        /// <summary>
        /// Wait for animation after using, do not spam the power (true)
        /// </summary>
        private const bool WAIT_FOR_ANIM = true;

        // Constants and variables used by the item-stats stuff
        private const int QUALITYWHITE = 0;
        private const int QUALITYBLUE = 1;
        private const int QUALITYYELLOW = 2;
        private const int QUALITYORANGE = 3;
        private static readonly string[] sQualityString = new string[4] { "White", "Magic", "Rare", "Legendary" };
        private const int GEMRUBY = 0;
        private const int GEMTOPAZ = 1;
        private const int GEMAMETHYST = 2;
        private const int GEMEMERALD = 3;
        private static readonly string[] sGemString = new string[4] { "Ruby", "Topaz", "Amethyst", "Emerald" };
        private static DateTime ItemStatsLastPostedReport = DateTime.Now;
        private static DateTime ItemStatsWhenStartedBot = DateTime.Now;
        private static bool bMaintainStatTracking = false;

        // Store items already logged by item-stats, to make sure no stats get doubled up by accident
        private static HashSet<string> _hashsetItemStatsLookedAt = new HashSet<string>();
        private static HashSet<string> _hashsetItemPicksLookedAt = new HashSet<string>();
        private static HashSet<string> _hashsetItemFollowersIgnored = new HashSet<string>();

        // These objects are instances of my stats class above, holding identical types of data for two different things - one holds item DROP stats, one holds item PICKUP stats
        internal static GItemStats ItemsDroppedStats = new GItemStats(0, new double[4], new double[64], new double[4, 64], 0, new double[64], 0, new double[4], new double[64], new double[4, 64], 0);
        internal static GItemStats ItemsPickedStats = new GItemStats(0, new double[4], new double[64], new double[4, 64], 0, new double[64], 0, new double[4], new double[64], new double[4, 64], 0);


        // Whether to try forcing a vendor-run for custom reasons
        public static bool ForceVendorRunASAP = false;
        public static bool IsReadyToTownRun = false;
        [Obsolete("Property bWantToTownRun is obsolete and will eventually be removed. Use IsReadyToTownRun instead.")]
        public static bool bWantToTownRun { get { return IsReadyToTownRun; } set { IsReadyToTownRun = value; } }

        // Stash mapper - it's an array representing every slot in your stash, true or false dictating if the slot is free or not
        private static bool[,] StashSlotBlocked = new bool[7, 30];

        /*
         * From RefreshDiaObject
         *
         * Bunch of temporary variables that get used when creating the current object/target list - this was just a nicer way for me to handle it code wise at first
         * Even if it looks a bit messy and probably should have just used it's own object instance of the cache-class instead! :D
         * c_ variables are all used in the caching mechanism
         */
        /// <summary>
        /// This contains the active cache of DiaObjects
        /// </summary>
        internal static List<GilesObject> GilesObjectCache;

        /// <summary>
        /// This will eventually be come our single source of truth and we can get rid of most/all of the below "c_" variables
        /// </summary>
        // private static GilesObject cacheEntry = null;

        private static Vector3 c_Position = Vector3.Zero;
        private static GObjectType c_ObjectType = GObjectType.Unknown;
        private static double c_Weight = 0d;
        /// <summary>
        /// Percent of total health remaining on unit
        /// </summary>
        private static double c_HitPointsPct = 0d;
        private static double c_HitPoints = 0d;
        private static float c_CentreDistance = 0f;
        private static float c_RadiusDistance = 0f;
        private static float c_Radius = 0f;
        private static float c_ZDiff = 0f;
        private static string c_ItemDisplayName = "";
        private static int c_GameBalanceID = 0;
        private static string c_InternalName = "";
        private static string c_IgnoreReason = "";
        private static string c_IgnoreSubStep = "";
        private static int c_ACDGUID = 0;
        private static int c_RActorGuid = 0;
        private static int c_GameDynamicID = 0;
        private static int c_BalanceID = 0;
        private static int c_ActorSNO = 0;
        private static int c_ItemLevel = 0;
        private static string c_ItemLink = String.Empty;
        private static int c_GoldStackSize = 0;
        private static bool c_IsOneHandedItem = false;
        private static bool c_IsTwoHandedItem = false;
        private static ItemQuality c_ItemQuality = ItemQuality.Invalid;
        private static ItemType c_DBItemType = ItemType.Unknown;
        private static ItemBaseType c_DBItemBaseType = ItemBaseType.None;
        private static FollowerType c_item_tFollowerType = FollowerType.None;
        private static GItemType c_item_GItemType = GItemType.Unknown;
        private static MonsterSize c_unit_MonsterSize = MonsterSize.Unknown;
        private static DiaObject c_diaObject = null;
        private static DiaUnit c_diaUnit = null;
        private static ACD c_CommonData = null;
        private static SNOAnim c_CurrentAnimation = SNOAnim.Invalid;
        private static bool c_unit_IsElite = false;
        private static bool c_unit_IsRare = false;
        private static bool c_unit_IsUnique = false;
        private static bool c_unit_IsMinion = false;
        private static bool c_unit_IsTreasureGoblin = false;
        private static bool c_IsEliteRareUnique = false;
        private static bool c_unit_IsBoss = false;
        private static bool c_unit_IsAttackable = false;
        private static bool c_unit_IsShielded = false;
        private static bool c_ForceLeapAgainst = false;
        private static bool c_IsObstacle = false;
        private static bool c_HasBeenNavigable = false;
        private static bool c_HasBeenRaycastable = false;
        private static bool c_HasBeenInLoS = false;
        private static string c_ItemMd5Hash = string.Empty;
        private static bool c_HasDotDPS = false;
        private static string c_ObjectHash = String.Empty;
        private static double c_KillRange = 0f;

        // From main RefreshDiaobjects
        /// <summary>
        /// The position of the last CurrentTarget (Primary Target)
        /// </summary>
        internal static Vector3 LastPrimaryTargetPosition;
        private static Vector3 vKitePointAvoid;
        /// <summary>
        /// The RActorGUID of the last CurrentTarget (PrimaryTarget)
        /// </summary>
        private static int CurrentTargetRactorGUID;
        /// <summary>
        /// The number of monsters within melee range distance of the player
        /// </summary>
        private static double w_HighestWeightFound;
        private static HashSet<int> hashDoneThisRactor;
        private static bool NeedToKite = false;
        private static bool TryToKite = false;


        /// <summary>
        /// Used for trimming off numbers from object names in RefreshDiaObject
        /// </summary>
        private static Regex nameNumberTrimRegex = new Regex(@"-\d+$");

        // The following 2 variables are used to clear the dictionaries out - clearing one dictionary out per maximum every 2 seconds, working through in sequential order
        private static DateTime lastClearedCacheDictionary = DateTime.Today;

        private static Button btnPauseBot, btnTownRun;

        // On death, clear the timers for all abilities
        private static DateTime lastDied = DateTime.Today;
        private static int iTotalDeaths = 0;

        // When did we last send a move-power command?
        private static DateTime lastSentMovePower = DateTime.Today;


        /// <summary>
        /// If we should force movement
        /// </summary>
        private static bool bForceNewMovement = false;

        /// <summary>
        /// Store player current position
        /// </summary>
        Vector3 vMyCurrentPosition = Vector3.Zero;

        // For path finding
        /// <summary>
        /// The Grid Provider for Navigation checks
        /// </summary>
        internal static ISearchAreaProvider gp
        {
            get
            {
                return (Navigator.SearchGridProvider as MainGridProvider);
            }
        }
        /// <summary>
        /// The PathFinder for Navigation checks
        /// </summary>
        //internal static PathFinder pf;

        /// <summary>
        /// Behaviors: How close we need to get to the target before we consider it "reached"
        /// </summary>
        private static float TargetRangeRequired = 1f;

        /// <summary>
        /// An adjusted distance from the current target, <seealso cref="GilesTrinity.TargetDistanceReduction"/>
        /// </summary>
        private static float TargetCurrentDistance;

        /// <summary>
        /// If our current target is in LoS for use in Behavior handling
        /// </summary>
        private static bool CurrentTargetIsInLoS;

        /// <summary>
        /// This value is subtracted from the distance to the target to get the modified <see cref="GilesTrinity.TargetCurrentDistance"/>
        /// </summary>
        private static float TargetDistanceReduction = 0f;

        /// <summary>
        /// Special check to force re-buffing before castign archon
        /// </summary>
        private static bool CanCastArchon = false;

        // Darkfriend's Looting Rule
        public static Interpreter StashRule = null; // = new Interpreter();

        // Tesslerc - used for using combination strike
        // ForesightFirstHit is used to track the 30 second buff from deadly reach.
        private static DateTime ForeSightFirstHit = new DateTime(1996, 6, 3, 22, 15, 0);
        // Foresight2 is used to track combination strike buff.
        private static DateTime ForeSight2 = DateTime.Now;
        // Otherthandeadlyreach is used for other spirit generators to track for combination strike buff.
        private static DateTime OtherThanDeadlyReach = DateTime.Now;
        // Set by sweeping winds or by blinding flash if the time is right for a swap.
        private static bool WantToSwap = false;

        // Xp Counter
        private static int iTotalXp = 0;
        private static int iLastXp = 0;
        private static int iNextLvXp = 0;
        // Gold counter
        private static int iTotalGold = 0;
        private static int iLastGold = 0;

        private static DefaultNavigationProvider NavProvider;
    }
}
