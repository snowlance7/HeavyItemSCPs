using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using HeavyItemSCPs.Items.SCP513;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP513
{
    // TODO: Make sure 513-1 follows the player even if they go to another moon
    public class SCP513Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP513Behavior? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemAudio;
        public AudioClip[] BellSFX;
        public GameObject SCP513_1Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        //public NetworkList<ulong> HauntedPlayers = new NetworkList<ulong>();

        const float maxFallDistance = 1f;
        const float ringCooldown = 3f;

        float timeSpawned;
        float timeSinceLastRing;
        float timeHeldByPlayer;
        Vector2 lastCameraAngles;

        public bool localPlayerHaunted;

        // Configs
        float maxTurnSpeed = 1000f;

        public override void Update()
        {
            base.Update();

            timeSpawned += Time.deltaTime;

            if (Instance != this)
            {
                grabbable = false;
                if (IsServerOrHost && timeSpawned > 3f)
                {
                    NetworkObject.Despawn(true);
                }
                return;
            }

            if (!StartOfRound.Instance.inShipPhase && !StartOfRound.Instance.shipIsLeaving && localPlayerHaunted && SCP513_1AI.Instance == null)
            {
                SpawnBellMan();
            }

            if (playerHeldBy == null || localPlayer != playerHeldBy)
            {
                timeHeldByPlayer = 0f;
                return;
            }

            timeSinceLastRing += Time.deltaTime;
            timeHeldByPlayer += Time.deltaTime;

            TrackCameraMovement();

            if (playerHeldBy.isJumping || playerHeldBy.isFallingFromJump || playerHeldBy.isFallingNoJump || playerHeldBy.isSprinting)
            {
                logger.LogDebug("Ringing bell from jumping or falling");
                RingBellServerRpc();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-513 in the scene. Removing this one.");
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-513");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnHitGround()
        {
            base.OnHitGround();
            if (!IsServerOrHost) { return; }
            float fallDistance = startFallingPosition.y - targetFloorPosition.y;
            logger.LogDebug("FallDistance: " + fallDistance);

            if (fallDistance > maxFallDistance)
            {
                logger.LogDebug("Ringing bell from fall distance");
                RingBellServerRpc(true);
            }
        }

        void TrackCameraMovement()
        {
            Vector2 currentAngles = new Vector2(playerHeldBy.gameplayCamera.transform.eulerAngles.x, playerHeldBy.gameplayCamera.transform.eulerAngles.y);

            // Calculate delta, account for angle wrapping (360 to 0)
            float deltaX = Mathf.DeltaAngle(lastCameraAngles.x, currentAngles.x);
            float deltaY = Mathf.DeltaAngle(lastCameraAngles.y, currentAngles.y);

            // Combine both axes into a single turn speed value
            float cameraTurnSpeed = new Vector2(deltaX, deltaY).magnitude / Time.deltaTime;
            lastCameraAngles = currentAngles;

            if (cameraTurnSpeed > maxTurnSpeed && timeHeldByPlayer > 1f)
            {
                logger.LogDebug("Ringing bell from turn speed");
                RingBellServerRpc();
            }
        }

        public void SpawnBellMan()
        {
            if (SCP513_1AI.Instance != null) { return; }
            Instantiate(SCP513_1Prefab, Vector3.zero, Quaternion.identity);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RingBellServerRpc(bool overrideRingCooldown = false)
        {
            if (!IsServerOrHost) { return; }

            if (timeSinceLastRing < ringCooldown && !overrideRingCooldown) { return; }
            timeSinceLastRing = 0f;

            RingBellClientRpc();
        }

        [ClientRpc]
        public void RingBellClientRpc()
        {
            RoundManager.PlayRandomClip(ItemAudio, BellSFX);

            if (Vector3.Distance(transform.position, localPlayer.bodyParts[0].transform.position) <= ItemAudio.maxDistance)
            {
                localPlayerHaunted = true;
            }
        }



        [ServerRpc(RequireOwnership = false)]
        public void SpawnGhostGirlServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }

            foreach (var girl in FindObjectsOfType<DressGirlAI>())
            {
                if (girl.hauntingPlayer == localPlayer)
                {
                    return;
                }
            }

            List<SpawnableEnemyWithRarity> enemies = Utils.GetEnemies();
            SpawnableEnemyWithRarity? ghostGirl = enemies.Where(x => x.enemyType.name == "DressGirl").FirstOrDefault();
            if (ghostGirl == null) { logger.LogError("Ghost girl could not be found"); return; }

            RoundManager.Instance.SpawnEnemyGameObject(Vector3.zero, 0, -1, ghostGirl.enemyType);
        }

        /*[ServerRpc]
        public void MimicJesterServerRpc()
        {
            if (!IsServerOrHost) { return; }

            float maxSpawnTime = 60f;
            float despawnDistance = 5f;

            if (mimicEnemyRoutine != null)
            {
                StopCoroutine(mimicEnemyRoutine);
                mimicEnemyRoutine = null;
            }

            EnemyType type = GetEnemies().Where(x => x.enemyType.name == "Jester").FirstOrDefault().enemyType;
            if (type == null) { logger.LogError("Couldnt find enemy to spawn in MimicEnemyServerRpc"); return; }

            Transform? spawnPosition = ChooseFarthestNodeFromPosition(targetPlayer.transform.position);

            if (spawnPosition == null) { logger.LogError("Couldnt find farthest node to spawn jester"); return; }

            NetworkObject netObj = RoundManager.Instance.SpawnEnemyGameObject(spawnPosition.position, 0f, -1, type);
            if (!netObj.TryGetComponent<EnemyAI>(out mimicEnemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }
            MimicEnemyClientRpc(mimicEnemy.NetworkObject);

            IEnumerator PopJesterCoroutine()
            {
                yield return null;
                yield return new WaitForSeconds(1f);
                mimicEnemy.SwitchToBehaviourServerRpc(1);
                yield return new WaitForSeconds(1f);
                mimicEnemy.SwitchToBehaviourServerRpc(2); // TODO: Ask slayer about this
                mimicEnemy.targetPlayer = targetPlayer;
            }

            IEnumerator MimicJesterCoroutine(float maxSpawnTime, float despawnDistance)
            {
                try
                {
                    float elapsedTime = 0f;

                    while (mimicEnemy != null
                        && mimicEnemy.NetworkObject.IsSpawned
                        && targetPlayer.isPlayerControlled)
                    {
                        yield return null;
                        elapsedTime += Time.deltaTime;
                        float distance = Vector3.Distance(mimicEnemy.transform.position, targetPlayer.transform.position);

                        if (elapsedTime > maxSpawnTime || distance < despawnDistance)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    if (mimicEnemy != null && mimicEnemy.NetworkObject.IsSpawned)
                    {
                        mimicEnemy.NetworkObject.Despawn(true);
                        mimicEnemy = null;
                        mimicEnemyRoutine = null;
                    }
                }
            }

            StartCoroutine(PopJesterCoroutine());
            mimicEnemyRoutine = StartCoroutine(MimicJesterCoroutine(maxSpawnTime, despawnDistance));
        }*/

        [ServerRpc]
        public void MimicEnemyServerRpc(string enemyName) // TODO: just spawn the enemy and send the network reference to the client so they can continue the coroutine do waituntil mimicenemy != null or something
        {
            if (!IsServerOrHost) { return; }

            float maxSpawnTime = 60f;
            float despawnDistance = 3f;

            logger.LogDebug("Attempting spawn enemy: " + enemyName);

            if (mimicEnemyRoutine != null)
            {
                StopCoroutine(mimicEnemyRoutine);
                mimicEnemyRoutine = null;
            }

            EnemyType type = GetEnemies().Where(x => x.enemyType.name == enemyName).FirstOrDefault().enemyType;
            if (type == null) { logger.LogError("Couldnt find enemy to spawn in MimicEnemyServerRpc"); return; }

            EnemyVent? vent = Utils.GetClosestVentToPosition(localPlayer.transform.position);
            if (vent == null)
            {
                logger.LogError("Couldnt find vent for mimic enemy event.");
                return;
            }

            NetworkObject netObj = RoundManager.Instance.SpawnEnemyGameObject(vent.floorNode.position, 0f, -1, type);
            if (!netObj.TryGetComponent<EnemyAI>(out mimicEnemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }
            MimicEnemyClientRpc(mimicEnemy.NetworkObject);

            IEnumerator MimicEnemyCoroutine(float maxSpawnTime, float despawnDistance)
            {
                try
                {
                    float elapsedTime = 0f;

                    while (mimicEnemy != null
                        && mimicEnemy.NetworkObject.IsSpawned
                        && localPlayer.isPlayerControlled)
                    {
                        yield return null;
                        elapsedTime += Time.deltaTime;
                        float distance = Vector3.Distance(mimicEnemy.transform.position, localPlayer.transform.position);

                        if (elapsedTime > maxSpawnTime || distance < despawnDistance)
                        {
                            break;
                        }

                        mimicEnemy.targetPlayer = localPlayer;
                    }
                }
                finally
                {
                    if (mimicEnemy != null && mimicEnemy.NetworkObject.IsSpawned)
                    {
                        switch (mimicEnemy.enemyType.name)
                        {
                            case "Butler":
                                ButlerEnemyAI.murderMusicAudio.Stop();
                                break;
                            default:
                                break;
                        }

                        mimicEnemy.NetworkObject.Despawn(true);
                        mimicEnemy = null;
                        mimicEnemyRoutine = null;
                    }
                }
            }

            mimicEnemyRoutine = StartCoroutine(MimicEnemyCoroutine(maxSpawnTime, despawnDistance));
        }

        [ClientRpc]
        public void MimicEnemyClientRpc(NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get netRef in MimicEnemyClientRpc"); return; }
            if (!netObj.TryGetComponent<EnemyAI>(out EnemyAI enemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }

            foreach (var collider in enemy.transform.root.gameObject.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
            }

            if (Plugin.localPlayer != localPlayer)
            {
                enemy.EnableEnemyMesh(false, true);
                enemy.creatureSFX.enabled = false;
                enemy.creatureVoice.enabled = false;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ResetStealthTimerServerRpc()
        {
            if (!IsServerOrHost) { return; }
            ResetStealthTimerClientRpc();
        }

        [ClientRpc]
        public void ResetStealthTimerClientRpc()
        {
            evadeStealthTimer = 0f;
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

                    localPlayer.activatingItem = false;
                    Utils.FreezePlayer(localPlayer, false);
                    if (Plugin.localPlayer == localPlayer) { shotgun.ShootGunAndSync(false); }
                    yield return null;
                    localPlayer.DamagePlayer(100, hasDamageSFX: true, callRPC: false, CauseOfDeath.Gunshots, 0, fallDamage: false, shotgun.shotgunRayPoint.forward * 30f);

                    yield return new WaitForSeconds(1f);
                }
                finally
                {
                    HallucinationManager.overrideShotguns.Remove(shotgun);
                    localPlayer.activatingItem = false;
                    Utils.FreezePlayer(localPlayer, false);
                }
            }

            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Cant get netObj"); return; }
            if (!netObj.TryGetComponent(out ShotgunItem shotgun)) { logger.LogError("Cant get ShotgunItem"); return; }

            StartCoroutine(RotateShotgunCoroutine(shotgun, duration));
        }
    }

    [HarmonyPatch]
    public class SCP513Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnShipLandedMiscEvents))]
        public static void OnShipLandedMiscEventsPostfix()
        {
            try
            {
                if (!IsServerOrHost) { return; }
                if (SCP513Behavior.Instance == null) { return; }

                foreach (var clientId in SCP513Behavior.Instance.HauntedPlayers)
                {
                    PlayerControllerB player = PlayerFromId(clientId);
                    if (player == null || !player.isPlayerControlled) { continue; }
                    SCP513Behavior.Instance.SpawnBellMan(clientId);
                }
            }
            catch (System.Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }
    }
}