using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using HeavyItemSCPs.Items.SCP513;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Services.Authentication;
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

        public static bool localPlayerHaunted;

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

            if (playerHeldBy == null || localPlayer != playerHeldBy)
            {
                timeHeldByPlayer = 0f;
                return;
            }

            timeSinceLastRing += Time.deltaTime;
            timeHeldByPlayer += Time.deltaTime;

            TrackCameraMovement();

            if (playerHeldBy.isJumping || playerHeldBy.isFallingFromJump || playerHeldBy.isSprinting || playerHeldBy.takingFallDamage)
            {
                if (timeSinceLastRing < ringCooldown) { return; }
                if (playerHeldBy.isCrouching || playerHeldBy.inSpecialInteractAnimation) { return; }
                //logger.LogDebug("Ringing bell from jumping or falling");
                RingBellServerRpc();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogWarning("There is already a SCP-513 in the scene. Removing this one.");
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
                Utils.FreezePlayer(localPlayer, false);
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
                //logger.LogDebug("Ringing bell from fall distance");
                RingBellServerRpc();
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
                if (timeSinceLastRing < ringCooldown) { return; }
                if (playerHeldBy.isClimbingLadder || playerHeldBy.inSpecialInteractAnimation) { return; }
                //logger.LogDebug("Ringing bell from turn speed");
                RingBellServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RingBellServerRpc()
        {
            if (!IsServerOrHost) { return; }

            RingBellClientRpc();
        }

        [ClientRpc]
        public void RingBellClientRpc()
        {
            timeSinceLastRing = 0f;
            RoundManager.PlayRandomClip(ItemAudio, BellSFX);

            if (localPlayerHaunted) { return; }

            if (Vector3.Distance(transform.position, localPlayer.bodyParts[0].transform.position) <= ItemAudio.maxDistance)
            {
                logger.LogDebug("This player is haunted");
                localPlayerHaunted = true;
            }
        }
    }

    [HarmonyPatch]
    internal class SCP513Patches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.ConnectClientToPlayerObject))]
        public static void ConnectClientToPlayerObjectPostfix()
        {
            try
            {
                if (!ES3.KeyExists("LocalPlayerHauntedByBell", GameNetworkManager.Instance.currentSaveFileName)) { return; }
                SCP513Behavior.localPlayerHaunted = ES3.Load<bool>("LocalPlayerHauntedByBell", GameNetworkManager.Instance.currentSaveFileName);
            }
            catch
            {
                return;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.SaveLocalPlayerValues))]
        public static void SaveLocalPlayerValuesPostfix()
        {
            try
            {
                ES3.Save("LocalPlayerHauntedByBell", SCP513Behavior.localPlayerHaunted, GameNetworkManager.Instance.currentSaveFileName);
            }
            catch
            {
                return;
            }
        }
    }
}