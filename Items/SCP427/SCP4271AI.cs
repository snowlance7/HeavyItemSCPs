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
using HarmonyLib;
using Unity.Netcode.Components;

namespace HeavyItemSCPs.Items.SCP427
{
    public class SCP4271AI : EnemyAI, IVisibleThreat
    {

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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Transform turnCompass;
        public Transform RightHandTransform;
        public AudioClip[] stompSFXList;
        public AudioClip roarSFX;
        public AudioClip warningRoarSFX;
        public AudioClip hitWallSFX;
        public NetworkAnimator networkAnimator;
        public Material[] materials;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        readonly float throwForce = 70f;

        // Speed values
        public static float SpeedAccelerationEffect = 2.3f; // default 2.4f
        public static float BaseAcceleration = 30f; // default 42f
        public static float SpeedIncreaseRate = 4f; // default 5f

        //public static bool throwingPlayerEnabled = true; // TESTING REMOVE LATER

        public static float heldObjectVerticalOffset = 6f; // TODO: Get this from testing

        GameObject targetObject = null!;
        GrabbableObject heldObject = null!;

        float averageVelocity;
        Vector3 previousPosition;
        float previousVelocity;
        float velocityInterval;
        float velocityAverageCount;
        float wallCollisionSFXDebounce;
        float agentSpeedWithNegative;

        bool throwingPlayer;
        bool grabbingPlayer;
        bool pickingUpScrap;

        float timeSinceDamagePlayer;
        float timeSinceThrowingPlayer;
        float timeSinceSeenPlayer;
        float idlingTimer;
        float timeSpawned;

        // For throwing player
        Vector3 forwardDirection;
        Vector3 upDirection;
        Vector3 throwDirection;

        EnemyAI targetEnemy = null!;

        // Config values
        DropMethod dropMethod;
        int maxHealth;

        public enum State
        {
            Roaming,
            Chasing,
            Throwing
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

        public void SwitchToBehaviourCustom(State state)
        {
            switch (state)
            {
                case State.Roaming:
                    DoAnimationClientRpc(walk: true, run: false);
                    StartSearch(transform.position);

                    break;
                case State.Chasing:
                    DoAnimationClientRpc(walk: false, run: true);
                    StopSearch(currentSearch);

                    break;
                case State.Throwing:
                    StopSearch(currentSearch);

                    break;
                default:
                    break;
            }

            SwitchToBehaviourClientRpc((int)state);
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-427-1 Spawned");

            dropMethod = config4271DropMethod.Value;
            maxHealth = config4271MaxHealth.Value;

            SetOutsideOrInside();
            //SetEnemyOutsideClientRpc(true);
            //debugEnemyAI = true;

            RoundManager.Instance.RefreshEnemiesList();
            HoarderBugAI.RefreshGrabbableObjectsInMapList();

            timeSinceSeenPlayer = Mathf.Infinity;
            SwitchToBehaviourCustom(State.Roaming);

            logger.LogDebug("Finished spawning SCP-427-1");
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

            if (grabbingPlayer && inSpecialAnimationWithPlayer != null)
            {
                inSpecialAnimationWithPlayer.transform.position = RightHandTransform.position;
            }

            if (heldObject != null)
            {
                heldObject.transform.position = RightHandTransform.position + new Vector3(0f, heldObjectVerticalOffset, 0f);
            }

            if (!IsServerOrHost) { return; }

            if (currentBehaviourStateIndex == (int)State.Roaming) { timeSinceSeenPlayer += Time.deltaTime; }

            if (idlingTimer > 0f)
            {
                idlingTimer -= Time.deltaTime;

                if (idlingTimer <= 0f && currentBehaviourStateIndex == (int)State.Roaming)
                {
                    DoAnimationClientRpc(walk: true, run: false);
                    idlingTimer = 0f;
                }
            }

            CalculateAgentSpeed();
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            if (stunNormalizedTimer > 0f || timeSpawned < 2.5f) { return; }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:

                    if (FoundClosestPlayerInRange(25f, 5f))
                    {
                        if (heldObject != null)
                        {
                            SwitchToBehaviourCustom(State.Throwing);
                            AttemptThrowScrapAtTargetPlayer();
                        }
                        else
                        {
                            StopSearch(currentSearch);

                            if (timeSinceSeenPlayer > 30f)
                            {
                                Roar();
                            }
                            else
                            {
                                SwitchToBehaviourCustom(State.Chasing);
                            }

                            idlingTimer = 0f;
                            timeSinceSeenPlayer = 0f;
                        }
                        break;
                    }

                    MoveTowardsScrapInLineOfSight();

                    break;

                case (int)State.Chasing:

                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 30f && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        logger.LogDebug("Stop Targeting");
                        targetPlayer = null;
                        SwitchToBehaviourCustom(State.Roaming);
                        return;
                    }

                    SetMovingTowardsTargetPlayer(targetPlayer);

                    break;

