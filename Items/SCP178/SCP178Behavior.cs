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

        public static SCP178Behavior? Instance { get; private set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        readonly Vector3 posOffsetWearing = new Vector3(-0.275f, -0.15f, -0.05f);
        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -60f, 0f);

        public static PlayerControllerB? lastPlayerHeldBy;

        Coroutine? spawnCoroutine;

        public bool wearing;
        public bool wearingOnLocalClient;
        float spawnTime;
        bool isOutside;

        int batchIndex = 0;
        public static float updateInterval = 0.2f;

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

            DespawnEntities();
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

            if (Instances == null || Instances.Count == 0)
                return;

            // Run one batch per interval
            if (Time.frameCount % Mathf.CeilToInt(updateInterval / Time.deltaTime) == 0)
            {
                //logger.LogDebug("Running batch");
                RunBatch();
            }
        }

        void RunBatch()
        {
            int total = Instances.Count;
            if (total == 0) return;

            // How many to process per batch (spread evenly)
            int batchSize = Mathf.Max(1, total / Mathf.CeilToInt(1f / updateInterval));

            for (int i = 0; i < batchSize; i++)
            {
                var instance = Instances[batchIndex];
                if (instance != null)
                {
                    bool hidden = instance.transform.position == Vector3.zero;
                    instance.EnableMesh(wearingOnLocalClient && !hidden);

                    if (wearingOnLocalClient && instance.renderer.isVisible)
                    {
                        instance.EnableServerRpc();
                    }
                }

                batchIndex++;
                if (batchIndex >= total)
                    batchIndex = 0; // wrap around
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
                wearingOnLocalClient = enable;
                SCP1783DVision.Instance.Enable3DVision(enable);
            }

            
            if (lastPlayerHeldBy.drunkness < 0.2f && enable) { lastPlayerHeldBy.drunkness = 0.2f; }
            SpawnEntities();
        }

        public void SpawnEntities()
        {
            if (!IsServerOrHost) { return; }
            if (spawnCoroutine != null) { return; }
            if (StartOfRound.Instance.inShipPhase) { return; }
            if (SCP1781AI.Instances.Count > 0) { return; }
            logger.LogDebug("Trying to spawn entities");

            IEnumerator SpawnEntitiesCoroutine()
            {
                try
                {
                    yield return null;

                    if (SCP1781AI.Instances.Count == 0)
                    {
                        int maxCount = UnityEngine.Random.Range(config1781MinCount.Value, config1781MaxCount.Value);
                        int count = 0;
                        List<GameObject> nodes = Utils.allAINodes.ToList();
                        nodes.Shuffle();
                        foreach (var node in nodes)
                        {
                            yield return null;
                            if (count >= maxCount) { break; }
                            logger.LogDebug("Count: " + count);
                            GameObject spawnableEnemy = Instantiate(SCP1781Prefab, node.transform.position, Quaternion.identity);
                            SCP1781AI scp = spawnableEnemy.GetComponent<SCP1781AI>();
                            scp.NetworkObject.Spawn(destroyWithScene: true);
                            count++;
                        }

                        logger.LogDebug($"Spawned {SCP1781AI.Instances.Count} SCP-178-1 instances");
                    }
                }
                finally
                {
                    spawnCoroutine = null;
                }
            }

            spawnCoroutine = StartCoroutine(SpawnEntitiesCoroutine());
        }

        public void DespawnEntities()
        {
            if (!IsServerOrHost) { return; }
            if (SCP1781AI.Instances.Count <= 0) { return; }

            List<SCP1781AI> entities = SCP1781AI.Instances.ToList();
            SCP1781AI.Instances = [];

            logger.LogDebug("Despawning SCP-178-1 Instances");
            foreach (var entity in entities)
            {
                //if (entity == null || !entity.NetworkObject.IsSpawned) { continue; }
                entity.NetworkObject.Despawn(true);
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
            if (configEnableSCP178.Value && SCP178Behavior.Instance != null)
            {
                PlayersAngerLevels.Clear();
                SCP178Behavior.Instance.DespawnEntities();
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