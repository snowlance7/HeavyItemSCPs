using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System;
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
        public static SCP323_1AI? Instance { get; private set; }

#pragma warning disable 0649
        //public Transform turnCompass = null!;
        public NetworkAnimator networkAnimator = null!;
        public AudioClip doorWooshSFX = null!;
        public AudioClip eatingSFX = null!;
        public AudioClip metalDoorSmashSFX = null!;
        public AudioClip bashSFX = null!;
        public AudioClip slashSFX = null!;
        public AudioClip roarSFX = null!;
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
        float timeSinceInLOS;

        bool decayHealth = true;
        float decayMultiplier = 1f;

        // Constants
        const float roamSpeed = 3f;
        const float chaseSpeed = 5f;
        const float bloodHuntRange = 50f;
        const float huntingRange = 25f;
        const float damage = 35f;

        // Config Values
        float playerBloodSenseRange = 50f;
        float maskedBloodSenseRange = 50f;
        

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
            StartCoroutine(DelayedStart());
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);

            if (Instance != null && NetworkObject.IsSpawned)
            {
                if (IsServerOrHost)
                {
                    logger.LogDebug("There is already a SCP-323-1 in the scene. Removing this one.");
                    NetworkObject.Despawn(true);
                }
            }
            else
            {
                Instance = this;

                SetOutsideOrInside();
                //SetEnemyOutsideClientRpc(true);
                //debugEnemyAI = true;

                currentBehaviourStateIndex = (int)State.Roaming;
                RoundManager.Instance.SpawnedEnemies.Add(this);
                StartSearch(transform.position);
                StartCoroutine(DecayHealthCoroutine());

                // TODO: Do black smoke here
                logger.LogDebug("Finished spawning SCP-323-1");
            }
        }

        public override void Update()
        {
            base.Update();

            CalculateAgentSpeed();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceInLOS += Time.deltaTime;
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
                case (int)State.Roaming:
                    if (currentSearch == null)
                    {
                        StartSearch(transform.position);
                    }
                    if (FoundClosestPlayerInRange(50f, 5f) || FoundClosestMaskedInRange(50f, 15f))
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.Hunting);

                    }

                    break;

                case (int)State.BloodSearch:
                    if (currentSearch != null)
                    {
                        StopSearch(currentSearch);
                    }
                    if (TargetPlayerInBloodSearch())
                    {
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        if (CheckLineOfSightForPosition(targetPlayer.transform.position, 70f, 60, 10))
                        {
                            SwitchToBehaviourClientRpc((int)State.Hunting);
                            Roar();
                            timeSinceInLOS = 0f;
                        }
                        return;
                    }
                    else if (TargetMaskedInBloodSearch())
                    {
                        SetDestinationToPosition(targetEnemy.transform.position);
                        if (CheckLineOfSightForPosition(targetEnemy.transform.position, 70f, 60, 10))
                        {
                            SwitchToBehaviourClientRpc((int)State.Hunting);
                            Roar();
                            timeSinceInLOS = 0f;
                        }
                        return;
                    }

                    SwitchToBehaviourClientRpc((int)State.Roaming);

                    break;

                case (int)State.Hunting:
                    if (targetPlayer != null && FoundClosestPlayerInRange(50f, 5f))
                    {
                        timeSinceInLOS = 0f;
                    }
                    break;

                case (int)State.Eating:

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        void CalculateAgentSpeed()
        {
            decayMultiplier = enemyHP / 20f;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation || inSpecialAnimationWithPlayer != null)
            {
                agent.speed = 0f;
                return;
            }

            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                agent.speed = roamSpeed * decayMultiplier;
                creatureAnimator.SetBool("run", false);
            }
            else
            {
                agent.speed = chaseSpeed * decayMultiplier;
                creatureAnimator.SetBool("run", true);
            }
        }

        IEnumerator DecayHealthCoroutine()
        {
            while (!isEnemyDead)
            {
                yield return new WaitForSecondsRealtime(20);
                
                if (!isEnemyDead)
                {
                    if (decayHealth)
                    {
                        enemyHP -= 1;
                    }

                    if (enemyHP <= 0)
                    {
                        if (IsOwner)
                        {
                            logger.LogDebug("SCP-323-1 ran out of health from decay");
                            KillEnemyOnOwnerClient();
                        }
                    }
                }
            }
        }

        public bool TargetPlayerInBloodSearch()
        {
            if (targetPlayer != null && Vector3.Distance(targetPlayer.transform.position, transform.position) <= playerBloodSenseRange)
            {
                return true;
            }

            return false;
        }

        public bool TargetMaskedInBloodSearch()
        {
            if (targetEnemy != null && Vector3.Distance(targetEnemy.transform.position, transform.position) <= maskedBloodSenseRange)
            {
                return true;
            }

            return false;
        }

        public bool TargetClosestEnemy(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = 2000f;
            EnemyAI? previousTarget = targetEnemy;
            targetEnemy = null;
            foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
            {
                if (!isEnemyDead && enemy.enemyType.name == "MaskedPlayer")
                {
                    if (!PathIsIntersectedByLineOfSight(enemy.transform.position, calculatePathDistance: false, avoidLineOfSight: false) && (!requireLineOfSight || CheckLineOfSightForPosition(enemy.transform.position, viewWidth, 50)))
                    {
                        tempDist = Vector3.Distance(transform.position, enemy.transform.position);
                        if (tempDist < mostOptimalDistance)
                        {
                            mostOptimalDistance = tempDist;
                            targetEnemy = enemy;
                        }
                    }
                }
            }
            if (targetEnemy != null && bufferDistance > 0f && previousTarget != null && Mathf.Abs(mostOptimalDistance - Vector3.Distance(transform.position, previousTarget.transform.position)) < bufferDistance)
            {
                targetEnemy = previousTarget;
            }

            return targetEnemy != null;
        }

        bool FoundClosestMaskedInRange(float range, float senseRange)
        {
            TargetClosestEnemy(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetEnemy == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestEnemy(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetEnemy != null && Vector3.Distance(transform.position, targetEnemy.transform.position) < range;
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

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0) // TODO: Set this up
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
        }

        public override void ReachedNodeInSearch()
        {
            base.ReachedNodeInSearch();

            /*if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                int randomNum = UnityEngine.Random.Range(0, 100);
                if (randomNum < 10)
                {
                    //creatureVoice.PlayOneShot(warningRoarSFX, 1f);
                }
            }*/
        }

        public override void KillEnemy(bool destroy = false)
        {
            logger.LogDebug("Killed SCP-323-1");
            CancelSpecialAnimationWithPlayer();
            StopAllCoroutines();
            base.KillEnemy(false);
            SpawnSkullOnHead();
        }

        public void SpawnSkullOnHead()
        {
            if (IsServerOrHost)
            {
                GameObject skullObj = UnityEngine.Object.Instantiate(SCP323Prefab, SkullTransform.position, SkullTransform.rotation, RoundManager.Instance.spawnedScrapContainer);
                skullObj.GetComponent<NetworkObject>().Spawn();
            }
            StartCoroutine(WaitTillNetworkSpawn());
        }

        IEnumerator WaitTillNetworkSpawn()
        {
            yield return new WaitUntil(() => SCP323Behavior.Instance != null && SCP323Behavior.Instance.NetworkObject.IsSpawned);
            SCP323Behavior.Instance!.AttachedToWendigo = this;
            SCP323Behavior.Instance.parentObject = SkullTransform;
            SCP323Behavior.Instance.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            SCP323Behavior.Instance.MeshObj.SetActive(false);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1)
        {
            if (!isEnemyDead)
            {
                enemyHP -= force;
                if (enemyHP <= 0)
                {
                    if (IsOwner)
                    {
                        KillEnemyOnOwnerClient();
                    }
                    return;
                }

                if (playerWhoHit != null)
                {
                    targetPlayer = playerWhoHit;
                }
                else
                {
                    TargetNearestEnemyIfInRange();
                }
            }
        }

        void TargetNearestEnemyIfInRange()
        {
            foreach (var enemy in UnityEngine.Object.FindObjectsOfType<EnemyAI>())
            {
                if (Vector3.Distance(enemy.transform.position, transform.position) <= 5)
                {
                    targetEnemy = enemy;
                    break;
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null)
            {
                int damageAmount = (int)(damage * decayMultiplier);
                DoAnimationServerRpc("claw");
                player.DamagePlayer(damageAmount, true, true, CauseOfDeath.Mauling);
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null!)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (IsServerOrHost)
            {
                if (collidedEnemy.enemyType.name == "MaskedPlayer" || (targetEnemy != null && collidedEnemy == targetEnemy))
                {
                    if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation)
                    {
                        networkAnimator.SetTrigger("claw");
                        collidedEnemy.HitEnemy(2, null, true);
                    }
                }
            }
        }

        void Roar()
        {
            SetEnemyStunned(true, 1.5f);
            networkAnimator.SetTrigger("roar");
        }

        public void DoRoarSFX() // TODO: Set up animation so that it plays the same time as the sfx
        {
            creatureSFX.PlayOneShot(roarSFX, 1f);
        }

        public void DoRandomWalkSFX()
        {
            if (walkingSFX.Length > 0)
            {
                int randomNum = UnityEngine.Random.Range(0, walkingSFX.Length);
                creatureSFX.PlayOneShot(walkingSFX[randomNum], 1f);
            }
        }

        public void PlayerDamaged(PlayerControllerB player)
        {
            if (Vector3.Distance(player.transform.position, transform.position) <= playerBloodSenseRange && !inSpecialAnimation)
            {
                if (targetPlayer != null && targetPlayer != player)
                {
                    if (targetPlayer.health < player.health)
                    {
                        return;
                    }
                }

                targetPlayer = player;
                SwitchToBehaviourClientRpc((int)State.BloodSearch);
            }
        }

        public void MaskedDamaged(MaskedPlayerEnemy masked)
        {
            if (Vector3.Distance(masked.transform.position, transform.position) <= maskedBloodSenseRange && !inSpecialAnimation)
            {
                if (targetPlayer != null)
                {
                    return;
                }

                targetEnemy = masked;
                SwitchToBehaviourClientRpc((int)State.BloodSearch);
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
            return 1000;
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

        [ServerRpc(RequireOwnership = false)]
        void DoAnimationServerRpc(string animationName)
        {
            if (IsServerOrHost)
            {
                networkAnimator.SetTrigger(animationName);
            }
        }

        [ClientRpc]
        void DoAnimationClientRpc(string animationName, bool value)
        {
            creatureAnimator.SetBool(animationName, value);
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayerDamagedServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                PlayerControllerB player = NetworkHandlerHeavy.PlayerFromId(clientId);
                PlayerDamaged(player);
            }
        }
    }

    [HarmonyPatch]
    internal class SCP3231Patches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.SpawnEnemyFromVent))]
        public static bool SpawnEnemyFromVentPrefix(EnemyVent vent)
        {
            try
            {
                if (IsServerOrHost)
                {
                    int index = vent.enemyTypeIndex;
                    if (index < 0) { return true; }
                    SpawnableEnemyWithRarity enemy = RoundManager.Instance.currentLevel.Enemies[index];
                    if (enemy != null && enemy.enemyType.name == "SCP323_1Enemy")
                    {
                        if (SCP323Behavior.Instance != null)
                        {
                            return false;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return true;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer))]
        public static void DamagePlayerPostfix(PlayerControllerB __instance)
        {
            try
            {
                if (SCP323_1AI.Instance != null)
                {
                    if (IsServerOrHost)
                    {
                        SCP323_1AI.Instance.PlayerDamaged(__instance);
                    }
                    else
                    {
                        SCP323_1AI.Instance.PlayerDamagedServerRpc(__instance.actualClientId);
                    }
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MaskedPlayerEnemy), nameof(MaskedPlayerEnemy.HitEnemy))]
        public static void HitEnemyPostfix(MaskedPlayerEnemy __instance)
        {
            try
            {
                if (SCP323_1AI.Instance != null)
                {
                    if (IsServerOrHost)
                    {
                        SCP323_1AI.Instance.MaskedDamaged(__instance);
                    }
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return;
            }
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity