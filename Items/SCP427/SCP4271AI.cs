using BepInEx.Logging;
using Dissonance.Audio.Codecs;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements.Experimental;
using static HeavyItemSCPs.Plugin;
using static HeavyItemSCPs.Utils;
using static UnityEngine.Rendering.DebugUI;

namespace HeavyItemSCPs.Items.SCP427
{
    public class SCP4271AI : EnemyAI, IVisibleThreat // TODO: Needs testing
    {
        // Run speed for animation: 1.1 or higher
        // Thumper variables for reference:
        /*
         * CrawlerAI.SpeedAccelerationEffect = 2.4f;
         * CrawlerAI.BaseAcceleration = 42f;
         * CrawlerAI.maxSearchAndRoamRadius = 55
         * CrawlerAI.SpeedIncreaseRate = 5f
         * agentSpeedWithNegative = 0.8539f // const?
         * 
         * 
         * EnemyAI.openDoorSpeedMultiplier = 0.3f
         * 
         * EnemyType.doorSpeedMultiplier = 0.3f
         * EnemyType.loudnessMultiplier = 2f
         * EnemyType.PowerLevel = 3f
         * EnemyType.pushPlayerDistance = 1.26f
         * EnemyType.pushPlayerForce = 0.16f
         * EnemyType.stunGameDifficultyMultiplier = 0.5f
         * EnemyType.stunTimeMultiplier = 1f
         * 
         * Agent.angularSpeed = 230f
         * Agent.acceleration = 13f
         * Agent.areaMask = 639
         * Agent.avoidancePriority = 50
         * Agent.speed = 15
         *
         */

        private static ManualLogSource logger = LoggerInstance;
        public static SCP4271AI? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Transform turnCompass;
        public Transform RightHandTransform;
        public AudioClip[] stompSFXList;
        public AudioClip roarSFX;
        public AudioClip warningRoarSFX;
        public AudioClip hitWallSFX;
        //public NetworkAnimator networkAnimator;
        public Material[] materials;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        readonly float throwForce = 70f;

        // Speed values
        public static float SpeedAccelerationEffect = 2.3f; // default 2.4f
        public static float BaseAcceleration = 30f; // default 42f
        public static float SpeedIncreaseRate = 4f; // default 5f

        public static bool DEBUG_throwingPlayerDisabled; // TESTING REMOVE LATER

        public float heldObjectVerticalOffset = 6f; // TODO: Get this from testing

        EnemyAI? targetEnemy;
        GameObject? targetObject;
        GrabbableObject? heldObject;

        PlayerControllerB? heldPlayer;
        //EnemyAI? heldEnemy;

        float averageVelocity;
        Vector3 previousPosition;
        float previousVelocity;
        float velocityInterval;
        float velocityAverageCount;
        float wallCollisionSFXDebounce;
        float agentSpeedWithNegative;

        bool movingTowardsScrap;
        bool facePlayer;

        float timeSinceDamagePlayer;
        float timeSinceThrowingPlayer;
        float timeSinceSeenPlayer;
        float idlingTimer;
        float timeSpawned;

        // For throwing player
        Vector3 forwardDirection;
        Vector3 upDirection;
        Vector3 throwDirection;
        Vector3 lastKnownTargetPlayerPosition;

        float currentSpeed => agent.velocity.magnitude / 2;

        int hashSpeed;

        // Config values
        DropMethod dropMethod => config4271DropMethod.Value;
        int maxHealth => config4271MaxHealth.Value;
        float distanceToLoseAggro = 30f;

        public enum State
        {
            Roaming,
            Chasing
        }

        public enum MaterialVariants
        {
            Player,
            Hoarderbug,
            BaboonHawk,
            None
        }

        public enum DropMethod
        {
            DropNothing,
            DropHeldItem,
            DropTwoHandedItem,
            DropAllItems
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-427-1 Spawned");

            if (Instance == null)
            {
                Instance = this;
            }

            SetOutsideOrInside();

            RoundManager.Instance.RefreshEnemiesList();
            HoarderBugAI.RefreshGrabbableObjectsInMapList();
            
            timeSinceSeenPlayer = Mathf.Infinity;

            hashSpeed = Animator.StringToHash("speed");

            logger.LogDebug("Finished spawning SCP-427-1");
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void Update()
        {
            base.Update();

            timeSpawned += Time.deltaTime;
            timeSinceThrowingPlayer += Time.deltaTime;
            timeSinceDamagePlayer += Time.deltaTime;

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            if (heldPlayer != null)
            {
                heldPlayer.transform.position = RightHandTransform.position;
            }

            if (heldObject != null)
            {
                heldObject.transform.position = RightHandTransform.position + new Vector3(0f, heldObjectVerticalOffset, 0f);
            }

            if (!IsServerOrHost) { return; }

            if (currentBehaviourStateIndex == (int)State.Roaming) { timeSinceSeenPlayer += Time.deltaTime; }

            if (idlingTimer > 0f)
            {
                agent.speed = 0f;
                idlingTimer -= Time.deltaTime;

                if (idlingTimer <= 0f)
                {
                    idlingTimer = 0f;
                }
            }

            CalculateAgentSpeed();
        }

        public void LateUpdate()
        {
            creatureAnimator.SetFloat(hashSpeed, currentSpeed); // TODO: Set this up correctly

            if (facePlayer)
            {
                turnCompass.LookAt(Plugin.localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 30f * Time.deltaTime);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            //logger.LogDebug(currentSpeed);

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || inSpecialAnimation)
            {
                return;
            };

            if (stunNormalizedTimer > 0f || timeSpawned < 2.5f) { return; }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:

                    if (!disableTargetting && FoundClosestPlayerInRange(25f, 5f))
                    {
                        StopSearch(currentSearch);

                        if (heldObject != null)
                        {
                            ThrowScrapClientRpc(targetPlayer.actualClientId);
                        }

                        idlingTimer = 0f;
                        SwitchToBehaviourClientRpc((int)State.Chasing);
                        break;
                    }

                    if (!MoveTowardsScrapInLineOfSight())
                    {
                        if (currentSearch == null || !currentSearch.inProgress)
                        {
                            StartSearch(transform.position);
                        }
                    }

                    break;

                case (int)State.Chasing:

                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > distanceToLoseAggro && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        logger.LogDebug("Stop Targeting");
                        targetPlayer = null;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        return;
                    }

                    if (timeSinceSeenPlayer > 30f)
                    {
                        creatureAnimator.SetTrigger("roar");
                    }

                    timeSinceSeenPlayer = 0f;
                    SetDestinationToPosition(targetPlayer.transform.position);

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
            }
        }

        #region Animation Events

        public void InSpecialAnimationTrue() // Used in animation events
        {
            inSpecialAnimation = true;
        }

        public void InSpecialAnimationFalse() // Used in animation events
        {
            inSpecialAnimation = false;
        }

        public void PlayStompSFX() // Used in animation events
        {
            float volume = currentBehaviourStateIndex == (int)State.Chasing ? 1f : 0.7f;
            RoundManager.PlayRandomClip(creatureSFX, stompSFXList, true, volume);
        }

        public void PlayRoarSFX() // Used in animation events
        {
            creatureVoice.PlayOneShot(roarSFX, 1f);
        }

        public void Grab() // Set up to run with animation
        {
            if (heldObject != null) { return; }

            if (targetObject  != null)
            {
                GrabbableObject grabbingObject = targetObject.GetComponent<GrabbableObject>();
                targetObject = null;
                grabbingObject.parentObject = RightHandTransform;
                grabbingObject.hasHitGround = false;
                grabbingObject.isHeldByEnemy = true;
                grabbingObject.GrabItemFromEnemy(this);
                grabbingObject.EnablePhysics(false);
                HoarderBugAI.grabbableObjectsInMap.Remove(grabbingObject.gameObject);
                heldObject = grabbingObject;
                return;
            }
            if (targetPlayer != null)
            {
                PlayerControllerB player = targetPlayer;
                player.playerRigidbody.isKinematic = false;

                forwardDirection = transform.TransformDirection(Vector3.forward).normalized * 2;
                upDirection = transform.TransformDirection(Vector3.up).normalized;
                throwDirection = (forwardDirection + upDirection).normalized;

                // Grab player
                logger.LogDebug("Grabbing player: " + player.playerUsername);
                //grabbingPlayer = true;
                heldPlayer = player;

                IEnumerator FailsafeCoroutine(PlayerControllerB player)
                {
                    yield return new WaitForSeconds(5f);
                    Utils.FreezePlayer(player, false);
                    //CancelSpecialAnimation(); // TODO: Set this up again to reset and drop player and other things
                }
                StartCoroutine(FailsafeCoroutine(player));
                return;
            }
        }

        public void Throw() // Set up to run with animation
        {
            if (heldObject != null)
            {
                DropItem(lastKnownTargetPlayerPosition);
                if (Vector3.Distance(lastKnownTargetPlayerPosition, targetPlayer.transform.position) < 1.5f && targetPlayer == localPlayer)
                {
                    targetPlayer.DamagePlayer(15, true, true, CauseOfDeath.Inertia);
                }

                return;
            }
            if (heldPlayer != null)
            {
                //grabbingPlayer = false;
                PlayerControllerB player = heldPlayer;
                logger.LogDebug("Throwing player: " + player.playerUsername);
                player.transform.position = transform.position + Vector3.up;

                // Throw player
                logger.LogDebug("Applying force: " + throwDirection * throwForce);
                MakePlayerDrop(player);
                player.playerRigidbody.velocity = Vector3.zero;
                player.externalForceAutoFade += throwDirection * throwForce;

                CancelSpecialAnimation();

                // Damage player
                if (localPlayer == player)
                {
                    IEnumerator InjureLocalPlayerCoroutine()
                    {
                        yield return new WaitUntil(() => localPlayer.thisController.isGrounded || localPlayer.isInHangarShipRoom);
                        if (localPlayer.isPlayerDead) { yield break; }
                        localPlayer.DamagePlayer(40, true, true, CauseOfDeath.Inertia);
                        localPlayer.sprintMeter /= 2;
                        localPlayer.JumpToFearLevel(0.8f);
                        localPlayer.drunkness = 0.2f;
                    }
                    StartCoroutine(InjureLocalPlayerCoroutine());
                }

                logger.LogDebug("Finished throwing player");
            }
            if (targetEnemy != null)
            {
                forwardDirection = transform.TransformDirection(Vector3.forward).normalized * 2;
                upDirection = transform.TransformDirection(Vector3.up).normalized;
                throwDirection = (forwardDirection + upDirection).normalized;

                targetEnemy.agent.enabled = false;
                Rigidbody enemyRb = targetEnemy.gameObject.AddComponent<Rigidbody>();
                enemyRb.isKinematic = false;

                enemyRb.velocity = Vector3.zero;
                enemyRb.AddForce(throwDirection.normalized * throwForce, ForceMode.Impulse);

                StartCoroutine(RemoveRigidbodyAfterDelay(1f));
                IEnumerator RemoveRigidbodyAfterDelay(float delay)
                {
                    yield return new WaitForSeconds(delay);

                    Rigidbody enemyRb = targetEnemy.gameObject.GetComponent<Rigidbody>();

                    if (enemyRb != null)
                    {
                        enemyRb.isKinematic = true;
                        Destroy(enemyRb);
                    }

                    targetEnemy.agent.enabled = true;
                    targetEnemy.HitEnemy(4, null, true);

                    targetEnemy = null;
                    SwitchToBehaviourClientRpc((int)State.Roaming); // TODO: is this necessary?
                }
            }
        }

        #endregion

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
            for (int i = 0; i < StartOfRound.Instance.connectedPlayersAmount + 1; i++)
            {
                tempDist = Vector3.Distance(transform.position, StartOfRound.Instance.allPlayerScripts[i].transform.position);
                if (tempDist < mostOptimalDistance)
                {
                    mostOptimalDistance = tempDist;
                    targetPlayer = StartOfRound.Instance.allPlayerScripts[i];
                }
            }