                case (int)State.Throwing:
                    agent.speed = 0f;

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourStateIndex);
                    break;
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
            if (stunNormalizedTimer >= 0f || currentBehaviourStateIndex == 2 || inSpecialAnimation || (idlingTimer > 0f && currentBehaviourStateIndex == 0))
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

        public void PlayStompSFX() // Used in animation events, works as intended
        {
            int index = Random.Range(0, stompSFXList.Count() - 1);
            float volume;
            if (currentBehaviourStateIndex == (int)State.Chasing) { volume = 1f; } else { volume = 0.7f; }
            creatureSFX.PlayOneShot(stompSFXList[index], volume);
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

        private void MoveTowardsScrapInLineOfSight()
        {
            if (heldObject != null) { return; }
            if (targetObject != null)
            {
                logger.LogDebug("Trying to pick up scrap when close"); // TODO: Test this, stops working if someone picks up scrap before it is picked up?
                PickUpScrapIfClose();
                return;
            }

            targetObject = CheckLineOfSight(HoarderBugAI.grabbableObjectsInMap);

            if (targetObject != null)
            {
                if (SetDestinationToPosition(targetObject.transform.position, true))
                {
                    logger.LogDebug("Moving to targetObject");
                    StopSearch(currentSearch);
                }
            }
        }

        private void PickUpScrapIfClose()
        {
            GrabbableObject scrap = targetObject.GetComponent<GrabbableObject>();

            if (scrap == null || scrap.isHeldByEnemy || scrap.isHeld)
            {
                targetObject = null;
                StartSearch(transform.position);
                DoAnimationClientRpc(walk: true, run: false);
                return;
            }

            if (Vector3.Distance(transform.position, targetObject.transform.position) < 1.5f)
            {
                pickingUpScrap = true;
                inSpecialAnimation = true;
                StartCoroutine(PickUpScrapCoroutine());
            }
        }

        public IEnumerator PickUpScrapCoroutine()
        {
            networkAnimator.SetTrigger("pickup");
            yield return new WaitForSeconds(0.1f);

            GrabbableObject grabbingObject = targetObject.GetComponent<GrabbableObject>();
            targetObject = null!;
            grabbingObject.parentObject = RightHandTransform;
            grabbingObject.hasHitGround = false;
            grabbingObject.GrabItemFromEnemy(this);
            grabbingObject.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(grabbingObject.gameObject);
            heldObject = grabbingObject;

            yield return new WaitForSeconds(0.5f);
            StartSearch(transform.position);
            pickingUpScrap = false;
            inSpecialAnimation = false;
            //networkAnimator.SetTrigger("startWalk");
        }

        public void AttemptThrowScrapAtTargetPlayer()
        {
            logger.LogDebug("Throwing held object at target player");
            StartCoroutine(ThrowScrapCoroutine());
        }

        public IEnumerator ThrowScrapCoroutine()
        {
            Vector3 lastKnownPlayerPosition = targetPlayer.transform.position;

            networkAnimator.SetTrigger("throw");
            yield return new WaitForSeconds(0.5f);
            DropItem(lastKnownPlayerPosition);
            yield return new WaitForSeconds(0.1f);
            if (Vector3.Distance(lastKnownPlayerPosition, targetPlayer.transform.position) < 1.5f)
            {
                targetPlayer.DamagePlayer(15, true, true, CauseOfDeath.Inertia);
            }

            if (timeSinceSeenPlayer > 60f)
            {
                Roar();
            }
            else
            {
                SwitchToBehaviourCustom(State.Chasing);
            }
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
            heldObject = null!;
        }

        public void Grab() // Set up to run with animation
        {
            if (!throwingPlayer)
            {
                return;
            }

            PlayerControllerB player = inSpecialAnimationWithPlayer;
            player.playerRigidbody.isKinematic = false;

            forwardDirection = transform.TransformDirection(Vector3.forward).normalized * 2;
            upDirection = transform.TransformDirection(Vector3.up).normalized;
            throwDirection = (forwardDirection + upDirection).normalized;

            // Grab player
            logger.LogDebug("Grabbing player: " + inSpecialAnimationWithPlayer.playerUsername);
            grabbingPlayer = true;
            StartCoroutine(FailsafeCoroutine());
        }

        public void Throw() // Set up to run with animation
        {
            if (!throwingPlayer)
            {
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
                }

                return;
            }

            grabbingPlayer = false;
            logger.LogDebug("Throwing player: " + inSpecialAnimationWithPlayer.playerUsername);
            PlayerControllerB player = inSpecialAnimationWithPlayer;
            player.transform.position = transform.position;

            // Throw player
            logger.LogDebug("Applying force: " + throwDirection * throwForce);
            player.playerRigidbody.velocity = Vector3.zero;
            player.externalForceAutoFade += throwDirection * throwForce;
            MakePlayerDrop(player);

            CancelSpecialAnimationWithPlayer();

            // Damage player
            if (localPlayer == player)
            {
                StartCoroutine(InjureLocalPlayerCoroutine());
            }

            logger.LogDebug("Finished throwing player");

            if (IsServerOrHost)
            {
                Roar();
            }
        }

