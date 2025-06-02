using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using static HeavyItemSCPs.Plugin;
using UnityEngine.Animations.Rigging;
using UnityEngine.UIElements.Experimental;
using UnityEngine.Android;
using Unity.Netcode.Components;
using System;
using UnityEngine.AI;
using Dissonance;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Transform turnCompass;
        public GameObject Mesh;
        public ScanNodeProperties ScanNode;
        public NetworkAnimator networkAnimator;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        PlayerControllerB lastPlayerHeldBy { get { return SCP178Behavior.Instance!.lastPlayerHeldBy!; } }

        public bool meshEnabledOnClient;
        public Vector3 spawnPosition;

        float stareTime;
        float observationTimer;
        float postObservationTimer;
        float observationGracePeriod;
        float distanceToAddAnger;
        float renderDistance;
        float distanceToStartChase = 5f;
        float distanceToLoseChase = 10f;

        Coroutine? wanderingRoutine;
        float timeSinceLastAngerCalc;
        float timeSinceEmoteUsed;
        float timeSinceDamagePlayer;
        float wanderingRadius;
        float wanderWaitTime;

        int hashSpeed;
        int hashAngryIdle;
        bool isBeingObserved;

        // Configs
        int maxAnger = 100;
        int playerDamage = 40;

        public enum State
        {
            Roaming,
            Observing,
            Chasing
        }

        public override void Start()
        {
            try
            {
                overlapColliders = new Collider[1];
                thisNetworkObject = NetworkObject;
                thisEnemyIndex = RoundManager.Instance.numberOfEnemiesInScene;
                RoundManager.Instance.numberOfEnemiesInScene++;
                RoundManager.Instance.SpawnedEnemies.Add(this);

                path1 = new NavMeshPath();
                openDoorSpeedMultiplier = enemyType.doorSpeedMultiplier;
                serverPosition = base.transform.position;
            }
            catch (Exception arg)
            {
                logger.LogError($"Error when initializing enemy variables for {base.gameObject.name} : {arg}");
            }

            hashSpeed = Animator.StringToHash("speed");
            hashAngryIdle = Animator.StringToHash("angryIdle");

            stareTime = config1781PostObservationTime.Value;
            wanderingRadius = config1781WanderingRadius.Value;
            wanderWaitTime = config1781WanderingWaitTime.Value;
            observationGracePeriod = config1781ObservationGracePeriod.Value;
            distanceToAddAnger = config1781DistanceToAddAnger.Value;
            renderDistance = config1781RenderDistance.Value;

            spawnPosition = transform.position;

            logger.LogDebug("SCP-178-1 Spawned");
        }

        public override void Update()
        {
            if (inSpecialAnimation || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceEmoteUsed += Time.deltaTime;
            timeSinceLastAngerCalc += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.Observing)
            {
                turnCompass.LookAt(lastPlayerHeldBy.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);

                if (isBeingObserved)
                {
                    lastPlayerHeldBy.IncreaseFearLevelOverTime(0.01f);
                    observationTimer += Time.deltaTime;
                    postObservationTimer = 0f;
                }
                else
                {
                    observationTimer = 0f;
                    postObservationTimer += Time.deltaTime;
                }
            }

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoSyncedAIInterval();
                if (IsServerOrHost) { DoAIInterval(); }
                updateDestinationInterval = AIIntervalTime;
            }
        }

        public void DoSyncedAIInterval()
        {
            isBeingObserved = PlayerHasLineOfSightToMe(lastPlayerHeldBy);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (!IsNearbyPlayer(lastPlayerHeldBy, renderDistance))
            {
                if (wanderingRoutine != null)
                {
                    StopCoroutine(wanderingRoutine);
                    wanderingRoutine = null;
                }
                return;
            }

            if (currentBehaviourStateIndex != (int)State.Chasing && TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
            {
                if (wanderingRoutine != null)
                {
                    StopCoroutine(wanderingRoutine);
                    wanderingRoutine = null;
                }
                SwitchToBehaviourClientRpc((int)State.Chasing);
                networkAnimator.SetTrigger("run");
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    agent.speed = 2f;
                    agent.stoppingDistance = 0f;

                    if (isBeingObserved)
                    {
                        SwitchToBehaviourClientRpc((int)State.Observing);
                        StopCoroutine(wanderingRoutine);
                        wanderingRoutine = null!;
                        break;
                    }
                    if (wanderingRoutine == null)
                    {
                        wanderingRoutine = StartCoroutine(WanderingCoroutine(spawnPosition, wanderingRadius));
                    }
                    break;

                case (int)State.Observing:
                    agent.speed = 0f;
                    agent.stoppingDistance = 0f;

                    if (!isBeingObserved && postObservationTimer > stareTime)
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }
                    if (timeSinceLastAngerCalc > 1f && isBeingObserved && observationTimer > observationGracePeriod)
                    {
                        timeSinceLastAngerCalc = 0f;
                        DoAngerCalculations();
                    }
                    break;

                case (int)State.Chasing:
                    agent.speed = 10f;
                    agent.stoppingDistance = 2.5f;

                    if (TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        public void LateUpdate()
        {
            EnableMesh(SCP178Behavior.Instance.wearing && lastPlayerHeldBy == localPlayer);

            creatureAnimator.SetFloat(hashSpeed, agent.velocity.magnitude / 2);
            creatureAnimator.SetBool(hashAngryIdle, isBeingObserved);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (SCP178Behavior.Instance == null) { return; }
            SCP178Behavior.Instance.SCP1781Instances.Remove(this);
        }

        public bool IsNearbyPlayer(PlayerControllerB player, float distance)
        {
            return Vector3.Distance(transform.position, player.transform.position) < distance;
        }

        bool IsWithinWanderingRadius(float multiplier = 1f)
        {
            return Vector3.Distance(transform.position, spawnPosition) <= wanderingRadius * multiplier;
        }

        public bool TargetPlayerIfClose()
        {
            if (targetPlayer != null && !targetPlayer.isPlayerControlled) { targetPlayer = null; } // TODO: Test this

            float closestDistance = Mathf.Infinity;

            foreach (var playerAnger in SCP178Behavior.Instance!.PlayersAngerLevels)
            {
                if (playerAnger.Value < maxAnger) { continue; }

                float distance = Vector3.Distance(transform.position, playerAnger.Key.transform.position);
                if (distance < distanceToStartChase)
                {
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        targetPlayer = playerAnger.Key;
                    }
                }
            }

            return targetPlayer != null;
        }

        bool PlayerHasLineOfSightToMe(PlayerControllerB player)
        {
            if (Physics.Raycast(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward, out RaycastHit hit, distanceToAddAnger, LayerMask.GetMask("Enemies")))
            {
                return hit.collider.gameObject.transform.parent.gameObject == gameObject;
            }

            return false;
        }

        IEnumerator WanderingCoroutine(Vector3 position, float radius)
        {
            yield return null;
            while (wanderingRoutine != null)
            {
                float timeStopped = 0f;
                Vector3 pos = position;
                pos = RoundManager.Instance.GetRandomNavMeshPositionInRadius(pos, radius, RoundManager.Instance.navHit);
                SetDestinationToPosition(pos);
                while (agent.enabled)
                {
                    yield return new WaitForSeconds(AIIntervalTime);
                    if (timeStopped > wanderWaitTime / 2f)
                    {
                        if (UnityEngine.Random.Range(0, 2) == 0)
                        {
                            networkAnimator.SetTrigger("scratch");
                            yield return new WaitForSecondsRealtime(1.6f);
                        }
                        else
                        {
                            yield return new WaitForSecondsRealtime(wanderWaitTime / 2f);
                        }

                        break;
                    }

                    if (agent.velocity == Vector3.zero)
                    {
                        timeStopped += AIIntervalTime;
                    }
                }
            }
        }

        public void DoAngerCalculations()
        {
            if (observationTimer < observationGracePeriod || !isBeingObserved) { return; }

            int anger = 1;

            bool wearing178 = SCP178Behavior.Instance.wearing;
            bool isSpeaking = lastPlayerHeldBy.voicePlayerState != null && lastPlayerHeldBy.voicePlayerState.IsSpeaking; // TODO: Test this
            logger.LogDebug("wearing178: " + wearing178);
            logger.LogDebug("isSpeaking: " + isSpeaking);

            if (!wearing178) { return; }

            anger = isSpeaking ? 4 : 2;

            if (lastPlayerHeldBy.performingEmote && timeSinceEmoteUsed > 5f)
            {
                timeSinceEmoteUsed = 0f;
                anger = 70;
            }

            SCP178Behavior.Instance.AddAngerToPlayer(lastPlayerHeldBy, anger);
        }

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet)
        {
            return;
        }

        public void EnableMesh(bool enable)
        {
            if (meshEnabledOnClient == enable) { return; }
            Mesh.SetActive(enable);
            ScanNode.gameObject.SetActive(enable);
            meshEnabledOnClient = enable;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);

            if (playerWhoHit != null)
            {
                SCP178Behavior.Instance!.AddAngerToPlayer(playerWhoHit, 30);
            }
        }

        public void DoAttackAnimation()
        {
            if (UnityEngine.Random.Range(0, 2) == 0)
            {
                networkAnimator.SetTrigger("verticalAttack");
            }
            else
            {
                networkAnimator.SetTrigger("horizontalAttack");
            }
        }

        public override void OnCollideWithPlayer(Collider other) // TODO: Rework
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null) { return; }
            if (currentBehaviourStateIndex == (int)State.Roaming) { return; }
            if (timeSinceDamagePlayer > 2f) { return; }

            timeSinceDamagePlayer = 0f;
            CollidedWithPlayerServerRpc(player.actualClientId);
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        private void CollidedWithPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
            if (IsServerOrHost)
            {
                PlayerControllerB player = PlayerFromId(clientId);

                if (targetPlayer != null && targetPlayer == player)
                {
                    DoAttackAnimation();
                    creatureSFX.PlayOneShot(creatureSFX.clip, 1f);
                    player.DamagePlayer(playerDamage);

                    if (!targetPlayer.isPlayerControlled)
                    {
                        if (SCP178Behavior.Instance.PlayersAngerLevels.ContainsKey(targetPlayer))
                        {
                            SCP178Behavior.Instance.PlayersAngerLevels.Remove(targetPlayer);
                        }
                        targetPlayer = null;
                    }
                }
                else
                {
                    if (SCP178Behavior.Instance.wearing && player == lastPlayerHeldBy)
                    {
                        SCP178Behavior.Instance.AddAngerToPlayer(player, 10);
                    }
                }
            }
        }
    }
}