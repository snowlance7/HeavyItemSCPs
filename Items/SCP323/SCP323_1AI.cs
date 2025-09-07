using BepInEx.Logging;
using GameNetcodeStuff;
using HandyCollections.Heap;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using static Netcode.Transports.Facepunch.FacepunchTransport;

namespace HeavyItemSCPs.Items.SCP323
{
    internal class SCP323_1AI : EnemyAI, IVisibleThreat // TODO: Make it so he targets masked if hes really low on hunger so he can heal and chase the player faster
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP323_1AI? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public NetworkAnimator networkAnimator;
        public AudioClip roarSFX;
        public AudioClip slashSFX;
        public AudioClip[] growlSFX;
        public AudioClip roamingNoisesSFX; // TODO: Set up
        public AudioClip eatingCorpseSFX;
        public GameObject SCP323Prefab;
        public Transform SkullTransform;
        public DoorCollisionDetect doorCollisionDetectScript;

        DeadBodyInfo? targetPlayerCorpse;
        EnemyAI? targetEnemyCorpse;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        EnemyAI? targetEnemy;
        EnemyAI? lastEnemyAttackedMe;

        bool enraged;

        Vector3 bloodSearchPosition = Vector3.zero;

        float timeSinceDamage;
        float timeSinceSeenPlayer;

        readonly bool decayHealth = true;
        public float decayMultiplier = 1f;
        //bool growlPlayed = false;

        Vector3 targetPlayerLastSeenPos;

        int currentFootstepSurfaceIndex;
        int previousFootstepClip;

        // Constants
        const string maskedName = "MaskedPlayerEnemy";
        const float huntingRange = 50f;
        const int minHPToDoMaxDamage = 5;

        // Config Values
        int maxHP => config3231MaxHP.Value;
        int maxDamage => config3231MaxDamage.Value;
        int minDamage => config3231MinDamage.Value;
        bool reverseDamage => config3231ReverseDamage.Value;
        float chaseMaxSpeed => config3231ChaseMaxSpeed.Value;
        float roamMaxSpeed => config3231RoamMaxSpeed.Value;
        float roamMinSpeed => config3231RoamMinSpeed.Value;
        float timeToLosePlayer => config3231TimeToLosePlayer.Value;
        float searchAfterLosePlayerTime => config3231SearchAfterLosePlayerTime.Value;
        float playerBloodSenseRange => config3231PlayerBloodSenseRange.Value;
        float maskedBloodSenseRange => config3231MaskedBloodSenseRange.Value;

        public enum State
        {
            Roaming,
            BloodSearch,
            Hunting,
            Eating
        }

