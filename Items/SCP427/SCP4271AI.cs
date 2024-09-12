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

namespace HeavyItemSCPs.Items.SCP427
{
    internal class SCP4271AI : EnemyAI
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

#pragma warning disable 0649
        public Transform turnCompass = null!;
#pragma warning restore 0649

        float throwForce = 70f;

        // Speed values
        public static float SpeedAccelerationEffect = 2.3f; // default 2.4f
        public static float BaseAcceleration = 30f; // default 42f
        public static float SpeedIncreaseRate = 4f; // default 5f

        public static bool throwingPlayerEnabled = true; // TESTING REMOVE LATER

        public Transform RightHandTransform;
        public static float heldObjectVerticalOffset = 4f; // TODO: Get this from testing

        GameObject targetObject;
        GrabbableObject heldObject;

        public AudioClip[] stompSFXList;
        public AudioClip roarSFX;
        public AudioClip warningRoarSFX;
        public AudioClip hitWallSFX;
        private float averageVelocity;
        private Vector3 previousPosition;
        private float previousVelocity;
        private float velocityInterval;
        private float velocityAverageCount;
        private float wallCollisionSFXDebounce;
        private float agentSpeedWithNegative;

        bool hasEnteredChaseMode;

        //bool walking;
        //bool running;
        //bool roaring;
        //bool grabbingPlayer;
        //bool pickingUpItem;
        //bool throwingScrap;

        float timeSinceSeenPlayer = 0f;
        float idlingTimer = 0f;

        public enum State
        {
            Roaming,
            Chasing,
            Throwing
        }

        public override void Start()
        {
            base.Start();
            logger.LogDebug("SCP-427-1 Spawned");

            currentBehaviourStateIndex = (int)State.Roaming;
            RoundManager.Instance.RefreshEnemiesList();
            HoarderBugAI.RefreshGrabbableObjectsInMapList();

            SetOutsideOrInside();
            //SetEnemyOutsideClientRpc(true);

            StartSearch(transform.position);
            creatureAnimator.SetTrigger("startWalk");
            //walking = true;
            //running = false;
        }

        public override void Update()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            base.Update();

            timeSinceSeenPlayer += Time.deltaTime;

            /*if (idlingTimer > 0f)
            {
                idlingTimer -= Time.deltaTime;

                if (idlingTimer <= 0f && currentBehaviourStateIndex == (int)State.Roaming)
                {
                    //DoAnimationClientRpc("startWalk");
                    creatureAnimator.SetTrigger("startWalk");
                }
            }*/

            CalculateAgentSpeed();

            if (currentBehaviourStateIndex == (int)State.Roaming && hasEnteredChaseMode)
            {
                hasEnteredChaseMode = false;
                //walking = true;
                //running = false;
                creatureAnimator.SetTrigger("startWalk");
            }
            else if (currentBehaviourStateIndex == (int)State.Chasing && !hasEnteredChaseMode)
            {
                hasEnteredChaseMode = true;
                agent.speed = 0f;
            }

            /*if (IsOwner && !inSpecialAnimation && inSpecialAnimationWithPlayer == null && state != (int)State.Throwing)
            {
                if (state == (int)State.Roaming)
                {
                    if (agent.remainingDistance <= agent.stoppingDistance + 0.1f && agent.velocity.sqrMagnitude < 0.01f)
                    {
                        if (walking)
                        {
                            walking = false;
                            running = false;

                            DoAnimationClientRpc("stopWalk");
                        }
                    }
                    else if (!walking)
                    {
                        walking = true;
                        running = false;

                        DoAnimationClientRpc("startWalk");
                    }
                }
                else if (state == (int)State.Chasing)
                {
                    if (!running)
                    {
                        running = true;
                        walking = false;
                        DoAnimationClientRpc("startRun");
                    }
                }
            }*/

            /*if (state == (int)State.Throwing && grabbingPlayer && targetPlayer != null)
            {
                targetPlayer.transform.position = RightHandTransform.position;
            }*/
            if (currentBehaviourStateIndex == (int)State.Throwing && inSpecialAnimationWithPlayer != null && targetPlayer != null)
            {
                targetPlayer.transform.position = RightHandTransform.position;
            }

