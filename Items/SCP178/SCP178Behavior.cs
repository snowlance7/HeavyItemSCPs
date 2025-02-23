using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP178Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public GameObject SCP1781Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static SCP178Behavior? Instance { get; private set; }

        public List<SCP1781AI> SCP1781Instances = [];
        public Dictionary<PlayerControllerB, int> PlayersAngerLevels = new Dictionary<PlayerControllerB, int>();

        readonly Vector3 posOffsetWearing = new Vector3(-0.275f, -0.15f, -0.05f);
        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -60f, 0f);

        public bool wearing;
        float despawnTimer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-178 in the scene. Removing this one.");
                if (!IsServerOrHost) { return; }
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-178");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void Update()
        {
            base.Update();

            if (wearing)
            {
                despawnTimer = config1781DespawnTime.Value;
            }
            else
            {
                despawnTimer -= Time.deltaTime;
                if (despawnTimer <= 0)
                {
                    DespawnEntities();
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            wearing = buttonDown;
            playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
            playerHeldBy.activatingItem = buttonDown;

            if (playerHeldBy == localPlayer)
            {
                SCP1783DVision.Instance.Enable3DVision(buttonDown);
            }

            if (buttonDown)
            {
                if (playerHeldBy.drunkness < 0.2f) { playerHeldBy.drunkness = 0.2f; }
                SpawnEntities();
            }
        }

        void SpawnEntities()
        {
            if (!IsServerOrHost) { return; }
            if (StartOfRound.Instance.inShipPhase || !StartOfRound.Instance.shipHasLanded || StartOfRound.Instance.shipIsLeaving) { return; }
            if (SCP1781Instances.Count > 0) { return; }

            StartCoroutine(SpawnEntitiesCoroutine());
        }

        IEnumerator SpawnEntitiesCoroutine()
        {
            yield return null;
            logger.LogDebug("Spawning SCP-178-1 Instances");

            int maxCountOutside = GetMaxCount(true);
            int count = 0;
            foreach (var node in RoundManager.Instance.outsideAINodes)
            {
                yield return null;
                if (count >= maxCountOutside && maxCountOutside != -1) { break; }
                GameObject spawnableEnemy = Instantiate(SCP1781Prefab, node.transform.position, Quaternion.identity);
                SCP1781AI scp = spawnableEnemy.GetComponent<SCP1781AI>();
                scp.NetworkObject.Spawn(destroyWithScene: true);
                RoundManager.Instance.SpawnedEnemies.Add(scp);
                SCP1781Instances.Add(scp);
                count++;
            }

            int maxCountInside = GetMaxCount(false);
            count = 0;
            foreach (var node in RoundManager.Instance.insideAINodes)
            {
                yield return null;
                if (count >= maxCountInside && maxCountInside != -1) { break; }
                GameObject spawnableEnemy = Instantiate(SCP1781Prefab, node.transform.position, Quaternion.identity);
                SCP1781AI scp = spawnableEnemy.GetComponent<SCP1781AI>();
                scp.NetworkObject.Spawn(destroyWithScene: true);
                RoundManager.Instance.SpawnedEnemies.Add(scp);
                SCP1781Instances.Add(scp);
                count++;
            }

            logger.LogDebug($"Spawned {SCP1781Instances.Count} SCP-178-1 instances");
        }

        void DespawnEntities()
        {
            if (!IsServerOrHost) { return; }
            if (SCP1781Instances.Count <= 0) { return; }

            StartCoroutine(DespawnEntitiesCoroutine());
        }

        IEnumerator DespawnEntitiesCoroutine()
        {
            yield return null;
            logger.LogDebug("Despawning SCP-178-1 Instances");
            foreach (var entity in SCP1781Instances.ToList())
            {
                yield return null;
                if (entity == null || !entity.NetworkObject.IsSpawned) { continue; }
                RoundManager.Instance.DespawnEnemyOnServer(entity.NetworkObject);
            }
        }

        public void AddAngerToPlayer(PlayerControllerB player, int anger)
        {
            logger.LogDebug("AddAngerToPlayer: " + player.playerUsername + " " + anger);
            if (!PlayersAngerLevels.ContainsKey(player))
            {
                PlayersAngerLevels.Add(player, anger);
            }
            else
            {
                PlayersAngerLevels[player] += anger;
            }
        }

        public static int GetMaxCount(bool outside)
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
    }

    [HarmonyPatch]
    public class SCP178Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.ConnectClientToPlayerObject))]
        public static void ConnectClientToPlayerObjectPostfix()
        {
            if (configEnableSCP178.Value)
            {
                SCP1783DVision.Instance.Init();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.DespawnPropsAtEndOfRound))]
        public static void DespawnPropsAtEndOfRoundPostfix()
        {
            if (SCP178Behavior.Instance != null)
            {
                SCP178Behavior.Instance.PlayersAngerLevels.Clear();
            }
        }
    }
}