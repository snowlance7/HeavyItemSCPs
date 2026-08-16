using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.SCP.SCP513;
using SnowyLib;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using Dawn;
using System.Collections;

namespace HeavyItemSCPs
{
    internal class NetworkHandler : NetworkBehaviour
    {
        public static NetworkHandler Instance { get; private set; } = null!;

        public static bool spawningBellMan;

        public override void OnNetworkSpawn()
        {
            if (IsServer && Instance != null)
                Instance.gameObject.GetComponent<NetworkObject>().Despawn(destroy: true);
            Instance = this;
            logger.LogDebug("NetworkHandler spawned");
            base.OnNetworkSpawn();
        }

        public void Update()
        {
            if (HeavyItemSCPsContentHandler.Instance.SCP513 != null)
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
                SpawnBellManOnLocalClient();
            }
        }

        public void SpawnBellManOnLocalClient()
        {
            if (SCP513_1AI.Instance != null) { return; }
            logger.LogDebug("Spawning bellman");
            spawningBellMan = true;
            Instantiate(HeavyItemSCPsContentHandler.Instance.SCP513!.SCP513_1Prefab, Vector3.zero, Quaternion.identity);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnGhostGirlServerRpc(ulong clientId)
        {
            if (!IsServer) { return; }

            if (FindObjectsOfType<DressGirlAI>().FirstOrDefault() != null) { return; }

            Utils.SpawnEnemy(EnemyKeys.Girl, Vector3.zero);
        }

        [ServerRpc]
        public void MimicEnemyServerRpc(ulong clientId, string enemyName)
        {
            if (!IsServer) { return; }

            PlayerControllerB? player = PlayerFromId(clientId);
            if (player == null) { logger.LogError($"MimicEnemyServerRpc: Couldnt find player with id: {clientId}"); return; }

            logger.LogDebug("Attempting spawn enemy: " + enemyName);

            try
            {
                var enemyToSpawn = LethalContent.Enemies.Where(x => x.Value.EnemyType.name == enemyName).FirstOrDefault();

                EnemyVent? vent = RoundManager.Instance.allEnemyVents.GetClosestToPosition(player.transform.position, (x) => x.transform.position);
                
                if (vent == null)
                {
                    logger.LogError("Couldnt find vent for mimic enemy event.");
                    return;
                }

                EnemyAI? enemy = Utils.SpawnEnemy(enemyToSpawn.Key, vent.floorNode.position);
                if (enemy == null) { logger.LogError($"MimicEnemyServerRpc: Failed to spawn enemy {enemyToSpawn}"); return; }
                enemy.ChangeOwnershipOfEnemy(clientId);
                MimicEnemyClientRpc(clientId, enemy.NetworkObject);
            }
            catch (System.Exception e)
            {
                logger.LogError("MimicEnemyServerRpc: " + e);
                return;
            }
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
            if (!IsServer) { return; }
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
                        localPlayer.FreezePlayer(false);
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
                        localPlayer.FreezePlayer(false);
                    }
                }
            }

            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Cant get netObj"); return; }
            if (!netObj.TryGetComponent(out ShotgunItem shotgun)) { logger.LogError("Cant get ShotgunItem"); return; }

            StartCoroutine(RotateShotgunCoroutine(shotgun, duration));
        }
    }

    [HarmonyPatch]
    public class NetworkHandlerPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void StartOfRound_Awake_PostFix(StartOfRound __instance)
        {
            if (!__instance.IsServer) { return; }
            var networkHandlerHost = UnityEngine.Object.Instantiate(HeavyItemSCPsContentHandler.Instance.HeavyItemSCPsAssets?.NetworkHandlerPrefab, Vector3.zero, Quaternion.identity);
            networkHandlerHost?.GetComponent<NetworkObject>().Spawn();
        }
    }
}