            if (heldObject != null)
            {
                heldObject.transform.position = RightHandTransform.position + new Vector3(0f, heldObjectVerticalOffset, 0f);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            if (inSpecialAnimation || inSpecialAnimationWithPlayer != null) { return; }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Roaming:
                    agent.speed = 2f;

                    if (FoundClosestPlayerInRange(25f, 5f))
                    {
                        if (heldObject != null)
                        {
                            SwitchToBehaviourClientRpc((int)State.Throwing);
                            AttemptThrowScrapAtTargetPlayer();
                        }
                        else
                        {
                            if (timeSinceSeenPlayer > 60f)
                            {
                                idlingTimer = 0f;
                                timeSinceSeenPlayer = 0f;
                                Roar(chaseAfterRoar: true);
                            }
                        }
                        break;
                    }

                    MoveTowardsScrapInLineOfSight();

                    break;

                case (int)State.Chasing:

                    if (!TargetClosestPlayerInAnyCase() || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 35f && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        logger.LogDebug("Stop Targeting");
                        targetPlayer = null;
                        SwitchToBehaviourClientRpc((int)State.Roaming);
                        StartSearch(transform.position);
                        //walking = false;
                        //running = false;
                        //DoAnimationClientRpc("startWalk");
                        creatureAnimator.SetTrigger("startWalk");
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
            if (targetPlayer == null) return false;
            return true;
        }

        private void CalculateAgentSpeed()
        {
            if (stunNormalizedTimer > 0f || inSpecialAnimation || inSpecialAnimationWithPlayer != null || currentBehaviourStateIndex == 2/* || (idlingTimer > 0f && currentBehaviourStateIndex == 0)*/)
            {
                agent.speed = 0f;
                agent.acceleration = 200f;
                return;
            }
            creatureAnimator.SetFloat("speedMultiplier", Mathf.Clamp(averageVelocity / 12f * 1.5f, 0.5f, 6f));
            float num = (transform.position - previousPosition).magnitude / (Time.deltaTime / 1.4f);
            if (velocityInterval <= 0f)
            {
                previousVelocity = averageVelocity;
                velocityInterval = 0.5f;
                velocityAverageCount += 1f;
                if (velocityAverageCount > 5f)
                {
                    averageVelocity += (num - averageVelocity) / 3f;
                }
                else
                {
                    averageVelocity += num;
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
            if (/*IsOwner && */averageVelocity - num > Mathf.Clamp(num * 0.17f, 2f, 100f) && num > 3f && currentBehaviourStateIndex == 1)
            {
                if (wallCollisionSFXDebounce > 0.5f)
                {
                    creatureSFX.PlayOneShot(hitWallSFX, 0.7f);
                    SetEnemyStunned(true, 0.5f);

                    NetworkHandlerHeavy.Instance.ShakePlayerCamerasServerRpc(ScreenShakeType.Big, 15f, transform.position);
                    averageVelocity = 0f;
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
                agent.speed = Mathf.Clamp(agentSpeedWithNegative, -3f, 10f);
                agent.acceleration = Mathf.Clamp(BaseAcceleration - averageVelocity * SpeedAccelerationEffect, 4f, 25f);
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

        private void MoveTowardsScrapInLineOfSight()
        {
            if (heldObject != null || inSpecialAnimation || inSpecialAnimationWithPlayer != null) { return; }
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

        public void SetOutsideOrInside()
        {
            GameObject closestOutsideNode = GetClosestAINode(RoundManager.Instance.outsideAINodes.ToList());
            GameObject closestInsideNode = GetClosestAINode(RoundManager.Instance.insideAINodes.ToList());

            if (Vector3.Distance(transform.position, closestOutsideNode.transform.position) < Vector3.Distance(transform.position, closestInsideNode.transform.position))
            {
                SetEnemyOutsideClientRpc(true);
            }
        }

        public GameObject GetClosestAINode(List<GameObject> nodes)
        {
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null;
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

        private void PickUpScrapIfClose()
        {
            GrabbableObject scrap = targetObject.GetComponent<GrabbableObject>();

            if (scrap == null || scrap.isHeldByEnemy || scrap.isHeld) // TODO: Test this
            {
                targetObject = null;
                StartSearch(transform.position);
                //DoAnimationClientRpc("startWalk");
                creatureAnimator.SetTrigger("startWalk");
                //walking = true;
                //running = false;
                return;
            }

            if (Vector3.Distance(transform.position, targetObject.transform.position) < 1.5f)
            {
                inSpecialAnimation = true;
                StartCoroutine(PickUpScrapCoroutine());
            }
        }

        public IEnumerator PickUpScrapCoroutine() // TODO: Needs testing
        {
            //DoAnimationClientRpc("pickup");
            creatureAnimator.SetTrigger("pickup");
            yield return new WaitForSeconds(0.1f);

            GrabbableObject grabbingObject = targetObject.GetComponent<GrabbableObject>();
            targetObject = null;
            grabbingObject.parentObject = RightHandTransform;
            grabbingObject.hasHitGround = false;
            grabbingObject.GrabItemFromEnemy(this);
            grabbingObject.EnablePhysics(false);
            HoarderBugAI.grabbableObjectsInMap.Remove(grabbingObject.gameObject);
            heldObject = grabbingObject;

            yield return new WaitForSeconds(0.5f);
            StartSearch(transform.position);
            inSpecialAnimation = false;
            //walking = true;
            //running = false;
            //DoAnimationClientRpc("startWalk");
            creatureAnimator.SetTrigger("startWalk");
        }

        public void AttemptThrowScrapAtTargetPlayer()
        {
            logger.LogDebug("Throwing held object at target player");
            inSpecialAnimation = true;
            StartCoroutine(ThrowScrapCoroutine());
        }

        public IEnumerator ThrowScrapCoroutine()
        {
            inSpecialAnimation = true;

            Vector3 lastKnownPlayerPosition = targetPlayer.transform.position;

            creatureAnimator.SetTrigger("throw");
            yield return new WaitForSeconds(0.5f);
            DropItem(lastKnownPlayerPosition);
            yield return new WaitForSeconds(0.1f);
            if (Vector3.Distance(lastKnownPlayerPosition, targetPlayer.transform.position) < 1.5f)
            {
                targetPlayer.DamagePlayer(15, true, true, CauseOfDeath.Inertia);
            }

            inSpecialAnimation = false;

            if (timeSinceSeenPlayer > 60f) { Roar(chaseAfterRoar: true); }
            else
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                creatureAnimator.SetTrigger("startRun");
                //running = true;
                //walking = false;
            }
        }

        private void DropItem(Vector3 targetFloorPosition) // TODO: This works but needs to be tested on network
        {
            if (heldObject == null)
            {
                //logger.LogWarning("My held item is null when attempting to drop it!!");
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
            heldObject = null;
        }

        public void ThrowTargetPlayer()
        {
            inSpecialAnimation = true;
            StartCoroutine(ThrowTargetPlayerCoroutine());
        }

        public IEnumerator ThrowTargetPlayerCoroutine() // TODO: Test this on network
        {
            targetPlayer.playerRigidbody.isKinematic = false;

            Vector3 forwardDirection = transform.TransformDirection(Vector3.forward).normalized * 2;
            Vector3 upDirection = transform.TransformDirection(Vector3.up).normalized;
            Vector3 throwDirection = (forwardDirection + upDirection).normalized;

            // Grab player
            logger.LogDebug("Grabbing player: " + targetPlayer.playerUsername);
            //DoAnimationClientRpc("pickupThrow");
            creatureAnimator.SetTrigger("pickupThrow");
            yield return new WaitForSeconds(0.1f);
            inSpecialAnimationWithPlayer = targetPlayer;
            targetPlayer.inAnimationWithEnemy = this;
            yield return new WaitForSeconds(1.32f);
            inSpecialAnimationWithPlayer = null;
            targetPlayer.inAnimationWithEnemy = null;
            targetPlayer.transform.position = transform.position; // TODO: Test this

            // Throw player
            logger.LogDebug("Applying force: " + throwDirection * throwForce);
            targetPlayer.playerRigidbody.velocity = Vector3.zero;
            targetPlayer.externalForceAutoFade += throwDirection * throwForce;
            yield return new WaitUntil(() => targetPlayer.thisController.isGrounded || targetPlayer.isPlayerDead);

            targetPlayer.playerRigidbody.isKinematic = true;
            inSpecialAnimation = false;
            logger.LogDebug("Grounded");

            // Damage player
            targetPlayer.DamagePlayer(40, true, true, CauseOfDeath.Inertia);
            if (targetPlayer.isPlayerDead)
            {
                targetPlayer = null;
            }
            else
            {
                targetPlayer.drunkness = 0.1f;
                targetPlayer.sprintMeter = Mathf.Clamp(targetPlayer.sprintMeter - 0.5f, 0f, 1f);
                if (!targetPlayer.criticallyInjured) { targetPlayer.playerBodyAnimator.SetBool("Limp", true); }
                NetworkHandlerHeavy.Instance.ShakePlayerCamerasServerRpc(ScreenShakeType.Big, 10f, targetPlayer.transform.position);
            }

            Roar(chaseAfterRoar: true, noLimpAfterRoar: true);
        }

        public void Roar(bool chaseAfterRoar = false, bool noLimpAfterRoar = false)
        {
            inSpecialAnimation = true;
            idlingTimer = 0f;
            SetEnemyStunned(true, 3f);
            StartCoroutine(RoarCoroutine(chaseAfterRoar, noLimpAfterRoar));
        }

        public IEnumerator RoarCoroutine(bool chaseAfterRoar = false, bool noLimpAfterRoar = false)
        {
            creatureVoice.PlayOneShot(roarSFX, 1f);
            //DoAnimationClientRpc("roar");
            creatureAnimator.SetTrigger("roar");
            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => Vector3.Distance(x.transform.position, transform.position) < 15f))
            {
                player.JumpToFearLevel(1f);
            }
            yield return new WaitUntil(() => stunNormalizedTimer <= 0f);

            inSpecialAnimation = false;
            if (noLimpAfterRoar)
            {
                if (!targetPlayer.criticallyInjured) { targetPlayer.playerBodyAnimator.SetBool("Limp", false); }
            }
            if (chaseAfterRoar)
            {
                //running = true;
                //walking = false;
                SwitchToBehaviourClientRpc((int)State.Chasing);
                creatureAnimator.SetTrigger("startRun");
                //DoAnimationClientRpc("startRun");
            }
        }

        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            base.DetectNoise(noisePosition, noiseLoudness, timesPlayedInOneSpot, noiseID);
            if (currentBehaviourStateIndex == (int)State.Roaming)
            {
                
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

                //idlingTimer = 2f;
                //running = false;
                //walking = false;
                //creatureAnimator.SetTrigger("stopWalk");
                //DoAnimationClientRpc("stopWalk");
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (!throwingPlayerEnabled) { return; } // TODO: For testing, remove later
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && currentBehaviourStateIndex != (int)State.Throwing)
            {
                logger.LogDebug($"{player.playerUsername} collided with SCP-427-1");
                logger.LogDebug("Throwing player");
                targetPlayer = player;
                SwitchToBehaviourClientRpc((int)State.Throwing);
                ThrowTargetPlayer();
            }
        }

        public override void HitFromExplosion(float distance)
        {
            base.HitFromExplosion(distance);

            if (!isEnemyDead)
            {
                enemyHP -= 1;
                if (enemyHP <= 0 && IsOwner)
                {
                    KillEnemyOnOwnerClient();
                }
                else
                {
                    Roar(chaseAfterRoar: true);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            CancelSpecialAnimationWithPlayer();
            inSpecialAnimation = false;

            targetObject = null;
            DropItem(transform.position);

            base.KillEnemy(false);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null, bool playHitSFX = true, int hitID = -1)
        {
            return;
        }

        public override void CancelSpecialAnimationWithPlayer()
        {
            if ((bool)inSpecialAnimationWithPlayer)
            {
                inSpecialAnimationWithPlayer.playerRigidbody.isKinematic = true;
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                inSpecialAnimationWithPlayer.snapToServerPosition = false;
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
                inSpecialAnimationWithPlayer = null;
            }
        }

        // RPC's

        /*[ClientRpc]
        private void DoAnimationClientRpc(string animationName)
        {
            logger.LogDebug("Animation: " + animationName);
            creatureAnimator.SetTrigger(animationName);
        }*/

        [ClientRpc]
        private void SetEnemyOutsideClientRpc(bool value)
        {
            SetEnemyOutside(value);
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

            if (enemyCollider != null && !enemyCollider.mainScript.isEnemyDead && enemyCollider.mainScript.enemyType.name == "SCP4271Enemy" && !__instance.hasExploded)
            {
                __instance.SetOffMineAnimation();
                __instance.sendingExplosionRPC = true;
                __instance.ExplodeMineServerRpc();
            }
        }
    }
}

// TODO: statuses: shakecamera, playerstun, drunkness, fear, insanity