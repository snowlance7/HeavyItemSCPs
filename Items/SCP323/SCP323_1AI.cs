﻿using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP323
{
    internal class SCP323_1AI : EnemyAI, IVisibleThreat
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649
        //public Transform turnCompass = null!;
        public NetworkAnimator networkAnimator = null!;
        public AudioClip doorWooshSFX = null!;
        public AudioClip eatingSFX = null!;
        public AudioClip metalDoorSmashSFX = null!;
        public AudioClip bashSFX = null!;
        public AudioClip slashSFX = null!;
        public AudioClip[] walkingSFX = null!;
        public GameObject SCP323Prefab = null!;
        public Transform SkullTransform = null!;
#pragma warning restore 0649

        Vector3 forwardDirection;
        Vector3 upDirection;
        Vector3 throwDirection;

        EnemyAI targetEnemy = null!;
        EnemyAI lastEnemyAttackedMe = null!;

        float timeSinceDamagePlayer;
        

        public enum State
        {
            Transforming,
            Roaming,
            BloodSearch,
            Hunting,
            Eating
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-323-1 Spawned");

            SetOutsideOrInside();
            //SetEnemyOutsideClientRpc(true);
            //debugEnemyAI = true;

            currentBehaviourStateIndex = (int)State.Roaming;
            RoundManager.Instance.SpawnedEnemies.Add(this);

            // TODO: Do black smoke here
            logger.LogDebug("Finished spawning SCP-323-1");
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            //logger.LogDebug("Doing AI Interval");

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            if (stunNormalizedTimer > 0f) { return; }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Transforming:
                    agent.speed = 0f;
                    if (timeSinceSpawn > 5f)
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaming);

                        StartSearch(transform.position);
                        StartCoroutine(DecayHealthCoroutine());
                    }
                    break;

                case (int)State.Roaming:

                    break;

                case (int)State.BloodSearch:

                    break;

                case (int)State.Hunting:

                    break;

                case (int)State.Eating:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        IEnumerator DecayHealthCoroutine()
        {
            while (!isEnemyDead)
            {
                yield return new WaitForSecondsRealtime(20);
                
                if (!isEnemyDead)
                {
                    enemyHP -= 1;

                    if (enemyHP <= 0 && IsOwner)
                    {
                        KillEnemyOnOwnerClient();
                    }
                }
            }
        }
        
        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool FoundClosestPlayerInRange(float range)
        {
            TargetClosestPlayer();
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerInAnyCase()
        {
            mostOptimalDistance = 2000f;
            targetPlayer = null;
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (!PlayerIsTargetable(player)) { continue; }
                tempDist = Vector3.Distance(transform.position, player.transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = player;
                }
            }

            return targetPlayer != null;
        }

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = GetClosestAINode(GameObject.FindGameObjectsWithTag("OutsideAINode").ToList());
            GameObject closestInsideNode = GetClosestAINode(GameObject.FindGameObjectsWithTag("AINode").ToList());

            if (Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position))
            {
                logger.LogDebug("Setting enemy outside");
                SetEnemyOutsideClientRpc(true);
                return;
            }
            logger.LogDebug("Setting enemy inside");
        }

        public GameObject GetClosestAINode(List<GameObject> nodes)
        {
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null!;
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

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
        }

        public override void ReachedNodeInSearch()
        {
            base.ReachedNodeInSearch();

            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                int randomNum = Random.Range(0, 100);
                if (randomNum < 10)
                {
                    //creatureVoice.PlayOneShot(warningRoarSFX, 1f);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            CancelSpecialAnimationWithPlayer();
            StopAllCoroutines();
            base.KillEnemy(false);
            SpawnSkullOnHead();
        }

        public void SpawnSkullOnHead()
        {
            //Vector3 position = base.transform.position + Vector3.up * 0.6f;
            //position += new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0f, UnityEngine.Random.Range(-0.8f, 0.8f));
            GameObject skullObj = UnityEngine.Object.Instantiate(SCP323Prefab, SkullTransform.position, SkullTransform.rotation, SkullTransform);
            skullObj.GetComponent<NetworkObject>().Spawn();
            skullObj.GetComponent<SCP323Behavior>().AttachedToWendigo = this;
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1)
        {
            if (!isEnemyDead)
            {
                enemyHP -= force;
                if (enemyHP <= 0 && IsOwner)
                {
                    KillEnemyOnOwnerClient();
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null)
            {

            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null!) // TODO: TEST THIS
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (collidedEnemy.enemyType.name == "MaskedPlayer")
            {
                if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation)
                {
                    if (IsServerOrHost)
                    {

                    }
                }
            }
        }

        public void DoRandomWalkSFX()
        {
            if (walkingSFX.Length > 0)
            {
                int randomNum = Random.Range(0, walkingSFX.Length);
                creatureSFX.PlayOneShot(walkingSFX[randomNum], 1f);
            }
        }

        // IVisibleThreat Interface
        public ThreatType type => ThreatType.Player;

        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            return 5;
        }

        int IVisibleThreat.GetInterestLevel()
        {
            return 1;
        }

        Transform IVisibleThreat.GetThreatLookTransform()
        {
            return eye;
        }

        Transform IVisibleThreat.GetThreatTransform()
        {
            return base.transform;
        }

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            if (base.IsOwner)
            {
                return agent.velocity;
            }
            return Vector3.zero;
        }

        float IVisibleThreat.GetVisibility()
        {
            if (isEnemyDead)
            {
                return 0f;
            }
            return 1f;
        }

        // RPC's

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity