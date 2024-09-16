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

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AI : EnemyAI
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        public Transform turnCompass = null!;
#pragma warning restore 0649

        bool walking;
        public bool meshEnabledOnClient;
        public Vector3 spawnPosition;

        public GameObject Mesh;
        public ScanNodeProperties ScanNode;

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

            var state = currentBehaviourStateIndex;

            if (state == (int)State.Roaming)
            {
                if (agent.remainingDistance <= agent.stoppingDistance + 0.1f && agent.velocity.sqrMagnitude < 0.01f)
                {
                    if (walking)
                    {
                        walking = false;
                        //DoAnimationClientRpc("stopWalk");
                    }
                }
                else if (!walking)
                {
                    walking = true;
                    //DoAnimationClientRpc("startWalk");
                }
            }
            else if (state == (int)State.Observing)
            {
                timeSincePlayerTalked += Time.deltaTime;
                //if (walking) { walking = false; DoAnimationClientRpc("stopWalk"); }
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
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    agent.speed = 2f;
                    if (CheckForPlayersLookingAtMe())
                    {
                        SwitchToBehaviourClientRpc((int)State.Observing);
                        StopCoroutine(wanderingRoutine);
                        wanderingRoutine = null;
                        return;
                    }
                    if (wanderingRoutine == null)
                    {
                        wanderingRoutine = StartCoroutine(WanderingCoroutine());
                    }
                    break;

                case (int)State.Observing:
                    agent.speed = 0f;

                    if (!CheckForPlayersLookingAtMe() && postObservationTimer > stareTime)
                    {
                        logger.LogDebug("Switching to roaming");
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }
                    if (Time.realtimeSinceStartup > timeSinceLastAngerCalc + 1f)
                    {
                        timeSinceLastAngerCalc = Time.realtimeSinceStartup;
                        DoAngerCalculations();
                    }
                    break;

                case (int)State.Chasing:
                    agent.speed = 10f;

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

            if (targetPlayer != null && (targetPlayer.isPlayerDead || targetPlayer.disconnectedMidGame)) { targetPlayer = null; }

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
            observingPlayer = null;
            float distance = -1f;
            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => x.isPlayerControlled))
            {
                if (player.HasLineOfSightToPosition(transform.position, 10f, (int)wanderingRadius * 2))
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
                destination = RoundManager.Instance.GetRandomNavMeshPositionInRadius(spawnPosition, wanderingRadius);
                SetDestinationToPosition(destination);
                yield return new WaitUntil(() => agent.remainingDistance <= agent.stoppingDistance + 0.1f && agent.velocity.sqrMagnitude < 0.01f);
                yield return new WaitForSecondsRealtime(wanderWaitTime);
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

        public override void EnableEnemyMesh(bool enable, bool overrideDoNotSet = false)
        {
            Mesh.SetActive(enable);
            ScanNode.gameObject.SetActive(enable);
            meshEnabledOnClient = enable;
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

            if (lastObservingPlayer != null && Vector3.Distance(lastObservingPlayer.transform.position, noisePosition) < 1f && noiseID == 75)
            {
                lobPlayerNoise = true;
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);

            if (player != null)
            {
                if (targetPlayer != null && targetPlayer == player)
                {
                    if (timeSinceDamagePlayer > 2f)
                    {
                        player.DamagePlayer(40);
                        timeSinceDamagePlayer = 0f;
                    }
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
                    if (SCP1781Manager.PlayersWearing178.Contains(player) && timeSinceDamagePlayer > 2f && !player.isPlayerDead)
                    {
                        timeSinceDamagePlayer = 0f;
                        SCP1781Manager.Instance.AddAngerToPlayer(player, 10);
                    }
                }
            }
        }

        // RPC's

        [ClientRpc]
        private void DoAnimationClientRpc(string animationName)
        {
            logger.LogDebug("Animation: " + animationName);
            creatureAnimator.SetTrigger(animationName);
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity