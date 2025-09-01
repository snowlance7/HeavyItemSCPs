using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using static HeavyItemSCPs.Items.SCP178.SCP1781AI;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP178Behavior : PhysicsProp // TODO: Tooltip errors and game crashes when despawning 278-1s
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public GameObject SCP1781Prefab;

        public static SCP178Behavior Instance { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public Dictionary<PlayerControllerB, int> PlayersAngerLevels = new Dictionary<PlayerControllerB, int>();

        readonly Vector3 posOffsetWearing = new Vector3(-0.275f, -0.15f, -0.05f);
        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -60f, 0f);

        public PlayerControllerB? lastPlayerHeldBy;

        Coroutine? spawnCoroutine;

        public bool wearing;
        public bool wearingOnLocalClient;
        float spawnTime;
        bool isOutside;

        public float AIIntervalTime = 0.5f;
        private float updateDestinationInterval;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-178 in the scene. Removing this one.");
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
            if (IsServerOrHost)
            {
                DespawnEntities();
            }
        }

        public override void Update()
        {
            base.Update();

            spawnTime += Time.deltaTime;
            
            if (Instance != this)
            {
                if (IsServerOrHost && spawnTime > 3f)
                {
                    NetworkObject.Despawn(true);
                }
                return;
            }

            if (playerHeldBy != null)
            {
                lastPlayerHeldBy = playerHeldBy;
            }

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoAIInterval();
                updateDestinationInterval = AIIntervalTime;
            }
        }

        public void DoAIInterval()
        {
            if (Instances.Count <= 0 || spawnCoroutine != null) { return; }

            foreach (var instance in Instances)
            {
                instance.isBeingObserved = instance.PlayerHasLineOfSightToMe(lastPlayerHeldBy);

                if ((wearingOnLocalClient || PlayersAngerLevels[localPlayer] >= maxAnger) && instance.renderer.isVisible) // TODO: Use chatgpts method for checking if player is in cameras view idk

                // Host stuff
                if (!IsServerOrHost) { continue; }

            }
        }

        public override void LateUpdate()
        {
            Vector3 rotOffset = wearing ? rotOffsetWearing : itemProperties.rotationOffset;
            Vector3 posOffset = wearing ? posOffsetWearing : itemProperties.positionOffset;

            if (parentObject != null)
            {
                base.transform.rotation = parentObject.rotation;
                base.transform.Rotate(rotOffset);
                base.transform.position = parentObject.position;
                Vector3 positionOffset = posOffset;
                positionOffset = parentObject.rotation * positionOffset;
                base.transform.position += positionOffset;
            }
            if (radarIcon != null)
            {
                radarIcon.position = base.transform.position;
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (!buttonDown) { return; }

            wearing = !wearing;
            EnableGlasses(wearing);
        }

        public override void DiscardItem()
        {
            base.DiscardItem();
            EnableGlasses(false);
        }

        void EnableGlasses(bool enable)
        {
            wearing = enable;
            lastPlayerHeldBy!.playerBodyAnimator.SetBool("HoldMask", wearing);
            lastPlayerHeldBy.activatingItem = wearing;

            if (lastPlayerHeldBy == localPlayer)
            {
                wearingOnLocalClient = wearing;
                SCP1783DVision.Instance.Enable3DVision(wearing);
            }

            
            if (lastPlayerHeldBy.drunkness < 0.2f && enable) { lastPlayerHeldBy.drunkness = 0.2f; }
            SpawnEntities(!lastPlayerHeldBy.isInsideFactory);
        }

        void SetOutside(bool outside)
        {
            if (!IsServerOrHost) { return; }
            if (spawnCoroutine != null) { return; }
            DespawnEntities();
            if (SCP1781Instances.Count <= 0)
            {
                SpawnEntities(outside);
                isOutside = outside;
            }
        }

        void SpawnEntities(bool outside)
        {
            if (!IsServerOrHost) { return; }
            if (spawnCoroutine != null) { return; }
            if (StartOfRound.Instance.inShipPhase || !StartOfRound.Instance.shipHasLanded || StartOfRound.Instance.shipIsLeaving) { return; }
            if (SCP1781Instances.Count > 0) { return; }

            IEnumerator SpawnEntitiesCoroutine(bool outside)
            {
                try
                {
                    yield return null;
                    logger.LogDebug("Spawning SCP-178-1 Instances");

                    int maxCount = GetMaxCount(outside);
                    int count = 0;
                    List<GameObject> nodes = outside ? Utils.outsideAINodes.ToList() : Utils.insideAINodes.ToList();
                    nodes.Shuffle();
                    foreach (var node in nodes)
                    {
                        yield return null;
                        if (count >= maxCount && maxCount != -1) { break; }
                        GameObject spawnableEnemy = Instantiate(SCP1781Prefab, node.transform.position, Quaternion.identity);
                        SCP1781AI scp = spawnableEnemy.GetComponent<SCP1781AI>();
                        scp.NetworkObject.Spawn(destroyWithScene: true);
                        scp.SetEnemyOutside(outside);
                        SCP1781Instances.Add(scp);
                        count++;
                    }

                    logger.LogDebug($"Spawned {SCP1781Instances.Count} SCP-178-1 instances");
                }
                finally
                {
                    spawnCoroutine = null;
                }
            }

            spawnCoroutine = StartCoroutine(SpawnEntitiesCoroutine(outside));
        }

        void DespawnEntities()
        {
            if (!IsServerOrHost) { return; }
            if (SCP1781Instances.Count <= 0) { return; }
            if (spawnCoroutine != null) { return; }
            logger.LogDebug("Despawning entities");

            IEnumerator DespawnEntitiesCoroutine()
            {
                try
                {
                    yield return null;
                    logger.LogDebug("Despawning SCP-178-1 Instances");
                    foreach (var entity in SCP1781Instances.ToList())
                    {
                        yield return null;
                        if (entity == null || !entity.NetworkObject.IsSpawned) { continue; }
                        //RoundManager.Instance.DespawnEnemyOnServer(entity.NetworkObject);
                        entity.NetworkObject.Despawn(true);
                        SCP1781Instances.Remove(entity);
                    }
                }
                finally
                {
                    spawnCoroutine = null;
                }
            }

            spawnCoroutine = StartCoroutine(DespawnEntitiesCoroutine());
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

            logger.LogDebug($"Anger for {player.playerUsername}: {PlayersAngerLevels[player]}");
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

    public static class ListExtensions
    {
        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;

            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                T temp = list[k];
                list[k] = list[n];
                list[n] = temp;
            }
        }
    }
}