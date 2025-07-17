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
        public AudioClip doorWooshSFX;
        public AudioClip metalDoorSmashSFX;
        public AudioClip bashSFX;
        public AudioClip roarSFX;
        public AudioClip slashSFX;
        public AudioClip[] walkingSFX;
        public AudioClip[] growlSFX;
        public AudioClip roamingNoisesSFX; // TODO: Set up
        public AudioClip eatingCorpseSFX;
        public GameObject SCP323Prefab;
        public Transform SkullTransform;
        public DoorCollisionDetect doorCollisionDetectScript;

        DeadBodyInfo targetPlayerCorpse;
        EnemyAI? targetEnemyCorpse;
        DoorLock? doorLock;
        Coroutine? roamCoroutine;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        PlayerControllerB targetPlayer;
        EnemyAI? targetEnemy;
        EnemyAI? lastEnemyAttackedMe;

        float timeSinceDamage;
        float timeSinceSeenPlayer;
        float timeSpawned;

        bool decayHealth = true;
        public float decayMultiplier = 1f;
        //bool growlPlayed = false;

        Vector3 targetPlayerLastSeenPos;

        // Constants
        const string maskedName = "MaskedPlayerEnemy";
        const float huntingRange = 50f;
        const int minHPToDoMaxDamage = 5;

        // Config Values
        float playerBloodSenseRange => config3231PlayerBloodSenseRange.Value;
        float maskedBloodSenseRange => config3231MaskedBloodSenseRange.Value;
        float doorBashForce => config3231DoorBashForce.Value;
        float doorBashDamage => config3231DoorBashDamage.Value;
        int doorBashAOEDamage => config3231DoorBashAOEDamage.Value;
        float doorBashAOERange => config3231DoorBashAOERange.Value;
        bool despawnDoorAfterBash => config3231DespawnDoorAfterBash.Value;
        float despawnDoorAfterBashTime => config3231DespawnDoorAfterBashTime.Value;
        int maxHP => config3231MaxHP.Value;
        int maxDamage => config3231MaxDamage.Value;
        int minDamage => config3231MinDamage.Value;
        bool reverseDamage => config3231ReverseDamage.Value;
        float chaseMaxSpeed => config3231ChaseMaxSpeed.Value;
        float roamMaxSpeed => config3231RoamMaxSpeed.Value;
        float roamMinSpeed => config3231RoamMinSpeed.Value;
        float timeToLosePlayer => config3231TimeToLosePlayer.Value;
        float searchAfterLosePlayerTime => config3231SearchAfterLosePlayerTime.Value;

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
            logger.LogDebug("SCP-323-1 Spawned");

            SetOutsideOrInside();

            timeSinceSeenPlayer = Mathf.Infinity;
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
            timeSpawned += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 50f))
            {
                localPlayer.insanityLevel += Time.deltaTime;
                localPlayer.IncreaseFearLevelOverTime(0.1f, 0.5f);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            //logger.LogDebug("Doing AI Interval");

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation)
            {
                return;
            };

            if (currentBehaviourStateIndex != (int)State.Eating)
            {
                CheckForCorpseToEat();
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    if (timeSpawned < 5f) { return; }
                    if (TargetClosestPlayerOrMasked())
                    {
                        SwitchToBehaviourClientRpc((int)State.Hunting);
                        if (targetPlayer != null) { targetPlayerLastSeenPos = targetPlayer.transform.position; }
                        Roar();
                    }

                    break;

                case (int)State.BloodSearch:

                    if (TargetPlayerInBloodSearch())
                    {
                        SetDestinationToPosition(targetPlayer.transform.position);
                        if (CheckLineOfSightForPosition(targetPlayer.transform.position, 70f, 60, 10))
                        {
                            SwitchToBehaviourClientRpc((int)State.Hunting);
                            timeSinceSeenPlayer = 0f;
                            Roar();
                        }
                        return;
                    }
                    else if (TargetMaskedInBloodSearch())
                    {
                        SetDestinationToPosition(targetEnemy.transform.position);
                        if (CheckLineOfSightForPosition(targetEnemy.transform.position, 70f, 60, 10))
                        {
                            SwitchToBehaviourClientRpc((int)State.Hunting);
                            timeSinceSeenPlayer = 0f;
                            Roar();
                        }
                        return;
                    }

                    SwitchToBehaviourClientRpc((int)State.Roaming);

                    break;

                case (int)State.Hunting:

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
                        if (timeSinceSeenPlayer < timeToLosePlayer || Vector3.Distance(targetPlayer.transform.position, transform.position) >= huntingRange)
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

        public void RoamStop()
        {
            if (!IsServerOrHost) { return; }
            StopSearch(currentSearch);
            if (roamCoroutine != null)
            {
                StopCoroutine(roamCoroutine);
                roamCoroutine = null;
            }
            agent.ResetPath();
        }

        public void RoamStart()
        {
            if (!IsServerOrHost) { return; }
            StopSearch(currentSearch);

            if (roamCoroutine != null)
            {
                StopCoroutine(roamCoroutine);
            }

            roamCoroutine = StartCoroutine(RoamCoroutine());
        }

        public IEnumerator RoamCoroutine()
        {
            yield return null;
            if (allAINodes == null || allAINodes.Length == 0)
            {
                logger.LogError("allAINodes is null or empty");
                yield break;
            }

            logger.LogDebug("Starting roam coroutine...");
            while (roamCoroutine != null)
            {
                targetNode = Utils.GetRandomNode(isOutside)?.transform;
                if (targetNode == null) { yield break; }
                yield return null;

                logger.LogDebug("Setting destination to position");
                while (SetDestinationToPosition(targetNode.position, checkForPath: true) && Vector3.Distance(transform.position, targetNode.position) > 1f)
                {
                    yield return new WaitForSeconds(AIIntervalTime);
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
                    targetPlayer = null;
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
                targetPlayer = null;
            }

            return true;
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

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = Utils.outsideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;
            GameObject closestInsideNode = Utils.insideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;

            bool outside = Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position);
            logger.LogDebug("Setting enemy outside: " + outside.ToString());
            SetEnemyOutsideClientRpc(true);
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
            if (distance < 2)
            {
                HitEnemy(20);
            }
            else if (distance < 3)
            {
                HitEnemy(15);
            }
            else if (distance < 5)
            {
                HitEnemy(19);
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
            if (timeSinceDamage <= 1f) { return; }
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && player.isPlayerControlled && !inSpecialAnimation && !isEnemyDead)
            {
                timeSinceDamage = 0f;
                int damageAmount = GetPlayerDamage();
                logger.LogDebug("Doing " + damageAmount + " damage to player");
                DoAnimationServerRpc("claw");
                player.DamagePlayer(damageAmount, true, true, CauseOfDeath.Mauling);
            }
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

        void Roar()
        {
            if (timeSinceSeenPlayer > 15f)
            {
                timeSinceSeenPlayer = 0f;
                networkAnimator.SetTrigger("roar");
            }
        }

        public void BeginBashDoor(DoorLock _doorLock)
        {
            logger.LogDebug("BeginBashDoor called");
            inSpecialAnimation = true;
            doorLock = _doorLock;
            creatureAnimator.SetTrigger("punch");
        }

        public void BashDoor()
        {
            DoDamageToNearbyPlayers();

            var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
            var doorMesh = steelDoorObj.transform.Find("DoorMesh").gameObject;

            GameObject flyingDoorPrefab = new GameObject("FlyingDoor");
            BoxCollider tempCollider = flyingDoorPrefab.AddComponent<BoxCollider>();
            tempCollider.isTrigger = true;
            tempCollider.size = new Vector3(1f, 1.5f, 3f);

            flyingDoorPrefab.AddComponent<DoorPlayerCollisionDetect>();

            AudioSource tempAS = flyingDoorPrefab.AddComponent<AudioSource>();
            tempAS.spatialBlend = 1;
            tempAS.maxDistance = 60;
            tempAS.rolloffMode = AudioRolloffMode.Linear;
            tempAS.volume = 1f;

            var flyingDoor = UnityEngine.Object.Instantiate(flyingDoorPrefab, doorLock.transform.position, doorLock.transform.rotation);
            doorMesh.transform.SetParent(flyingDoor.transform);

            GameObject.Destroy(flyingDoorPrefab);

            Rigidbody rb = flyingDoor.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.useGravity = true;
            rb.isKinematic = true;

            // Determine which direction to apply the force
            Vector3 doorForward = flyingDoor.transform.position + flyingDoor.transform.right * 2f;
            Vector3 doorBackward = flyingDoor.transform.position - flyingDoor.transform.right * 2f;
            Vector3 direction;

            if (Vector3.Distance(doorForward, transform.position) < Vector3.Distance(doorBackward, transform.position))
            {
                // Wendigo is at front of door
                direction = (doorBackward - doorForward).normalized;
                flyingDoor.transform.position = flyingDoor.transform.position - flyingDoor.transform.right;
            }
            else
            {
                // Wendigo is at back of door
                direction = (doorForward - doorBackward).normalized;
                flyingDoor.transform.position = flyingDoor.transform.position + flyingDoor.transform.right;
            }

            Vector3 upDirection = transform.TransformDirection(Vector3.up).normalized * 0.1f;
            Vector3 playerHitDirection = (direction + upDirection).normalized;
            flyingDoor.GetComponent<DoorPlayerCollisionDetect>().force = playerHitDirection * doorBashForce;

            // Release the Rigidbody from kinematic state
            rb.isKinematic = false;

            // Add an impulse force to the door
            rb.AddForce(direction * doorBashForce, ForceMode.Impulse);

            AudioSource doorAudio = flyingDoor.GetComponent<AudioSource>();
            doorAudio.PlayOneShot(bashSFX, 1f);

            string flowType = RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name;
            if (flowType == "Level1Flow" || flowType == "Level1FlowExtraLarge" || flowType == "Level1Flow3Exits" || flowType == "Level3Flow")
            {
                doorAudio.PlayOneShot(metalDoorSmashSFX, 0.8f);
            }

            doorAudio.PlayOneShot(doorWooshSFX, 1f);

            doorCollisionDetectScript.triggering = false;
            doorLock = null!;
            inSpecialAnimation = false;

            if (despawnDoorAfterBash)
            {
                Destroy(flyingDoor, despawnDoorAfterBashTime);
            }
        } // TODO: Add explosion damage

        /*[Debug  :HeavyItemSCPs] Level1Flow
        [Debug  :HeavyItemSCPs] Level2Flow
        [Debug  :HeavyItemSCPs] Level1FlowExtraLarge
        [Debug  :HeavyItemSCPs] Level1Flow3Exits
        [Debug  :HeavyItemSCPs] Level3Flow*/

        void DoDamageToNearbyPlayers()
        {
            foreach(var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (Vector3.Distance(player.transform.position, transform.position) <= doorBashAOERange)
                {
                    player.DamagePlayer(doorBashAOEDamage, true, true, CauseOfDeath.Blast, 8);
                }
            }
        }

        /*public void PlayerDamaged(PlayerControllerB player)
        {
            if (targetPlayer != null && targetPlayer == player) { return; }
            if (targetPlayer == null || (targetPlayer.health > player.health) || player.bleedingHeavily)
            {
                if (!inSpecialAnimation)
                {
                    if (player.bleedingHeavily)
                    {
                        if ((player.isInsideFactory && !isOutside) || (!player.isInsideFactory && isOutside))
                        {
                            targetPlayer = player;
                            SwitchToBehaviourClientRpc((int)State.BloodSearch);
                            return;
                        }
                    }
                    if (Vector3.Distance(player.transform.position, transform.position) <= playerBloodSenseRange)
                    {
                        targetPlayer = player;
                        SwitchToBehaviourClientRpc((int)State.BloodSearch);
                    }
                }
            }
        }*/

        public void MaskedDamaged(MaskedPlayerEnemy masked)
        {
            if (targetEnemy != null && targetEnemy == masked) { return; }
            if (targetEnemy == null || (targetEnemy.enemyHP > masked.enemyHP))
            {
                if (Vector3.Distance(masked.transform.position, transform.position) <= maskedBloodSenseRange && !inSpecialAnimation)
                {
                    targetEnemy = masked;
                    SwitchToBehaviourClientRpc((int)State.BloodSearch);
                }
            }
        }

        // Animation stuff

        public void SetInSpecialAnimationTrue()
        {
            inSpecialAnimation = true;
        }

        public void SetInSpecialAnimationFalse()
        {
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

        public void DoRandomWalkSFX()
        {
            if (currentBehaviourStateIndex == (int)State.BloodSearch || walkingSFX.Length == 0) { return; }
            float volume = currentBehaviourStateIndex == (int)State.Roaming ? 0.3f : 1f;
            RoundManager.PlayRandomClip(creatureSFX, walkingSFX, true, volume);
        }

        public void StartEatingAnimation()
        {
            float randSoundTime = UnityEngine.Random.Range(0f, 19f);
            creatureSFX.clip = eatingCorpseSFX;
            creatureSFX.time = randSoundTime;
            creatureSFX.Play();
        }

        public void FinishEatingAnimation()
        {
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

        /*[ServerRpc(RequireOwnership = false)]
        public void PlayerDamagedServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                PlayerControllerB player = PlayerFromId(clientId);
                PlayerDamaged(player);
            }
        }*/

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

        /*[HarmonyPostfix]
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
        }*/ // TODO: May be unneeded

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