using DunGen.Tags;
using GameNetcodeStuff;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using static HeavyItemSCPs.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

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

        public static List<SpawnableEnemyWithRarity> GetEnemies()
        {
            List<SpawnableEnemyWithRarity> enemies = new List<SpawnableEnemyWithRarity>();
            enemies = GameObject.Find("Terminal")
                .GetComponentInChildren<Terminal>()
                .moonsCatalogueList
                .SelectMany(x => x.Enemies.Concat(x.DaytimeEnemies).Concat(x.OutsideEnemies))
                .Where(x => x != null && x.enemyType != null && x.enemyType.name != null)
                .GroupBy(x => x.enemyType.name, (k, v) => v.First())
                .ToList();

            return enemies;
        }

        public static EnemyVent GetClosestVentToPosition(Vector3 pos)
        {
            float mostOptimalDistance = 2000f;
            EnemyVent targetVent = null!;
            foreach (var vent in RoundManager.Instance.allEnemyVents)
            {
                float distance = Vector3.Distance(pos, vent.floorNode.transform.position);

                if (distance < mostOptimalDistance)
                {
                    mostOptimalDistance = distance;
                    targetVent = vent;
                }
            }

            return targetVent;
        }

        public static bool CalculatePath(Vector3 fromPos, Vector3 toPos)
        {
            Vector3 from = RoundManager.Instance.GetNavMeshPosition(fromPos, RoundManager.Instance.navHit, 1.75f);
            Vector3 to = RoundManager.Instance.GetNavMeshPosition(toPos, RoundManager.Instance.navHit, 1.75f);

            NavMeshPath path = new();
            return NavMesh.CalculatePath(from, to, -1, path) && Vector3.Distance(path.corners[path.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(to, RoundManager.Instance.navHit, 2.7f)) <= 1.55f; // TODO: Test this
        }
    }
}
