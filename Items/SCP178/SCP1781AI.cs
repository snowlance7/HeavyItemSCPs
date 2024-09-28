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

        Coroutine wanderingRoutine;
        float stareTime;
        float postObservationTimer;
        float timeSinceLastAngerCalc;
        float timeSinceEmoteUsed;
        float timeSinceDamagePlayer = 0f;
        float timeSincePlayerTalked = 0f;
        float wanderingRadius;
        float wanderWaitTime;
        bool lobPlayerNoise = false;

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

            spawnPosition = transform.position;

            //SetOutsideOrInside(); // TODO: May be unneeded

            logger.LogDebug("SCP-178-1 Spawned");
        }

        public override void Update()
        {
            base.Update();

            if (StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceEmoteUsed += Time.deltaTime;
            timeSinceLastAngerCalc += Time.deltaTime;

            if (currentBehaviourStateIndex == (int)State.Observing)
            {
                timeSincePlayerTalked += Time.deltaTime;
                turnCompass.LookAt(lastObservingPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 10f * Time.deltaTime);

                if (observingPlayer != null)
                {
                    postObservationTimer = 0f;
                }
                else
                {
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

            if (currentBehaviourStateIndex != (int)State.Chasing && TargetPlayerIfClose())
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

                    if (CheckForPlayersLookingAtMe())
                    {
                        SwitchToBehaviourClientRpc((int)State.Observing);
                        StopCoroutine(wanderingRoutine);
                        wanderingRoutine = null!;
                        networkAnimator.SetTrigger("angryIdle");
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

                    if (!CheckForPlayersLookingAtMe() && postObservationTimer > stareTime)
                    {
                        logger.LogDebug("Switching to roaming");
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }
                    if (timeSinceLastAngerCalc > 1f)
                    {
                        DoAngerCalculations();
                        timeSinceLastAngerCalc = 0f;
                    }
                    break;

                case (int)State.Chasing:
                    agent.speed = 10f;
                    agent.stoppingDistance = 3f; // TODO: Get this right

                    if (TargetPlayerIfClose())
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

        public bool CheckForPlayersLookingAtMe()
        {
            observingPlayer = null!;
            float distance = -1f;
            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => x.isPlayerControlled))
            {
                if (player.HasLineOfSightToPosition(transform.position, 15f, (int)wanderingRadius * 2)) // TODO: Test this
                {
                    if (distance == -1f || distance > Vector3.Distance(transform.position, player.transform.position))
                    {
                        distance = Vector3.Distance(transform.position, player.transform.position);
                        observingPlayer = player;
                        lastObservingPlayer = player;
                    }
                }
            }
            return observingPlayer != null;
        }

        public IEnumerator WanderingCoroutine() // TODO: Test this
        {
            while (true)
            {
                networkAnimator.SetTrigger("walk");
                destination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(spawnPosition, wanderingRadius);
                SetDestinationToPosition(destination);
                yield return new WaitForSecondsRealtime(1f);
                yield return new WaitUntil(() => agent.remainingDistance <= agent.stoppingDistance + 0.1f && agent.velocity.sqrMagnitude < 0.01f);
                if (SCP1781Manager.IsAngeryAtPlayer) { networkAnimator.SetTrigger("angryIdle"); }
                else { networkAnimator.SetTrigger("passiveIdle"); }

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
            //logger.LogDebug(lastObservingPlayer.playerUsername);
            //if (lastObservingPlayer == null) { return; }

            bool wearing178 = SCP1781Manager.PlayersWearing178.Contains(lastObservingPlayer);
            logger.LogDebug($"Wearing178: {wearing178}");

            int anger = 1;

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

            if (lastObservingPlayer.performingEmote && timeSinceEmoteUsed > 5f)
            {
                timeSinceEmoteUsed = 0f;
                anger = 70;
            }

            SCP1781Manager.Instance.AddAngerToPlayer(lastObservingPlayer, anger); // TODO: Isnt getting to here
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

            List<GameObject> nodes = inside ? RoundManager.Instance.insideAINodes.ToList() : RoundManager.Instance.outsideAINodes.ToList();

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

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
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

            if (player != null)
            {
                //logger.LogDebug("Collided with player " + player.playerUsername);

                if (timeSinceDamagePlayer > 1f)
                {
                    timeSinceDamagePlayer = 0f;
                    CollidedWithPlayerServerRpc(player.actualClientId);
                }
            }
        }

        // RPC's

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CollidedWithPlayerServerRpc(ulong clientId)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                PlayerControllerB player = NetworkHandlerHeavy.PlayerFromId(clientId);

                if (targetPlayer != null && targetPlayer == player)
                {
                    player.DamagePlayer(40);
                    DoAttackAnimation();

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
                    if (SCP1781Manager.PlayersWearing178.Contains(player) && !player.isPlayerDead)
                    {
                        SCP1781Manager.Instance.AddAngerToPlayer(player, 10);
                    }
                }
            }
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity