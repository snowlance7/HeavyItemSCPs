using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using HeavyItemSCPs.Items.SCP427;
using HeavyItemSCPs.Items.SCP513;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.ProBuilder;
using static HeavyItemSCPs.Utils;

namespace HeavyItemSCPs
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInDependency("SCP500", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public static Plugin PluginInstance;
        public static ManualLogSource LoggerInstance;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return StartOfRound.Instance.localPlayerController; } }
        public static PlayerControllerB? PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts?.Where(x => x.actualClientId == id).FirstOrDefault(); }

        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        public static AssetBundle? ModAssets;

        // SCP-427 Configs
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public static ConfigEntry<bool> configEnableSCP427;
        public static ConfigEntry<int> config427MinValue;
        public static ConfigEntry<int> config427MaxValue;
        public static ConfigEntry<string> config427LevelRarities;
        public static ConfigEntry<string> config427CustomLevelRarities;

        public static ConfigEntry<float> configTimeToTransform;
        public static ConfigEntry<int> configHealthPerSecondOpen;
        public static ConfigEntry<bool> config427_500Compatibility;

        // SCP-427-1 Enemy Configs
        public static ConfigEntry<string> config4271LevelRarities;
        public static ConfigEntry<string> config4271CustomLevelRarities;

        // SCP-178 Configs
        public static ConfigEntry<bool> configEnableSCP178;
        public static ConfigEntry<int> config178MinValue;
        public static ConfigEntry<int> config178MaxValue;
        public static ConfigEntry<int> config178PartMinValue;
        public static ConfigEntry<int> config178PartMaxValue;

        public static ConfigEntry<string> config178LevelRarities;
        public static ConfigEntry<string> config178CustomLevelRarities;
        public static ConfigEntry<string> config178PartLevelRarities;
        public static ConfigEntry<string> config178PartCustomLevelRarities;

        public static ConfigEntry<float> config178LensDistortion;
        public static ConfigEntry<float> config178ChromaticAberration;
        public static ConfigEntry<string> config178ColorTint;

        // SCP-178-1 Configs
        public static ConfigEntry<int> config1781MinCount;
        public static ConfigEntry<int> config1781MaxCount;

        // SCP-323
        public static ConfigEntry<bool> configEnableSCP323;
        public static ConfigEntry<int> config323MinValue;
        public static ConfigEntry<int> config323MaxValue;
        public static ConfigEntry<string> config323LevelRarities;
        public static ConfigEntry<string> config323CustomLevelRarities;

        public static ConfigEntry<bool> config323ShowInsanity;

        // SCP-323-1
        public static ConfigEntry<string> config3231LevelRarities;
        public static ConfigEntry<string> config3231CustomLevelRarities;

        // SCP-513
        public static ConfigEntry<bool> configEnableSCP513;
        public static ConfigEntry<int> config513MinValue;
        public static ConfigEntry<int> config513MaxValue;
        public static ConfigEntry<string> config513LevelRarities;
        public static ConfigEntry<string> config513CustomLevelRarities;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.


        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            LoggerInstance = PluginInstance.Logger;

            harmony.PatchAll();

            InitializeNetworkBehaviours();

            InitConfigs();
            
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
                RegisterItem("Assets/ModAssets/SCP178/SCP1781Part_1.asset", config178PartLevelRarities.Value, config178PartCustomLevelRarities.Value, config178PartMinValue.Value, config178PartMaxValue.Value);
                RegisterItem("Assets/ModAssets/SCP178/SCP1781Part_2.asset", config178PartLevelRarities.Value, config178PartCustomLevelRarities.Value, config178PartMinValue.Value, config178PartMaxValue.Value);
                RegisterItem("Assets/ModAssets/SCP178/SCP1781Part_3.asset", config178PartLevelRarities.Value, config178PartCustomLevelRarities.Value, config178PartMinValue.Value, config178PartMaxValue.Value);
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

            // SCP-513
            if (configEnableSCP513.Value)
            {
                RegisterItem("Assets/ModAssets/SCP513/SCP513Item.asset", config513LevelRarities.Value, config513CustomLevelRarities.Value, config513MinValue.Value, config513MaxValue.Value);
                //RegisterEnemy("Assets/ModAssets/SCP513/SCP513_1Enemy.asset", "Assets/ModAssets/SCP513/Bestiary/SCP5131TN.asset", "Assets/ModAssets/SCP513/Bestiary/SCP5131TK.asset");
            }

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        void InitConfigs()
        {
            // Configs
            // SCP-427
            configEnableSCP427 = Config.Bind("SCP-427", "Enable SCP-427", true, "Whether or not SCP-427 can spawn as scrap.");
            config427MinValue = Config.Bind("SCP-427", "Minimum value", 100, "The minimum value of SCP-427.");
            config427MaxValue = Config.Bind("SCP-427", "Maximum value", 300, "The maximum value of SCP-427.");
            config427LevelRarities = Config.Bind("SCP-427 Rarities", "Level Rarities", "All: 10, Modded:10", "Rarities for each level. See default for formatting.");
            config427CustomLevelRarities = Config.Bind("SCP-427 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");

            configTimeToTransform = Config.Bind("SCP-427", "Time to transform", 60f, "How long a player can hold the necklace before they transform into SCP-427-1. Should be higher that 30. Set to -1 to disable transforming.");
            configHealthPerSecondOpen = Config.Bind("SCP-427", "Health per second open", 5, "The health gained per second while opening SCP-427.");
            config427_500Compatibility = Config.Bind("SCP-427", "SCP-500 compatibility", true, "Whether or not SCP-427 should be compatible with the SCP-500 mod. This will only work if you have the SCP-500 mod installed. If enabled, it will temporarily halt the transformation timer when holding or using SCP-427 when you take SCP-500.");

            // SCP-427-1
            config4271LevelRarities = Config.Bind("SCP-427-1 Rarities", "Level Rarities", "All: 10, Modded:10", "Rarities for each level. See default for formatting.");
            config4271CustomLevelRarities = Config.Bind("SCP-427-1 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");
            
            // SCP-178
            configEnableSCP178 = Config.Bind("SCP-178", "Enable SCP-178", true, "Whether or not SCP-178 can spawn as scrap.");
            config178MinValue = Config.Bind("SCP-178", "Minimum value", 100, "The minimum value of SCP-178.");
            config178MaxValue = Config.Bind("SCP-178", "Maximum value", 150, "The maximum value of SCP-178.");
            config178LevelRarities = Config.Bind("SCP-178 Rarities", "Level Rarities", "All:10, Modded:10", "Rarities for each level. See default for formatting.");
            config178CustomLevelRarities = Config.Bind("SCP-178 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");
            config178PartMinValue = Config.Bind("SCP-178", "Minimum value", 250, "The minimum value of SCP-178 Parts.");
            config178PartMaxValue = Config.Bind("SCP-178", "Maximum value", 500, "The maximum value of SCP-178 Parts.");
            config178PartLevelRarities = Config.Bind("SCP-178 Parts Rarities", "Level Rarities", "All:30, Modded:30", "Rarities for each level. See default for formatting.");
            config178PartCustomLevelRarities = Config.Bind("SCP-178 Parts Rarities", "Custom Level Rarities", "Secret LabsLevel:150", "Rarities for modded levels. Same formatting as level rarities.");

            config178LensDistortion = Config.Bind("SCP-178 3D Effects", "Lens Distortion", -0.2f, "Changes the lens distortion effect of the 3D glasses.");
            config178ChromaticAberration = Config.Bind("SCP-178 3D Effects", "Chromatic Aberration", 3f, "Changes the chromatic aberration effect of the 3D glasses.");
            config178ColorTint = Config.Bind("SCP-178 3D Effects", "Color Tint", "500,0,500", "Changes the RGB color tint effect of the 3D glasses.");

            // SCP-1781
            config1781MinCount = Config.Bind("SCP-1781", "Min Count", 50, "The minimum number of SCP-178-1 instances that can be spawned.");
            config1781MaxCount = Config.Bind("SCP-1781", "Max Count", 100, "The maximum number of SCP-178-1 instances that can be spawned.");

            // SCP-323
            configEnableSCP323 = Config.Bind("SCP-323", "Enable SCP-323", true, "Whether or not SCP-323 can spawn as scrap.");
            config323MinValue = Config.Bind("SCP-323", "Minimum value", 100, "The minimum value of SCP-323.");
            config323MaxValue = Config.Bind("SCP-323", "Maximum value", 150, "The maximum value of SCP-323.");
            config323LevelRarities = Config.Bind("SCP-323 Rarities", "Level Rarities", "All: 10, Modded:10", "Rarities for each level. See default for formatting.");
            config323CustomLevelRarities = Config.Bind("SCP-323 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");

            config323ShowInsanity = Config.Bind("SCP-323", "Show insanity", false, "Blur the players vision when they are near SCP-323 based on their insanity.");

            // SCP-323-1
            config3231LevelRarities = Config.Bind("SCP-323-1 Rarities", "Level Rarities", "All: 10, Modded:10", "Rarities for each level. See default for formatting.");
            config3231CustomLevelRarities = Config.Bind("SCP-323-1 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");

            // SCP-513
            configEnableSCP513 = Config.Bind("SCP-513", "Enable SCP-513", true, "Whether or not SCP-513 can spawn as scrap.");
            config513MinValue = Config.Bind("SCP-513", "Minimum value", 150, "The minimum value of SCP-513.");
            config513MaxValue = Config.Bind("SCP-513", "Maximum value", 300, "The maximum value of SCP-513.");
            config513LevelRarities = Config.Bind("SCP-513 Rarities", "Level Rarities", "All: 10, Modded:10", "Rarities for each level. See default for formatting.");
            config513CustomLevelRarities = Config.Bind("SCP-513 Rarities", "Custom Level Rarities", "Secret LabsLevel:100", "Rarities for modded levels. Same formatting as level rarities.");
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
