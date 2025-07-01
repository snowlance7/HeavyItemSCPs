using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TextCore.Text;
using static HeavyItemSCPs.Plugin;
using static HeavyItemSCPs.Utils;
using static UnityEngine.VFX.VisualEffectControlTrackController;

namespace HeavyItemSCPs.Items.SCP513
{
    public class SCP513_1AI : MonoBehaviour // TODO: Changing this to monobehavior and use scp513 for network functions
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP513_1AI? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public NavMeshAgent agent;
        public Animator creatureAnimator;
        public float AIIntervalTime = 0.2f;
        public AudioSource creatureSFX;
        public AudioSource creatureVoice;

        public Transform turnCompass;
        public GameObject enemyMesh;
        public GameObject ScanNode;
        public AudioClip[] BellSFX;
        public AudioClip[] AmbientSFX;
        public AudioClip[] ScareSFX;
        public AudioClip[] StepChaseSFX;

        public AudioClip[] MinorSoundEffectSFX;
        public AudioClip[] MajorSoundEffectSFX;

        public GameObject SoundObjectPrefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        GameObject[] insideAINodes => RoundManager.Instance.insideAINodes ?? GameObject.FindGameObjectsWithTag("AINode");
        GameObject[] outsideAINodes => RoundManager.Instance.outsideAINodes ?? GameObject.FindGameObjectsWithTag("OutsideAINode");
        public bool isOutside => !localPlayer.isInsideFactory;
        public GameObject[] allAINodes => isOutside ? outsideAINodes : insideAINodes;

        public bool gotFarthestNodeAsync;
        public float updateDestinationInterval;
        public NavMeshPath? path1;
        public bool moveTowardsDestination;
        public bool movingTowardsTargetPlayer;
        public Vector3 destination;

        public float mostOptimalDistance;
        public Transform? targetNode;
        public GameObject[] nodesTempArray = [];
        public float pathDistance;
        public int getFarthestNodeAsyncBookmark;

        public State currentBehaviourState;

        public EnemyAI? mimicEnemy;
        Coroutine? mimicEnemyRoutine;
        public PlayerControllerB? mimicPlayer;

        bool enemyMeshEnabled;
        public bool facePlayer;

        float cooldownMultiplier;
        float timeSinceCommonEvent;
        float timeSinceUncommonEvent;
        float timeSinceRareEvent;

        float nextCommonEventTime;
        float nextUncommonEventTime;
        float nextRareEventTime;

        int currentFootstepSurfaceIndex;
        int previousFootstepClip;

        float stareTime;
        public bool playerHasLOS;

        Transform? farthestNodeFromTargetPlayer;
        bool gettingFarthestNodeFromPlayerAsync;
        int maxAsync;
        bool stalkRetreating;
        float evadeStealthTimer;
        bool wasInEvadeMode;
        List<Transform> ignoredNodes = [];

        // Constants
        int hashRunSpeed;
        const float maxInsanity = 50f;
        const float maxInsanityThreshold = 35;
        const float minCooldown = 0.5f;
        const float maxCooldown = 1f;
        public const float LOSAngle = 45f;
        public const float LOSOffset = 1f;

        // Configs
        float commonEventMinCooldown = 5f;
        float commonEventMaxCooldown = 15f;
        float uncommonEventMinCooldown = 15f;
        float uncommonEventMaxCooldown = 30f;
        float rareEventMinCooldown = 100f;
        float rareEventMaxCooldown = 200f;

        public enum State
        {
            InActive,
            Manifesting,
            Chasing,
            Stalking,
            MimicPlayer
        }

        public void Start()
        {
            logger.LogDebug("SCP-513-1 spawned");

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            //insideAINodes = GameObject.FindGameObjectsWithTag("AINode");
            //outsideAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");

            path1 = new NavMeshPath();

            hashRunSpeed = Animator.StringToHash("speed");
            currentBehaviourState = (int)State.InActive;

            nextCommonEventTime = commonEventMaxCooldown;
            nextUncommonEventTime = uncommonEventMaxCooldown;
            nextRareEventTime = rareEventMaxCooldown;
        }

        public void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                DespawnMimicEnemy();

                if (mimicPlayer != null)
                {
                    Utils.MakePlayerInvisible(mimicPlayer, false);
                    mimicPlayer = null;
                }
            }
        }

        public void Update()
        {
            if (SCP513Behavior.Instance == null || !SCP513Behavior.Instance.localPlayerHaunted || StartOfRound.Instance.shipIsLeaving || StartOfRound.Instance.inShipPhase)
            {
                Destroy(gameObject);
                return;
            }

            if (localPlayer.isPlayerDead)
            {
                SCP513Behavior.Instance.localPlayerHaunted = false;
                Destroy(gameObject);
                return;
            }

            if (updateDestinationInterval >= 0f)
            {
                updateDestinationInterval -= Time.deltaTime;
            }
            else
            {
                DoAIInterval();
                updateDestinationInterval = AIIntervalTime + UnityEngine.Random.Range(-0.015f, 0.015f);
            }

            //float newFear = targetPlayer.insanityLevel / maxInsanity;
            //targetPlayer.playersManager.fearLevel = Mathf.Max(targetPlayer.playersManager.fearLevel, newFear); // Change fear based on insanity

            //cooldownMultiplier = 1f - localPlayer.playersManager.fearLevel;

            float t = Mathf.Clamp01(Plugin.localPlayer.insanityLevel / maxInsanityThreshold);
            cooldownMultiplier = Mathf.Lerp(minCooldown, maxCooldown, t);

            timeSinceCommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceUncommonEvent += Time.deltaTime * cooldownMultiplier;
            timeSinceRareEvent += Time.deltaTime * cooldownMultiplier;

            playerHasLOS = localPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset, LOSAngle);

            if (playerHasLOS)
            {
                stareTime += Time.deltaTime;
            }
            else
            {
                stareTime = 0f;
            }

            if (currentBehaviourState == State.Stalking)
            {
                if (gettingFarthestNodeFromPlayerAsync && localPlayer != null)
                {
                    float distanceFromPlayer = Vector3.Distance(base.transform.position, localPlayer.transform.position);
                    if (distanceFromPlayer < 16f)
                    {
                        maxAsync = 100;
                    }
                    else if (distanceFromPlayer < 40f)
                    {
                        maxAsync = 25;
                    }
                    else
                    {
                        maxAsync = 4;
                    }
                    Transform? transform = ChooseFarthestNodeFromPosition(localPlayer.transform.position, avoidLineOfSight: true, 0, doAsync: true, maxAsync, capDistance: true);
                    if (!gotFarthestNodeAsync)
                    {
                        return;
                    }
                    farthestNodeFromTargetPlayer = transform;
                    gettingFarthestNodeFromPlayerAsync = false;
                }
                if (playerHasLOS)
                {
                    if (!stalkRetreating)
                    {
                        stalkRetreating = true;
                        agent.speed = 0f;
                        evadeStealthTimer = 0f;
                    }
                    else if (evadeStealthTimer > 0.5f)
                    {
                        evadeStealthTimer = 0f;
                    }
                }

                if (stalkRetreating)
                {
                    if (!wasInEvadeMode)
                    {
                        RoundManager.Instance.PlayAudibleNoise(base.transform.position, 7f, 0.8f);
                        wasInEvadeMode = true;
                    }
                    evadeStealthTimer += Time.deltaTime;
                    if (evadeStealthTimer > 5f)
                    {
                        evadeStealthTimer = 0f;
                        stalkRetreating = false;
                    }
                }
                else
                {
                    if (wasInEvadeMode)
                    {
                        wasInEvadeMode = false;
                        evadeStealthTimer = 0f;
                    }
                }
            }
        }

        public void LateUpdate()
        {
            creatureAnimator.SetFloat(hashRunSpeed, agent.velocity.magnitude / 2);

            if (facePlayer)
            {
                turnCompass.LookAt(Plugin.localPlayer.gameplayCamera.transform.position);
                transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 30f * Time.deltaTime);
            }
        }

        public void SwitchToBehavior(State state)
        {
            if (currentBehaviourState == state) { return; }
            currentBehaviourState = state;

            if (mimicPlayer != null)
            {
                Utils.MakePlayerInvisible(mimicPlayer, false);
                mimicPlayer = null;
            }
        }

        public void DoAIInterval()
        {
            if (moveTowardsDestination)
            {
                agent.SetDestination(destination);
            }

            EnableEnemyMesh(currentBehaviourState != (int)State.InActive);
            //SetEnemyOutside(!localPlayer.isInsideFactory);

            switch (currentBehaviourState)
            {
                case State.InActive:
                    agent.speed = 0f;
                    facePlayer = false;
                    creatureSFX.volume = 1f;

                    break;

                case State.Manifesting:
                    agent.speed = 0f;
                    creatureSFX.volume = 1f;

                    break;

                case State.Chasing:
                    creatureSFX.volume = 1f;

                    SetDestinationToPosition(localPlayer.transform.position);

                    break;

                case State.Stalking:
                    creatureSFX.volume = 0.5f;

                    if (stalkRetreating)
                    {
                        agent.speed = 15f;
                        creatureAnimator.SetBool("armsCrossed", true);
                        facePlayer = true;

                        StalkingAvoidPlayer();
                    }
                    else
                    {
                        agent.speed = Vector3.Distance(transform.position, localPlayer.transform.position); // TODO: Test this
                        creatureAnimator.SetBool("armsCrossed", false);
                        facePlayer = false;

                        StalkingChooseClosestNodeToPlayer();
                    }

                    break;

                case State.MimicPlayer:

                    if (mimicPlayer == null)
                    {
                        SwitchToBehavior(State.MimicPlayer);
                        return;
                    }

                    if (!mimicPlayer.isPlayerControlled)
                    {
                        Utils.MakePlayerInvisible(mimicPlayer, false);
                        mimicPlayer = null;
                        SwitchToBehavior(State.MimicPlayer);
                        return;
                    }

                    transform.position = mimicPlayer.transform.position;
                    SetDestinationToPosition(mimicPlayer.transform.position);

                    break;

                default:
                    logger.LogWarning("Invalid state: " + currentBehaviourState);
                    break;
            }

            if (Utils.testing) { return; }

            if (timeSinceCommonEvent > nextCommonEventTime)
            {
                logger.LogDebug("Running Common Event");
                timeSinceCommonEvent = 0f;
                nextCommonEventTime = UnityEngine.Random.Range(commonEventMinCooldown, commonEventMaxCooldown);
                HallucinationManager.Instance!.RunRandomEvent(0);
                return;
            }
            if (timeSinceUncommonEvent > nextUncommonEventTime)
            {
                logger.LogDebug("Running Uncommon Event");
                timeSinceUncommonEvent = 0f;
                nextUncommonEventTime = UnityEngine.Random.Range(uncommonEventMinCooldown, uncommonEventMaxCooldown);
                HallucinationManager.Instance!.RunRandomEvent(1);
                return;
            }
            if (timeSinceRareEvent > nextRareEventTime)
            {
                logger.LogDebug("Running Rare Event");
                timeSinceRareEvent = 0f;
                nextRareEventTime = UnityEngine.Random.Range(rareEventMinCooldown, rareEventMaxCooldown);
                HallucinationManager.Instance!.RunRandomEvent(2);
                return;
            }
        }

        public bool SetDestinationToPosition(Vector3 position, bool checkForPath = false)
        {
            if (checkForPath)
            {
                position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 1.75f);
                path1 = new NavMeshPath();
                if (!agent.CalculatePath(position, path1))
                {
                    return false;
                }
                if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
                {
                    return false;
                }
            }
            moveTowardsDestination = true;
            movingTowardsTargetPlayer = false;
            destination = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit, -1f);
            return true;
        }

        public Vector3 GetRandomPositionAroundPlayer(float minDistance, float maxDistance, int cap)
        {
            Vector3 pos = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(localPlayer.transform.position, maxDistance, RoundManager.Instance.navHit);

            int _cap = 0;
            while ((Physics.Linecast(localPlayer.gameplayCamera.transform.position, pos + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) || Vector3.Distance(localPlayer.transform.position, pos) < minDistance) && _cap < cap)
            {
                logger.LogDebug("Reroll");
                _cap++;
                pos = RoundManager.Instance.GetRandomNavMeshPositionInRadiusSpherical(localPlayer.transform.position, maxDistance, RoundManager.Instance.navHit);
            }

            return pos;
        }

        public Transform? TryFindingHauntPosition()
        {
            for (int i = 0; i < allAINodes.Length; i++)
            {
                if (!Physics.Linecast(localPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault) && !playerHasLOS)
                {
                    logger.LogDebug($"Player distance to haunt position: {Vector3.Distance(localPlayer.transform.position, allAINodes[i].transform.position)}");
                    return allAINodes[i].transform;
                }
            }
            return null;
        }

        public Transform? ChoosePositionInFrontOfPlayer(float minDistance, float maxDistance)
        {
            logger.LogDebug("Choosing position in front of player");
            Transform? result = null;
            logger.LogDebug(allAINodes.Count() + " ai nodes");
            foreach (var node in allAINodes)
            {
                if (node == null) { continue; }
                Vector3 nodePos = node.transform.position + Vector3.up * LOSOffset;
                Vector3 playerPos = localPlayer.gameplayCamera.transform.position;
                float distance = Vector3.Distance(playerPos, nodePos);
                if (distance < minDistance || distance > maxDistance) { continue; }
                if (Physics.Linecast(nodePos, playerPos, StartOfRound.Instance.collidersAndRoomMaskAndDefault, queryTriggerInteraction: QueryTriggerInteraction.Ignore)) { continue; }
                if (!localPlayer.HasLineOfSightToPosition(nodePos, LOSAngle)) { continue; }

                mostOptimalDistance = distance;
                result = node.transform;
            }

            logger.LogDebug($"null: {targetNode == null}");
            return result;
        }
        public bool PathIsIntersectedByLineOfSight(Vector3 targetPos, bool calculatePathDistance = false, bool avoidLineOfSight = true, bool checkLOSToTargetPlayer = false)
        {
            pathDistance = 0f;
            if (agent.isOnNavMesh && !agent.CalculatePath(targetPos, path1))
            {
                return true;
            }
            if (path1 == null || path1.corners.Length == 0)
            {
                return true;
            }
            if (Vector3.Distance(path1.corners[path1.corners.Length - 1], RoundManager.Instance.GetNavMeshPosition(targetPos, RoundManager.Instance.navHit, 2.7f)) > 1.55f)
            {
                return true;
            }
            bool flag = false;
            if (calculatePathDistance)
            {
                for (int j = 1; j < path1.corners.Length; j++)
                {
                    pathDistance += Vector3.Distance(path1.corners[j - 1], path1.corners[j]);
                    if ((!avoidLineOfSight && !checkLOSToTargetPlayer) || j > 15)
                    {
                        continue;
                    }
                    if (!flag && j > 8 && Vector3.Distance(path1.corners[j - 1], path1.corners[j]) < 2f)
                    {
                        flag = true;
                        continue;
                    }
                    flag = false;
                    if (localPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(path1.corners[j - 1], localPlayer.transform.position + Vector3.up * 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true;
                    }
                    if (avoidLineOfSight && Physics.Linecast(path1.corners[j - 1], path1.corners[j], 262144))
                    {
                        return true;
                    }
                }
            }
            else if (avoidLineOfSight)
            {
                for (int k = 1; k < path1.corners.Length; k++)
                {
                    if (!flag && k > 8 && Vector3.Distance(path1.corners[k - 1], path1.corners[k]) < 2f)
                    {
                        flag = true;
                        continue;
                    }
                    if (localPlayer != null && checkLOSToTargetPlayer && !Physics.Linecast(Vector3.Lerp(path1.corners[k - 1], path1.corners[k], 0.5f) + Vector3.up * 0.25f, localPlayer.transform.position + Vector3.up * 0.25f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        return true;
                    }
                    if (Physics.Linecast(path1.corners[k - 1], path1.corners[k], 262144))
                    {
                        return true;
                    }
                    if (k > 15)
                    {
                        return false;
                    }
                }
            }
            return false;
        }
        public Transform? ChooseClosestNodeToPosition(Vector3 pos, bool avoidLineOfSight = false, int offset = 0)
        {
            nodesTempArray = allAINodes.OrderBy((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
            if (nodesTempArray.Length <= 0) { logger.LogError("No nodes found in choose closest node to position"); return null; }
            Transform result = nodesTempArray[0].transform;
            for (int i = 0; i < nodesTempArray.Length; i++)
            {
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: false, avoidLineOfSight))
                {
                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    if (offset == 0 || i >= nodesTempArray.Length - 1)
                    {
                        break;
                    }
                    offset--;
                }
            }
            return result;
        }

        public Transform? ChooseStalkPosition(Vector3 pos, float minDistance)
        {
            nodesTempArray = allAINodes.OrderBy((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
            Transform? result = null;
            for (int i = 0; i < nodesTempArray.Length; i++)
            {
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: true, avoidLineOfSight: true, checkLOSToTargetPlayer: false) && !Physics.Linecast(localPlayer.gameplayCamera.transform.position, nodesTempArray[i].transform.position + Vector3.up * LOSOffset, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    float distance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    if (distance < minDistance) { continue; }

                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    return result;
                }
            }

            for (int i = 0; i < nodesTempArray.Length; i++)
            {
                if (!PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: true, avoidLineOfSight: true, checkLOSToTargetPlayer: true))
                {
                    float distance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    if (distance < minDistance) { continue; }

                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    return result;
                }
            }

            return ChooseFarthestNodeFromPosition(pos);
        }

        public void StalkingChooseClosestNodeToPlayer()
        {
            if (targetNode == null)
            {
                targetNode = allAINodes[0].transform;
            }
            Transform transform = ChooseClosestNodeToPosition(localPlayer.transform.position, avoidLineOfSight: true);
            if (transform != null)
            {
                targetNode = transform;
            }
            float num = Vector3.Distance(localPlayer.transform.position, base.transform.position);
            if (num - mostOptimalDistance < 0.1f && (!PathIsIntersectedByLineOfSight(localPlayer.transform.position, calculatePathDistance: true) || num < 3f))
            {
                if (pathDistance > 10f && !ignoredNodes.Contains(targetNode) && ignoredNodes.Count < 4)
                {
                    ignoredNodes.Add(targetNode);
                }
                movingTowardsTargetPlayer = true;
            }
            else
            {
                SetDestinationToPosition(targetNode.position);
            }
        }

        public void StalkingAvoidPlayer()
        {
            if (farthestNodeFromTargetPlayer == null)
            {
                gettingFarthestNodeFromPlayerAsync = true;
                return;
            }
            Transform transform = farthestNodeFromTargetPlayer;
            farthestNodeFromTargetPlayer = null;
            if (transform != null && mostOptimalDistance > 5f && Physics.Linecast(transform.transform.position, localPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                targetNode = transform;
                SetDestinationToPosition(targetNode.position);
                return;
            }

            if (stareTime > 3f)
            {
                stareTime = 0f;
                if (farthestNodeFromTargetPlayer == null)
                {
                    farthestNodeFromTargetPlayer = ChooseFarthestNodeFromPosition(localPlayer.transform.position);
                }

                Teleport(farthestNodeFromTargetPlayer.position);
                HallucinationManager.Instance?.FlickerLights();
                RoundManager.PlayRandomClip(creatureVoice, BellSFX);
            }

            agent.speed = 0f;
        }

        public void EnableEnemyMesh(bool enable)
        {
            if (enemyMeshEnabled == enable) { return; }
            logger.LogDebug($"EnableEnemyMesh({enable})");
            enemyMesh.SetActive(enable);
            ScanNode.SetActive(enable);
            enemyMeshEnabled = enable;
        }

        public void Teleport(Vector3 position)
        {
            logger.LogDebug("Teleporting to " + position.ToString());
            position = RoundManager.Instance.GetNavMeshPosition(position, RoundManager.Instance.navHit);
            agent.Warp(position);
            transform.position = position;
        }

        public void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (currentBehaviourState == State.MimicPlayer) { return; }
            RoundManager.PlayRandomClip(creatureSFX, BellSFX);
            SwitchToBehavior(State.InActive);
            localPlayer.insanityLevel = 0f;
        }

        public void OnCollideWithPlayer(Collider other) // This only runs on client collided with // TODO: Add sounds like these when he collides with player https://www.youtube.com/watch?v=fUKbe4OR6ZA
        {
            if (currentBehaviourState == State.InActive) { return; }
            if (currentBehaviourState == State.MimicPlayer) { return; }
            if (!other.gameObject.TryGetComponent(out PlayerControllerB player)) { return; }
            if (player == null || player != localPlayer) { return; }

            RoundManager.PlayRandomClip(creatureVoice, ScareSFX);
            player.insanityLevel = 50f;
            player.JumpToFearLevel(1f);
            if (localPlayer.drunkness < 0.3f) { localPlayer.drunkness = 0.3f; }
            localPlayer.sprintMeter = 0f;
            localPlayer.DropAllHeldItemsAndSync();
            SwitchToBehavior(State.InActive);
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

        public Transform? ChooseFarthestNodeFromPosition(Vector3 pos, bool avoidLineOfSight = false, int offset = 0, bool doAsync = false, int maxAsyncIterations = 50, bool capDistance = false)
        {
            if (!doAsync || gotFarthestNodeAsync || getFarthestNodeAsyncBookmark <= 0 || nodesTempArray == null || nodesTempArray.Length == 0)
            {
                nodesTempArray = allAINodes.OrderByDescending((GameObject x) => Vector3.Distance(pos, x.transform.position)).ToArray();
            }
            Transform result = nodesTempArray[0].transform;
            int num = 0;
            if (doAsync)
            {
                if (getFarthestNodeAsyncBookmark >= nodesTempArray.Length)
                {
                    getFarthestNodeAsyncBookmark = 0;
                }
                num = getFarthestNodeAsyncBookmark;
                gotFarthestNodeAsync = false;
            }
            for (int i = num; i < nodesTempArray.Length; i++)
            {
                if (doAsync && i - getFarthestNodeAsyncBookmark > maxAsyncIterations)
                {
                    gotFarthestNodeAsync = false;
                    getFarthestNodeAsyncBookmark = i;
                    return null;
                }
                if ((!capDistance || !(Vector3.Distance(base.transform.position, nodesTempArray[i].transform.position) > 60f)) && !PathIsIntersectedByLineOfSight(nodesTempArray[i].transform.position, calculatePathDistance: false, avoidLineOfSight))
                {
                    mostOptimalDistance = Vector3.Distance(pos, nodesTempArray[i].transform.position);
                    result = nodesTempArray[i].transform;
                    if (offset == 0 || i >= nodesTempArray.Length - 1)
                    {
                        break;
                    }
                    offset--;
                }
            }
            getFarthestNodeAsyncBookmark = 0;
            gotFarthestNodeAsync = true;
            return result;
        }

        // Animation Methods

        void PlayFootstepSFX()
        {
            int index;

            if (currentBehaviourState == State.Chasing)
            {
                creatureSFX.pitch = Random.Range(0.93f, 1.07f);
                index = UnityEngine.Random.Range(0, StepChaseSFX.Length);
                creatureSFX.PlayOneShot(StepChaseSFX[index]);
                return;
            }

            GetCurrentMaterialStandingOn();
            index = Random.Range(0, StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length);
            if (index == previousFootstepClip)
            {
                index = (index + 1) % StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length;
            }
            creatureSFX.pitch = Random.Range(0.93f, 1.07f);
            creatureSFX.PlayOneShot(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips[index], 0.6f);
            previousFootstepClip = index;
        }

        internal void OnCollideWithEnemy(Collider other, EnemyAI mainScript)
        {
            return;
        }

        internal void HitEnemyOnLocalClient(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            throw new System.NotImplementedException();
        }

        public void MimicEnemy(string enemyName)
        {
            float maxSpawnTime = 60f;
            float despawnDistance = 3f;

            if (mimicEnemyRoutine != null)
            {
                StopCoroutine(mimicEnemyRoutine);
                DespawnMimicEnemy();
            }

            SCP513Behavior.Instance!.MimicEnemyServerRpc(localPlayer.actualClientId, enemyName);

            IEnumerator MimicEnemyCoroutine(float maxSpawnTime, float despawnDistance)
            {
                yield return new WaitUntil(() => mimicEnemy != null);

                float elapsedTime = 0f;
                while (mimicEnemy != null
                    && mimicEnemy.NetworkObject != null
                    && mimicEnemy.NetworkObject.IsSpawned
                    && localPlayer.isPlayerControlled)
                {
                    yield return null;
                    elapsedTime += Time.deltaTime;
                    float distance = Vector3.Distance(mimicEnemy.transform.position, localPlayer.transform.position);

                    if (elapsedTime > maxSpawnTime || distance < despawnDistance)
                    {
                        break;
                    }

                    mimicEnemy.targetPlayer = localPlayer;
                }

                DespawnMimicEnemy();
            }

            mimicEnemyRoutine = StartCoroutine(MimicEnemyCoroutine(maxSpawnTime, despawnDistance));
        }

        public void DespawnMimicEnemy()
        {
            if (mimicEnemy == null || !mimicEnemy.NetworkObject.IsSpawned) { return; }
            logger.LogDebug("Despawning mimic enemy " + mimicEnemy.enemyType.name);

            switch (mimicEnemy.enemyType.name)
            {
                case "Butler":
                    ButlerEnemyAI.murderMusicAudio.Stop();
                    break;
                default:
                    break;
            }

            mimicEnemy.NetworkObject.Despawn(true);
            mimicEnemy = null;
            mimicEnemyRoutine = null;
        }
    }
}