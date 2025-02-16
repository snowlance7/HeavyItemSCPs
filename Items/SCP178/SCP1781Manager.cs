using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781Manager : MonoBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        public static SCP1781Manager Instance = null!;
        public static GameObject SCP1781Prefab = null!;

        public Dictionary<PlayerControllerB, int> PlayersAngerLevels = new Dictionary<PlayerControllerB, int>();

        private float despawnTimer;

        public static void Init()
        {
            if (Instance == null)
            {
                GameObject singletonObject = new GameObject("SCP1781Manager");
                Instance = singletonObject.AddComponent<SCP1781Manager>();
            }
        }

        public void Start()
        {
            logger.LogDebug("Starting SCP1781Manager");

            SCPEnemy scpEnemy = SCPItems.SCPEnemiesList.Where(x => x.enemyType.name == "SCP1781Enemy").FirstOrDefault();
            if (scpEnemy == null) { logger.LogError("Error: Couldnt get SCP-178-1 from enemies list"); return; }
            GameObject enemyPrefab = scpEnemy.enemyType.enemyPrefab;


            int maxCountOutside = GetMaxCount(true);
            int count = 0;
            foreach (var node in RoundManager.Instance.outsideAINodes)
            {
                if (count >= maxCountOutside && maxCountOutside != -1) { break; }
                GameObject spawnableEnemy = Instantiate(enemyPrefab, node.transform.position, Quaternion.identity);
                spawnableEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                RoundManager.Instance.SpawnedEnemies.Add(spawnableEnemy.GetComponent<SCP1781AI>());
                SCP1781Instances.Add(spawnableEnemy);
                spawnableEnemy.GetComponent<SCP1781AI>().SetEnemyOutsideClientRpc(true);
                count++;
            }

            int maxCountInside = GetMaxCount(false);
            count = 0;
            foreach (var node in RoundManager.Instance.insideAINodes)
            {
                if (count >= maxCountInside && maxCountInside != -1) { break; }
                GameObject spawnableEnemy = Instantiate(enemyPrefab, node.transform.position, Quaternion.identity);
                spawnableEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                SCP1781Instances.Add(spawnableEnemy);
                count++;
            }

            logger.LogDebug($"Spawned {SCP1781Instances.Count} SCP-178-1 instances");
        }

        public int GetMaxCount(bool outside)
        {
            if (outside)
            {
                if (config1781UsePercentageBasedCount.Value && config1781MaxPercentCountOutside.Value < 1 && config1781MaxPercentCountOutside.Value > 0)
                {
                    return (int)(RoundManager.Instance.outsideAINodes.Length * config1781MaxPercentCountOutside.Value);
                }
                else
                {
                    return config1781MaxCountOutside.Value;
                }
            }
            else
            {
                if (config1781UsePercentageBasedCount.Value && config1781MaxPercentCountInside.Value < 1 && config1781MaxPercentCountInside.Value > 0)
                {
                    return (int)(RoundManager.Instance.insideAINodes.Length * config1781MaxPercentCountInside.Value);
                }
                else
                {
                    return config1781MaxCountInside.Value;
                }
            }
        }

        public void Update()
        {
            if (SCP178Behavior.Instance != null && SCP178Behavior.Instance.playerWornBy != null)
            {
                despawnTimer = config1781DespawnTime.Value;
            }
            else
            {
                //logger.LogDebug("Despawning in " + despawnTimer);
                despawnTimer -= Time.deltaTime;
                if (despawnTimer <= 0)
                {
                    logger.LogDebug("Despawning all SCP-178-1 instances");
                    Destroy(this);
                }
            }
        }

        public void AddAngerToPlayer(PlayerControllerB player, int anger)
        {
            logger.LogDebug("AddAngerToPlayer: " + player.playerUsername + " " + anger);
            if (!Instance.PlayersAngerLevels.ContainsKey(player))
            {
                Instance.PlayersAngerLevels.Add(player, anger);
            }
            else
            {
                Instance.PlayersAngerLevels[player] += anger;
            }
        }

        public static void EnableAll1781MeshesOnLocalClient(bool enable)
        {
            foreach (var scp in FindObjectsOfType<SCP1781AI>())
            {
                scp.EnableMesh(enable);
            }
        }

        public void OnDestroy()
        {
            try
            {
                logger.LogDebug("in SCP1781Manager OnDestroy()");
                if (SCP1781Instances != null && SCP1781Instances.Count > 0)
                {
                    foreach (var scp in SCP1781Instances)
                    {
                        if (scp != null)
                        {
                            NetworkObjectReference scpRef = scp.GetComponent<NetworkObject>();
                            RoundManager.Instance.DespawnEnemyOnServer(scpRef);
                        }
                    }

                    UnityEngine.Object.Destroy(this.gameObject);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return;
            }
        }
    }
}