        public override void Start()
        {
            base.Start();

            SetOutsideOrInside();

            timeSinceSeenPlayer = Mathf.Infinity;

            logger.LogDebug("SCP-323-1 Spawned");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-323-1 in the scene. Removing this one.");
                if (!IsServerOrHost) { return; }
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-323-1");

            if (!IsServerOrHost) { return; }

            inSpecialAnimation = true;
            StartCoroutine(DelayedStart(5f));
            StartCoroutine(DecayHealthCoroutine());
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            CalculateAgentSpeed();

            timeSinceDamage += Time.deltaTime;
            timeSinceSeenPlayer += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 50f))
            {
                localPlayer.insanityLevel += Time.deltaTime;
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);
            }

            if (IsServerOrHost && targetPlayerCorpse != null && targetPlayerCorpse.grabBodyObject.playerHeldBy != null)
            {
                targetPlayer = targetPlayerCorpse.grabBodyObject.playerHeldBy;
                targetPlayerCorpse = null;
                SetEnragedClientRpc(true);
                creatureSFX.Stop();
                Roar(true);
                SwitchToBehaviourClientRpc((int)State.Hunting);
                return;
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            //logger.LogDebug("Doing AI Interval");

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation)
            {
                //logger.LogDebug("Skipping DoAIInterval");
                return;
            };

            if (currentBehaviourStateIndex != (int)State.Eating)
            {
                CheckForCorpseToEat();
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    if (TargetClosestPlayerOrMasked())
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.Hunting);
                        if (targetPlayer != null) { targetPlayerLastSeenPos = targetPlayer.transform.position; }
                        Roar();
                        return;
                    }

                    if (currentSearch == null || !currentSearch.inProgress)
                    {
                        logger.LogDebug("Starting search");
                        StartSearch(transform.position);
                    }

                    break;

                case (int)State.BloodSearch:
                    if (TargetClosestPlayerOrMasked())
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.Hunting);
                        if (targetPlayer != null) { targetPlayerLastSeenPos = targetPlayer.transform.position; }
                        Roar();
                        return;
                    }

                    if (Vector3.Distance(transform.position, bloodSearchPosition) < 1f || !SetDestinationToPosition(bloodSearchPosition, true))
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    break;

                case (int)State.Hunting:

                    if (enraged)
                    {
                        if (!SetDestinationToPosition(targetPlayer.transform.position, true))
                        {
                            if (enraged) { SetEnragedClientRpc(false); }
                        }
                        return;
                    }

                    if (!TargetClosestPlayerOrMasked())
                    {
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    if (targetPlayer != null && targetPlayer.isPlayerControlled)
                    {
                        if (CheckLineOfSightForPosition(targetPlayer.transform.position, 70f, 60, 10))
                        {
                            StopSearch(currentSearch);

                            if (timeSinceSeenPlayer > 1.5f)
                            {
                                PlayGrowlSFXClientRpc();
                            }

                            timeSinceSeenPlayer = 0f;
                            targetPlayerLastSeenPos = targetPlayer.transform.position;
                        }
                        if (timeSinceSeenPlayer < timeToLosePlayer || Vector3.Distance(targetPlayer.transform.position, transform.position) <= huntingRange)
                        {
                            SetDestinationToPosition(targetPlayer.transform.position);
                            return;
                        }
                        else
                        {
                            if (currentSearch == null) { StartSearch(targetPlayerLastSeenPos); }
                            if (timeSinceSeenPlayer < searchAfterLosePlayerTime)
                            {
                                return;
                            }
                        }
                    }
                    if (targetEnemy != null && !targetEnemy.isEnemyDead)
                    {
                        if (Vector3.Distance(targetEnemy.transform.position, transform.position) <= huntingRange)
                        {
                            SetDestinationToPosition(targetEnemy.transform.position);
                            return;
                        }
                    }

                    SwitchToBehaviourClientRpc((int)State.Roaming);

                    break;

                case (int)State.Eating:

                    if (targetPlayerCorpse != null && targetPlayerCorpse.grabBodyObject != null && !targetPlayerCorpse.grabBodyObject.isHeld && !targetPlayerCorpse.isInShip)
                    {
                        if (!inSpecialAnimation && Vector3.Distance(targetPlayerCorpse.grabBodyObject.transform.position, transform.position) <= 1f)
                        {
                            inSpecialAnimation = true;
                            logger.LogDebug($"Eating player corpse of player {targetPlayerCorpse.playerScript.playerUsername} with client id {targetPlayerCorpse.playerScript.actualClientId}");
                            EatPlayerBodyClientRpc(targetPlayerCorpse.playerScript.actualClientId);
                            return;
                        }

                        SetDestinationToPosition(targetPlayerCorpse.grabBodyObject.transform.position);

                        return;
                    }
                    else if (targetEnemyCorpse != null && targetEnemyCorpse.isEnemyDead)
                    {
                        if (!inSpecialAnimation && Vector3.Distance(targetEnemyCorpse.transform.position, transform.position) <= 1f)
                        {
                            networkAnimator.SetTrigger("eat");
                            return;
                        }

                        SetDestinationToPosition(targetEnemyCorpse.transform.position);

                        return;
                    }
                    
                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        void CalculateAgentSpeed()
        {
            decayMultiplier = (float)enemyHP / (float)maxHP;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation)
            {
                agent.speed = 0f;
                return;
            }

            if (currentBehaviourStateIndex == (int)State.Roaming || decayMultiplier < 0.5f)
            {
                float newSpeed = roamMaxSpeed * decayMultiplier;
                agent.speed = Mathf.Clamp(newSpeed, roamMinSpeed, roamMaxSpeed);
                creatureAnimator.SetBool("run", false);
            }
            else
            {
                float newSpeed = chaseMaxSpeed * decayMultiplier;
                agent.speed = Mathf.Clamp(newSpeed, roamMaxSpeed, chaseMaxSpeed);
                creatureAnimator.SetBool("run", true);
            }
        }

        IEnumerator DelayedStart(float timeToStart)
        {
            yield return null;
            yield return new WaitForSeconds(timeToStart);
            inSpecialAnimation = false;
            networkAnimator.SetTrigger("start");
        }

        IEnumerator DecayHealthCoroutine()
        {
            while (!isEnemyDead)
            {
                yield return new WaitForSecondsRealtime(20);
                
                if (!isEnemyDead)
                {
                    if (currentBehaviourStateIndex != (int)State.Eating)
                    {
                        logger.LogDebug("Decaying 1 hp");
                        enemyHP -= 1;
                    }

                    if (enemyHP <= 0)
                    {
                        logger.LogDebug("SCP-323-1 ran out of health from decay");
                        KillEnemyOnOwnerClient();
                    }
                }
            }
        }

        void CheckForCorpseToEat()
        {
            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.isPlayerDead
                    && player.deadBody != null
                    && player.deadBody.grabBodyObject != null
                    && !player.deadBody.grabBodyObject.isHeld
                    && !player.deadBody.isInShip
                    && !player.deadBody.deactivated)
                {
                    if (Vector3.Distance(player.deadBody.grabBodyObject.transform.position, transform.position) <= playerBloodSenseRange * 2f)
                    {
                        if (enraged) { SetEnragedClientRpc(false); }
                        targetPlayerCorpse = player.deadBody;
                        SwitchToBehaviourClientRpc((int)State.Eating);
                        return;
                    }
                }
            }

            foreach (var masked in UnityEngine.Object.FindObjectsOfType<MaskedPlayerEnemy>())
            {
                if (masked.isEnemyDead && Vector3.Distance(masked.transform.position, transform.position) <= maskedBloodSenseRange * 2f)
                {
                    if (enraged) { SetEnragedClientRpc(false); }
                    targetEnemyCorpse = masked;
                    SwitchToBehaviourClientRpc((int)State.Eating);
                    return;
                }
            }
        }

        public bool TargetClosestMasked(float bufferDistance = 1.5f, bool requireLineOfSight = false, float viewWidth = 70f)
        {
            mostOptimalDistance = 2000f;
            EnemyAI? previousTarget = targetEnemy;
            targetEnemy = null!;
            foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
            {
                if (enemy.isEnemyDead) { continue; }
                if (enemy.enemyType.name == maskedName)
                {
                    if (!PathIsIntersectedByLineOfSight(enemy.transform.position, calculatePathDistance: false, avoidLineOfSight: false) && (!requireLineOfSight || CheckLineOfSightForPosition(enemy.transform.position, viewWidth, 60, 10f)))
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
            TargetClosestMasked(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetEnemy == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestMasked(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetEnemy != null && Vector3.Distance(transform.position, targetEnemy.transform.position) < range;
        }

        bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            if (Utils.disableTargetting) { return false; }
            TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: true);
            if (targetPlayer == null)
            {
                // Couldn't see a player, so we check if a player is in sensing distance instead
                TargetClosestPlayer(bufferDistance: 1.5f, requireLineOfSight: false);
                range = senseRange;
            }
            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        bool TargetClosestPlayerOrMasked()
        {
            bool foundMask = FoundClosestMaskedInRange(50f, 15f);
            bool foundPlayer = FoundClosestPlayerInRange(50f, 5f);

            if (!foundMask && !foundPlayer)
                return false;

            if (foundMask && foundPlayer)
            {
                float playerDistance = Vector3.Distance(transform.position, targetPlayer.transform.position);
                float maskDistance = Vector3.Distance(transform.position, targetEnemy!.transform.position);

                if (playerDistance < maskDistance)
                {
                    targetEnemy = null;
                }
                else
                {
                    targetPlayer = null!;
                }

                return true;
            }

            // Only one was found
            if (foundPlayer)
            {
                targetEnemy = null;
            }
            else
            {
                targetPlayer = null!;
            }

            return true;
        }

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = Utils.outsideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;
            GameObject closestInsideNode = Utils.insideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;

            bool outside = Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position);
            logger.LogDebug("Setting enemy outside: " + outside.ToString());
            SetEnemyOutsideClientRpc(outside);
        }

        public override void KillEnemy(bool destroy = false)
        {
            if (!inSpecialAnimation && currentBehaviourStateIndex != (int)State.Eating)
            {
                logger.LogDebug("Killed SCP-323-1");
                CancelSpecialAnimationWithPlayer();
                StopAllCoroutines();
                creatureVoice.Stop();
                creatureSFX.Stop();
                base.KillEnemy(false);
                SpawnSkullOnHead();
            }
        }

        public void SpawnSkullOnHead()
        {
            if (IsServerOrHost)
            {
                GameObject skullObj = UnityEngine.Object.Instantiate(SCP323Prefab, SkullTransform.position, SkullTransform.rotation, RoundManager.Instance.spawnedScrapContainer);
                skullObj.GetComponent<NetworkObject>().Spawn();
            }

            IEnumerator WaitTillNetworkSpawn()
            {
                yield return new WaitUntil(() => SCP323Behavior.Instance != null && SCP323Behavior.Instance.NetworkObject.IsSpawned);
                SCP323Behavior.Instance!.AttachedToWendigo = this;
                SCP323Behavior.Instance.parentObject = SkullTransform;
                SCP323Behavior.Instance.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            }

            StartCoroutine(WaitTillNetworkSpawn());
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null!, bool playHitSFX = true, int hitID = -1)
        {
            if (!isEnemyDead && currentBehaviourStateIndex != (int)State.Eating && !inSpecialAnimation)
            {
                enemyHP -= force;
                if (enemyHP <= 0)
                {
                    KillEnemyOnOwnerClient();
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

        public override void HitFromExplosion(float distance)
        {
            base.HitFromExplosion(distance);
            if (distance < 1)
            {
                HitEnemy(10);
            }
            else if (distance < 2)
            {
                HitEnemy(5);
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

        public double MapValue(double a0, double a1, double b0, double b1, double a) // Thanks Hamunii for this function
        {
            return b0 + (b1 - b0) * ((a - a0) / (a1 - a0));
        }

        int GetPlayerDamage()
        {
            if (reverseDamage)
            {
                // Calculate damage based on linear interpolation between max and min points
                //float damage = ((maxDamage - minDamage) / (minHPToDoMaxDamage - maxHP)) * (enemyHP - maxHP) + minDamage;
                float damage = (float)MapValue(minHPToDoMaxDamage, maxHP, maxDamage, minDamage, enemyHP);
                return (int)Mathf.Clamp(damage, minDamage, maxDamage);  // Ensure damage is within bounds
            }
            else
            {
                return (int)Mathf.Clamp(maxDamage * decayMultiplier, minDamage, maxDamage);
            }
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            if (Utils.disableTargetting) { return; }
            if (timeSinceDamage <= 1f) { return; }
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !player.isPlayerControlled || player != localPlayer || inSpecialAnimation || isEnemyDead) { return; }

            timeSinceDamage = 0f;
            int damageAmount = enraged ? 999 : GetPlayerDamage();
            logger.LogDebug("Doing " + damageAmount + " damage to player");
            DoAnimationServerRpc("claw");
            player.DamagePlayer(damageAmount, true, true, CauseOfDeath.Mauling);
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null!)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);
            if (!IsServerOrHost || timeSinceDamage <= 1f) { return; }

            if (collidedEnemy.enemyType.name == maskedName || (targetEnemy != null && collidedEnemy == targetEnemy))
            {
                if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation)
                {
                    timeSinceDamage = 0f;
                    networkAnimator.SetTrigger("claw");
                    collidedEnemy.HitEnemy(2, null, true);
                }
            }
        }

        void Roar(bool overrideTimer = false)
        {
            if (timeSinceSeenPlayer > 15f || overrideTimer)
            {
                timeSinceSeenPlayer = 0f;
                networkAnimator.SetTrigger("roar");
            }
        }

        public void MaskedDamaged(MaskedPlayerEnemy masked)
        {
            if (!IsServerOrHost) { return; }
            if (inSpecialAnimation) { return; }
            if (currentBehaviourStateIndex == (int)State.BloodSearch)
            {
                if (Vector3.Distance(masked.transform.position, transform.position) > Vector3.Distance(transform.position, bloodSearchPosition)) { return; }
                if (!SetDestinationToPosition(masked.transform.position, true)) { return; }
                bloodSearchPosition = masked.transform.position;
            }
            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                if (!SetDestinationToPosition(masked.transform.position, true)) { return; }
                bloodSearchPosition = masked.transform.position;
                SwitchToBehaviourClientRpc((int)State.BloodSearch);
            }
        }

        public void PlayerDamaged(PlayerControllerB player)
        {
            if (inSpecialAnimation) { return; }
            if (currentBehaviourStateIndex == (int)State.BloodSearch)
            {
                if (Vector3.Distance(player.transform.position, transform.position) > Vector3.Distance(transform.position, bloodSearchPosition)) { return; }
                if (!SetDestinationToPosition(player.transform.position, true)) { return; }
                bloodSearchPosition = player.transform.position;
            }
            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                if (!SetDestinationToPosition(player.transform.position, true)) { return; }
                bloodSearchPosition = player.transform.position;
                SwitchToBehaviourClientRpc((int)State.BloodSearch);
            }
        }

        // Animation stuff

        public void SetInSpecialAnimationTrue()
        {
            logger.LogDebug("SetInSpecialAnimationTrue");
            inSpecialAnimation = true;
        }

        public void SetInSpecialAnimationFalse()
        {
            logger.LogDebug("SetInSpecialAnimationFalse");
            inSpecialAnimation = false;
        }

        public void DoSlashSFX()
        {
            creatureSFX.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            creatureSFX.PlayOneShot(slashSFX, 1f);
        }

        public void DoRoarSFX()
        {
            creatureVoice.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            creatureVoice.PlayOneShot(roarSFX, 1f);
            if (Vector3.Distance(transform.position, localPlayer.transform.position) < 10f)
            {
                localPlayer.JumpToFearLevel(0.8f);
            }
        }

        public void StartEatingAnimation()
        {
            inSpecialAnimation = true;
            float randSoundTime = UnityEngine.Random.Range(0f, 19f);
            creatureSFX.clip = eatingCorpseSFX;
            creatureSFX.time = randSoundTime;
            creatureSFX.volume = 1f;
            creatureSFX.Play();
        }

        public void FinishEatingAnimation()
        {
            inSpecialAnimation = false;
            creatureSFX.Stop();
            enemyHP = maxHP;

            if (targetPlayerCorpse != null)
            {
                targetPlayerCorpse.DeactivateBody(setActive: false);
                targetPlayerCorpse = null;
            }
            if (targetEnemyCorpse != null)
            {
                RoundManager.Instance.DespawnEnemyOnServer(targetEnemyCorpse.NetworkObject);
                targetEnemyCorpse = null;
            }

            SwitchToBehaviourStateOnLocalClient((int)State.Roaming);
        }

        public void PlayFootstepSFX()
        {
            if (currentBehaviourStateIndex == (int)State.BloodSearch) { return; }

            GetCurrentMaterialStandingOn();
            int index = UnityEngine.Random.Range(0, StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length);
            if (index == previousFootstepClip)
            {
                index = (index + 1) % StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length;
            }
            creatureSFX.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            creatureSFX.PlayOneShot(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips[index], 0.6f);
            previousFootstepClip = index;
        }

        public void BashDoor()
        {
            doorCollisionDetectScript.BashDoor();
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

        #region IVisibleThreat
        // IVisibleThreat Interface
        public ThreatType type => ThreatType.Player;
        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }
        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            return 999999999;
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

        public GrabbableObject? GetHeldObject()
        {
            return null;
        }

        public bool IsThreatDead()
        {
            return isEnemyDead;
        }
        #endregion

        // RPC's

        [ClientRpc]
        public void SetEnragedClientRpc(bool value) => enraged = value;

        [ServerRpc(RequireOwnership = false)]
        public void PlayerDamagedServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
            PlayerDamaged(PlayerFromId(clientId));
        }

        [ClientRpc]
        void IncreaseFearOfNearbyPlayersClientRpc(float value, float distance)
        {
            if (Vector3.Distance(transform.position, localPlayer.transform.position) < distance)
            {
                localPlayer.JumpToFearLevel(value);
            }
        }

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }

        [ServerRpc(RequireOwnership = false)]
        void DoAnimationServerRpc(string animationName)
        {
            if (!IsServerOrHost) { return; }
            networkAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        void DoAnimationClientRpc(string animationName, bool value)
        {
            creatureAnimator.SetBool(animationName, value);
        }

        [ClientRpc]
        public void EatPlayerBodyClientRpc(ulong clientId)
        {
            targetPlayerCorpse = PlayerFromId(clientId).deadBody;
            targetPlayerCorpse.canBeGrabbedBackByPlayers = true;
            targetPlayerCorpse.grabBodyObject.grabbable = true;
            targetPlayerCorpse.grabBodyObject.grabbableToEnemies = false;

            creatureAnimator.SetTrigger("eat");
        }

        [ClientRpc]
        public void PlayGrowlSFXClientRpc()
        {
            RoundManager.PlayRandomClip(creatureSFX, growlSFX);
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
                if (!IsServerOrHost) { return true; }
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
            catch (System.Exception e)
            {
                logger.LogError(e);
                return true;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer))]
        public static void DamagePlayerPostfix(PlayerControllerB __instance, int damageNumber)
        {
            try
            {
                if (__instance.health >= 100)
                {
                    return;
                }
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
                if (!IsServerOrHost) { return; }
                SCP323_1AI.Instance?.MaskedDamaged(__instance);
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