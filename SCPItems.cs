﻿using ES3Types;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

// scp-428

namespace HeavyItemSCPs
{
    public static class SCPItems
    {
        public static List<SCPItem> SCPItemsList = new List<SCPItem>();
        public static List<EnemyType> SCPEnemiesList = new List<EnemyType>();


        public static void Load(string pathName, ObjectClass objClass, string? levelRarities = null, string? customLevelRarities = null, int minValue = 0, int maxValue = 0, int? maxSpawns = null, bool tabletop = false, bool general = false, bool small = false)
        {
            Item SCP = ModAssets.LoadAsset<Item>($"Assets/ModAssets/{pathName}/{pathName}Item.asset");
            if (SCP == null) { LoggerInstance.LogError($"Error: Couldnt get {pathName} from assets"); return; }
            LoggerInstance.LogDebug($"Got {pathName} prefab");

            if (maxValue > 0 && minValue < maxValue)
            {
                SCP.minValue = minValue;
                SCP.maxValue = maxValue;
            }

            SCPItem scp = new SCPItem();
            scp.item = SCP;
            scp.MaxSpawns = maxSpawns;
            scp.ObjectClass = objClass;
            scp.TabletopItem = tabletop;
            scp.GeneralItem = general;
            scp.SmallItem = small;
            SCPItemsList.Add(scp);

            Dictionary<Levels.LevelTypes, int> levelRaritiesDict = new Dictionary<Levels.LevelTypes, int>();
            Dictionary<string, int> customLevelRaritiesDict = new Dictionary<string, int>();

            if (levelRarities != null)
            {
                string[] levels = levelRarities.Split(',');

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
            if (customLevelRarities != null)
            {
                string[] levels = customLevelRarities.Split(',');

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

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP.spawnPrefab);
            LethalLib.Modules.Utilities.FixMixerGroups(SCP.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(SCP, levelRaritiesDict, customLevelRaritiesDict);
        }

        public static void LoadShopItem(string pathName, ObjectClass objClass, int price = -1, bool keter = false)
        {
            Item SCP = ModAssets.LoadAsset<Item>($"Assets/ModAssets/{pathName}/{pathName}Item.asset");
            if (SCP == null) { LoggerInstance.LogError($"Error: Couldnt get {pathName} from assets"); return; }
            LoggerInstance.LogDebug($"Got {pathName} prefab");

            SCPItem scp = new SCPItem();
            scp.item = SCP;
            scp.ObjectClass = objClass;
            SCPItemsList.Add(scp);

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP.spawnPrefab);
            LethalLib.Modules.Utilities.FixMixerGroups(SCP.spawnPrefab);
            LethalLib.Modules.Items.RegisterShopItem(SCP, price);
        }

        public static void LoadEnemy(string enemyPath, string itemPath, bool enableSpawning = true)
        {
            EnemyType enemy = ModAssets.LoadAsset<EnemyType>($"Assets/ModAssets/{itemPath}/{enemyPath}Enemy.asset");
            if (enemy == null) { LoggerInstance.LogError($"Error: Couldnt get {enemyPath} from assets"); return; }
            LoggerInstance.LogDebug($"Got {enemyPath} prefab");
            enemy.spawningDisabled = !enableSpawning;
            TerminalNode terminalNode = ModAssets.LoadAsset<TerminalNode>($"Assets/ModAssets/{itemPath}/Bestiary/{enemyPath}TN.asset");
            TerminalKeyword terminalKeyword = ModAssets.LoadAsset<TerminalKeyword>($"Assets/ModAssets/{itemPath}/Bestiary/{enemyPath}TK.asset");
            if (terminalNode == null) { LoggerInstance.LogError($"Error: Couldnt get {enemyPath}TN from assets"); return; }
            if (terminalKeyword == null) { LoggerInstance.LogError($"Error: Couldnt get {enemyPath}TK from assets"); return; }

            SCPEnemiesList.Add(enemy);
            LoggerInstance.LogDebug($"Registering {enemyPath} enemy network prefab...");
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(enemy.enemyPrefab);
            LoggerInstance.LogDebug($"Registering {enemyPath} enemy...");
            LethalLib.Modules.Enemies.RegisterEnemy(enemy, 0, Levels.LevelTypes.All, terminalNode, terminalKeyword);
        }
    }

    public class SCPItem
    {
        public Item item;
        public int? MaxSpawns;
        public ObjectClass ObjectClass;

        public bool TabletopItem = false;
        public bool GeneralItem = false;
        public bool SmallItem = false;

        public int itemsSpawnedInRound = 0;
    }
    public enum ObjectClass
    {
        Safe,
        Euclid,
        Keter
    }
}