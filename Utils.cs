using BepInEx.Logging;
using GameNetcodeStuff;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs
{
    public static class Utils
    {
        private static ManualLogSource logger = LoggerInstance;

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

        public static T? GetClosestGameObjectOfType<T>(Vector3 position) where T : Component
        {
            T[] objects = GameObject.FindObjectsOfType<T>();
            T closest = null!;
            float closestDistance = Mathf.Infinity;

            foreach (T obj in objects)
            {
                float distance = Vector3.Distance(position, obj.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = obj;
                }
            }

            return closest;
        }

        public static Dictionary<string, GameObject> GetAllHazards()
        {
            Dictionary<string, GameObject> hazards = new Dictionary<string, GameObject>();
            List<SpawnableMapObject> spawnableMapObjects = (from x in StartOfRound.Instance.levels.SelectMany((SelectableLevel level) => level.spawnableMapObjects)
                                              group x by ((UnityEngine.Object)x.prefabToSpawn).name into g
                                              select g.First()).ToList();
            foreach (SpawnableMapObject item in spawnableMapObjects)
            {
                hazards.Add(item.prefabToSpawn.name, item.prefabToSpawn);
            }
            return hazards;
        }

        public static Vector3 GetRandomNavMeshPositionInAnnulus(Vector3 center, float minRadius, float maxRadius, int sampleCount = 10)
        {
            Vector3 randomDirection;
            float y = center.y;

            // Make sure minRadius is less than maxRadius
            if (minRadius >= maxRadius)
            {
                logger.LogWarning("minRadius should be less than maxRadius. Returning original position.");
                return center;
            }

            // Try a few times to get a valid point
            for (int i = 0; i < sampleCount; i++)
            {
                // Get a random direction
                randomDirection = UnityEngine.Random.insideUnitSphere;
                randomDirection.y = 0f;
                randomDirection.Normalize();

                // Random distance between min and max radius
                float distance = UnityEngine.Random.Range(minRadius, maxRadius);

                // Calculate the new position
                Vector3 pos = center + randomDirection * distance;
                pos.y = y;

                // Check if it's on the NavMesh
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            logger.LogWarning("Unable to find valid NavMesh position in annulus. Returning original position.");
            return center;
        }


        public static List<Vector3> GetEvenlySpacedNavMeshPositions(Vector3 center, int count, float minRadius, float maxRadius)
        {
            List<Vector3> positions = new List<Vector3>();

            // Validate
            if (count <= 0 || minRadius > maxRadius)
            {
                logger.LogWarning("Invalid parameters for turret spawn positions.");
                return positions;
            }

            float y = center.y;
            float angleStep = 360f / count;

            for (int i = 0; i < count; i++)
            {
                // Angle in degrees
                float angle = i * angleStep;

                // Convert angle to radians
                float radians = angle * Mathf.Deg2Rad;

                // Use random radius between min and max for some variation (optional)
                float radius = UnityEngine.Random.Range(minRadius, maxRadius);

                // Direction on XZ plane
                float x = Mathf.Cos(radians) * radius;
                float z = Mathf.Sin(radians) * radius;

                Vector3 pos = new Vector3(center.x + x, y, center.z + z);

                // Try to snap to NavMesh
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    positions.Add(hit.position);
                }
                else
                {
                    logger.LogWarning($"Could not find valid NavMesh position for turret {i}. Skipping.");
                }
            }

            return positions;
        }

    }
}
