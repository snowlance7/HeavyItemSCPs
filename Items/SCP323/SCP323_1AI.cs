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
        // TODO: Make sure he works on the new version
        // TODO: Increase his size
        // TODO: Fix bug where you dont transform if outside?? or in ship??
        private static ManualLogSource logger = LoggerInstance;
        public static SCP323_1AI? Instance { get; private set; }

#pragma warning disable 0649
        public NetworkAnimator networkAnimator = null!;
        public AudioClip doorWooshSFX = null!;
        public AudioClip metalDoorSmashSFX = null!;
        public AudioClip bashSFX = null!;
        public AudioClip roarSFX = null!;
        public AudioClip slashSFX = null!;
        public AudioClip[] walkingSFX = null!;
        public GameObject SCP323Prefab = null!;
        public Transform SkullTransform = null!;
        public DoorCollisionDetect doorCollisionDetectScript = null!;
#pragma warning restore 0649

        Vector3 forwardDirection;
        Vector3 upDirection;
        Vector3 throwDirection;

        EnemyAI targetEnemy = null!;
        EnemyAI lastEnemyAttackedMe = null!;

        float timeSinceDamagePlayer = 35f;
        float timeSinceSeenPlayer;
        float timeSpawned;

        bool decayHealth = true;
        float decayMultiplier = 1f;

        Vector3 targetPlayerLastSeenPos;
        DeadBodyInfo targetPlayerCorpse = null!;
        EnemyAI targetEnemyCorpse = null!;
        DoorLock doorLock = null!;

        // Constants
        const string maskedName = "MaskedPlayerEnemy";
        const float roamSpeed = 5f;
        const float chaseSpeed = 10f;
        const float bloodHuntRange = 50f;
        const float huntingRange = 25f;
        const float damage = 35f;

        // Config Values
        float playerBloodSenseRange = 50f;
        float maskedBloodSenseRange = 50f;
        float doorBashForce = 25f;
        int doorBashDamage = 30;
        int doorBashAOEDamage = 5;
        float doorBashAOERange = 5f;
        bool despawnDoorAfterBash = true;
        float despawnDoorAfterBashTime = 3f;

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

            playerBloodSenseRange = config3231PlayerBloodSenseRange.Value;
            maskedBloodSenseRange = config3231MaskedBloodSenseRange.Value;
            doorBashForce = config3231DoorBashForce.Value;
            doorBashDamage = config3231DoorBashDamage.Value;
            doorBashAOEDamage = config3231DoorBashAOEDamage.Value;
            doorBashAOERange = config3231DoorBashAOERange.Value;
            despawnDoorAfterBash = config3231DespawnDoorAfterBash.Value;
            despawnDoorAfterBashTime = config3231DespawnDoorAfterBashTime.Value;

            RoundManager.Instance.SpawnedEnemies.Add(this);
            SetOutsideOrInside();
            //SetEnemyOutsideClientRpc(true);
            //debugEnemyAI = true;

            timeSinceSeenPlayer = 50f;

            // TODO: Do black smoke here
            logger.LogDebug("Finished spawning SCP-323-1");
            if (IsServerOrHost) { StartCoroutine(DelayedStart()); }
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);

            if (Instance != null && NetworkObject.IsSpawned)
            {
                logger.LogDebug("There is already a SCP-323-1 in the scene. Removing this one.");
                NetworkObject.Despawn(true);
            }
            else
            {
                Instance = this;

                yield return new WaitForSeconds(2f);

                StartCoroutine(DecayHealthCoroutine());

                SwitchToBehaviourClientRpc((int)State.Roaming);
                networkAnimator.SetTrigger("start");
                StartSearch(transform.position);
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

            timeSinceDamagePlayer += Time.deltaTime;
            timeSinceSeenPlayer += Time.deltaTime;
            timeSpawned += Time.deltaTime;

            if (localPlayer.HasLineOfSightToPosition(transform.position, 70f))
            {
                localPlayer.insanityLevel += Time.deltaTime;
                localPlayer.IncreaseFearLevelOverTime();
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            //logger.LogDebug("Doing AI Interval");

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f)
            {
                return;
            };

            if (currentBehaviourStateIndex != (int)State.Eating)
            {
                CheckForCorpseToEat();
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Transforming:
                    agent.speed = 0f;
                    break;
                case (int)State.Roaming:
                    if (timeSpawned < 5f) { return; }
                    if (FoundClosestPlayerInRange(50f, 5f) || FoundClosestMaskedInRange(50f, 15f))
                    {
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.Hunting);
                        if (targetPlayer != null) { targetPlayerLastSeenPos = targetPlayer.transform.position; }
                        Roar();
                    }

                    break;

                case (int)State.BloodSearch:

                    if (TargetPlayerInBloodSearch()) // TODO: Rework this
                    {
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        if (CheckLineOfSightForPosition(targetPlayer.transform.position, 70f, 60, 10))
                        {
                            StopSearch(currentSearch);
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
                            StopSearch(currentSearch);
                            SwitchToBehaviourClientRpc((int)State.Hunting);
                            timeSinceSeenPlayer = 0f;
                            Roar();
                        }
                        return;
                    }

                    RestartSearch(transform.position);
                    SwitchToBehaviourClientRpc((int)State.Roaming);

                    break;

                case (int)State.Hunting:

                    if (targetPlayer != null && !targetPlayer.isPlayerDead)
                    {
                        if (CheckLineOfSightForPosition(targetPlayer.transform.position, 45f, 50, 10f))
                        {
                            timeSinceSeenPlayer = 0f;
                            targetPlayerLastSeenPos = targetPlayer.transform.position;
                        }
                        if (timeSinceSeenPlayer < 5f || Vector3.Distance(targetPlayer.transform.position, transform.position) > huntingRange)
                        {
                            SetMovingTowardsTargetPlayer(targetPlayer);
                            return;
                        }
                        RestartSearch(targetPlayerLastSeenPos);
                    }
                    if (targetEnemy != null && !targetEnemy.isEnemyDead)
                    {
                        if (Vector3.Distance(targetEnemy.transform.position, transform.position) <= huntingRange)
                        {
                            SetDestinationToPosition(targetEnemy.transform.position);
                            return;
                        }
                        RestartSearch(transform.position);
                    }

                    targetPlayer = null!;
                    targetEnemy = null!;
                    SwitchToBehaviourClientRpc((int)State.Roaming);

                    break;

                case (int)State.Eating:

                    if (targetPlayerCorpse != null && targetPlayerCorpse.grabBodyObject != null && !targetPlayerCorpse.grabBodyObject.isHeld && !targetPlayerCorpse.isInShip)
                    {
                        if (!inSpecialAnimation)
                        {
                            if (Vector3.Distance(targetPlayerCorpse.grabBodyObject.transform.position, transform.position) <= 3f)
                            {
                                inSpecialAnimation = true;
                                logger.LogDebug($"Eating player corpse of player {targetPlayerCorpse.playerScript.playerUsername} with client id {targetPlayerCorpse.playerScript.actualClientId}");
                                EatPlayerBodyClientRpc(targetPlayerCorpse.playerScript.actualClientId);
                                return;
                            }

                            SetDestinationToPosition(targetPlayerCorpse.grabBodyObject.transform.position);
                        }

                        return;
                    }
                    else if (targetEnemyCorpse != null && targetEnemyCorpse.isEnemyDead)
                    {
                        if (!inSpecialAnimation)
                        {
                            if (Vector3.Distance(targetEnemyCorpse.transform.position, transform.position) <= 3f)
                            {
                                inSpecialAnimation = true;
                                EatMaskedBodyClientRpc();
                                return;
                            }

                            SetDestinationToPosition(targetEnemyCorpse.transform.position);
                        }

                        return;
                    }
                    
                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        void RestartSearch(Vector3 position)
        {
            if (currentSearch != null)
            {
                StopSearch(currentSearch, true);
            }
            StartSearch(position);
        }

        void CalculateAgentSpeed()
        {
            decayMultiplier = enemyHP / 20f;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || stunNormalizedTimer > 0f || inSpecialAnimation || inSpecialAnimationWithPlayer != null)
            {
                agent.speed = 0f;
                return;
            }

            if ((currentBehaviourStateIndex == (int)State.Roaming && timeSinceSeenPlayer > 25f) || decayMultiplier < 0.5f)
            {
                float newSpeed = roamSpeed * decayMultiplier;
                agent.speed = Mathf.Clamp(newSpeed, 3f, roamSpeed);
                creatureAnimator.SetBool("run", false);
            }
            else
            {
                float newSpeed = chaseSpeed * decayMultiplier;
                agent.speed = Mathf.Clamp(newSpeed, roamSpeed, chaseSpeed);
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
                        if (IsOwner)
                        {
                            logger.LogDebug("SCP-323-1 ran out of health from decay");
                            KillEnemyOnOwnerClient();
                        }
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
                    if (Vector3.Distance(player.deadBody.grabBodyObject.transform.position, transform.position) <= 50f)
                    {
                        targetPlayerCorpse = player.deadBody;
                        targetPlayer = null!;
                        targetEnemy = null!;
                        StopSearch(currentSearch);
                        SwitchToBehaviourClientRpc((int)State.Eating);
                        return;
                    }
                }
            }
            foreach (var masked in UnityEngine.Object.FindObjectsOfType<MaskedPlayerEnemy>())
            {
                if (masked.isEnemyDead && Vector3.Distance(masked.transform.position, transform.position) <= 50f)
                {
                    targetEnemyCorpse = masked;
                    targetEnemy = null!;
                    targetPlayer = null!;
                    StopSearch(currentSearch);
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
            if (Vector3.Distance(transform.position, noisePosition) < huntingRange)
            {
                if (currentBehaviourStateIndex == (int)State.Roaming)
                {
                    RestartSearch(noisePosition);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            if (!inSpecialAnimation && currentBehaviourStateIndex != (int)State.Eating)
            {
                logger.LogDebug("Killed SCP-323-1");
                CancelSpecialAnimationWithPlayer();
                StopAllCoroutines();
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
            if (!isEnemyDead && currentBehaviourStateIndex != (int)State.Eating && !inSpecialAnimation)
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
            if (timeSinceDamagePlayer > 1f)
            {
                PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
                if (player != null && !player.isPlayerDead && !inSpecialAnimation && !isEnemyDead)
                {
                    timeSinceDamagePlayer = 0f;
                    int damageAmount = (int)(damage * decayMultiplier);
                    DoAnimationServerRpc("claw");
                    player.DamagePlayer(damageAmount, true, true, CauseOfDeath.Mauling);
                }
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null!)
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (IsServerOrHost)
            {
                if (timeSinceDamagePlayer > 1f)
                {
                    if (collidedEnemy.enemyType.name == maskedName || (targetEnemy != null && collidedEnemy == targetEnemy))
                    {
                        if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation)
                        {
                            timeSinceDamagePlayer = 0f;
                            networkAnimator.SetTrigger("claw");
                            collidedEnemy.HitEnemy(2, null, true);
                        }
                    }
                }
            }
        }

        void Roar()
        {
            if (timeSinceSeenPlayer > 15f)
            {
                timeSinceSeenPlayer = 0f;
                SetEnemyStunned(true);
            }
        }

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1, PlayerControllerB setStunnedByPlayer = null!)
        {
            base.SetEnemyStunned(setToStunned, 2.133f, setStunnedByPlayer);
            networkAnimator.SetTrigger("roar");
        }

        // Animation stuff
        public void DoSlashSFX()
        {
            creatureSFX.PlayOneShot(slashSFX, 1f);
        }

        public void DoRoarSFX()
        {
            creatureVoice.PlayOneShot(roarSFX, 1f);
            if (Vector3.Distance(transform.position, localPlayer.transform.position) < 10f)
            {
                localPlayer.JumpToFearLevel(0.7f);
            }
        }

        public void DoRandomWalkSFX()
        {
            if (currentBehaviourStateIndex != (int)State.BloodSearch)
            {
                if (walkingSFX.Length > 0)
                {
                    int randomNum = UnityEngine.Random.Range(0, walkingSFX.Length);
                    if (currentBehaviourStateIndex == (int)State.Roaming)
                    {
                        creatureSFX.PlayOneShot(walkingSFX[randomNum], 0.5f);
                    }
                    else
                    {
                        creatureSFX.PlayOneShot(walkingSFX[randomNum], 1f);
                    }
                }
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

            Rigidbody rb = flyingDoor.GetComponent<Rigidbody>() ?? flyingDoor.AddComponent<Rigidbody>();
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

            flyingDoor.GetComponent<DoorPlayerCollisionDetect>().force = direction * doorBashForce * 2f;

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

            doorAudio.PlayOneShot(doorWooshSFX, 1f); // TODO: Test this

            doorCollisionDetectScript.triggering = false;
            doorLock = null!;
            inSpecialAnimation = false;

            if (despawnDoorAfterBash)
            {
                Destroy(flyingDoor, despawnDoorAfterBashTime);
            }
        }

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

        public IEnumerator EatPlayerBodyCoroutine()
        {
            logger.LogDebug("Eating player body...");
            creatureAnimator.SetBool("eat", true);
            float randSoundTime = UnityEngine.Random.Range(0f, 19f);
            creatureVoice.time = randSoundTime;
            creatureVoice.Play();

            logger.LogDebug("Waiting for 10 seconds...");
            yield return new WaitForSecondsRealtime(10f);

            creatureVoice.Stop();
            creatureAnimator.SetBool("eat", false);
            enemyHP = 20;
            
            logger.LogDebug("Despawning player body...");
            targetPlayerCorpse.DeactivateBody(setActive: false);

            logger.LogDebug("Switching to roaming state...");

            if (IsServerOrHost) { RestartSearch(transform.position); }
            SwitchToBehaviourStateOnLocalClient((int)State.Roaming);
            targetEnemy = null!;
            targetPlayer = null!;

            targetPlayerCorpse = null!;
            inSpecialAnimation = false;

            logger.LogDebug("Finished eating player body.");
        }

        public IEnumerator EatMaskedBodyCoroutine()
        {
            logger.LogDebug("Eating masked body...");
            creatureAnimator.SetBool("eat", true);
            float randSoundTime = UnityEngine.Random.Range(0f, 19f);
            creatureVoice.time = randSoundTime;
            creatureVoice.Play();

            yield return new WaitForSecondsRealtime(10f);

            creatureVoice.Stop();
            creatureAnimator.SetBool("eat", false);
            enemyHP = 20;

            if (IsServerOrHost)
            {
                RoundManager.Instance.DespawnEnemyOnServer(targetEnemyCorpse.NetworkObject);
                RestartSearch(transform.position);
                SwitchToBehaviourClientRpc((int)State.Roaming);
                targetEnemy = null!;
                targetEnemy = null!;
            }

            targetEnemyCorpse = null!;
            inSpecialAnimation = false;
        }

        public void PlayerDamaged(PlayerControllerB player)
        {
            if (targetPlayer != null && targetPlayer == player) { return; }
            if (targetPlayer == null || (targetPlayer.health > player.health))
            {
                if (Vector3.Distance(player.transform.position, transform.position) <= playerBloodSenseRange && !inSpecialAnimation)
                {
                    targetPlayer = player;
                    StopSearch(currentSearch);
                    SwitchToBehaviourClientRpc((int)State.BloodSearch);
                }
            }
        }

        public void MaskedDamaged(MaskedPlayerEnemy masked)
        {
            if (targetPlayer != null)
            {
                return;
            }
            if (targetEnemy != null && targetEnemy == masked) { return; }
            if (targetEnemy == null || (targetEnemy.enemyHP > masked.enemyHP))
            {
                if (Vector3.Distance(masked.transform.position, transform.position) <= maskedBloodSenseRange && !inSpecialAnimation)
                {
                    targetEnemy = masked;
                    StopSearch(currentSearch);
                    SwitchToBehaviourClientRpc((int)State.BloodSearch);
                }
            }
        }

        // IVisibleThreat Interface
        public ThreatType type => ThreatType.Player;
        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }
        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition) // TODO: Figure out how this works and fix it
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

        [ClientRpc]
        public void EatPlayerBodyClientRpc(ulong clientId)
        {
            logger.LogDebug("in eat player body clientrpc");
            inSpecialAnimation = true;
            targetPlayerCorpse = NetworkHandlerHeavy.PlayerFromId(clientId).deadBody;
            targetPlayerCorpse.canBeGrabbedBackByPlayers = false;
            targetPlayerCorpse.grabBodyObject.grabbable = false;
            targetPlayerCorpse.grabBodyObject.grabbableToEnemies = false;
            logger.LogDebug("Eating player body coroutine...");
            StartCoroutine(EatPlayerBodyCoroutine());
        }

        [ClientRpc]
        public void EatMaskedBodyClientRpc()
        {
            inSpecialAnimation = true;
            StartCoroutine(EatMaskedBodyCoroutine());
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
        public static void DamagePlayerPostfix(PlayerControllerB __instance, int damageNumber)
        {
            try
            {
                if (__instance.health >= 100)
                {
                    return;
                }
                else
                {
                    //dtransf
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