using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP513;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs
{
    public class NetworkHandlerHeavyItemSCPs : NetworkBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public GameObject SCP513_1Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public static NetworkHandlerHeavyItemSCPs? Instance { get; private set; }

        public static bool spawningBellMan;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (Instance != null && Instance != this)
                {
                    Instance.gameObject.GetComponent<NetworkObject>().Despawn(true);
                }
            }

            hideFlags = HideFlags.HideAndDontSave;
            Instance = this;

            SCP513_1Prefab = ModAssets!.LoadAsset<GameObject>("Assets/ModAssets/SCP513/SCP513_1.prefab");
            
            logger.LogDebug("NetworkHandlerHeavyItemSCPs spawned");
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Update()
        {
            if (configEnableSCP513.Value)
            {
                DoBellmanStuff();
            }
        }

        void DoBellmanStuff()
        {
            if (SCP513Behavior.localPlayerHaunted)
            {
                if (localPlayer.isPlayerDead || StartOfRound.Instance.firingPlayersCutsceneRunning)
                {
                    logger.LogDebug("This player is no longer haunted");
                    SCP513Behavior.localPlayerHaunted = false;
                    return;
                }

                if (SCP513_1AI.Instance != null) { spawningBellMan = false; return; }
                if (spawningBellMan) { return; }
                if (StartOfRound.Instance.shipIsLeaving || StartOfRound.Instance.inShipPhase) { return; }
                if (!localPlayer.isPlayerControlled) { return; }
                if (Utils.isOnCompanyMoon || Utils.allAINodes.Length <= 0) { return; }
                NetworkHandlerHeavyItemSCPs.Instance?.SpawnBellManOnLocalClient();
            }
        }

        public void SpawnBellManOnLocalClient()
        {
            if (SCP513_1AI.Instance != null) { return; }
            logger.LogDebug("Spawning bellman");
            spawningBellMan = true;
            Instantiate(SCP513_1Prefab, Vector3.zero, Quaternion.identity);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnGhostGirlServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }

            if (FindObjectsOfType<DressGirlAI>().FirstOrDefault() != null) { return; }

            List<SpawnableEnemyWithRarity> enemies = Utils.GetEnemies();
            SpawnableEnemyWithRarity? ghostGirl = enemies.Where(x => x.enemyType.name == "DressGirl").FirstOrDefault();
            if (ghostGirl == null) { logger.LogError("Ghost girl could not be found"); return; }

            RoundManager.Instance.SpawnEnemyGameObject(Vector3.zero, 0, -1, ghostGirl.enemyType);
        }

        [ServerRpc]
        public void MimicEnemyServerRpc(ulong clientId, string enemyName)
        {
            if (!IsServerOrHost) { return; }

            logger.LogDebug("Attempting spawn enemy: " + enemyName);

            EnemyType type = Utils.GetEnemies().Where(x => x.enemyType.name == enemyName).FirstOrDefault().enemyType;
            if (type == null) { logger.LogError("Couldnt find enemy to spawn in MimicEnemyServerRpc"); return; }

            EnemyVent? vent = Utils.GetClosestVentToPosition(localPlayer.transform.position);
            if (vent == null)
            {
                logger.LogError("Couldnt find vent for mimic enemy event.");
                return;
            }

            NetworkObject netObj = RoundManager.Instance.SpawnEnemyGameObject(vent.floorNode.position, 0f, -1, type);
            if (!netObj.TryGetComponent(out EnemyAI enemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }
            enemy.ChangeOwnershipOfEnemy(clientId);
            MimicEnemyClientRpc(clientId, enemy.NetworkObject);
        }

        [ClientRpc]
        public void MimicEnemyClientRpc(ulong clientId, NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get netRef in MimicEnemyClientRpc"); return; }
            if (!netObj.TryGetComponent<EnemyAI>(out EnemyAI enemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }

            foreach (var collider in enemy.transform.root.gameObject.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            if (localPlayer.actualClientId != clientId)
            {
                enemy.EnableEnemyMesh(false, true);
                enemy.creatureSFX.enabled = false;
                enemy.creatureVoice.enabled = false;
                return;
            }

            if (SCP513_1AI.Instance == null) { return; }
            SCP513_1AI.Instance.mimicEnemy = enemy;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ShotgunSuicideServerRpc(NetworkObjectReference netRef, float duration)
        {
            if (!IsServerOrHost) { return; }
            ShotgunSuicideClientRpc(netRef, duration);
        }

        [ClientRpc]
        public void ShotgunSuicideClientRpc(NetworkObjectReference netRef, float duration)
        {
            IEnumerator RotateShotgunCoroutine(ShotgunItem shotgun, float duration)
            {
                PlayerControllerB player = shotgun.playerHeldBy;

                try
                {
                    if (!HallucinationManager.overrideShotgunsRotOffsets.ContainsKey(shotgun)) { HallucinationManager.overrideShotgunsRotOffsets.Add(shotgun, shotgun.itemProperties.rotationOffset); }
                    if (!HallucinationManager.overrideShotgunsPosOffsets.ContainsKey(shotgun)) { HallucinationManager.overrideShotgunsPosOffsets.Add(shotgun, shotgun.itemProperties.positionOffset); }
                    HallucinationManager.overrideShotguns.Add(shotgun);

                    float elapsedTime = 0f;
                    Vector3 startRot = shotgun.itemProperties.rotationOffset;
                    Vector3 endRot = new Vector3(105f, -50f, -50f);
                    Vector3 startPos = shotgun.itemProperties.positionOffset;
                    Vector3 endPos = new Vector3(0f, 0.7f, -0.1f);

                    while (elapsedTime < duration)
                    {
                        float t = elapsedTime / duration;

                        Vector3 _rotOffset = Vector3.Lerp(startRot, endRot, t);
                        Vector3 _posOffset = Vector3.Lerp(startPos, endPos, t);

                        HallucinationManager.overrideShotgunsRotOffsets[shotgun] = _rotOffset;
                        HallucinationManager.overrideShotgunsPosOffsets[shotgun] = _posOffset;

                        elapsedTime += Time.deltaTime;
                        yield return null;
                    }

                    yield return new WaitForSeconds(3f);

                    if (player == localPlayer)
                    {
                        localPlayer.activatingItem = false;
                        Utils.FreezePlayer(localPlayer, false);
                        shotgun.ShootGunAndSync(false);
                        yield return null;
                        localPlayer.DamagePlayer(100, hasDamageSFX: true, callRPC: true, CauseOfDeath.Gunshots, 0, fallDamage: false, shotgun.shotgunRayPoint.forward * 30f);
                    }

                    yield return new WaitForSeconds(1f);
                }
                finally
                {
                    HallucinationManager.overrideShotguns.Remove(shotgun);
                    if (player == localPlayer)
                    {
                        localPlayer.activatingItem = false;
                        Utils.FreezePlayer(localPlayer, false);
                    }
                }
            }

            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Cant get netObj"); return; }
            if (!netObj.TryGetComponent(out ShotgunItem shotgun)) { logger.LogError("Cant get ShotgunItem"); return; }

            StartCoroutine(RotateShotgunCoroutine(shotgun, duration));
        }
    }

    [HarmonyPatch]
    public class NetworkObjectManager
    {
        static GameObject? networkPrefab;
        private static ManualLogSource logger = Plugin.LoggerInstance;

        /*[HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            if (networkPrefab != null)
                return;

            if (ModAssets == null) { logger.LogError("Couldnt get ModAssets to create network handler"); return; }
            networkPrefab = (GameObject)ModAssets.LoadAsset("Assets/ModAssets/NetworkHandlerHeavyItemSCPs.prefab"); // TODO: Set this up in unity editor

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
        }*/

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            logger.LogDebug("Init");

            if (networkPrefab != null)
                return;

            networkPrefab = new GameObject("NetworkHandlerHeavyItemSCPs");
            
            var netObj = networkPrefab.AddComponent<NetworkObject>();
            netObj.AlwaysReplicateAsRoot = false;
            netObj.SynchronizeTransform = false;
            netObj.ActiveSceneSynchronization = false;
            netObj.SceneMigrationSynchronization = true;
            netObj.SpawnWithObservers = true;
            netObj.DontDestroyWithOwner = false;
            netObj.AutoObjectParentSync = false;

            networkPrefab.AddComponent<NetworkHandlerHeavyItemSCPs>();

            UnityEngine.Object.DontDestroyOnLoad(networkPrefab);
            networkPrefab.hideFlags = HideFlags.HideAndDontSave;
            //networkPrefab.SetActive(false);

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            logger.LogDebug("SpawnNetworkHandler");
            if (!IsServerOrHost) { return; }

            GameObject networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab!, Vector3.zero, Quaternion.identity);
            networkHandlerHost!.GetComponent<NetworkObject>().Spawn();
            logger.LogDebug("Spawned NetworkHandlerHeavyItemSCPs");
        }
    }
}