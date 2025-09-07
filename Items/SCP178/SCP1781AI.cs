using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using static HeavyItemSCPs.Plugin;
using static HeavyItemSCPs.Items.SCP178.SCP178Behavior;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AI : NetworkBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Transform turnCompass;
        public GameObject Mesh;
        public ScanNodeProperties ScanNode;
        public NavMeshAgent agent;
        public Animator creatureAnimator;
        public AudioSource creatureSFX;
        public AudioSource creatureVoice;
        public SkinnedMeshRenderer renderer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public static List<SCP1781AI> Instances = [];

        public static Dictionary<PlayerControllerB, int> PlayersAngerLevels = new Dictionary<PlayerControllerB, int>();

        public PlayerControllerB? targetPlayer;

        //public bool inSpecialAnimation;
        public State currentBehaviorState;
        private bool moveTowardsDestination;
        private Vector3 destination;

        private State previousBehaviorStateIndex;

        private NavMeshPath path1 = new NavMeshPath();

        public bool meshEnabledOnClient;
        public Vector3 spawnPosition;

        float postObservationTime;
        float observationTimer;
        float observationGracePeriod;
        float distanceToAddAnger;
        float distanceToStartChase = 5f;

        Coroutine? wanderingRoutine;

        float timeSinceVisible;
        float timeSinceLastStaredAt;
        float timeSinceLastAngerCalc;
        float timeSinceEmoteUsed;

        float wanderingRadius;
        float wanderWaitTime;

        int hashSpeed;
        int hashAngryIdle;
        public bool isBeingObserved;
        private Vector3 lastPosition;
        private float updateDestinationInterval;
        private float AIIntervalTime = 0.2f;

        // Configs
        public static int maxAnger = 100;
        int playerDamage = 40;
        float activeTimeAfterVisible = 15f;
        float renderDistance = 20f;

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

            timeSinceVisible = Mathf.Infinity;
            timeSinceLastStaredAt = Mathf.Infinity;

            enabled = false;

            logger.LogDebug("SCP-178-1 Spawned");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instances.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            Instances.Remove(this);
        }

        public void OnEnable() // TODO
        {
            logger.LogDebug("SCP1781AI enabled");
            timeSinceVisible = 0f;
            creatureAnimator.enabled = true;

            if (TargetPlayerIfClose())
            {
                previousBehaviorStateIndex = currentBehaviorState;
                currentBehaviorState = State.Chasing;
            }
            else if (currentBehaviorState != State.Roaming)
            {
                previousBehaviorStateIndex = currentBehaviorState;
                currentBehaviorState = State.Roaming;
            }
        }

        public void OnDisable()
        {
            logger.LogDebug("SCP1781AI disabled");
            StopAllCoroutines();
            wanderingRoutine = null;
            creatureAnimator.enabled = false;
        }

        public void Update()
        {
            timeSinceLastStaredAt += Time.deltaTime;
            timeSinceVisible += Time.deltaTime;
            timeSinceEmoteUsed += Time.deltaTime;
            timeSinceLastAngerCalc += Time.deltaTime;

            if (StartOfRound.Instance.inShipPhase) { enabled = false; return; }

            if (currentBehaviorState == State.Observing)
            {
                turnCompass.LookAt(lastPlayerHeldBy!.gameplayCamera.transform.position);
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

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoAIInterval();
                updateDestinationInterval = AIIntervalTime;
            }
        }

        public void LateUpdate()
        {
            Vector3 delta = transform.position - lastPosition;
            float speed = delta.magnitude / Time.deltaTime;

            creatureAnimator.SetFloat(hashSpeed, speed);
            creatureAnimator.SetBool(hashAngryIdle, isBeingObserved);

            lastPosition = transform.position;
        }

        public void DoAIInterval()
        {
            if (lastPlayerHeldBy != null)
            {
                isBeingObserved = IsPlayerLookingAtMe(lastPlayerHeldBy);
            }

            if (!IsServerOrHost) { return; }

            if (moveTowardsDestination)
            {
                agent.SetDestination(destination);
            }

            if (currentBehaviorState != State.Chasing && TargetPlayerIfClose())
            {
                StopAllCoroutines();
                wanderingRoutine = null;
                SwitchToBehaviorClientRpc(State.Chasing);
                return;
            }

            switch (currentBehaviorState)
            {
                case State.Roaming:
                    agent.speed = 2f;
                    agent.stoppingDistance = 0f;

                    if (isBeingObserved)
                    {
                        StopAllCoroutines();
                        wanderingRoutine = null;
                        SwitchToBehaviorClientRpc(State.Observing);
                        break;
                    }
                    if (wanderingRoutine == null)
                    {
                        wanderingRoutine = StartCoroutine(WanderingCoroutine());
                    }
                    break;

                case State.Observing:
                    agent.speed = 0f;
                    agent.stoppingDistance = 0f;

                    if (!isBeingObserved && timeSinceLastStaredAt > postObservationTime)
                    {
                        SwitchToBehaviorClientRpc(State.Roaming);
                        return;
                    }
                    if (isBeingObserved && timeSinceLastAngerCalc > 1f && observationTimer > observationGracePeriod)
                    {
                        timeSinceLastAngerCalc = 0f;
                        DoStaringAngerCalculations();
                    }
                    break;

                case State.Chasing:
                    agent.speed = 10f;
                    agent.stoppingDistance = 2.5f;

                    if (!TargetPlayerIfClose() || !SetDestinationToPosition(targetPlayer!.transform.position, true))
                    {
                        SwitchToBehaviorClientRpc(State.Roaming);
                        return;
                    }

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviorState);
                    break;
            }
        }

        public void Teleport(Vector3 position)
        {
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
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

        /*bool IsWithinWanderingRadius(float multiplier = 1f)
        {
            return Vector3.Distance(transform.position, spawnPosition) <= wanderingRadius * multiplier;
        }*/

        public bool TargetPlayerIfClose()
        {
            if (targetPlayer != null && !targetPlayer.isPlayerControlled) { targetPlayer = null; } // TODO: Test this

            float closestDistance = Mathf.Infinity;

            foreach (var playerAnger in PlayersAngerLevels)
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

        public bool IsPlayerLookingAtMe(PlayerControllerB player)
        {
            if (Physics.Raycast(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward, out RaycastHit hit, distanceToAddAnger, LayerMask.GetMask("Enemies")))
            {
                return hit.collider.gameObject.transform.parent.gameObject == gameObject;
            }
            
            return false;
        }

        /*public bool IsVisibleToLocalPlayer() // TODO: TEST THIS
        {
            if (renderer.isVisible) // quick frustum culling check
            {
                Vector3 start = localPlayer.gameplayCamera.transform.position;
                Vector3 end = renderer.bounds.center;

                if (!Physics.Linecast(start, end, StartOfRound.Instance.collidersAndRoomMask, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }
            return false;
        }*/

        public bool IsNearbyLocalPlayer()
        {
            return Vector3.Distance(localPlayer.transform.position, transform.position) < renderDistance;
        }



        IEnumerator WanderingCoroutine()
        {
            yield return null;
            while (wanderingRoutine != null && enabled && agent.enabled)
            {
                float timeStopped = 0f;
                Vector3 pos = spawnPosition;
                pos = RoundManager.Instance.GetRandomNavMeshPositionInRadius(pos, wanderingRadius, RoundManager.Instance.navHit);
                SetDestinationToPosition(pos);
                while (wanderingRoutine != null && enabled && agent.enabled)
                {
                    yield return new WaitForSeconds(SCP178Behavior.updateInterval);
                    if (timeStopped > wanderWaitTime / 2f)
                    {
                        if (UnityEngine.Random.Range(0, 2) == 0)
                        {
                            DoAnimationServerRpc("scratch");
                            yield return new WaitForSecondsRealtime(1.6f);
                        }
                        else
                        {
                            yield return new WaitForSecondsRealtime(wanderWaitTime / 2f);
                        }

                        if (timeSinceVisible > activeTimeAfterVisible)
                        {
                            EnableClientRpc();
                            yield break;
                        }

                        break;
                    }

                    if (agent.velocity == Vector3.zero)
                    {
                        timeStopped += SCP178Behavior.updateInterval;
                    }
                }
            }
        }

        public void DoStaringAngerCalculations()
        {
            if (observationTimer < observationGracePeriod || !isBeingObserved) { return; }

            bool isSpeaking = lastPlayerHeldBy?.voicePlayerState != null && lastPlayerHeldBy.voicePlayerState.IsSpeaking; // TODO: Test this
            logger.LogDebug("isSpeaking: " + isSpeaking);

            int anger = isSpeaking ? 4 : 2;

            if (lastPlayerHeldBy!.performingEmote && timeSinceEmoteUsed > 5f)
            {
                timeSinceEmoteUsed = 0f;
                anger = 70;
            }

            AddAngerToPlayerServerRpc(lastPlayerHeldBy.actualClientId, anger);
        }

        public void EnableMesh(bool enable)
        {
            if (meshEnabledOnClient == enable) { return; }
            Mesh.SetActive(enable);
            ScanNode.gameObject.SetActive(enable);
            meshEnabledOnClient = enable;
        }

        internal void HitEnemyOnLocalClient(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            if (playerWhoHit != null)
            {
                AddAngerToPlayerServerRpc(playerWhoHit.actualClientId, 30);
            }
        }

        public void OnCollideWithPlayer(PlayerControllerB player)
        {
            if (!PlayersAngerLevels.ContainsKey(player)) { PlayersAngerLevels.Add(player, 0); }
            if (player != localPlayer) { return; }

            if (PlayersAngerLevels[player] >= 100)
            {
                CollidedWithPlayerServerRpc(player.actualClientId);
                return;
            }

            if (Instance != null && Instance.wearingOnLocalClient)
            {
                AddAngerToPlayerServerRpc(player.actualClientId, 10);
            }
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        public void AddAngerToPlayerServerRpc(ulong clientId, int anger)
        {
            if (!IsServerOrHost) { return; }
            AddAngerToPlayerClientRpc(clientId, anger);
        }

        [ClientRpc]
        public void AddAngerToPlayerClientRpc(ulong clientId, int anger)
        {
            PlayerControllerB player = PlayerFromId(clientId);

            //logger.LogDebug("AddAngerToPlayer: " + player.playerUsername + " " + anger);
            if (!PlayersAngerLevels.ContainsKey(player))
            {
                PlayersAngerLevels.Add(player, anger);
            }
            else
            {
                PlayersAngerLevels[player] += anger;
            }

            logger.LogDebug($"Anger for {player.playerUsername}: {PlayersAngerLevels[player]}");
        }

        [ServerRpc(RequireOwnership = false)]
        public void EnableServerRpc()
        {
            if (!IsServerOrHost) { return; }
            EnableClientRpc();
        }

        [ClientRpc]
        public void EnableClientRpc()
        {
            enabled = true;
            timeSinceVisible = 0f;
        }

        [ClientRpc]
        public void SwitchToBehaviorClientRpc(State state)
        {
            if (currentBehaviorState != state)
            {
                previousBehaviorStateIndex = currentBehaviorState;
                currentBehaviorState = state;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationName)
        {
            if (!IsServerOrHost) { return; }
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationName)
        {
            creatureAnimator.SetTrigger(animationName);
        }

        [ServerRpc(RequireOwnership = false)]
        public void CollidedWithPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
            CollideWithPlayerClientRpc(clientId, UnityEngine.Random.Range(0, 1));
        }

        [ClientRpc]
        public void CollideWithPlayerClientRpc(ulong clientId, int attackIndex)
        {
            enabled = true;
            creatureAnimator.enabled = true;
            creatureAnimator.SetTrigger(attackIndex == 1 ? "verticalAttack" : "horizontalAttack");
            creatureSFX.PlayOneShot(creatureSFX.clip, 1f);
            PlayerFromId(clientId).DamagePlayer(playerDamage);
        }
    }
}