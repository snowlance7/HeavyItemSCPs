using BepInEx.Logging;
using Dissonance;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Android;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements.Experimental;
using static HeavyItemSCPs.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AI : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Transform turnCompass;
        public GameObject Mesh;
        public ScanNodeProperties ScanNode;
        public NetworkAnimator networkAnimator;
        public NavMeshAgent agent;
        public Animator creatureAnimator;
        public AudioSource creatureSFX;
        public AudioSource creatureVoice;
        public SkinnedMeshRenderer renderer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public static HashSet<SCP1781AI> Instances = [];

        PlayerControllerB lastPlayerHeldBy { get { return SCP178Behavior.Instance!.lastPlayerHeldBy!; } }
        public PlayerControllerB? targetPlayer;

        //public bool inSpecialAnimation;
        public State currentBehaviorState;
        private bool moveTowardsDestination;
        private Vector3 destination;

        public State currentBehaviourState;
        private State previousBehaviourStateIndex;

        private NavMeshPath path1;

        public bool meshEnabledOnClient;
        public Vector3 spawnPosition;

        float postObservationTime;
        float observationTimer;
        float observationGracePeriod;
        float distanceToAddAnger;
        float distanceToStartChase = 5f;
        float distanceToLoseChase = 10f;

        Coroutine? wanderingRoutine;

        float timeSinceLastVisible;
        float timeSinceLastStaredAt;
        float timeSinceLastAngerCalc;
        float timeSinceEmoteUsed;
        float timeSinceDamagePlayer;

        float wanderingRadius;
        float wanderWaitTime;

        int hashSpeed;
        int hashAngryIdle;
        public bool isBeingObserved;

        // Configs
        public static int maxAnger = 100;
        int playerDamage = 40;
        float activeTimeAfterVisible = 15f;

        public enum State
        {
            Roaming,
            Observing,
            Chasing
        }

        public void Start()
        {
            hashSpeed = Animator.StringToHash("speed");
            hashAngryIdle = Animator.StringToHash("angryIdle");

            postObservationTime = config1781PostObservationTime.Value;
            wanderingRadius = config1781WanderingRadius.Value;
            wanderWaitTime = config1781WanderingWaitTime.Value;
            observationGracePeriod = config1781ObservationGracePeriod.Value;
            distanceToAddAnger = config1781DistanceToAddAnger.Value;

            spawnPosition = transform.position;

            timeSinceLastVisible = Mathf.Infinity;
            timeSinceLastStaredAt = Mathf.Infinity;

            logger.LogDebug("SCP-178-1 Spawned");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            SCP178Behavior.Instance.SCP1781Instances.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            SCP178Behavior.Instance.SCP1781Instances.Remove(this);
        }

        public void OnEnable()
        {

        }

        public void OnDisable()
        {

        }

        public void Update()
        {
            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceEmoteUsed += Time.deltaTime;
            timeSinceLastAngerCalc += Time.deltaTime;

            if (currentBehaviorState == State.Observing)
            {
                turnCompass.LookAt(lastPlayerHeldBy.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);

                if (isBeingObserved)
                {
                    lastPlayerHeldBy.IncreaseFearLevelOverTime(0.01f);
                    observationTimer += Time.deltaTime;
                    timeSinceLastStaredAt = 0f;
                }
                else
                {
                    observationTimer = 0f;
                    timeSinceLastStaredAt += Time.deltaTime;
                }
            }
        }

        public void DoAIInterval()
        {
            if (moveTowardsDestination)
            {
                agent.SetDestination(destination);
            }

            if (!IsNearbyPlayer(lastPlayerHeldBy, renderDistance))
            {
                if (wanderingRoutine != null)
                {
                    StopCoroutine(wanderingRoutine);
                    wanderingRoutine = null;
                }
                return;
            }

            if (currentBehaviorState != State.Chasing && TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
            {
                if (wanderingRoutine != null)
                {
                    StopCoroutine(wanderingRoutine);
                    wanderingRoutine = null;
                }
                SwitchToBehaviourClientRpc(State.Chasing);
                networkAnimator.SetTrigger("run");
                return;
            }

            switch (currentBehaviorState)
            {
                case State.Roaming:
                    agent.speed = 2f;
                    agent.stoppingDistance = 0f;

                    if (isBeingObserved)
                    {
                        SwitchToBehaviourClientRpc(State.Observing);
                        StopCoroutine(wanderingRoutine);
                        wanderingRoutine = null!;
                        break;
                    }
                    if (wanderingRoutine == null)
                    {
                        wanderingRoutine = StartCoroutine(WanderingCoroutine(spawnPosition, wanderingRadius));
                    }
                    break;

                case State.Observing:
                    agent.speed = 0f;
                    agent.stoppingDistance = 0f;

                    if (!isBeingObserved && timeSinceLastStaredAt > postObservationTime)
                    {
                        SwitchToBehaviourClientRpc(State.Roaming);
                        return;
                    }
                    if (timeSinceLastAngerCalc > 1f && isBeingObserved && observationTimer > observationGracePeriod)
                    {
                        timeSinceLastAngerCalc = 0f;
                        DoAngerCalculations();
                    }
                    break;

                case State.Chasing:
                    agent.speed = 10f;
                    agent.stoppingDistance = 2.5f;

                    if (TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                    }
                    else
                    {
                        SwitchToBehaviourClientRpc(State.Roaming);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviorState);
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

        public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
        {
            if (checkForPath)
            {
                position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
                path1 = new NavMeshPath();
                if (!agent.CalculatePath(position, path1))
                {
                    return false;
                }
                if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
                {
                    return false;
                }
            }
            moveTowardsDestination = true;
            destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
            return true;
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

        public bool PlayerHasLineOfSightToMe(PlayerControllerB player)
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

        public void EnableMesh(bool enable)
        {
            if (meshEnabledOnClient == enable) { return; }
            Mesh.SetActive(enable);
            ScanNode.gameObject.SetActive(enable);
            meshEnabledOnClient = enable;
        }

        public void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
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

        public void OnCollideWithPlayer(Collider other) // TODO: Rework
        {
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !player.isPlayerControlled || player != localPlayer) { return; }
            logger.LogDebug("pass1");
            //if (currentBehaviorState == (int)State.Roaming) { return; }
            if (timeSinceDamagePlayer < 2f) { return; }
            logger.LogDebug("pass2");

            timeSinceDamagePlayer = 0f;
            CollidedWithPlayerServerRpc(player.actualClientId);
        }

        // RPC's

        [ClientRpc]
        public void SwitchToBehaviourClientRpc(State state)
        {
            if (currentBehaviourState != state)
            {
                previousBehaviourStateIndex = currentBehaviourState;
                currentBehaviourState = state;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void CollidedWithPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }

            PlayerControllerB player = PlayerFromId(clientId);

            logger.LogDebug("Collided with player: " + player.playerUsername);

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

        internal void HitEnemyOnLocalClient(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            throw new NotImplementedException();
        }
    }
}