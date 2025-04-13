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
        public AudioClip[] StepChaseSFX;

        public AudioClip[] MinorSoundEffectSFX;
        public AudioClip[] MajorSoundEffectSFX;

        public SCP513Behavior SCP513Script;
        public HallucinationManager? hallucManager;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        EnemyAI? mimicEnemy;
        //Coroutine? mimicEnemyCoroutine;

        bool enemyMeshEnabled;
        public bool facePlayer;

        float cooldownMultiplier;
        float timeSinceCommonEvent;
        float timeSinceUncommonEvent;
        float timeSinceRareEvent;

        float nextCommonEventTime;
        float nextUncommonEventTime;
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

        private int currentFootstepSurfaceIndex;
        private int previousFootstepClip;

        public enum State
        {
            Inactive,
            Active,
            Manifesting,
            Chasing,
            Stalking
        }

        public override void Start()
        {
            logger.LogDebug("SCP-513-1 spawned");
            base.Start();

            currentBehaviourStateIndex = (int)State.Inactive;

            nextCommonEventTime = commonEventMaxCooldown;
            nextUncommonEventTime = uncommonEventMaxCooldown;
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

            if (facePlayer)
            {
                turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);
            }

            float newFear = targetPlayer.insanityLevel / maxInsanity;
            targetPlayer.playersManager.fearLevel = Mathf.Max(targetPlayer.playersManager.fearLevel, newFear); // Change fear based on insanity

            cooldownMultiplier = 1f - localPlayer.playersManager.fearLevel;

            if (currentBehaviourStateIndex != (int)State.Active) { return; }
            timeSinceCommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceUncommonEvent += Time.deltaTime * cooldownMultiplier;
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
                    facePlayer = false;

                    if (enemyMeshEnabled)
                    {
                        EnableEnemyMesh(false);
                    }

                    if (timeSinceCommonEvent > nextCommonEventTime)
                    {
                        logger.LogDebug("Running Common Event");
                        timeSinceCommonEvent = 0f;
                        nextCommonEventTime = UnityEngine.Random.Range(commonEventMinCooldown, commonEventMaxCooldown);
                        hallucManager!.RunRandomEvent(0);
                        return;
                    }
                    if (timeSinceUncommonEvent > nextUncommonEventTime)
                    {
                        logger.LogDebug("Running Uncommon Event");
                        timeSinceUncommonEvent = 0f;
                        nextUncommonEventTime = UnityEngine.Random.Range(uncommonEventMinCooldown, uncommonEventMaxCooldown);
                        hallucManager!.RunRandomEvent(1);
                        return;
                    }
                    if (timeSinceRareEvent > nextRareEventTime)
                    {
                        logger.LogDebug("Running Rare Event");
                        timeSinceRareEvent = 0f;
                        nextRareEventTime = UnityEngine.Random.Range(rareEventMinCooldown, rareEventMaxCooldown);
                        hallucManager!.RunRandomEvent(2);
                        return;
                    }

                    break;

                case (int)State.Manifesting:
                    if (!enemyMeshEnabled)
                    {
                        EnableEnemyMesh(true);
                    }

                    break;

                case (int)State.Chasing:
                    creatureSFX.volume = 1f;

                    if (!enemyMeshEnabled)
                    {
                        EnableEnemyMesh(true);
                    }

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Stalking:
                    creatureSFX.volume = 0.5f;

                    if (!enemyMeshEnabled)
                    {
                        EnableEnemyMesh(true);
                    }

                    Vector3 pos = ChooseClosestNodeToPosition(targetPlayer.transform.position, true).position;
                    SetDestinationToPosition(pos);

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (SCP513Script != null)
            {
                SCP513Script.HauntedPlayers.Remove(targetPlayer.actualClientId);
                SCP513Script.BellManInstances.Remove(this);
            }
            if (hallucManager != null)
                UnityEngine.GameObject.Destroy(hallucManager);
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

        void GetCurrentMaterialStandingOn()
        {
            Ray interactRay = new Ray(transform.position + Vector3.up, -Vector3.up);
            if (!Physics.Raycast(interactRay, out RaycastHit hit, 6f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore) || hit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].surfaceTag))
            {
                return;
            }
            for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
            {
                if (hit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[i].surfaceTag))
                {
                    currentFootstepSurfaceIndex = i;
                    break;
                }
            }
        }

        // Animation Methods

        void PlayFootstepSFX()
        {
            int index;

            if (currentBehaviourStateIndex == (int)State.Chasing)
            {
                creatureSFX.pitch = Random.Range(0.93f, 1.07f);
                index = UnityEngine.Random.Range(0, StepChaseSFX.Length);
                creatureSFX.PlayOneShot(StepChaseSFX[index]);
                return;
            }

            GetCurrentMaterialStandingOn();
            index = Random.Range(0, StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length);
            if (index == previousFootstepClip)
            {
                index = (index + 1) % StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length;
            }
            creatureSFX.pitch = Random.Range(0.93f, 1.07f);
            creatureSFX.PlayOneShot(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips[index], 0.6f);
            previousFootstepClip = index;
        }

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
            timeSinceCommonEvent = 0f;
            timeSinceUncommonEvent = 0f;
            timeSinceRareEvent = 0f;

            if (localPlayer == targetPlayer)
            {
                hallucManager = new HallucinationManager(this, targetPlayer);
            }
        }

        [ServerRpc]
        public void MimicEnemyServerRpc(string enemyName, bool chasing)
        {
            if (!IsServerOrHost) { return; }

            float maxDistanceFromPlayer = 25f;
            float despawnDistance = 3f;

            if (mimicEnemy != null)
            {
                mimicEnemy.NetworkObject.Despawn(true);
            }

            EnemyType type = GetEnemies().Where(x => x.enemyType.name == enemyName).FirstOrDefault().enemyType;
            if (type == null) { logger.LogError("Couldnt find enemy to spawn in MimicEnemyServerRpc"); return; }

            EnemyVent vent = Utils.GetClosestVentToPosition(targetPlayer.transform.position);
            NetworkObjectReference netRef = RoundManager.Instance.SpawnEnemyGameObject(vent.floorNode.position, 0f, -1, type);
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Couldnt get netRef in MimicEnemyClientRpc"); return; }
            if (!netObj.TryGetComponent<EnemyAI>(out mimicEnemy)) { logger.LogError("Couldnt get netObj in MimicEnemyClientRpc"); return; }
            mimicEnemy.NetworkObject.Spawn(true);
            MimicEnemyClientRpc(mimicEnemy.NetworkObject);

            IEnumerator MimicEnemyCoroutine()
            {
                try
                {
                    switch (enemyName)
                    {
                        default:

                            while (mimicEnemy != null
                                && mimicEnemy.NetworkObject.IsSpawned
                                && targetPlayer.isPlayerControlled)
                            {
                                yield return new WaitForSeconds(0.2f);
                                float distance = Vector3.Distance(mimicEnemy.transform.position, targetPlayer.transform.position);

                                if (distance > maxDistanceFromPlayer || distance < despawnDistance)
                                {
                                    break;
                                }

                                if (chasing)
                                {
                                    mimicEnemy.targetPlayer = targetPlayer;
                                }
                            }

                            break;
                    }
                }
                finally
                {
                    if (mimicEnemy != null && mimicEnemy.NetworkObject.IsSpawned)
                    {
                        mimicEnemy.NetworkObject.Despawn(true);
                    }
                }
            }

            StartCoroutine(MimicEnemyCoroutine());
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

            if (localPlayer != targetPlayer)
            {
                enemy.EnableEnemyMesh(false, true);
                enemy.creatureSFX.enabled = false;
                enemy.creatureVoice.enabled = false;
            }
        }
    }
}