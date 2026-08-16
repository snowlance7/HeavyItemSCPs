using GameNetcodeStuff;
using HarmonyLib;
using PSCPLibrary;
using PSCPLibrary.Interfaces;
using SnowyLib;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.SCP.SCP513
{
    // TODO: Make sure 513-1 follows the player even if they go to another moon
    public class SCP513Behavior : PhysicsProp, ISingletonItem, ISCP
    {
        public static SCP513Behavior? Instance { get; private set; }

        SCPInfo ISCP.SCPInfo => info;

        public SCPInfo info = null!;
        public AudioSource audioSource = null!;
        public AudioClip[] bellSFX = null!;
        public GameObject SCP513_1Prefab = null!;

        //public NetworkList<ulong> HauntedPlayers = new NetworkList<ulong>();

        const float maxFallDistance = 1f;
        const float ringCooldown = 3f;

        float timeSinceLastRing;
        float timeHeldByPlayer;
        Vector2 lastCameraAngles;

        public static bool localPlayerHaunted;

        const float maxTurnSpeed = 1000f;
        public static bool unhauntedOnBellDespawn { get; private set; } = false;

        [InitConfig]
        public static void InitConfigs()
        {
            unhauntedOnBellDespawn = PluginInstance.Config.Bind("SCP-513 Options", "SCP-513 | Unhaunted on bell despawn", false, "When true, if the bell is despawned or sold to the company, all haunted players will become unhaunted.").Value;
        }

        public override void Update()
        {
            base.Update();

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
            if (Instance != null && Instance != this) { return; }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
                localPlayer.FreezePlayer(false);
                
                if (unhauntedOnBellDespawn)
                {
                    localPlayerHaunted = false;
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
            RoundManager.PlayRandomClip(audioSource, bellSFX);

            if (localPlayerHaunted) { return; }

            if (Vector3.Distance(transform.position, localPlayer.bodyParts[0].transform.position) <= audioSource.maxDistance)
            {
                logger.LogDebug("This player is haunted");
                localPlayerHaunted = true;
            }
        }
    }

    [HarmonyPatch]
    internal class SCP513Patches
    {
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