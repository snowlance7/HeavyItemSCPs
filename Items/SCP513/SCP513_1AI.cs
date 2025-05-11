using BepInEx.Logging;
using GameNetcodeStuff;
using HeavyItemSCPs.Patches;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TextCore.Text;
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
        public AudioClip[] ScareSFX;
        public AudioClip[] StepChaseSFX;

        public AudioClip[] MinorSoundEffectSFX;
        public AudioClip[] MajorSoundEffectSFX;

        public GameObject SoundObjectPrefab;

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

        int currentFootstepSurfaceIndex;
        int previousFootstepClip;

        float stareTime;

        // Constants
        int hashRunSpeed;
        const float maxInsanity = 50f;
        const float maxInsanityThreshold = 35;
        const float minCooldown = 0.5f;
        const float maxCooldown = 1f;

        // Configs
        float commonEventMinCooldown = 5f;
        float commonEventMaxCooldown = 15f;
        float uncommonEventMinCooldown = 15f;
        float uncommonEventMaxCooldown = 30f;
        float rareEventMinCooldown = 100f;
        float rareEventMaxCooldown = 200f;

        public enum State
        {
            InActive,
            Manifesting,
            Chasing,
            Stalking
        }

        public override void Start()
        {
            logger.LogDebug("SCP-513-1 spawned");
            base.Start();

            hashRunSpeed = Animator.StringToHash("speed");
            currentBehaviourStateIndex = (int)State.InActive;

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

            //float newFear = targetPlayer.insanityLevel / maxInsanity;
            //targetPlayer.playersManager.fearLevel = Mathf.Max(targetPlayer.playersManager.fearLevel, newFear); // Change fear based on insanity

            //cooldownMultiplier = 1f - localPlayer.playersManager.fearLevel;

            float t = Mathf.Clamp01(targetPlayer.insanityLevel / maxInsanityThreshold);
            cooldownMultiplier = Mathf.Lerp(minCooldown, maxCooldown, t);

            timeSinceCommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceUncommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceRareEvent += Time.deltaTime * cooldownMultiplier;
        }

        public void LateUpdate()
        {
            creatureAnimator.SetFloat(hashRunSpeed, agent.velocity.magnitude / 2);

            if (facePlayer)
            {
                turnCompass.LookAt(localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 30f * Time.deltaTime);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (!base.IsOwner) { return; }
            if (targetPlayer == null) { return; }

            EnableEnemyMesh(currentBehaviourStateIndex != (int)State.InActive);
            SetEnemyOutside(!targetPlayer.isInsideFactory);

            switch (currentBehaviourStateIndex)
            {
                case (int)State.InActive:
                    agent.speed = 0f;
                    facePlayer = false;
                    creatureSFX.volume = 1f;

                    break;

                case (int)State.Manifesting:
                    agent.speed = 0f;
                    creatureSFX.volume = 1f;

                    break;

                case (int)State.Chasing:
                    creatureSFX.volume = 1f;
                    
                    if (!SetDestinationToPosition(targetPlayer.transform.position, true))
                    {
                        SwitchToBehaviourClientRpc((int)State.InActive);
                        return;
                    }

                    break;

                case (int)State.Stalking: // TODO: Figure out how to get this to work like the Flowerman/Braken or have him teleport after staring for too long
                    creatureSFX.volume = 0.5f;

                    if (targetPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * HallucinationManager.LOSOffset, 80f, 2000))
                    {
                        agent.speed = 15f;
                        creatureAnimator.SetBool("armsCrossed", true);
                        facePlayer = true;
                    }
                    else
                    {
                        agent.speed = Vector3.Distance(transform.position, targetPlayer.transform.position)/* / 2f*/; // TODO: Test this
                        creatureAnimator.SetBool("armsCrossed", false);
                        facePlayer = false;
                    }

                    Vector3 pos = ChooseStalkPosition(targetPlayer.transform.position, 3f).position;
                    SetDestinationToPosition(pos);

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }

            if (TESTING.testing) { return; }

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
                UnityEngine.GameObject.Destroy(hallucManager.gameObject);
        }

        public Vector3 GetRandomPositionAroundPlayer(float minDistance, float maxDistance)
        {
            Vector3 pos = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(targetPlayer.transform.position, maxDistance, RoundManager.Instance.navHit);
            
            while (Physics.Linecast(targetPlayer.gameplayCamera.transform.position, pos + Vector3.up * HallucinationManager.LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) || Vector3.Distance(targetPlayer.transform.position, pos) < minDistance)
            {
                logger.LogDebug("Reroll");
                pos = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(targetPlayer.transform.position, maxDistance, RoundManager.Instance.navHit);
            }

            return pos;
        }

        public Transform? TryFindingHauntPosition()
        {
            for (int i = 0; i < allAINodes.Length; i++)
            {
                if (!Physics.Linecast(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position + Vector3.up * HallucinationManager.LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) && !targetPlayer.HasLineOfSightToPosition(allAINodes[i].transform.position, 80f, 100))
                {
                    logger.LogDebug($"Player distance to haunt position: {Vector3.Distance(targetPlayer.transform.position, allAINodes[i].transform.position)}");
                    return allAINodes[i].transform;
                }
            }
            return null;
        }

        public Transform? ChoosePositionInFrontOfPlayer(float minDistance, float maxDistance)
        {
            logger.LogDebug("Choosing position in front of player");
            Transform? result = null;
            logger.LogDebug(allAINodes.Count() + " ai nodes");
            foreach (var node in allAINodes)
            {
                if (node == null) { continue; }
                Vector3 nodePos = node.transform.position + Vector3.up * HallucinationManager.LOSOffset;
                Vector3 playerPos = targetPlayer.gameplayCamera.transform.position;
                float distance = Vector3.Distance(playerPos, nodePos);
                if (distance < minDistance || distance > maxDistance) { continue; }
                if (Physics.Linecast(nodePos, playerPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, queryTriggerInteraction: QueryTriggerInteraction.Ignore)) { continue; }
                if (!targetPlayer.HasLineOfSightToPosition(nodePos, 80f, 100)) { continue; }

                mostOptimalDistance = distance;
                result = node.transform;
            }

            logger.LogDebug($"null: {targetNode == null}");
            return result;
        }

        public Transform ChooseStalkPosition(Vector3 pos, float minDistance)
        {
            nodesTempArray = allAINodes.OrderBy((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
            Transform? result = null;
            for (int i = 0; i < nodesTempArray.Length; i++)
            {
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: true, avoidLineOfSight: true, checkLOSToTargetPlayer: false) && !Physics.Linecast(targetPlayer.gameplayCamera.transform.position, nodesTempArray[i].transform.position + Vector3.up * HallucinationManager.LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    float distance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    if (distance < minDistance) { continue; }

                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    return result;
                }
            }

            for (int i = 0; i < nodesTempArray.Length; i++)
            {
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: true, avoidLineOfSight: true, checkLOSToTargetPlayer: true))
                {
                    float distance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    if (distance < minDistance) { continue; }

                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    return result;
                }
            }

            return ChooseFarthestNodeFromPosition(pos);
        }

        public override void SetEnemyOutside(bool outside = false)
        {
            if (isOutside == outside) { return; }
            logger.LogDebug("SettingOutside: " + outside);
            base.SetEnemyOutside(outside);
        }

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false)
        {
            if (enemyMeshEnabled == enable) { return; }
            logger.LogDebug($"EnableEnemyMesh({enable})");
            enemyMesh.SetActive(enable);
            ScanNode.SetActive(enable);
            enemyMeshEnabled = enable;
        }

        public void Teleport(Vector3 position)
        {
            logger.LogDebug("Teleporting to " + position.ToString());
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            RoundManager.PlayRandomClip(creatureSFX, BellSFX);
            SwitchToBehaviourServerRpc((int)State.InActive);
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client collided with
        {
            base.OnCollideWithPlayer(other);
            if (inSpecialAnimation) { return; }
            if (currentBehaviourStateIndex == (int)State.InActive) { return; }
            if (!other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; }
            if (player == null || player != localPlayer) { return; }

            RoundManager.PlayRandomClip(creatureVoice, ScareSFX);
            player.insanityLevel = 50f;
            player.JumpToFearLevel(1f);
            StunGrenadeItem.StunExplosion(transform.position, false, 5f, 0f);
            SwitchToBehaviourServerRpc((int)State.InActive);
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
        public void SpawnGhostGirlServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }

            foreach (var girl in FindObjectsOfType<DressGirlAI>())
            {
                if (girl.hauntingPlayer == targetPlayer)
                {
                    return;
                }
            }

            List<SpawnableEnemyWithRarity> enemies = Utils.GetEnemies();
            SpawnableEnemyWithRarity? ghostGirl = enemies.Where(x => x.enemyType.name == "DressGirl").FirstOrDefault();
            if (ghostGirl == null) { logger.LogError("Ghost girl could not be found"); return; }

            RoundManager.Instance.SpawnEnemyGameObject(Vector3.zero, 0, -1, ghostGirl.enemyType);
        }

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
                GameObject obj = Instantiate(new GameObject(), Vector3.zero, Quaternion.identity, RoundManager.Instance.mapPropsContainer.transform);
                hallucManager = obj.AddComponent<HallucinationManager>();
                hallucManager.SCPInstance = this;
                hallucManager.targetPlayer = targetPlayer;
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