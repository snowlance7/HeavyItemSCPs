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

        readonly float throwForce = 70f;

        // Speed values
        public static float SpeedAccelerationEffect = 2.3f; // default 2.4f
        public static float BaseAcceleration = 30f; // default 42f
        public static float SpeedIncreaseRate = 4f; // default 5f

        public static bool throwingPlayerEnabled = true; // TESTING REMOVE LATER

        public static float heldObjectVerticalOffset = 4f; // TODO: Get this from testing

        GameObject targetObject = null!;
        GrabbableObject heldObject = null!;

        float averageVelocity;
        Vector3 previousPosition;
        float previousVelocity;
        float velocityInterval;
        float velocityAverageCount;
        float wallCollisionSFXDebounce;
        float agentSpeedWithNegative;

        bool hasEnteredChaseMode;

        //bool walking;
        //bool running;
        //bool roaring;
        bool throwingPlayer;
        //bool throwingScrap;
        bool grabbingPlayer;
        bool pickingUpScrap;

        float timeSinceSeenPlayer = 100f;
        float idlingTimer = 0f;

        // For throwing player
        Vector3 forwardDirection;
        Vector3 upDirection;
        Vector3 throwDirection;

#pragma warning disable 0649
        public Transform turnCompass = null!;
        public Transform RightHandTransform = null!;

        public AudioClip[] stompSFXList = null!;
        public AudioClip roarSFX = null!;
        public AudioClip warningRoarSFX = null!;
        public AudioClip hitWallSFX = null!;

        public NetworkAnimator networkAnimator = null!;
#pragma warning restore 0649

        private NetworkVariable<NetworkBehaviourReference> _playerNetVar = new NetworkVariable<NetworkBehaviourReference>();

        PlayerControllerB PlayerBeingThrown
        {
            get
            {
                return (PlayerControllerB)(NetworkBehaviour)_playerNetVar.Value;
            }
            set
            {
                if (value == null)
                {
                    _playerNetVar.Value = null;
                }
                else
                {
                    _playerNetVar.Value = new NetworkBehaviourReference(value);
                }
            }
        }

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

            //SetOutsideOrInside();
            //SetEnemyOutsideClientRpc(true);

            StartSearch(transform.position);
            networkAnimator.SetTrigger("startWalk");
        }

        public override void Update()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            };

            base.Update();

            if (grabbingPlayer && PlayerBeingThrown == localPlayer)
            {
                localPlayer.transform.position = RightHandTransform.position;
            }

            //if (!NetworkManager.Singleton.IsServer || !NetworkManager.Singleton.IsHost) { return; } // TODO: Test this

            if (currentBehaviourStateIndex == (int)State.Roaming) { timeSinceSeenPlayer += Time.deltaTime; }

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

            if (heldObject != null)
            {
                heldObject.transform.position = RightHandTransform.position + new Vector3(0f, heldObjectVerticalOffset, 0f);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            //logger.LogDebug("Doing AI Interval");

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
                            if (timeSinceSeenPlayer > 30f)
                            {
                                SwitchToBehaviourClientRpc((int)State.Chasing);
                                Roar(chaseAfterRoar: true);
                            }
                            else
                            {
                                SwitchToBehaviourClientRpc((int)State.Chasing);
                                networkAnimator.SetTrigger("startRun");
                            }

                            idlingTimer = 0f;
                            timeSinceSeenPlayer = 0f;
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
                        networkAnimator.SetTrigger("startWalk");
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
            if (stunNormalizedTimer > 0f || currentBehaviourStateIndex == 2 || pickingUpScrap || grabbingPlayer/* || (idlingTimer > 0f && currentBehaviourStateIndex == 0)*/)
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
            if (IsOwner && averageVelocity - num > Mathf.Clamp(num * 0.17f, 2f, 100f) && num > 3f && currentBehaviourStateIndex == 1)
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
                networkAnimator.SetTrigger("startWalk");
                return;
            }

            if (Vector3.Distance(transform.position, targetObject.transform.position) < 1.5f)
            {
                pickingUpScrap = true;
                StartCoroutine(PickUpScrapCoroutine());
            }
        }

        public IEnumerator PickUpScrapCoroutine() // TODO: Needs testing
        {
            networkAnimator.SetTrigger("pickup");
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
            pickingUpScrap = false;
            networkAnimator.SetTrigger("startWalk");
        }

        public void AttemptThrowScrapAtTargetPlayer()
        {
            logger.LogDebug("Throwing held object at target player");
            //throwingScrap = true;
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

            //throwingScrap = false;

            if (timeSinceSeenPlayer > 60f)
            {
                Roar(chaseAfterRoar: true);
            }
            else
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                networkAnimator.SetTrigger("startRun");
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

        public void GrabTargetPlayer() // TODO: Set up to run with animation
        {
            if (PlayerBeingThrown == null) { return; }
            PlayerControllerB player = PlayerBeingThrown;
            player.playerRigidbody.isKinematic = false;

            forwardDirection = transform.TransformDirection(Vector3.forward).normalized * 2;
            upDirection = transform.TransformDirection(Vector3.up).normalized;
            throwDirection = (forwardDirection + upDirection).normalized;

            // Grab player
            logger.LogDebug("Grabbing player: " + player.playerUsername);
            grabbingPlayer = true;
        }

        public void ThrowTargetPlayer() // TODO: Set up to run with animation
        {
            if (PlayerBeingThrown == null) { return; }
            StartCoroutine(ThrowPlayerCoroutine());
        }

        public IEnumerator ThrowPlayerCoroutine() // TODO: Test this on network
        {
            PlayerControllerB player = PlayerBeingThrown;
            grabbingPlayer = false;
            player.transform.position = transform.position; // TODO: Test this

            // Throw player
            logger.LogDebug("Applying force: " + throwDirection * throwForce);
            player.playerRigidbody.velocity = Vector3.zero;
            player.externalForceAutoFade += throwDirection * throwForce;

            yield return new WaitUntil(() => localPlayer.thisController.isGrounded || localPlayer.isPlayerDead || throwingPlayer == false);

            player.playerRigidbody.isKinematic = true;
            logger.LogDebug("Grounded");

            // Damage player
            player.DamagePlayer(40, true, true, CauseOfDeath.Inertia);
            if (localPlayer == PlayerBeingThrown && !localPlayer.isPlayerDead)
            {
                PlayerHitGroundServerRpc();
            }

            InjureLocalPlayer();

            throwingPlayer = false;
            logger.LogDebug("Finished throwing player");

            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                targetPlayer = player;

                logger.LogDebug("Roaring");
                Roar(chaseAfterRoar: true);
            }
        }

        public void InjureLocalPlayer()
        {
            localPlayer.JumpToFearLevel(0.8f);
            localPlayer.drunkness = 0.1f;
            localPlayer.MakeCriticallyInjured(true);
        }

        public override void OnCollideWithPlayer(Collider other) // TODO: This only runs on client???
        {
            base.OnCollideWithPlayer(other);
            //if (!throwingPlayerEnabled) { return; } // TODO: For testing, remove later
            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && currentBehaviourStateIndex != (int)State.Throwing && !throwingPlayer)
            {
                logger.LogDebug($"{player.playerUsername} collided with SCP-427-1");
                logger.LogDebug("Throwing player");

                throwingPlayer = true;

                ThrowPlayerServerRpc((int)player.actualClientId);
            }
        }

        public void Roar(bool chaseAfterRoar = false) // TODO: Test this on network
        {
            idlingTimer = 0f;
            SetEnemyStunned(true, 3f);
            StartCoroutine(RoarCoroutine(chaseAfterRoar));
        }

        public IEnumerator RoarCoroutine(bool chaseAfterRoar = false)
        {
            creatureVoice.PlayOneShot(roarSFX, 1f);
            networkAnimator.SetTrigger("roar");
            foreach (var player in StartOfRound.Instance.allPlayerScripts.Where(x => Vector3.Distance(x.transform.position, transform.position) < 15f)) // TODO: set up networking
            {
                player.JumpToFearLevel(1f);
            }
            yield return new WaitUntil(() => stunNormalizedTimer <= 0f);

            if (chaseAfterRoar)
            {
                SwitchToBehaviourClientRpc((int)State.Chasing);
                networkAnimator.SetTrigger("startRun");
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
                //DoAnimationClientRpc("stopWalk"); // TODO: Set this up
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
                else if (!throwingPlayer && !pickingUpScrap && !grabbingPlayer)
                {
                    Roar(chaseAfterRoar: true);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            CancelSpecialAnimationWithPlayer();
            StopAllCoroutines();
            grabbingPlayer = false;
            throwingPlayer = false;

            if (PlayerBeingThrown != null)
            {
                PlayerBeingThrown.playerRigidbody.isKinematic = true;
                PlayerBeingThrown.disableMoveInput = false;
            }

            targetObject = null;
            DropItem(transform.position);

            base.KillEnemy(false);
        }

        public override void HitEnemy(int force = 0, PlayerControllerB playerWhoHit = null, bool playHitSFX = true, int hitID = -1)
        {
            return;
        }

        // RPC's

        [ServerRpc(RequireOwnership = false)]
        private void ThrowPlayerServerRpc(int clientId)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                PlayerBeingThrown = NetworkHandlerHeavy.PlayerFromId((ulong)clientId);
                throwingPlayer = true;

                SwitchToBehaviourClientRpc((int)State.Throwing);
                networkAnimator.SetTrigger("pickupThrow");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PlayerHitGroundServerRpc()
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                PlayerHitGroundClientRpc();
            }
        }

        [ClientRpc]
        private void PlayerHitGroundClientRpc()
        {
            throwingPlayer = false;
        }

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