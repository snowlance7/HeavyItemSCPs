using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using static HeavyItemSCPs.Utils;

namespace HeavyItemSCPs.Items.SCP513
{
    internal class SCP513_1AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Transform turnCompass;
        public GameObject enemyMesh;
        public GameObject ScanNode;
        public AudioClip[] BellSFX;
        public AudioClip[] AmbientSFX;
        public AudioClip[] StalkSFX;

        public SCP513Behavior SCP513Script;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        bool enemyMeshEnabled;
        public static bool hauntingLocalPlayer;
        bool staring;

        float cooldownMultiplier;
        float timeSinceCommonEvent;
        float timeSinceMediumEvent;
        float timeSinceRareEvent;

        float nextCommonEventTime;
        float nextMediumEventTime;
        float nextRareEventTime;

        // Constants
        const float maxInsanity = 50f;

        // Configs
        float commonEventMinCooldown = 10f;
        float commonEventMaxCooldown = 20f;
        float uncommonEventMinCooldown = 30f;
        float uncommonEventMaxCooldown = 60f;
        float rareEventMinCooldown = 180f;
        float rareEventMaxCooldown = 240f;

        int eventNothingWeight = 100;
        int eventBlockDoorWeight = 50;
        int eventStareWeight = 50;
        int eventJumpscareWeight = 50;
        int eventChaseWeight = 50;
        int eventMimicEnemyWeight = 25;
        int eventMimicPlayerWeight = 25;
        int eventMimicObstaclesWeight = 25;

        public enum State
        {
            Inactive,
            Active,
            Manifesting
        }

        public override void Start()
        {
            logger.LogDebug("SCP-513-1 spawned");
            base.Start();

            currentBehaviourStateIndex = (int)State.Inactive;

            nextCommonEventTime = commonEventMaxCooldown;
            nextMediumEventTime = uncommonEventMaxCooldown;
            nextRareEventTime = rareEventMaxCooldown;
        }

        public override void Update()
        {
            base.Update();

            if (StartOfRound.Instance.allPlayersDead) { return; }

            if (IsServerOrHost && targetPlayer != null && !targetPlayer.isPlayerControlled)
            {
                NetworkObject.Despawn(true);
                return;
            }
            else if (!base.IsOwner)
            {
                if (enemyMeshEnabled)
                {
                    EnableEnemyMesh(false);
                }
                return;
            }
            else if (targetPlayer != null && localPlayer != targetPlayer)
            {
                ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
            }

            if (targetPlayer == null
                || targetPlayer.isPlayerDead
                || targetPlayer.disconnectedMidGame
                || !targetPlayer.isPlayerControlled
                || inSpecialAnimation)
            {
                return;
            }

            turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);

            float newFear = targetPlayer.insanityLevel / maxInsanity;
            targetPlayer.playersManager.fearLevel = Mathf.Max(targetPlayer.playersManager.fearLevel, newFear); // Change fear based on insanity

            cooldownMultiplier = 1f - localPlayer.playersManager.fearLevel;

            timeSinceCommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceMediumEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceRareEvent += Time.deltaTime * cooldownMultiplier;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (!base.IsOwner) { return; }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Inactive:

                    if (targetPlayer == null) { return; }
                    SwitchToBehaviourClientRpc((int)State.Active);

                    break;

                case (int)State.Active:

                    if (enemyMeshEnabled)
                    {
                        EnableEnemyMesh(false);
                    }

                    break;

                case (int)State.Manifesting:

                    if (!enemyMeshEnabled)
                    {
                        EnableEnemyMesh(true);
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public Transform? ChoosePositionInFrontOfPlayer(float minDistance)
        {
            logger.LogDebug("Choosing position in front of player");
            Transform? result = null;
            logger.LogDebug(allAINodes.Count() + " ai nodes");
            foreach (var node in allAINodes)
            {
                if (node == null) { continue; }
                Vector3 nodePos = node.transform.position + Vector3.up * 0.5f;
                Vector3 playerPos = targetPlayer.gameplayCamera.transform.position;
                float distance = Vector3.Distance(playerPos, nodePos);
                if (distance < minDistance) { continue; }
                if (Physics.Linecast(nodePos, playerPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault/*, queryTriggerInteraction: QueryTriggerInteraction.Ignore*/)) { continue; }
                if (!targetPlayer.HasLineOfSightToPosition(nodePos)) { continue; }

                mostOptimalDistance = distance;
                result = node.transform;
            }

            logger.LogDebug($"null: {targetNode == null}");
            return result;
        }

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false)
        {
            logger.LogDebug($"EnableEnemyMesh({enable})");
            enemyMesh.SetActive(enable);
            ScanNode.SetActive(enable);
            enemyMeshEnabled = enable;
        }

        public Vector3 FindPositionOutOfLOS()
        {
            targetNode = ChooseClosestNodeToPosition(targetPlayer.transform.position, true);
            return RoundManager.Instance.GetNavMeshPosition(targetNode.position);
        }

        public void Teleport(Vector3 position)
        {
            logger.LogDebug("Teleporting to " + position.ToString());
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            if (inSpecialAnimation) { return; }
            if (currentBehaviourStateIndex == (int)State.Inactive) { return; }
            if (!other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; }
            if (player == null || player != localPlayer) { return; }


        }

        // Animation Methods



        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void ChangeTargetPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
            NetworkObject.ChangeOwnership(clientId);
            ChangeTargetPlayerClientRpc(clientId);
        }

        [ClientRpc]
        public void ChangeTargetPlayerClientRpc(ulong clientId)
        {
            PlayerControllerB player = PlayerFromId(clientId);
            player.insanityLevel = 0f;
            targetPlayer = player;
            logger.LogDebug($"SCP-513-1: Haunting player with playerClientId: {targetPlayer.playerClientId}; actualClientId: {targetPlayer.actualClientId}");
            ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
            hauntingLocalPlayer = localPlayer == targetPlayer;
        }
    }
}