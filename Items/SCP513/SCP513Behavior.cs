using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using HeavyItemSCPs.Items.SCP513;
using HeavyItemSCPs.Patches;

namespace HeavyItemSCPs.Items.SCP513
{
    // TODO: Make sure 513-1 follows the player even if they go to another moon
    internal class SCP513Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP513Behavior? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemAudio;
        public AudioClip[] BellSFX;
        public GameObject SCP513_1Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public List<SCP513_1AI> BellManInstances = [];
        public NetworkList<ulong> HauntedPlayers = new NetworkList<ulong>();

        const float maxFallDistance = 1f;
        const float ringCooldown = 3f;

        float timeSpawned;
        float timeSinceLastRing;
        float timeHeldByPlayer;
        Vector2 lastCameraAngles;

        // Configs
        float maxTurnSpeed = 1000f;

        public override void Update()
        {
            base.Update();

            timeSpawned += Time.deltaTime;

            if (IsServerOrHost && Instance != this && timeSpawned > 3f)
            {
                NetworkObject.Despawn(true);
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
                RingBell();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-513 in the scene. Removing this one.");
                if (!IsServerOrHost) { return; }
                //NetworkObject.Despawn(true);
                
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

                if (!IsServerOrHost) { return; }

                foreach (var bellMan in BellManInstances)
                {
                    if (bellMan == null || !bellMan.NetworkObject.IsSpawned) { continue; }
                    bellMan.NetworkObject.Despawn(true);
                }
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
                RingBell(true);
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
                RingBell();
            }
        }

        void RingBell(bool overrideRingCooldown = false)
        {
            if (timeSinceLastRing < ringCooldown && !overrideRingCooldown) { return; }
            timeSinceLastRing = 0f;

            RingBellServerRpc();
        }

        public void SpawnBellMan(ulong targetPlayerClientId)
        {
            if (!IsServerOrHost) { return; }
            GameObject bellManObj = Instantiate(SCP513_1Prefab, Vector3.zero, Quaternion.identity);
            SCP513_1AI bellMan = bellManObj.GetComponent<SCP513_1AI>();
            bellMan.NetworkObject.Spawn(true);
            RoundManager.Instance.SpawnedEnemies.Add(bellMan);
            BellManInstances.Add(bellMan);
            bellMan.ChangeTargetPlayerClientRpc(targetPlayerClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RingBellServerRpc()
        {
            if (!IsServerOrHost) { return; }

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled) { continue; }
                if (HauntedPlayers.Contains(player.actualClientId)) { continue; }
                if (Vector3.Distance(transform.position, player.bodyParts[0].transform.position) <= ItemAudio.maxDistance)
                {
                    HauntedPlayers.Add(player.actualClientId);

                    if (!TESTING.testing && (StartOfRound.Instance.inShipPhase || StartOfRound.Instance.shipIsLeaving)) { continue; }
                    SpawnBellMan(player.actualClientId);
                }
            }

            RingBellClientRpc();
        }

        [ClientRpc]
        public void RingBellClientRpc()
        {
            RoundManager.PlayRandomClip(ItemAudio, BellSFX);
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