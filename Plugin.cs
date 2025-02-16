using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using HeavyItemSCPs.Items.SCP427;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.ProBuilder;

namespace HeavyItemSCPs
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("SCP500", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin PluginInstance;
        public static ManualLogSource LoggerInstance;
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return StartOfRound.Instance.localPlayerController; } }
        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).First(); }

        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        public static AssetBundle? ModAssets;

        // SCP-427 Configs
        public static ConfigEntry<bool> configEnableSCP427;
        public static ConfigEntry<int> config427MinValue;
        public static ConfigEntry<int> config427MaxValue;
        public static ConfigEntry<string> config427LevelRarities;
        public static ConfigEntry<string> config427CustomLevelRarities;
        public static ConfigEntry<int> config427SCPDungeonRarity;

        public static ConfigEntry<float> configTimeToTransform;
        public static ConfigEntry<int> configHealthPerSecondOpen;
        public static ConfigEntry<float> configHoarderBugTransformTime;
        public static ConfigEntry<float> configBaboonHawkTransformTime;
        public static ConfigEntry<float> configOtherEnemyTransformTime;
        public static ConfigEntry<float> configTimeToSpawnSCP4271;
        public static ConfigEntry<int> configMaxSpawns;
        public static ConfigEntry<bool> config427_500Compatibility;

        // SCP-427-1 Enemy Configs
        public static ConfigEntry<string> config4271LevelRarities;
        public static ConfigEntry<string> config4271CustomLevelRarities;
        public static ConfigEntry<int> config4271SCPDungeonRarity;
        public static ConfigEntry<SCP4271AI.DropMethod> config4271DropMethod;
        public static ConfigEntry<int> config4271MaxHealth;

        // SCP-178 Configs
        public static ConfigEntry<bool> configEnableSCP178;
        public static ConfigEntry<int> config178MinValue;
        public static ConfigEntry<int> config178MaxValue;

        public static ConfigEntry<string> config178LevelRarities;
        public static ConfigEntry<string> config178CustomLevelRarities;
        public static ConfigEntry<int> config178SCPDungeonRarity;

        public static ConfigEntry<float> config178LensDistortion;
        public static ConfigEntry<float> config178ChromaticAberration;
        public static ConfigEntry<string> config178ColorTint;
        public static ConfigEntry<bool> config178SeeScrapThroughWalls;
        public static ConfigEntry<float> config178SeeScrapRange;

        // SCP-178-1 Configs
        public static ConfigEntry<int> config1781MaxCountInside;
        public static ConfigEntry<int> config1781MaxCountOutside;
        public static ConfigEntry<float> config1781RenderDistance;
        public static ConfigEntry<float> config1781DespawnTime;
        public static ConfigEntry<float> config1781WanderingWaitTime;
        public static ConfigEntry<float> config1781WanderingRadius;
        public static ConfigEntry<float> config1781PostObservationTime;
        public static ConfigEntry<float> config1781DistanceToAddAnger;
        public static ConfigEntry<float> config1781ObservationGracePeriod;
        public static ConfigEntry<bool> config1781UsePercentageBasedCount;
        public static ConfigEntry<float> config1781MaxPercentCountInside;
        public static ConfigEntry<float> config1781MaxPercentCountOutside;

        // SCP-323
        public static ConfigEntry<bool> configEnableSCP323;
        public static ConfigEntry<int> config323MinValue;
        public static ConfigEntry<int> config323MaxValue;
        public static ConfigEntry<string> config323LevelRarities;
        public static ConfigEntry<string> config323CustomLevelRarities;
        public static ConfigEntry<int> config323SCPDungeonRarity;

        public static ConfigEntry<float> config323DistanceToIncreaseInsanity;
        public static ConfigEntry<int> config323InsanityNearby;
        public static ConfigEntry<int> config323InsanityHolding;
        public static ConfigEntry<int> config323InsanityWearing;
        public static ConfigEntry<int> config323InsanityToTransform;
        public static ConfigEntry<bool> config323ShowInsanity;
        public static ConfigEntry<bool> config323BlurVisionWhenAddingInsanity;

        // SCP-323-1
        public static ConfigEntry<string> config3231LevelRarities;
        public static ConfigEntry<string> config3231CustomLevelRarities;
        public static ConfigEntry<int> config3231SCPDungeonRarity;

        public static ConfigEntry<float> config3231PlayerBloodSenseRange;
        public static ConfigEntry<float> config3231MaskedBloodSenseRange;
        public static ConfigEntry<float> config3231DoorBashForce;
        public static ConfigEntry<int> config3231DoorBashDamage;
        public static ConfigEntry<int> config3231DoorBashAOEDamage;
        public static ConfigEntry<float> config3231DoorBashAOERange;
        public static ConfigEntry<bool> config3231DespawnDoorAfterBash;
        public static ConfigEntry<float> config3231DespawnDoorAfterBashTime;
        public static ConfigEntry<int> config3231MaxHP;
        public static ConfigEntry<int> config3231MaxDamage;
        public static ConfigEntry<int> config3231MinDamage;
        public static ConfigEntry<bool> config3231ReverseDamage;
        public static ConfigEntry<float> config3231ChaseMaxSpeed;
        public static ConfigEntry<float> config3231RoamMaxSpeed;
        public static ConfigEntry<float> config3231RoamMinSpeed;
        public static ConfigEntry<float> config3231TimeToLosePlayer;
        public static ConfigEntry<float> config3231SearchAfterLosePlayerTime;

        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            LoggerInstance = PluginInstance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            // Configs
            // SCP-427
            configEnableSCP427 = Config.Bind("SCP-427", "Enable SCP-427", true, "Whether or not SCP-427 can spawn as scrap.");
            config427MinValue = Config.Bind("SCP-427", "Minimum value", 100, "The minimum value of SCP-427.");
            config427MaxValue = Config.Bind("SCP-427", "Maximum value", 300, "The maximum value of SCP-427.");
            config427LevelRarities = Config.Bind("SCP-427 Rarities", "Level Rarities", "ExperimentationLevel:5, AssuranceLevel:7, VowLevel:10, OffenseLevel:15, AdamanceLevel:25, MarchLevel:15, RendLevel:30, DineLevel:35, TitanLevel:45, ArtificeLevel:25, EmbrionLevel:50, Modded:15", "Rarities for each level. See default for formatting.");
            config427CustomLevelRarities = Config.Bind("SCP-427 Rarities", "Custom Level Rarities", "Secret LabsLevel:50", "Rarities for modded levels. Same formatting as level rarities.");
            config427SCPDungeonRarity = Config.Bind("SCP-427 Rarities", "SCP Dungeon Rarity", 100, "The rarity of SCP-427 in the SCP Dungeon. Set to -1 to use level rarities.");

            configTimeToTransform = Config.Bind("SCP-427", "Time to transform", 30f, "How long a player can hold the necklace before they transform into SCP-427-1. Set to -1 to disable transforming.");
            configHealthPerSecondOpen = Config.Bind("SCP-427", "Health per second open", 5, "The health gained per second while opening SCP-427.");
            configHoarderBugTransformTime = Config.Bind("SCP-427", "Hoarder bug transform time", 5f, "The time it takes for the hoarder bug to transform into SCP-427-1. Set to -1 to disable transforming.");
            configBaboonHawkTransformTime = Config.Bind("SCP-427", "Baboon hawk transform time", 5f, "The time it takes for the baboon hawk to transform into SCP-427-1. Set to -1 to disable transforming.");
            configOtherEnemyTransformTime = Config.Bind("SCP-427", "Other enemy transform time", 10f, "The time it takes for the other enemies to transform into SCP-427-1. Set to -1 to disable transforming.");
            configTimeToSpawnSCP4271 = Config.Bind("SCP-427", "Time to spawn SCP-427-1", 300f, "The time it takes for SCP-427 to spawn SCP-427-1 when on the ground. Set to -1 to disable spawning from necklace.");
            configMaxSpawns = Config.Bind("SCP-427", "Max spawns", 1, "The maximum number of SCP-427-1 instances that can be spawned when SCP-427 is on the ground.");
            config427_500Compatibility = Config.Bind("SCP-427", "SCP-500 compatibility", true, "Whether or not SCP-427 should be compatible with the SCP-500 mod. This will only work if you have the SCP-500 mod installed. If enabled, it will temporarily halt the transformation timer when holding or using SCP-427 when you take SCP-500.");
            
            // SCP-427-1
            config4271LevelRarities = Config.Bind("SCP-427-1 Rarities", "Level Rarities", "ExperimentationLevel:0, AssuranceLevel:0, VowLevel:0, OffenseLevel:0, AdamanceLevel:0, MarchLevel:0, RendLevel:0, DineLevel:0, TitanLevel:0, ArtificeLevel:0, EmbrionLevel:0, Modded:0", "Rarities for each level. See default for formatting.");
            config4271CustomLevelRarities = Config.Bind("SCP-427-1 Rarities", "Custom Level Rarities", "Secret LabsLevel:0", "Rarities for modded levels. Same formatting as level rarities.");
            config4271SCPDungeonRarity = Config.Bind("SCP-427-1 Rarities", "SCP Dungeon Rarity", -1, "The rarity of SCP-427-1 in the SCP Dungeon. Set to -1 to use level rarities.");
            config4271DropMethod = Config.Bind("SCP-427-1", "Drop method", SCP4271AI.DropMethod.DropAllItems, "When the player is grabbed by SCP-427-1, they should drop: 0 = Drop nothing, 1 = Drop held item, 2 = Drop two-handed item, 3 = Drop all items.");
            config4271MaxHealth = Config.Bind("SCP-427-1", "Max health", 50, "The maximum health of SCP-427-1.");

            // SCP-178
            configEnableSCP178 = Config.Bind("SCP-178", "Enable SCP-178", true, "Whether or not SCP-178 can spawn as scrap.");
            config178MinValue = Config.Bind("SCP-178", "Minimum value", 100, "The minimum value of SCP-178.");
            config178MaxValue = Config.Bind("SCP-178", "Maximum value", 150, "The maximum value of SCP-178.");
            config178LevelRarities = Config.Bind("SCP-178 Rarities", "Level Rarities", "ExperimentationLevel:10, AssuranceLevel:15, VowLevel:15, OffenseLevel:20, AdamanceLevel:30, MarchLevel:20, RendLevel:50, DineLevel:55, TitanLevel:60, ArtificeLevel:60, EmbrionLevel:65, Modded:20", "Rarities for each level. See default for formatting.");
            config178CustomLevelRarities = Config.Bind("SCP-178 Rarities", "Custom Level Rarities", "Secret LabsLevel:75", "Rarities for modded levels. Same formatting as level rarities.");
            config178SCPDungeonRarity = Config.Bind("SCP-178 Rarities", "SCP Dungeon Rarity", 100, "The rarity of SCP-178 in the SCP Dungeon. Set to -1 to use level rarities.");

            config178LensDistortion = Config.Bind("SCP-178 3D Effects", "Lens Distortion", -0.2f, "Changes the lens distortion effect of the 3D glasses.");
            config178ChromaticAberration = Config.Bind("SCP-178 3D Effects", "Chromatic Aberration", 3f, "Changes the chromatic aberration effect of the 3D glasses.");
            config178ColorTint = Config.Bind("SCP-178 3D Effects", "Color Tint", "500,0,500", "Changes the RGB color tint effect of the 3D glasses.");
            config178SeeScrapThroughWalls = Config.Bind("SCP-178", "See Scrap Through Walls", true, "Changes whether or not the 3D glasses can allow you to see scrap through walls.");
            config178SeeScrapRange = Config.Bind("SCP-178", "See Scrap Range", 50f, "Changes the range at which the 3D glasses can see scrap.");

            // SCP-1781
            config1781MaxCountOutside = Config.Bind("SCP-1781", "Max count outside", 50, "The maximum number of SCP-178-1 instances that can be spawned outside. -1 spawns on all ai nodes.");
            config1781MaxCountInside = Config.Bind("SCP-1781", "Max count inside", 50, "The maximum number of SCP-178-1 instances that can be spawned inside. -1 spawns on all ai nodes.");
            config1781RenderDistance = Config.Bind("SCP-1781", "Render distance", 30f, "The distance at which SCP-178-1 instances will run their AI. Any instances outside this distance will be disabled, still showing the model but not moving around. Lower values can help with performance. -1 disables this feature.");
            config1781DespawnTime = Config.Bind("SCP-1781", "Despawn time", 30f, "The time it takes for SCP-178-1 instances to despawn when not wearing the glasses.");
            config1781PostObservationTime = Config.Bind("SCP-1781", "Post observation time", 5f, "The time it takes for SCP-178-1 instances to return to their roaming phase after being stared at.");
            config1781WanderingRadius = Config.Bind("SCP-1781", "Wandering radius", 5f, "The radius around SCP-178-1 spawn position that they will roam around in.");
            config1781WanderingWaitTime = Config.Bind("SCP-1781", "Wandering wait time", 5f, "When spawned, SCP-178-1 will pick a random position in their wandering radius and walk to it. This determines how long they will wait until picking another position to walk to.");
            config1781DistanceToAddAnger = Config.Bind("SCP-1781", "Distance to add anger", 10f, "The distance you need to be from SCP-178-1 to increase their anger meter when looking at them.");
            config1781ObservationGracePeriod = Config.Bind("SCP-1781", "Observation grace period", 1f, "The time it takes for SCP-178-1 instances to start getting angry after staring at them.");
            config1781UsePercentageBasedCount = Config.Bind("SCP-1781 Percentage Based", "Use percentage based count", true, "If true, when putting on the 3D glasses, instead of using max count, it will get the amount of AI nodes and times it by this value to get the amount of SCP-178-1 instances it should spawn.");
            config1781MaxPercentCountInside = Config.Bind("SCP-1781 Percentage Based", "Max percent count inside", 0.5f, "The percentage of inside AI nodes that should have SCP-178-1 instances spawned on them.");
            config1781MaxPercentCountOutside = Config.Bind("SCP-1781 Percentage Based", "Max percent count outside", 0.5f, "The percentage of outside AI nodes that should have SCP-178-1 instances spawned on them.");

            // SCP-323
            configEnableSCP323 = Config.Bind("SCP-323", "Enable SCP-323", true, "Whether or not SCP-323 can spawn as scrap.");
            config323MinValue = Config.Bind("SCP-323", "Minimum value", 100, "The minimum value of SCP-323.");
            config323MaxValue = Config.Bind("SCP-323", "Maximum value", 150, "The maximum value of SCP-323.");
            config323LevelRarities = Config.Bind("SCP-323 Rarities", "Level Rarities", "ExperimentationLevel:5, AssuranceLevel:7, VowLevel:10, OffenseLevel:15, AdamanceLevel:25, MarchLevel:15, RendLevel:30, DineLevel:35, TitanLevel:45, ArtificeLevel:25, EmbrionLevel:50, Modded:15", "Rarities for each level. See default for formatting.");
            config323CustomLevelRarities = Config.Bind("SCP-323 Rarities", "Custom Level Rarities", "Secret LabsLevel:50", "Rarities for modded levels. Same formatting as level rarities.");
            config323SCPDungeonRarity = Config.Bind("SCP-323 Rarities", "SCP Dungeon Rarity", 100, "The rarity in the SCP Dungeon. Set to -1 to use level rarities.");

            config323DistanceToIncreaseInsanity = Config.Bind("SCP-323", "Distance to increase insanity", 10f, "The distance you need to be from SCP-323 for it to start decreasing your insanity.");
            config323InsanityNearby = Config.Bind("SCP-323", "Insanity nearby", 5, "The amount of insanity you will gain every 10 seconds of being near SCP-323.");
            config323InsanityHolding = Config.Bind("SCP-323", "Insanity holding", 10, "The amount of insanity you will gain every 10 seconds when you are holding SCP-323.");
            config323InsanityWearing = Config.Bind("SCP-323", "Insanity wearing", 10, "The amount of insanity you will gain every 10 seconds when you are wearing SCP-323.");
            config323InsanityToTransform = Config.Bind("SCP-323", "Insanity to transform", 50, "You will be forced to transform when you reach this insanity value. It cannot be stopped.");
            config323ShowInsanity = Config.Bind("SCP-323", "Show insanity", false, "Blur the players vision when they are near SCP-323 based on their insanity.");
            config323BlurVisionWhenAddingInsanity = Config.Bind("SCP-323", "Blur vision when adding insanity", true, "When adding sanity, the players vision will blur.");

            // SCP-323-1
            config3231LevelRarities = Config.Bind("SCP-323-1 Rarities", "Level Rarities", "ExperimentationLevel:0, AssuranceLevel:0, VowLevel:0, OffenseLevel:0, AdamanceLevel:0, MarchLevel:0, RendLevel:0, DineLevel:0, TitanLevel:0, ArtificeLevel:0, EmbrionLevel:0, Modded:0", "Rarities for each level. See default for formatting.");
            config3231CustomLevelRarities = Config.Bind("SCP-323-1 Rarities", "Custom Level Rarities", "Secret LabsLevel:0", "Rarities for modded levels. Same formatting as level rarities.");
            config3231SCPDungeonRarity = Config.Bind("SCP-323-1 Rarities", "SCP Dungeon Rarity", -1, "The rarity in the SCP Dungeon. Set to -1 to use level rarities.");

            config3231PlayerBloodSenseRange = Config.Bind("SCP-323-1", "Player blood sense range", 75f, "When the player takes damage, SCP-323-1 will enter BloodSearch phase if the player is in this range.");
            config3231MaskedBloodSenseRange = Config.Bind("SCP-323-1", "Masked blood sense range", 75f, "When a masked enemy takes damage, SCP-323-1 will enter BloodSearch phase if a masked is in this range.");
            config3231DoorBashForce = Config.Bind("SCP-323-1", "Door bash force", 35f, "The amount of force to apply to the door when SCP-323-1 bashes it.");
            config3231DoorBashDamage = Config.Bind("SCP-323-1", "Door bash damage", 30, "The amount of damage the player will take if the door hits them.");
            config3231DoorBashAOEDamage = Config.Bind("SCP-323-1", "Door bash AoE damage", 10, "The amount of damage the player will take if they are within range of the door when it is being bashed.");
            config3231DoorBashAOERange = Config.Bind("SCP-323-1", "Door bash AoE range", 5f, "The range of the AoE damage.");
            config3231DespawnDoorAfterBash = Config.Bind("SCP-323-1", "Despawn door after bash", true, "Whether the door should despawn after its been bashed.");
            config3231DespawnDoorAfterBashTime = Config.Bind("SCP-323-1", "Despawn door after bash time", 3f, "The time it takes for the door to despawn after being bashed.");

            config3231MaxHP = Config.Bind("SCP-323-1", "Max HP", 20, "The maximum amount of health SCP-323-1 can have.");
            config3231MaxDamage = Config.Bind("SCP-323-1", "Max damage", 50, "The maximum amount of damage SCP-323-1 can deal.");
            config3231MinDamage = Config.Bind("SCP-323-1", "Min damage", 10, "The minimum amount of damage SCP-323-1 can deal.");
            config3231ReverseDamage = Config.Bind("SCP-323-1", "Reverse damage", true, "When false, SCP-323-1 will do more damage the more health it has. When true, it will do more damage the less health it has.");
            config3231ChaseMaxSpeed = Config.Bind("SCP-323-1", "Chase max speed", 8.5f, "The maximum speed at which SCP-323-1 will chase the player based on health.");
            config3231RoamMaxSpeed = Config.Bind("SCP-323-1", "Roam max speed", 5f, "The maximum speed at which SCP-323-1 will roam based on health. This is also the min speed SCP-323-1 will roam at.");
            config3231RoamMinSpeed = Config.Bind("SCP-323-1", "Roam min speed", 3f, "The minimum speed at which SCP-323-1 will roam based on health.");
            config3231TimeToLosePlayer = Config.Bind("SCP-323-1", "Time to lose player", 5f, "The time the player needs to be out of LOS to lose SCP-323-1.");
            config3231SearchAfterLosePlayerTime = Config.Bind("SCP-323-1", "Search after lose player time", 25f, "The time it takes for SCP-323-1 to search after losing the player.");

            // Loading Assets
            string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "heavy_assets"));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            LoggerInstance.LogDebug($"Got AssetBundle at: {Path.Combine(sAssemblyLocation, "heavy_assets")}");

            // SCP-427
            if (configEnableSCP427.Value)
            {
                RegisterItem("Assets/ModAssets/SCP427/SCP427Item.asset", config427LevelRarities.Value, config427CustomLevelRarities.Value, config427MinValue.Value, config427MaxValue.Value);
                RegisterEnemy("Assets/ModAssets/SCP427/SCP4271Enemy.asset", "Assets/ModAssets/SCP427/Bestiary/SCP4271TN.asset", "Assets/ModAssets/SCP427/Bestiary/SCP4271TK.asset", config4271LevelRarities.Value, config4271CustomLevelRarities.Value);
            }

            // SCP-178
            if (configEnableSCP178.Value)
            {
                RegisterItem("Assets/ModAssets/SCP178/SCP178Item.asset", config178LevelRarities.Value, config178CustomLevelRarities.Value, config178MinValue.Value, config178MaxValue.Value);
                RegisterEnemy("Assets/ModAssets/SCP178/SCP1781Enemy.asset", "Assets/ModAssets/SCP178/Bestiary/SCP1781TN.asset", "Assets/ModAssets/SCP178/Bestiary/SCP1781TK.asset");
                SCP1783DVision.Load();
            }

            // SCP-323
            if (configEnableSCP323.Value)
            {
                RegisterItem("Assets/ModAssets/SCP323/SCP323Item.asset", config323LevelRarities.Value, config323CustomLevelRarities.Value, config323MinValue.Value, config323MaxValue.Value);
                RegisterEnemy("Assets/ModAssets/SCP323/SCP323_1Enemy.asset", "Assets/ModAssets/SCP323/Bestiary/SCP323_1TN.asset", "Assets/ModAssets/SCP323/Bestiary/SCP323_1TK.asset", config3231LevelRarities.Value, config3231CustomLevelRarities.Value);
            }

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        void RegisterItem(string itemPath, string levelRarities = "", string customLevelRarities = "", int minValue = 0, int maxValue = 0)
        {
            Item item = ModAssets!.LoadAsset<Item>(itemPath);
            if (item == null) { LoggerInstance.LogError($"Error: Couldn't get prefab from {itemPath}"); return; }
            LoggerInstance.LogDebug($"Got {item.name} prefab");

            item.minValue = minValue;
            item.maxValue = maxValue;

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(item.spawnPrefab);
            Utilities.FixMixerGroups(item.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(item, GetLevelRarities(levelRarities), GetCustomLevelRarities(customLevelRarities));
        }

        void RegisterEnemy(string enemyPath, string tnPath, string tkPath, string levelRarities = "", string customLevelRarities = "")
        {
            EnemyType enemy = ModAssets!.LoadAsset<EnemyType>(enemyPath);
            if (enemy == null) { LoggerInstance.LogError($"Error: Couldn't get prefab from {enemyPath}"); return; }
            LoggerInstance.LogDebug($"Got {enemy.name} prefab");

            TerminalNode tn = ModAssets.LoadAsset<TerminalNode>(tnPath);
            TerminalKeyword tk = ModAssets.LoadAsset<TerminalKeyword>(tkPath);

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(enemy.enemyPrefab);
            Enemies.RegisterEnemy(enemy, GetLevelRarities(levelRarities), GetCustomLevelRarities(customLevelRarities), tn, tk);
        }

        public static Dictionary<Levels.LevelTypes, int>? GetLevelRarities(string? levelsString)
        {
            try
            {
                Dictionary<Levels.LevelTypes, int> levelRaritiesDict = new Dictionary<Levels.LevelTypes, int>();

                if (levelsString != null && levelsString != "")
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (Enum.TryParse<Levels.LevelTypes>(levelType, out Levels.LevelTypes levelTypeEnum) && int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            levelRaritiesDict.Add(levelTypeEnum, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return levelRaritiesDict;
            }
            catch (Exception e)
            {
                LoggerInstance.LogError($"Error: {e}");
                return null;
            }
        }

        public static Dictionary<string, int>? GetCustomLevelRarities(string? levelsString)
        {
            try
            {
                Dictionary<string, int> customLevelRaritiesDict = new Dictionary<string, int>();

                if (levelsString != null)
                {
                    string[] levels = levelsString.Split(',');

                    foreach (string level in levels)
                    {
                        string[] levelSplit = level.Split(':');
                        if (levelSplit.Length != 2) { continue; }
                        string levelType = levelSplit[0].Trim();
                        string levelRarity = levelSplit[1].Trim();

                        if (int.TryParse(levelRarity, out int levelRarityInt))
                        {
                            customLevelRaritiesDict.Add(levelType, levelRarityInt);
                        }
                        else
                        {
                            LoggerInstance.LogError($"Error: Invalid level rarity: {levelType}:{levelRarity}");
                        }
                    }
                }
                return customLevelRaritiesDict;
            }
            catch (Exception e)
            {
                LoggerInstance.LogError($"Error: {e}");
                return null;
            }
        }

        public static void DespawnItemInSlotOnClient(int itemSlot)
        {
            HUDManager.Instance.itemSlotIcons[itemSlot].enabled = false;
            localPlayer.DestroyItemInSlotAndSync(itemSlot);
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
            LoggerInstance.LogDebug("Finished initializing network behaviours");
        }
    }
}
