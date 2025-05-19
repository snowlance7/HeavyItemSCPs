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
        public bool playerHasLOS;

        Transform? farthestNodeFromTargetPlayer;
        bool gettingFarthestNodeFromPlayerAsync;
        int maxAsync;
        bool stalkRetreating;
        float evadeStealthTimer;
        bool wasInEvadeMode;
        List<Transform> ignoredNodes = [];

        // Constants
        int hashRunSpeed;
        const float maxInsanity = 50f;
        const float maxInsanityThreshold = 35;
        const float minCooldown = 0.5f;
        const float maxCooldown = 1f;
        public const float LOSAngle = 45f;
        public const float LOSOffset = 1f;

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

            if (TESTING.testing && IsServerOrHost)
            {
                targetPlayer = StartOfRound.Instance.allPlayerScripts.Where(x => x.isPlayerControlled).FirstOrDefault();
                ChangeTargetPlayerClientRpc(targetPlayer.actualClientId);
            }
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

            playerHasLOS = targetPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset, LOSAngle);

            if (playerHasLOS)
            {
                stareTime += Time.deltaTime;
            }
            else
            {
                stareTime = 0f;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.InActive:

                    break;

                case (int)State.Manifesting:

                    break;

                case (int)State.Chasing:

                    break;

                case (int)State.Stalking: // TODO: Figure out how to get this to work like the Flowerman/Braken or have him teleport after staring for too long

                    if (gettingFarthestNodeFromPlayerAsync && targetPlayer != null)
                    {
                        float distanceFromPlayer = Vector3.Distance(base.transform.position, targetPlayer.transform.position);
                        if (distanceFromPlayer < 16f)
                        {
                            maxAsync = 100;
                        }
                        else if (distanceFromPlayer < 40f)
                        {
                            maxAsync = 25;
                        }
                        else
                        {
                            maxAsync = 4;
                        }
                        Transform transform = ChooseFarthestNodeFromPosition(targetPlayer.transform.position, avoidLineOfSight: true, 0, doAsync: true, maxAsync, capDistance: true);
                        if (!gotFarthestNodeAsync)
                        {
                            return;
                        }
                        farthestNodeFromTargetPlayer = transform;
                        gettingFarthestNodeFromPlayerAsync = false;
                    }
                    if (playerHasLOS)
                    {
                        if (!stalkRetreating)
                        {
                            stalkRetreating = true;
                            agent.speed = 0f;
                            evadeStealthTimer = 0f;
                        }
                        else if (evadeStealthTimer > 0.5f)
                        {
                            ResetStealthTimerServerRpc();
                        }
                    }

                    if (stalkRetreating)
                    {
                        if (!wasInEvadeMode)
                        {
                            RoundManager.Instance.PlayAudibleNoise(base.transform.position, 7f, 0.8f);
                            wasInEvadeMode = true;
                        }
                        evadeStealthTimer += Time.deltaTime;
                        if (thisNetworkObject.IsOwner)
                        {
                            if (evadeStealthTimer > 5f)
                            {
                                evadeStealthTimer = 0f;
                                stalkRetreating = false;
                            }
                        }
                    }
                    else
                    {
                        if (wasInEvadeMode)
                        {
                            wasInEvadeMode = false;
                            evadeStealthTimer = 0f;
                        }
                    }

                    break;
                default:
                    break;
            }
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

                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                case (int)State.Stalking: // TODO: Figure out how to get this to work like the Flowerman/Braken or have him teleport after staring for too long
                    creatureSFX.volume = 0.5f;

                    if (stalkRetreating)
                    {
                        agent.speed = 15f;
                        creatureAnimator.SetBool("armsCrossed", true);
                        facePlayer = true;

                        StalkingAvoidPlayer();
                    }
                    else
                    {
                        agent.speed = Vector3.Distance(transform.position, targetPlayer.transform.position); // TODO: Test this
                        creatureAnimator.SetBool("armsCrossed", false);
                        facePlayer = false;

                        StalkingChooseClosestNodeToPlayer();
                    }

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
            
            while (Physics.Linecast(targetPlayer.gameplayCamera.transform.position, pos + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) || Vector3.Distance(targetPlayer.transform.position, pos) < minDistance)
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
                if (!Physics.Linecast(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) && !playerHasLOS)
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
                Vector3 nodePos = node.transform.position + Vector3.up * LOSOffset;
                Vector3 playerPos = targetPlayer.gameplayCamera.transform.position;
                float distance = Vector3.Distance(playerPos, nodePos);
                if (distance < minDistance || distance > maxDistance) { continue; }
                if (Physics.Linecast(nodePos, playerPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, queryTriggerInteraction: QueryTriggerInteraction.Ignore)) { continue; }
                if (!targetPlayer.HasLineOfSightToPosition(nodePos, LOSAngle)) { continue; }

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
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: true, avoidLineOfSight: true, checkLOSToTargetPlayer: false) && !Physics.Linecast(targetPlayer.gameplayCamera.transform.position, nodesTempArray[i].transform.position + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
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

        public void StalkingChooseClosestNodeToPlayer()
        {
            if (targetNode == null)
            {
                targetNode = allAINodes[0].transform;
            }
            Transform transform = ChooseClosestNodeToPosition(targetPlayer.transform.position, avoidLineOfSight: true);
            if (transform != null)
            {
                targetNode = transform;
            }
            float num = Vector3.Distance(targetPlayer.transform.position, base.transform.position);
            if (num - mostOptimalDistance < 0.1f && (!PathIsIntersectedByLineOfSight(targetPlayer.transform.position, calculatePathDistance: true) || num < 3f))
            {
                if (pathDistance > 10f && !ignoredNodes.Contains(targetNode) && ignoredNodes.Count < 4)
                {
                    ignoredNodes.Add(targetNode);
                }
                movingTowardsTargetPlayer = true;
            }
            else
            {
                SetDestinationToPosition(targetNode.position);
            }
        }

        public void StalkingAvoidPlayer()
        {
            if (farthestNodeFromTargetPlayer == null)
            {
                gettingFarthestNodeFromPlayerAsync = true;
                return;
            }
            Transform transform = farthestNodeFromTargetPlayer;
            farthestNodeFromTargetPlayer = null;
            if (transform != null && mostOptimalDistance > 5f && Physics.Linecast(transform.transform.position, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                targetNode = transform;
                SetDestinationToPosition(targetNode.position);
                return;
            }

            if (stareTime > 3f)
            {
                stareTime = 0f;
                if (farthestNodeFromTargetPlayer == null)
                {
                    farthestNodeFromTargetPlayer = ChooseFarthestNodeFromPosition(targetPlayer.transform.position);
                }

                Teleport(farthestNodeFromTargetPlayer.position);
                hallucManager?.FlickerLights();
                RoundManager.PlayRandomClip(creatureVoice, BellSFX);
            }

            agent.speed = 0f;
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
            targetPlayer.insanityLevel = 0f;
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
            StunGrenadeItem.StunExplosion(transform.position, false, 1f, 0f);
            targetPlayer.sprintMeter = 0f;
            targetPlayer.DropAllHeldItemsAndSync();
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

            if (currentBehaviourStateIndex == (int)State.Chasing || stalkRetreating)
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

                    targetPlayer.activatingItem = false;
                    Utils.FreezePlayer(targetPlayer, false);
                    if (localPlayer == targetPlayer) { shotgun.ShootGunAndSync(false); }
                    yield return null;
                    targetPlayer.DamagePlayer(100, hasDamageSFX: true, callRPC: false, CauseOfDeath.Gunshots, 0, fallDamage: false, shotgun.shotgunRayPoint.forward * 30f);

                    yield return new WaitForSeconds(1f);
                }
                finally
                {
                    HallucinationManager.overrideShotguns.Remove(shotgun);
                    targetPlayer.activatingItem = false;
                    Utils.FreezePlayer(targetPlayer, false);
                }
            }

            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Cant get netObj"); return; }
            if (!netObj.TryGetComponent(out ShotgunItem shotgun)) { logger.LogError("Cant get ShotgunItem"); return; }

            StartCoroutine(RotateShotgunCoroutine(shotgun, duration));
        }
    }
}