using GameNetcodeStuff;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs
{
    public static class Utils
    {
        public static void RegisterItem(string itemPath, string levelRarities = "", string customLevelRarities = "", int minValue = 0, int maxValue = 0)
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

        public static void RegisterEnemy(string enemyPath, string tnPath, string tkPath, string levelRarities = "", string customLevelRarities = "")
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

        public static void FreezePlayer(PlayerControllerB player, bool value)
        {
            player.disableInteract = value;
            player.disableLookInput = value;
            player.disableMoveInput = value;
        }

        public static void DespawnItemInSlotOnClient(int itemSlot)
        {
            HUDManager.Instance.itemSlotIcons[itemSlot].enabled = false;
            localPlayer.DestroyItemInSlotAndSync(itemSlot);
        }

        public static void MakePlayerInvisible(PlayerControllerB player, bool value)
        {
            GameObject scavengerModel = player.gameObject.transform.Find("ScavengerModel").gameObject;
            if (scavengerModel == null) { LoggerInstance.LogError("ScavengerModel not found"); return; }
            scavengerModel.transform.Find("LOD1").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD2").gameObject.SetActive(!value);
            scavengerModel.transform.Find("LOD3").gameObject.SetActive(!value);
            player.playerBadgeMesh.gameObject.SetActive(value);
        }
    }
}
