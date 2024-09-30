using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;

//using HeavyItemSCPs.Items.SCP178;
using LethalLib.Modules;
using Steamworks.Data;
using Steamworks.Ugc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyItemSCPs
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("SCP500", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        const string PLUGIN_GUID = "ProjectSCP.HeavyItemSCPs";
        const string PLUGIN_NAME = "HeavyItemSCPs";
        const string PLUGIN_VERSION = "1.1.0";

        public static Plugin PluginInstance;
        public static ManualLogSource LoggerInstance;
        private readonly Harmony harmony = new Harmony(PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return StartOfRound.Instance.localPlayerController; } }

        public static AssetBundle? ModAssets;

        // SCP-427 Configs
        public static ConfigEntry<bool> configEnableSCP427;
        public static ConfigEntry<int> config427MinValue;
        public static ConfigEntry<int> config427MaxValue;
        public static ConfigEntry<string> config427LevelRarities;
        public static ConfigEntry<string> config427CustomLevelRarities;
        public static ConfigEntry<int> config427SCPDungeonRarity;

        public static ConfigEntry<bool> configEnableOpenNecklace;
        public static ConfigEntry<float> configTimeToTransform;
        public static ConfigEntry<float> configTransformOpenMultiplier;
        public static ConfigEntry<int> configHealthPerSecondHolding;
        public static ConfigEntry<int> configHealthPerSecondOpen;
        public static ConfigEntry<float> configHoarderBugTransformTime;
        public static ConfigEntry<float> configBaboonHawkTransformTime;
        public static ConfigEntry<float> configTimeToSpawnSCP4271;
        public static ConfigEntry<bool> configContinueSpawningAfterPickup;
        public static ConfigEntry<int> configMaxSpawns;
        public static ConfigEntry<bool> configIncreaseTimeWhenInInventory;
        public static ConfigEntry<bool> config427_500Compatibility;
        public static ConfigEntry<bool> config427ResetTransformTimeAtOrbit;

        // SCP-427-1 Enemy Configs
        public static ConfigEntry<bool> configEnableSCP4271Transformation;
        public static ConfigEntry<bool> configEnableSCP4271Spawning;
        public static ConfigEntry<string> config4271LevelRarities;
        public static ConfigEntry<string> config4271CustomLevelRarities;
        public static ConfigEntry<int> config4271SCPDungeonRarity;

        // SCP-178 Configs
        public static ConfigEntry<bool> configEnableSCP178;
        public static ConfigEntry<int> config178MinValue;
        public static ConfigEntry<int> config178MaxValue;

        public static ConfigEntry<string> config178LevelRarities;
        public static ConfigEntry<string> config178CustomLevelRarities;

        public static ConfigEntry<float> config178LensDistortion;
        public static ConfigEntry<float> config178ChromaticAberration;
        public static ConfigEntry<string> config178ColorTint;

        // SCP-178-1 Configs
        public static ConfigEntry<int> config1781MaxCount;
        //public static ConfigEntry<float> config1781RenderDistance;
        public static ConfigEntry<float> config1781DespawnTime;
        public static ConfigEntry<float> config1781WanderingWaitTime;
        public static ConfigEntry<float> config1781WanderingRadius;
        public static ConfigEntry<float> config1781PostObservationTime;

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
            config427MaxValue = Config.Bind("SCP-427", "Maximum value", 150, "The maximum value of SCP-427.");
            config427LevelRarities = Config.Bind("SCP-427 Rarities", "Level Rarities", "ExperimentationLevel:5, AssuranceLevel:7, VowLevel:10, OffenseLevel:15, AdamanceLevel:25, MarchLevel:15, RendLevel:30, DineLevel:35, TitanLevel:45, ArtificeLevel:25, EmbrionLevel:50, All:10, Modded:15", "Rarities for each level. See default for formatting.");
            config427CustomLevelRarities = Config.Bind("SCP-427 Rarities", "Custom Level Rarities", "Secret LabsLevel:50", "Rarities for modded levels. Same formatting as level rarities.");
            config427SCPDungeonRarity = Config.Bind("SCP-427 Rarities", "SCP Dungeon Rarity", 100, "The rarity of SCP-427 in the SCP Dungeon. Set to -1 to use level rarities.");

            configEnableOpenNecklace = Config.Bind("SCP-427", "Enable isOpen necklace", true, "Whether or not players can isOpen the necklace.");
            configTimeToTransform = Config.Bind("SCP-427", "Time to transform", 120f, "How long a player can hold the necklace before they transform into SCP-427-1. Set to -1 to disable transforming.");
            configTransformOpenMultiplier = Config.Bind("SCP-427", "Transform isOpen multiplier", 3f, "The multiplier applied to the time it takes to transform when the necklace is opened. configTimeToTransform will be multiplied by this value.");
            configHealthPerSecondHolding = Config.Bind("SCP-427", "Health per second holding", 5, "The health gained per second while holding SCP-427.");
            configHealthPerSecondOpen = Config.Bind("SCP-427", "Health per second isOpen", 10, "The health gained per second while opening SCP-427.");
            configHoarderBugTransformTime = Config.Bind("SCP-427", "Hoarder bug transform time", 15f, "The time it takes for the hoarder bug to transform into SCP-427-1. Set to -1 to disable transforming.");
            configBaboonHawkTransformTime = Config.Bind("SCP-427", "Baboon hawk transform time", 10f, "The time it takes for the baboon hawk to transform into SCP-427-1. Set to -1 to disable transforming.");
            configTimeToSpawnSCP4271 = Config.Bind("SCP-427", "Time to spawn SCP-427-1", 300f, "The time it takes for SCP-427 to spawn SCP-427-1 when on the ground. Set to -1 to disable spawning from necklace.");
            configContinueSpawningAfterPickup = Config.Bind("SCP-427", "Continue spawning after picked up", false, "Whether or not SCP-427 should continue spawning SCP-427-1 instances after being picked up by a player or lootbug. Setting this to true will make SCP-427 keep spawning SCP-427-1 instances if its on the ground, regardless of whether its been picked up or not."); // TODO: Set to true for testing, change back later
            configMaxSpawns = Config.Bind("SCP-427", "Max spawns", 1, "The maximum number of SCP-427-1 instances that can be spawned when SCP-427 is on the ground.");
            configIncreaseTimeWhenInInventory = Config.Bind("SCP-427", "Increase time when in inventory", true, "Whether or not timeHoldingSCP427 should increase when SCP-427 is on hotbar but not being held.");
            config427_500Compatibility = Config.Bind("SCP-427", "SCP-500 compatibility", true, "Whether or not SCP-427 should be compatible with the SCP-500 mod. This will only work if you have the SCP-500 mod installed. If enabled, it will temporarily halt the transformation timer when holding or using SCP-427 when you take SCP-500.");
            config427ResetTransformTimeAtOrbit = Config.Bind("SCP-427", "Reset transform time at orbit", false, "Whether or not SCP-427 should reset its transformation timer when orbiting.");

            // SCP-427-1
            configEnableSCP4271Transformation = Config.Bind("SCP-427-1", "Enable SCP-427-1 transformation", true, "Whether or not players or monsters can turn into SCP-427-1.");
            configEnableSCP4271Spawning = Config.Bind("SCP-427-1", "Enable SCP-427-1 spawning", false, "Whether or not SCP-427-1 can spawn naturally from vents.");
            config4271LevelRarities = Config.Bind("SCP-427-1 Rarities", "Level Rarities", "ExperimentationLevel:40, AssuranceLevel:40, VowLevel:40, OffenseLevel:60, AdamanceLevel:60, MarchLevel:60, RendLevel:25, DineLevel:25, TitanLevel:65, ArtificeLevel:70, EmbrionLevel:75, All:30, Modded:30", "Rarities for each level. See default for formatting.");
            config4271CustomLevelRarities = Config.Bind("SCP-427-1 Rarities", "Custom Level Rarities", "", "Rarities for modded levels. Same formatting as level rarities.");
            config4271SCPDungeonRarity = Config.Bind("SCP-427-1 Rarities", "SCP Dungeon Rarity", 100, "The rarity of SCP-427-1 in the SCP Dungeon. Set to -1 to use level rarities.");

            // SCP-178
            configEnableSCP178 = Config.Bind("SCP-178", "Enable SCP-178", true, "Whether or not SCP-178 can spawn as scrap.");
            config178MinValue = Config.Bind("SCP-178", "Minimum value", 100, "The minimum value of SCP-178.");
            config178MaxValue = Config.Bind("SCP-178", "Maximum value", 150, "The maximum value of SCP-178.");
            config178LevelRarities = Config.Bind("SCP-178 Rarities", "Level Rarities", "ExperimentationLevel:30, AssuranceLevel:30, VowLevel:30, OffenseLevel:25, AdamanceLevel:25, MarchLevel:25, RendLevel:20, DineLevel:20, TitanLevel:10, ArtificeLevel:15, EmbrionLevel:50, All:20, Modded:30", "Rarities for each level. See default for formatting.");
            config178CustomLevelRarities = Config.Bind("SCP-178 Rarities", "Custom Level Rarities", "Secret LabsLevel:75", "Rarities for modded levels. Same formatting as level rarities.");

            config178LensDistortion = Config.Bind("SCP-178 3D Effects", "Lens Distortion", -0.2f, "Changes the lens distortion effect of the 3D glasses.");
            config178ChromaticAberration = Config.Bind("SCP-178 3D Effects", "Chromatic Aberration", 3f, "Changes the chromatic aberration effect of the 3D glasses.");
            config178ColorTint = Config.Bind("SCP-178 3D Effects", "Color Tint", "484.5,0,675", "Changes the RGB color tint effect of the 3D glasses.");

            // SCP-1781
            config1781MaxCount = Config.Bind("SCP-1781", "Max count", 50, "The maximum number of SCP-178-1 instances that can be spawned. This is for outside and inside instances. ex: 50 will spawn a max of 50 inside and 50 outside.");
            //config1781RenderDistance = Config.Bind("SCP-1781", "Render distance", 50f, "The distance at which SCP-178-1 instances will run their AI. Any instances outside this distance will be disabled, still showing the model but not moving around. Lower values can help with performance.");
            config1781DespawnTime = Config.Bind("SCP-1781", "Despawn time", 30f, "The time it takes for SCP-178-1 instances to despawn when not wearing the glasses.");
            config1781PostObservationTime = Config.Bind("SCP-1781", "Post observation time", 3f, "The time it takes for SCP-178-1 instances to return to their roaming phase after being stared at.");
            config1781WanderingRadius = Config.Bind("SCP-1781", "Wandering radius", 10f, "The radius around SCP-178-1 spawn position that they will roam around in.");
            config1781WanderingWaitTime = Config.Bind("SCP-1781", "Wandering wait time", 5f, "When spawned, SCP-178-1 will pick a random position in their wandering radius and walk to it. This determines how long they will wait until picking another position to walk to.");


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
                SCPItems.Load("SCP427", ObjectClass.Safe, config427LevelRarities.Value, config427CustomLevelRarities.Value, config427SCPDungeonRarity.Value, config427MinValue.Value, config427MaxValue.Value, 1);
                SCPItems.LoadEnemy("SCP4271", "SCP427", config4271LevelRarities.Value, config4271CustomLevelRarities.Value, config4271SCPDungeonRarity.Value, configEnableSCP4271Spawning.Value);
            }

            // SCP-178
            if (configEnableSCP178.Value)
            {
                SCPItems.Load("SCP178", ObjectClass.Safe, config178LevelRarities.Value, config178CustomLevelRarities.Value, default, config178MinValue.Value, config178MaxValue.Value, 1);
                SCPItems.LoadEnemy("SCP1781", "SCP178", null, null, 0, false);
                SCP1783DVision.Load();
            }

            // Finished
            Logger.LogInfo($"{PLUGIN_GUID} v{PLUGIN_VERSION} has loaded!");
        }

        public static Dictionary<Levels.LevelTypes, int> GetLevelRarities(string levelsString)
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

        public static Dictionary<string, int> GetCustomLevelRarities(string levelsString)
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
