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

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public Transform turnCompass = null!;
        public GameObject Mesh = null!;
        public ScanNodeProperties ScanNode = null!;
        public NetworkAnimator networkAnimator = null!;
#pragma warning restore 0649

        private static GameObject[] InsideAINodes = null!;
        private static GameObject[] OutsideAINodes = null!;

        bool walking;
        public bool meshEnabledOnClient;
        public Vector3 spawnPosition;

        public PlayerControllerB observingPlayer;
        public PlayerControllerB lastObservingPlayer;
        float stareTime;
        float observationTimer;
        float postObservationTimer;
        float observationGracePeriod;
        float distanceToAddAnger;
        float renderDistance;

        Coroutine wanderingRoutine;
        float timeSinceLastAngerCalc;
        float timeSinceEmoteUsed;
        float timeSinceDamagePlayer;
        float timeSincePlayerTalked;
        float wanderingRadius;
        float wanderWaitTime;
        bool lobPlayerNoise = false;
        bool playersNearby = false;

        float timeSincePlayersCheck;
        bool angryIdle;

        public enum State
        {
            Roaming,
            Observing,
            Chasing
        }

        public override void Start()
        {
            base.Start();

            currentBehaviourStateIndex = (int)State.Roaming;

            stareTime = config1781PostObservationTime.Value;
            wanderingRadius = config1781WanderingRadius.Value;
            wanderWaitTime = config1781WanderingWaitTime.Value;
            observationGracePeriod = config1781ObservationGracePeriod.Value;
            distanceToAddAnger = config1781DistanceToAddAnger.Value;
            renderDistance = config1781RenderDistance.Value;

            spawnPosition = transform.position;

            //SetOutsideOrInside();

            logger.LogDebug("SCP-178-1 Spawned");
        }

        public override void Update() // TODO: They arent using angryidle when being stared at and animations are being weird
        {
            base.Update();

            timeSincePlayersCheck += Time.deltaTime;

            if (timeSincePlayersCheck > 1f)
            {
                playersNearby = ArePlayersAroundMe(renderDistance);
            }

            if (StartOfRound.Instance.allPlayersDead || !playersNearby)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceEmoteUsed += Time.deltaTime;
            timeSinceLastAngerCalc += Time.deltaTime;
            

            if (currentBehaviourStateIndex == (int)State.Observing)
            {
                timeSincePlayerTalked += Time.deltaTime;
                if (lastObservingPlayer != null)
                {
                    turnCompass.LookAt(lastObservingPlayer.gameplayCamera.transform.position);
                    transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);
                }

                if (observingPlayer != null)
                {
                    observingPlayer.IncreaseFearLevelOverTime(0.01f);
                    observationTimer += Time.deltaTime;
                    postObservationTimer = 0f;
                }
                else
                {
                    observationTimer = 0f;
                    postObservationTimer += Time.deltaTime;
                }
            }
        }
        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            if (currentBehaviourStateIndex != (int)State.Chasing && TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                networkAnimator.SetTrigger("run");
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    agent.speed = 2f;
                    agent.stoppingDistance = 0f;
                    openDoorSpeedMultiplier = 0f;

                    if (CheckForPlayersLookingAtMe())
                    {
                        logger.LogDebug("Switching to observing");
                        SwitchToBehaviourClientRpc((int)State.Observing);
                        StopCoroutine(wanderingRoutine);
                        wanderingRoutine = null!;
                        networkAnimator.SetTrigger("passiveIdle");
                        break;
                    }
                    if (wanderingRoutine == null)
                    {
                        wanderingRoutine = StartCoroutine(WanderingCoroutine());
                    }
                    break;

                case (int)State.Observing:
                    agent.speed = 0f;
                    agent.stoppingDistance = 0f;
                    openDoorSpeedMultiplier = 0f;

                    if (!CheckForPlayersLookingAtMe() && postObservationTimer > stareTime)
                    {
                        logger.LogDebug("Switching to roaming");
                        angryIdle = false;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }
                    if (timeSinceLastAngerCalc > 1f && observingPlayer != null && observationTimer > observationGracePeriod)
                    {
                        DoAngerCalculations();
                        timeSinceLastAngerCalc = 0f;

                        if (!angryIdle)
                        {
                            angryIdle = true;
                            networkAnimator.SetTrigger("angryIdle");
                        }
                    }
                    break;

                case (int)State.Chasing:
                    agent.speed = 10f;
                    agent.stoppingDistance = 2.5f;
                    openDoorSpeedMultiplier = 1f;

                    if (TargetPlayerIfClose() && IsWithinWanderingRadius(2f))
                    {
                        SetMovingTowardsTargetPlayer(targetPlayer);
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

        public bool ArePlayersAroundMe(float distance)
        {
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isPlayerDead || !player.isPlayerControlled) { continue; }
                if (Vector3.Distance(transform.position, player.transform.position) < distance) { return true; }
            }

            return false;
        }

        bool IsWithinWanderingRadius(float multiplier = 1f)
        {
            return Vector3.Distance(transform.position, spawnPosition) < wanderingRadius * multiplier;
        }

        public bool TargetPlayerIfClose()
        {
            if (SCP1781Manager.Instance.PlayersAngerLevels == null) { return false; }

            if (targetPlayer != null && (targetPlayer.isPlayerDead || targetPlayer.disconnectedMidGame)) { targetPlayer = null; } // TODO: Test this

            float closestDistance = -1f;

            foreach (var player in SCP1781Manager.Instance.PlayersAngerLevels)
            {
                if (player.Value < 100) { continue; }

                float distance = Vector3.Distance(transform.position, player.Key.transform.position);
                if (distance < wanderingRadius)
                {
                    if (closestDistance == -1f || distance < closestDistance)
                    {
                        closestDistance = distance;
                        targetPlayer = player.Key;
                    }
                }
            }

            return targetPlayer != null;
        }

        bool PlayerHasLineOfSightToMe(PlayerControllerB player)
        {
            //logger.LogDebug("Checking los");
            if (Physics.Raycast(player.playerEye.transform.position, player.playerEye.transform.forward, out RaycastHit hit, distanceToAddAnger, LayerMask.GetMask("Enemies")))
            {
                //logger.LogDebug(hit.collider.gameObject.transform.parent.gameObject.name);
                if (hit.collider.gameObject.transform.parent.gameObject == gameObject)
                {
                    Debug.DrawRay(player.playerEye.transform.position, spawnPosition - player.playerEye.transform.position, Color.red, 1f);
                    //logger.LogDebug("Player has line of sight to me");
                    return true;
                }
            }

            return false;
        }

        public bool CheckForPlayersLookingAtMe()
        {
            PlayerControllerB? _observingPlayer = null;
            float distance = -1f;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player, false, true)) { continue; }
                float distanceToPlayer = Vector3.Distance(transform.position, player.transform.position);
                if (distanceToPlayer <= distanceToAddAnger)
                {
                    if (distance == -1f || distance > Vector3.Distance(transform.position, player.transform.position))
                    {
                        if (PlayerHasLineOfSightToMe(player)) // TODO: Test this
                        {
                            distance = Vector3.Distance(transform.position, player.transform.position);
                            _observingPlayer = player;
                        }
                    }
                }
            }

            if (_observingPlayer != null)
            {
                lastObservingPlayer = _observingPlayer;
                //logger.LogDebug(_observingPlayer + " is the observing player");
            }

            observingPlayer = _observingPlayer!;
            return observingPlayer != null;
        }

        public IEnumerator WanderingCoroutine() // TODO: Test this
        {
            yield return null;
            while (wanderingRoutine != null)
            {
                networkAnimator.SetTrigger("walk");
                destination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(spawnPosition, wanderingRadius, RoundManager.Instance.navHit);
                SetDestinationToPosition(destination);
                yield return new WaitForSecondsRealtime(1f);
                yield return new WaitUntil(() => Vector3.Distance(transform.position, destination) < 1f);
                networkAnimator.SetTrigger("passiveIdle");

                yield return new WaitForSecondsRealtime(wanderWaitTime / 2f);

                if (UnityEngine.Random.Range(0, 2) == 0)
                {
                    networkAnimator.SetTrigger("scratch");
                    yield return new WaitForSecondsRealtime(1.6f);
                }
                else
                {
                    yield return new WaitForSecondsRealtime(wanderWaitTime / 2f);
                }
            }
        }

        public void DoAngerCalculations()
        {
            if (observationTimer < observationGracePeriod || observingPlayer == null) { return; }

            int anger = 1;

            //bool wearing178 = SCP1781Manager.PlayersWearing178.Contains(observingPlayer);
            bool wearing178 = SCP178Behavior.Instance != null && SCP178Behavior.Instance.playerWornBy != null && SCP178Behavior.Instance.playerWornBy == observingPlayer;
            logger.LogDebug($"wearing178: {wearing178}");

            if (!wearing178 && !IsNearbySCP178(observingPlayer)) { return; }


            if (wearing178 && lobPlayerNoise)
            {
                anger = 4;
                lobPlayerNoise = false;
            }
            else if (wearing178 || lobPlayerNoise)
            {
                anger = 2;
                lobPlayerNoise = false;
            }

            if (observingPlayer.performingEmote && timeSinceEmoteUsed > 5f)
            {
                timeSinceEmoteUsed = 0f;
                anger = 70;
            }

            SCP1781Manager.Instance.AddAngerToPlayer(observingPlayer, anger);
        }

        public bool IsNearbySCP178(PlayerControllerB player)
        {
            //return FindObjectsOfType<SCP178Behavior>().Any(x => Vector3.Distance(player.transform.position, x.transform.position) < distanceToAddAnger);
            if (SCP178Behavior.Instance == null) { return false; }
            return Vector3.Distance(player.transform.position, SCP178Behavior.Instance.transform.position) < distanceToAddAnger;
        }

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet)
        {
            return;
        }

        public void EnableMesh(bool enable)
        {
            Mesh.SetActive(enable);
            ScanNode.gameObject.SetActive(enable);
            meshEnabledOnClient = enable;
        }

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = GetClosestAINode(false);
            GameObject closestInsideNode = GetClosestAINode(true);

            if (Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position))
            {
                SetEnemyOutsideClientRpc(true);
            }
        }

        public GameObject GetClosestAINode(bool inside)
        {
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null!;

            List<GameObject> nodes = inside ? GameObject.FindGameObjectsWithTag("AINode").ToList() : GameObject.FindGameObjectsWithTag("OutsideAINode").ToList();

            foreach (GameObject node in nodes)
            {
                float distanceToNode = Vector3.Distance(transform.position, node.transform.position);
                if (distanceToNode < closestDistance)
                {
                    closestDistance = distanceToNode;
                    closestNode = node;
                }
            }
            return closestNode;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1) // TODO: Check if this is run on host or client
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);

            if (playerWhoHit != null)
            {
                SCP1781Manager.Instance.AddAngerToPlayer(playerWhoHit, 30);
            }
        }

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);

            if (lastObservingPlayer != null && Vector3.Distance(lastObservingPlayer.transform.position, noisePosition) < wanderingRadius * 2 && noiseID == 75)
            {
                lobPlayerNoise = true;
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

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other, default, true);

            if (player != null && currentBehaviourStateIndex != (int)State.Roaming)
            {
                //logger.LogDebug("Collided with player " + player.playerUsername);

                if (timeSinceDamagePlayer > 2f)
                {
                    timeSinceDamagePlayer = 0f;
                    CollidedWithPlayerServerRpc(player.actualClientId);
                }
            }
        }

        // RPC's

        [ClientRpc]
        public void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CollidedWithPlayerServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                PlayerControllerB player = PlayerFromId(clientId);

                if (targetPlayer != null && targetPlayer == player)
                {
                    player.DamagePlayer(40);
                    DoAttackAnimation();
                    creatureSFX.PlayOneShot(creatureSFX.clip, 1f);

                    if (targetPlayer.isPlayerDead || targetPlayer.disconnectedMidGame)
                    {
                        if (SCP1781Manager.Instance.PlayersAngerLevels.ContainsKey(targetPlayer))
                        {
                            SCP1781Manager.Instance.PlayersAngerLevels.Remove(targetPlayer);
                        }
                        targetPlayer = null;
                    }
                }
                else
                {
                    if (SCP178Behavior.Instance != null && SCP178Behavior.Instance.playerWornBy != null && SCP178Behavior.Instance.playerWornBy == player && !player.isPlayerDead)
                    {
                        SCP1781Manager.Instance.AddAngerToPlayer(player, 10);
                    }
                }
            }
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity