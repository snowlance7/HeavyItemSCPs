using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

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
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        bool enemyMeshEnabled;
        public static bool hauntingLocalPlayer;
        float timeSinceStare;
        float timeSinceChaseAttempt;
        bool staring;

        // Constants
        const float maxInsanity = 50f;

        // Configs
        float insanityPhase3 = 0.3f;
        float stareCooldown = 20f;
        float stareBufferTime = 5f;
        float stareTime = 10f;
        float insanityIncreaseOnLook = 10f;
        float somethingChaseSpeed = 10f;
        float chaseCooldown = 5f;
        float insanityPhase1 = 0f;
        float insanityPhase2 = 0.1f;

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

            timeSinceStare += Time.deltaTime;
            timeSinceChaseAttempt += Time.deltaTime;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (!base.IsOwner) { return; }

            if (StartOfRound.Instance.allPlayersDead
                || targetPlayer == null
                || targetPlayer.isPlayerDead
                || targetPlayer.disconnectedMidGame
                || !targetPlayer.isPlayerControlled
                || inSpecialAnimation)
            {
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Inactive:

                    

                    break;

                case (int)State.Active:



                    break;

                case (int)State.Manifesting:



                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        IEnumerator FreezePlayerCoroutine(float freezeTime)
        {
            FreezePlayer(targetPlayer, true);
            yield return new WaitForSeconds(freezeTime);
            FreezePlayer(targetPlayer, false);
        }

        void StarePlayer()
        {
            targetNode = TryFindingHauntPosition();
            if (targetNode == null)
            {
                logger.LogDebug("targetNode is null!");
                timeSinceStare = stareCooldown - stareBufferTime;
                staring = false;
                return;
            }

            Teleport(targetNode.position);
            EnableEnemyMesh(true);
            //RoundManager.PlayRandomClip(creatureVoice, ambientSFX);
            StartCoroutine(StopStareAfterDelay(stareTime));
        }

        IEnumerator StopStareAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (enemyMeshEnabled && currentBehaviourStateIndex != (int)State.Active)
            {
                EnableEnemyMesh(false);
                timeSinceStare = 0f;
                staring = false;
            }
        }

        private Transform? TryFindingHauntPosition(bool mustBeInLOS = true)
        {
            if (targetPlayer.isInsideFactory)
            {
                for (int i = 0; i < allAINodes.Length; i++)
                {
                    if ((!mustBeInLOS || !Physics.Linecast(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) && !targetPlayer.HasLineOfSightToPosition(allAINodes[i].transform.position, 80f, 100, 8f))
                    {
                        Debug.DrawLine(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, Color.green, 2f);
                        Debug.Log($"Player distance to haunt position: {Vector3.Distance(targetPlayer.transform.position, allAINodes[i].transform.position)}");
                        return allAINodes[i].transform;
                    }
                }
            }
            return null;
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

        Vector3 FindPositionOutOfLOS()
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

        GameObject GetRandomAINode(List<GameObject> nodes)
        {
            int randIndex = UnityEngine.Random.Range(0, nodes.Count);
            return nodes[randIndex];
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            if (inSpecialAnimation) { return; }
            if (currentBehaviourStateIndex == (int)State.Inactive) { return; }
            if (!other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; }
            if (player == null || player != localPlayer) { return; }


        }

        // Animations



        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void ChangeTargetPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
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

            if (IsServerOrHost)
            {
                NetworkObject.ChangeOwnership(targetPlayer.actualClientId);
            }
        }
    }
}