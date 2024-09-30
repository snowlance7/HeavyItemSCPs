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

        /*private static SCP1781Manager _instance = null!;

        public static SCP1781Manager Instance
        {
            get
            {
                // If the instance doesn't exist, try to find it in the scene
                if (_instance == null)
                {
                    _instance = FindObjectOfType<SCP1781Manager>();

                    // If it's still null, create a new GameObject and add the component
                    if (_instance == null)
                    {
                        GameObject singletonObject = new GameObject("SCP1781Manager");
                        _instance = singletonObject.AddComponent<SCP1781Manager>();
                    }
                }
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }*/
        public List<GameObject> SCP1781Instances = new List<GameObject>();

        public static List<PlayerControllerB> PlayersWearing178 = new List<PlayerControllerB>();
        /*public static List<PlayerControllerB> PlayersWearing178
        {
            get
            {
                List<PlayerControllerB> players = new List<PlayerControllerB>();
                foreach (var scp178 in FindObjectsOfType<GrabbableObject>().OfType<SCP178Behavior>())
                {
                    if (scp178.playerWornBy != null)
                    {
                        players.Add(scp178.playerWornBy);
                    }
                }
                return players;
            }
        }*/

        public Dictionary<PlayerControllerB, int> PlayersAngerLevels = new Dictionary<PlayerControllerB, int>();

        private float timeSinceAIInterval = 0f;
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

            int count = 0;
            foreach (var node in RoundManager.Instance.outsideAINodes)
            {
                if (count >= config1781MaxCount.Value && config1781MaxCount.Value != -1) { break; }
                GameObject spawnableEnemy = Instantiate(enemyPrefab, node.transform.position, Quaternion.identity);
                spawnableEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                SCP1781Instances.Add(spawnableEnemy);
                count++;
            }

            count = 0;
            foreach (var node in RoundManager.Instance.insideAINodes)
            {
                if (count >= config1781MaxCount.Value && config1781MaxCount.Value != -1) { break; }
                GameObject spawnableEnemy = Instantiate(enemyPrefab, node.transform.position, Quaternion.identity);
                spawnableEnemy.GetComponentInChildren<NetworkObject>().Spawn(destroyWithScene: true);
                SCP1781Instances.Add(spawnableEnemy);
                count++;
            }

            RoundManager.Instance.RefreshEnemiesList();
            logger.LogDebug($"Spawned {SCP1781Instances.Count} SCP-178-1 instances");
            NetworkHandlerHeavy.Instance.Spawned1781Instances.Value = true;
        }

        public void Update()
        {
            timeSinceAIInterval += Time.deltaTime;
            if (timeSinceAIInterval >= 0.2f)
            {
                DoAIInterval();
                timeSinceAIInterval = 0f;
            }
        }

        public void DoAIInterval()
        {
            if (PlayersWearing178.Count != 0)
            {
                despawnTimer = config1781DespawnTime.Value;
            }
            else
            {
                despawnTimer -= Time.deltaTime;
                if (despawnTimer <= 0)
                {
                    Destroy(gameObject);
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
            if (SCP1781Instances != null && SCP1781Instances.Count > 0)
            {
                foreach (var scp in SCP1781Instances)
                {
                    Destroy(scp.gameObject);
                    scp.GetComponent<NetworkObject>().Despawn();
                }
            }

            NetworkHandlerHeavy.Instance.Spawned1781Instances.Value = false;

            /*if (Instance == this)
            {
                Instance = null;
            }*/
        }
    }
}