        public IEnumerator FailsafeCoroutine()
        {
            yield return new WaitForSeconds(5f);
            CancelSpecialAnimationWithPlayer();
        }

        public IEnumerator RemoveRigidbodyAfterDelay(float delay)
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

            targetEnemy = null!;
            inSpecialAnimation = false;
            SwitchToBehaviourCustom(State.Roaming);
        }

        public IEnumerator InjureLocalPlayerCoroutine()
        {
            yield return new WaitUntil(() => localPlayer.thisController.isGrounded || localPlayer.isInHangarShipRoom);
            if (localPlayer.isPlayerDead) { yield break; }
            localPlayer.DamagePlayer(40, true, true, CauseOfDeath.Inertia);
            localPlayer.sprintMeter /= 2;
            localPlayer.JumpToFearLevel(0.8f);
            localPlayer.drunkness = 0.2f;
        }

        public void FreezePlayer(PlayerControllerB player, float freezeTime)
        {
            StartCoroutine(FreezePlayerCoroutine(player, freezeTime));
        }

        private IEnumerator FreezePlayerCoroutine(PlayerControllerB player, float freezeTime)
        {
            player.disableMoveInput = true;
            player.disableInteract = true;
            yield return new WaitForSeconds(freezeTime);
            player.disableInteract = false;
            player.disableMoveInput = false;
        }

        public void Roar()
        {
            idlingTimer = 0f;


            StopSearch(currentSearch);
            SetEnemyStunned(true, 3.2f);
            StartCoroutine(RoarCoroutine());
        }

        public IEnumerator RoarCoroutine()
        {
            creatureVoice.PlayOneShot(roarSFX, 1f);
            networkAnimator.SetTrigger("roar");

            yield return new WaitUntil(() => stunNormalizedTimer <= 0f);

            if (targetPlayer != null)
            {
                SwitchToBehaviourCustom(State.Chasing);
            }
            else
            {
                SwitchToBehaviourCustom(State.Roaming);
            }
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
                DoAnimationClientRpc(walk: false, run: false);
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
                CancelSpecialAnimationWithPlayer();
                Roar();
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            CancelSpecialAnimationWithPlayer();
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

        public override void CancelSpecialAnimationWithPlayer()
        {
            if (inSpecialAnimationWithPlayer != null)
            {
                inSpecialAnimationWithPlayer.playerRigidbody.isKinematic = true;
            }
            base.CancelSpecialAnimationWithPlayer();
            grabbingPlayer = false;
            throwingPlayer = false;
            pickingUpScrap = false;
            targetObject = null!;
        }

        public override void OnCollideWithPlayer(Collider other) // This only runs on client
        {
            base.OnCollideWithPlayer(other);
            //if (!throwingPlayerEnabled) { return; } // TODO: For testing, remove later
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && currentBehaviourStateIndex != (int)State.Throwing && !inSpecialAnimation && heldObject == null)
            {
                if (timeSinceDamagePlayer > 2f)
                {
                    logger.LogDebug($"{player.playerUsername} collided with SCP-427-1");
                    timeSinceDamagePlayer = 0f;
                    
                    if (timeSinceThrowingPlayer > 10f)
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

        public override void OnCollideWithEnemy(Collider other, EnemyAI collidedEnemy = null) // TODO: TEST THIS
        {
            base.OnCollideWithEnemy(other, collidedEnemy);

            if (collidedEnemy.enemyType.name == "BaboonHawk" || collidedEnemy.enemyType.name == "HoarderBug")
            {
                if (!isEnemyDead && !collidedEnemy.isEnemyDead && !inSpecialAnimation && currentBehaviourStateIndex != (int)State.Throwing)
                {
                    if (IsServerOrHost)
                    {
                        logger.LogDebug("Throwing enemy");
                        inSpecialAnimation = true;
                        targetEnemy = collidedEnemy;
                        SwitchToBehaviourCustom(State.Throwing);
                        networkAnimator.SetTrigger("throw");
                    }
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

        // RPC's

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

        [ClientRpc]
        void DoAnimationClientRpc(bool walk, bool run)
        {
            creatureAnimator.SetBool("walk", walk);
            creatureAnimator.SetBool("run", run);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ThrowPlayerServerRpc(ulong clientId)
        {
            if (IsServerOrHost)
            {
                ThrowPlayerClientRpc(clientId);
                throwingPlayer = true;
                networkAnimator.SetTrigger("pickupThrow");
            }
        }

        [ClientRpc]
        private void ThrowPlayerClientRpc(ulong clientId)
        {
            logger.LogDebug("In throwplayerclientid");
            throwingPlayer = true;
            currentBehaviourStateIndex = (int)State.Throwing;
            inSpecialAnimation = true;
            inSpecialAnimationWithPlayer = PlayerFromId(clientId);
            inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
        }

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
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