            return targetPlayer != null;
        }

        private void CalculateAgentSpeed()
        {
            if (stunNormalizedTimer >= 0f || currentBehaviourStateIndex == 2 || inSpecialAnimation || (idlingTimer > 0f && currentBehaviourStateIndex == 0) || Utils.disableMoving)
            {
                //logger.LogDebug("Stopping agent speed");
                agent.speed = 0f;
                agent.acceleration = 200f;
                return;
            }
            creatureAnimator.SetFloat("speedMultiplier", Mathf.Clamp(averageVelocity / 12f * 1.5f, 0.5f, 3f));
            float currentVelocity = (transform.position - previousPosition).magnitude / (Time.deltaTime / 1.4f);
            if (velocityInterval <= 0f)
            {
                previousVelocity = averageVelocity;
                velocityInterval = 0.05f;
                velocityAverageCount += 1f;
                if (velocityAverageCount > 5f)
                {
                    averageVelocity += (currentVelocity - averageVelocity) / 3f;
                }
                else
                {
                    averageVelocity += currentVelocity;
                    if (velocityAverageCount == 2f)
                    {
                        averageVelocity /= velocityAverageCount;
                    }
                }
            }
            else
            {
                velocityInterval -= Time.deltaTime;
            }
            if (IsOwner &&
                averageVelocity - currentVelocity > Mathf.Clamp(currentVelocity * 0.5f, 5f, 100f) &&
                currentVelocity < 5f && // Slow down needed to detect if hitting wall
                currentBehaviourStateIndex == 1)
            {
                if (wallCollisionSFXDebounce > 0.5f)
                {
                    creatureSFX.PlayOneShot(hitWallSFX, 0.7f);
                    SetEnemyStunned(true, 0.5f);

                    //NetworkHandlerHeavy.Instance.ShakePlayerCamerasServerRpc(ScreenShakeType.Big, 15f, transform.position);
                    //averageVelocity = 0f;
                }
                agentSpeedWithNegative *= 0.2f; // ??
                wallCollisionSFXDebounce = 0f;
            }
            wallCollisionSFXDebounce += Time.deltaTime;
            previousPosition = transform.position;
            if (currentBehaviourStateIndex == 0)
            {
                agent.speed = 2f;
                agent.acceleration = 26f;
            }
            else if (currentBehaviourStateIndex == 1)
            {
                agentSpeedWithNegative += Time.deltaTime * SpeedIncreaseRate;
                agent.speed = Mathf.Clamp(agentSpeedWithNegative, 0f, 11f);
                agent.acceleration = Mathf.Clamp(BaseAcceleration - averageVelocity * SpeedAccelerationEffect, 4f, 30f);
                if (agent.acceleration > 15f)
                {
                    agent.angularSpeed = 500f;
                    agent.acceleration += 25f;
                }
                else
                {
                    agent.angularSpeed = 230f;
                }
            }
        }

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = Utils.outsideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;
            GameObject closestInsideNode = Utils.insideAINodes.ToList().GetClosestGameObjectToPosition(transform.position)!;

            bool outside = Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position);
            logger.LogDebug("Setting enemy outside: " + outside.ToString());
            SetEnemyOutsideClientRpc(outside);
        }

        bool MoveTowardsScrapInLineOfSight()
        {
            if (heldObject != null) { return false; }
            if (targetObject != null)
            {
                GrabbableObject scrap = targetObject.GetComponent<GrabbableObject>();

                if (scrap == null || scrap.isHeldByEnemy || scrap.isHeld)
                {
                    targetObject = null;
                    return false;
                }

                if (Vector3.Distance(transform.position, targetObject.transform.position) < 1.5f)
                {
                    logger.LogDebug("Grabbing scrap");
                    GrabScrapClientRpc(scrap.NetworkObject);
                }

                return true;
            }

            targetObject = CheckLineOfSight(HoarderBugAI.grabbableObjectsInMap);
            if (targetObject == null) { return false; }
            if (!SetDestinationToPosition(targetObject.transform.position, true)) { targetObject = null; return false; }
            StopSearch(currentSearch);
            return true;
        }

        private void DropItem(Vector3 targetFloorPosition) // TODO: This works but needs to be tested on network
        {
            if (heldObject == null)
            {
                return;
            }
            GrabbableObject itemGrabbableObject = heldObject;
            itemGrabbableObject.parentObject = null;
            itemGrabbableObject.transform.SetParent(StartOfRound.Instance.propsContainer, worldPositionStays: true);
            itemGrabbableObject.EnablePhysics(enable: true);
            itemGrabbableObject.fallTime = 0f;
            itemGrabbableObject.startFallingPosition = itemGrabbableObject.transform.parent.InverseTransformPoint(itemGrabbableObject.transform.position);
            itemGrabbableObject.targetFloorPosition = itemGrabbableObject.transform.parent.InverseTransformPoint(targetFloorPosition);
            itemGrabbableObject.floorYRot = -1;
            itemGrabbableObject.DiscardItemFromEnemy();
            itemGrabbableObject.isHeldByEnemy = false;
            HoarderBugAI.grabbableObjectsInMap.Add(itemGrabbableObject.gameObject);
            heldObject = null!;
        }

        public void FreezePlayer(PlayerControllerB player, float freezeTime)
        {
            IEnumerator FreezePlayerCoroutine(PlayerControllerB player, float freezeTime)
            {
                player.disableInteract = true;
                player.disableLookInput = true;
                player.disableMoveInput = true;
                yield return new WaitForSeconds(freezeTime);
                player.disableInteract = false;
                player.disableMoveInput = false;
                player.disableLookInput = false;
            }

            StartCoroutine(FreezePlayerCoroutine(player, freezeTime));
        }

        public override void ReachedNodeInSearch()
        {
            base.ReachedNodeInSearch();

            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                int randomNum = Random.Range(0, 100);
                if (randomNum < 10)
                {
                    creatureVoice.PlayOneShot(warningRoarSFX, 1f);
                }

                idlingTimer = 2f;
            }
        }

        public override void HitFromExplosion(float distance)
        {
            base.HitFromExplosion(distance);

            if (!isEnemyDead)
            {
                enemyHP -= (maxHealth / 2);
                if (enemyHP <= 0 && IsOwner)
                {
                    KillEnemyOnOwnerClient();
                    return;
                }
                CancelSpecialAnimation();
                creatureAnimator.SetTrigger("roar");
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            CancelSpecialAnimation();
            StopAllCoroutines();

            base.KillEnemy(false);
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

        public void CancelSpecialAnimation()
        {
            if (heldPlayer != null)
            {
                heldPlayer.playerRigidbody.isKinematic = true;
                heldPlayer.inAnimationWithEnemy = null;
            }
            if (targetPlayer != null)
            {
                targetPlayer.playerRigidbody.isKinematic = true;
            }
            inSpecialAnimation = false;
            heldPlayer = null;
            targetPlayer = null;
            targetEnemy = null;
            targetObject = null;
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            if (inSpecialAnimation) { return; }
            if (timeSinceDamagePlayer < 2f) { return; }
            if (heldObject != null) { DropItem(transform.position); }
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !player.isPlayerControlled || player != localPlayer || isEnemyDead) { return; }
            logger.LogDebug($"{player.playerUsername} collided with SCP-427-1");
            timeSinceDamagePlayer = 0f;

            if (timeSinceThrowingPlayer > 10f && !DEBUG_throwingPlayerDisabled)
            {
                logger.LogDebug("Throwing player");

                if (player.currentlyHeldObjectServer != null && player.currentlyHeldObjectServer.itemProperties.name == "CaveDwellerBaby")
                {
                    player.DiscardHeldObject();
                }

                inSpecialAnimation = true;
                timeSinceThrowingPlayer = 0f;

                FreezePlayer(player, 2f);
                ThrowPlayerServerRpc(player.actualClientId);
            }
            else
            {
                DoAnimationServerRpc("throw");
                player.DamagePlayer(10, true, true, CauseOfDeath.Mauling);
            }
        }

        void MakePlayerDrop(PlayerControllerB player)
        {
            switch (dropMethod)
            {
                case DropMethod.DropHeldItem:
                    if (player.currentlyHeldObjectServer != null)
                    {
                        player.DiscardHeldObject();
                    }
                    break;
                case DropMethod.DropTwoHandedItem:
                    if (player.currentlyHeldObjectServer != null && player.twoHanded)
                    {
                        player.DiscardHeldObject();
                    }
                    break;
                case DropMethod.DropAllItems:
                    player.DropAllHeldItemsAndSync();
                    break;
                default:
                    if (player.currentlyHeldObjectServer != null && player.currentlyHeldObjectServer.itemProperties.name == "CaveDwellerBaby")
                    {
                        player.DiscardHeldObject();
                    }
                    break;
            }
        }

        public override void OnCollideWithEnemy(Collider other, EnemyAI? collidedEnemy = null) // TODO: TEST THIS
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (collidedEnemy == null) { return; }

            if (collidedEnemy.enemyType.name == "BaboonHawk" || collidedEnemy.enemyType.name == "HoarderBug")
            {
                if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation)
                {
                    targetEnemy = collidedEnemy;
                    creatureAnimator.SetTrigger("throw");
                }
            }
        }

        // IVisibleThreat Settings
        public ThreatType type => ThreatType.ForestGiant;

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

        public GrabbableObject? GetHeldObject()
        {
            return heldObject;
        }

        public bool IsThreatDead()
        {
            return isEnemyDead;
        }

        // RPC's

        [ClientRpc]
        void ThrowScrapClientRpc(ulong targetClientId)
        {
            targetPlayer = PlayerFromId(targetClientId);
            lastKnownTargetPlayerPosition = targetPlayer.transform.position;
            creatureAnimator.SetTrigger("throw");
        }

        [ClientRpc]
        void GrabScrapClientRpc(NetworkObjectReference netRef)
        {
            if (!netRef.TryGet(out NetworkObject netObj)) { logger.LogError("Cant get netObj from netRef GrabClientRpc"); return; }
            if (!netObj.TryGetComponent(out GrabbableObject scrap)) { logger.LogError("Cant find GrabbableObject from netObj GrabClientRpc"); return; }
            
            targetObject = scrap.gameObject;
            creatureAnimator.SetTrigger("pickup");
        }

        [ServerRpc(RequireOwnership = false)]
        void DoAnimationServerRpc(string animationName)
        {
            if (!IsServerOrHost) { return; }
            DoAnimationClientRpc(animationName);
        }

        [ClientRpc]
        void DoAnimationClientRpc(string animationName)
        {
            creatureAnimator.SetTrigger(animationName);
        }

        [ClientRpc]
        void DoAnimationClientRpc(string animationName, bool value)
        {
            creatureAnimator.SetBool(animationName, value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ThrowPlayerServerRpc(ulong clientId)
        {
            if (!IsServerOrHost) { return; }
            ThrowPlayerClientRpc(clientId);
        }

        [ClientRpc]
        private void ThrowPlayerClientRpc(ulong clientId)
        {
            logger.LogDebug("In ThrowPlayerClientRpc");
            CancelSpecialAnimation();
            targetPlayer = PlayerFromId(clientId);
            targetPlayer.inAnimationWithEnemy = this;
            inSpecialAnimation = true;
            creatureAnimator.SetTrigger("pickupThrow");
        }

        [ClientRpc]
        void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
        }

        [ClientRpc]
        public void SetMaterialVariantClientRpc(MaterialVariants variant)
        {
            skinnedMeshRenderers[0].material = materials[(int)variant];
        }
    }

    [HarmonyPatch]
    internal class SCP4271Patches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Landmine), nameof(Landmine.OnTriggerEnter))]
        public static void OnTriggerEnterPostfix(ref Landmine __instance, Collider other, ref float ___pressMineDebounceTimer)
        {
            // SCP4271Enemy
            EnemyAICollisionDetect enemyCollider = other.gameObject.GetComponent<EnemyAICollisionDetect>();

            if (enemyCollider != null && !enemyCollider.mainScript.isEnemyDead && enemyCollider.mainScript.enemyType.name == "SCP4271Enemy" && !__instance.hasExploded && enemyCollider.mainScript.currentBehaviourStateIndex != 0)
            {
                __instance.SetOffMineAnimation();
                __instance.sendingExplosionRPC = true;
                __instance.ExplodeMineServerRpc();
            }
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity