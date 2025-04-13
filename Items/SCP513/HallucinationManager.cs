using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP513
{
    /*
     Status effects

    - Ears ringing
    - Fear
    - Bleeding
    - Encumbered
    - Insanity
    - 
     */

    internal class HallucinationManager : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public SCP513_1AI? Instance {  get; set; }
        public PlayerControllerB targetPlayer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        delegate void MethodDelegate();

        List<MethodDelegate> commonEvents = new List<MethodDelegate>();
        List<MethodDelegate> uncommonEvents = new List<MethodDelegate>();
        List<MethodDelegate> rareEvents = new List<MethodDelegate>();

        const float LOSOffset = 1f;

        public enum State
        {
            Inactive,
            Active,
            Manifesting,
            Chasing,
            Stalking
        }

        public void Start()
        {
            commonEvents.Add(Jumpscare);
            commonEvents.Add(FlickerLights);
            commonEvents.Add(Stare);
            commonEvents.Add(PlayAmbientSFXNearby);
            commonEvents.Add(PlaySoundEffectMinor);
            commonEvents.Add(WallBloodMessage);
            commonEvents.Add(PlayBellSFX);
            logger.LogDebug("CommonEvents: " + commonEvents.Count);

            uncommonEvents.Add(BlockDoor);
            uncommonEvents.Add(StalkPlayer);
            //uncommonEvents.Add(MimicHazard);
            uncommonEvents.Add(PlayFakeSoundEffectMajor);
            uncommonEvents.Add(ShowFakeShipLeavingDisplayTip);
            uncommonEvents.Add(SpawnFakeBody);
            uncommonEvents.Add(SlowWalkToPlayer);
            uncommonEvents.Add(MimicEnemy);
            logger.LogDebug("UncommonEvents: " + uncommonEvents.Count);

            rareEvents.Add(MimicEnemyChase);
            rareEvents.Add(MimicPlayer);
            rareEvents.Add(ChasePlayer);
            rareEvents.Add(SpawnGhostGirl);
            rareEvents.Add(TurnOffAllLights);
            rareEvents.Add(SpawnFakeLandMineUnderPlayer);
            logger.LogDebug("RareEvents: " + rareEvents.Count);
        }

        public void RunRandomEvent(int eventCategory)
        {
            int index;

            switch (eventCategory)
            {
                case 0:
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
                    logger.LogDebug("Running common event at index " + index);
                    commonEvents[index]?.Invoke();
                    break;
                case 1:
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
                    logger.LogDebug("Running uncommon event at index " + index);
                    uncommonEvents[index]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
                    logger.LogDebug("Running rare event at index " + index);
                    rareEvents[index]?.Invoke();
                    break;
                default:
                    break;
            }
        }

        void SwitchToBehavior(State state)
        {
            Instance.SwitchToBehaviourServerRpc((int)state);
        }

        public void OnDestroy()
        {
            StopAllCoroutines();
        }

        Coroutine? activeCoroutine = null;
        int currentCoroutineTier = -1; // -1 = none, 0 = common, 1 = uncommon, 2 = rare

        private bool TryStartCoroutine(IEnumerator coroutineMethod, int tier)
        {
            if (activeCoroutine != null)
            {
                if (tier <= currentCoroutineTier)
                {
                    // A higher or equal priority coroutine is already running, don't start this one
                    return false;
                }

                // Cancel lower priority coroutine
                StopCoroutine(activeCoroutine);
                activeCoroutine = null;
                currentCoroutineTier = -1;
            }

            activeCoroutine = StartCoroutine(WrapCoroutine(coroutineMethod, tier));
            currentCoroutineTier = tier;
            return true;
        }

        private IEnumerator WrapCoroutine(IEnumerator coroutineMethod, int tier)
        {
            yield return StartCoroutine(coroutineMethod);
            // Once it's done, clear the tracking
            if (tier == currentCoroutineTier)
            {
                SwitchToBehavior(State.Active);
                activeCoroutine = null;
                currentCoroutineTier = -1;
            }
        }

        #region Common

        void Jumpscare()
        {
            logger.LogDebug("Jumpscare");
            IEnumerator DisappearAfterLOS()
            {
                float runSpeed = 15f;

                yield return null;

                Instance.Teleport(GetPositionBehindPlayer(targetPlayer, 1f));
                Instance.creatureAnimator.SetBool("armsCrossed", true);
                SwitchToBehavior(State.Manifesting);
                RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);

                float timeElapsed = 0f;

                while (timeElapsed < 10f)
                {
                    yield return new WaitForSeconds(0.2f);
                    timeElapsed += 0.2f;

                    if (targetPlayer.HasLineOfSightToPosition(Instance.transform.position + Vector3.up * LOSOffset, 70f, 100))
                    {
                        Instance.agent.speed = runSpeed;
                        SwitchToBehavior(State.Chasing);
                        yield break;
                    }
                }
            }

            TryStartCoroutine(DisappearAfterLOS(), 0);
        }

        void FlickerLights()
        {
            logger.LogDebug("FlickerLights");
            RoundManager.Instance.FlickerLights(true, true);
            GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.9f);
        }

        void Stare()
        {
            logger.LogDebug("Stare");
            GameObject[] aiNodes = targetPlayer.isInsideFactory ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
            Transform? outsideLOS = TryFindingHauntPosition(targetPlayer, aiNodes, false);
            if (outsideLOS == null) { return; }


            IEnumerator StareCoroutine()
            {
                float stareTime = 15f;

                yield return null;

                Instance.Teleport(outsideLOS.position);
                SwitchToBehavior(State.Manifesting);
                Instance.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (targetPlayer.HasLineOfSightToPosition(Instance.transform.position + Vector3.up * LOSOffset, 70f, 100))
                    {
                        FlickerLights();
                        SwitchToBehavior(State.Active);

                        yield break;
                    }
                }
            }

            TryStartCoroutine(StareCoroutine(), 0);
        }

        void PlayAmbientSFXNearby()
        {
            logger.LogDebug("PlayAmbientSFXNearby");
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            Instance.Teleport(pos);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);
        }

        void PlaySoundEffectMinor()
        {
            logger.LogDebug("PlaySoundEffectMinor");
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            Instance.Teleport(pos);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);
        }

        void WallBloodMessage()
        {
            logger.LogDebug("WallBloodMessage");
            throw new NotImplementedException();
        }

        void PlayBellSFX()
        {
            logger.LogDebug("PlayBellSFX");
            Instance.Teleport(targetPlayer.transform.position);
            RoundManager.PlayRandomClip(Instance.creatureVoice, Instance.BellSFX);
        }

        #endregion

        #region UnCommon

        void BlockDoor() //
        {
            logger.LogDebug("BlockDoor");
            float doorDistance = 10f;
            float blockPosOffset = 1f;

            DoorLock[] doorLocks = GetDoorLocksNearbyPosition(targetPlayer.transform.position, doorDistance).ToArray();
            if (doorLocks.Length == 0) { return; }
            int index = UnityEngine.Random.Range(0, doorLocks.Length);
            DoorLock doorLock = doorLocks[index];

            var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
            Vector3 blockPos = RoundManager.Instance.GetNavMeshPosition(steelDoorObj.transform.position + Vector3.forward * blockPosOffset);

            IEnumerator StarePlayerCoroutine(Vector3 blockPos)
            {
                float disappearDistance = 15f;
                float disappearTime = 15f;

                yield return null;

                Instance.Teleport(blockPos);
                SwitchToBehavior(State.Manifesting);
                Instance.facePlayer = true;

                float elapsedTime = 0f;
                while (elapsedTime < disappearTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;
                    if (Vector3.Distance(Instance.transform.position, targetPlayer.transform.position) > disappearDistance)
                    {
                        yield break;
                    }
                }
            }

            TryStartCoroutine(StarePlayerCoroutine(blockPos), 1);
        }

        void StalkPlayer()
        {
            logger.LogDebug("StalkPlayer");
            IEnumerator StalkCoroutine()
            {
                float playerDistanceToDisappear = 3f;

                yield return null;

                Vector3 teleportPos = Instance.ChooseClosestNodeToPosition(targetPlayer.transform.position, true).position;
                Instance.Teleport(teleportPos);
                SwitchToBehavior(State.Stalking);

                while (Vector3.Distance(targetPlayer.transform.position, Instance.transform.position) > playerDistanceToDisappear)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }

            TryStartCoroutine(StalkCoroutine(), 1);
        }

        void PlayFakeSoundEffectMajor()
        {
            logger.LogDebug("PlayFakeSoundEffectMajor");
        }

        void ShowFakeShipLeavingDisplayTip()
        {
            logger.LogDebug("ShowFakeShipLeavingDisplayTip");
        }

        void SpawnFakeBody()
        {
            logger.LogDebug("SpawnFakeBody");
        }

        void SlowWalkToPlayer() // Use arms crossed
        {
            logger.LogDebug("SlowWalkToPlayer");
        }

        void MimicEnemy()
        {
            logger.LogDebug("MimicEnemy");
            // See MimicableEnemies.txt
        }

        #endregion

        #region Rare

        void MimicEnemyChase()
        {
            // See MimicableEnemies.txt
            logger.LogDebug("MimicEnemyChase");
        }

        void MimicPlayer()
        {
            logger.LogDebug("MimicPlayer");
        }

        void ChasePlayer() // Use arms down, faster
        {
            logger.LogDebug("ChasePlayer");
        }

        void SpawnGhostGirl() // DressGirl
        {
            logger.LogDebug("SpawnGhostGirl");
        }

        void TurnOffAllLights()
        {
            logger.LogDebug("TurnOffAllLights");
        }

        void SpawnFakeLandMineUnderPlayer()
        {
            logger.LogDebug("SpawnFakeLandMineUnderPlayer");
        }

        #endregion

        #region Miscellaneous

        public static List<DoorLock> GetDoorLocksNearbyPosition(Vector3 pos, float distance)
        {
            List<DoorLock> doors = [];

            foreach (var door in GameObject.FindObjectsOfType<DoorLock>())
            {
                if (door == null) continue;
                if (Vector3.Distance(pos, door.transform.position) < distance)
                {
                    doors.Add(door);
                }
            }

            return doors;
        }

        public static Transform? TryFindingHauntPosition(PlayerControllerB targetPlayer, GameObject[] allAINodes, bool mustBeInLOS = true)
        {
            for (int i = 0; i < allAINodes.Length; i++)
            {
                if ((!mustBeInLOS || !Physics.Linecast(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) && !targetPlayer.HasLineOfSightToPosition(allAINodes[i].transform.position, 80f, 100, 8f))
                {
                    //Debug.DrawLine(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, Color.green, 2f);
                    //Debug.Log($"Player distance to haunt position: {Vector3.Distance(targetPlayer.transform.position, allAINodes[i].transform.position)}");
                    return allAINodes[i].transform;
                }
            }
            return null;
        }

        public static GameObject GetRandomAINode(List<GameObject> nodes)
        {
            int randIndex = UnityEngine.Random.Range(0, nodes.Count);
            return nodes[randIndex];
        }

        public static Vector3 GetPositionBehindPlayer(PlayerControllerB player, float distance)
        {
            Vector3 pos = player.transform.position - player.transform.forward * distance;
            return RoundManager.Instance.GetNavMeshPosition(pos);
        }

        #endregion
    }
}
