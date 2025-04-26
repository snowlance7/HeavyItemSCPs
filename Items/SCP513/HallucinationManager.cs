using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using static HeavyItemSCPs.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

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
        public static HallucinationManager Instance { get; private set; }
        public SCP513_1AI SCPInstance {  get; set; }
        public PlayerControllerB targetPlayer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        delegate void MethodDelegate();

        List<MethodDelegate> commonEvents = new List<MethodDelegate>();
        List<MethodDelegate> uncommonEvents = new List<MethodDelegate>();
        List<MethodDelegate> rareEvents = new List<MethodDelegate>();

        public const float LOSOffset = 1f;

        public enum State
        {
            InActive,
            Manifesting,
            Chasing,
            Stalking
        }

        public void Start()
        {
            commonEvents.Add(FlickerLights);
            commonEvents.Add(Stare);
            commonEvents.Add(PlayAmbientSFXNearby);
            commonEvents.Add(PlayFakeSoundEffectMinor);
            commonEvents.Add(WallBloodMessage);
            commonEvents.Add(PlayBellSFX);
            logger.LogDebug("CommonEvents: " + commonEvents.Count);

            uncommonEvents.Add(Jumpscare);
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
            rareEvents.Add(SpawnMultipleFakeBodies);
            logger.LogDebug("RareEvents: " + rareEvents.Count);

            Instance = this;
        }

        public void RunRandomEvent(int eventRarity)
        {
            int eventIndex;

            switch (eventRarity)
            {
                case 0:
                    eventIndex = UnityEngine.Random.Range(0, commonEvents.Count);
                    logger.LogDebug("Running common event 0 at index " + eventIndex);
                    commonEvents[eventIndex]?.Invoke();
                    break;
                case 1:
                    eventIndex = UnityEngine.Random.Range(0, uncommonEvents.Count);
                    logger.LogDebug("Running uncommon event 1 at index " + eventIndex);
                    uncommonEvents[eventIndex]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    eventIndex = UnityEngine.Random.Range(0, rareEvents.Count);
                    logger.LogDebug("Running rare event 2 at index " + eventIndex);
                    rareEvents[eventIndex]?.Invoke();
                    break;
                default:
                    break;
            }
        }

        public void RunEvent(int eventRarity, int eventIndex)
        {
            switch (eventRarity)
            {
                case 0:
                    logger.LogDebug("Running common event 0 at index " + eventIndex);
                    commonEvents[eventIndex]?.Invoke();
                    break;
                case 1:
                    logger.LogDebug("Running uncommon event 1 at index " + eventIndex);
                    uncommonEvents[eventIndex]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    logger.LogDebug("Running rare event 2 at index " + eventIndex);
                    rareEvents[eventIndex]?.Invoke();
                    break;
                default:
                    break;
            }
        }

        void SwitchToBehavior(State state)
        {
            SCPInstance.SwitchToBehaviourServerRpc((int)state);
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
                if (tier < currentCoroutineTier)
                {
                    // A higher priority coroutine is already running, don't start this one
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
                //SwitchToBehavior(State.InActive);
                activeCoroutine = null;
                currentCoroutineTier = -1;
            }
        }

        #region Common

        void FlickerLights() // 0 0
        {
            logger.LogDebug("FlickerLights");
            if (!targetPlayer.isInsideFactory) { return; }
            RoundManager.Instance.FlickerLights(true, true);
            GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.9f);
        }

        void Stare() // 0 1 // TODO: not teleporting behind player
        {
            logger.LogDebug("Stare");
            Vector3 _outsideLOS = SCPInstance.TryFindingHauntPosition(false);
            if (_outsideLOS == Vector3.zero) { logger.LogDebug("Unable to find position outside LOS"); return; }


            IEnumerator StareCoroutine(Vector3 outsideLOS)
            {
                float stareTime = 15f;

                yield return null;

                SCPInstance.Teleport(outsideLOS);
                SwitchToBehavior(State.Manifesting);
                SCPInstance.facePlayer = true;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(SCPInstance.creatureSFX, SCPInstance.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (targetPlayer.HasLineOfSightToPosition(SCPInstance.transform.position + Vector3.up * LOSOffset, 70f, 100))
                    {
                        FlickerLights();
                        break;
                    }
                }
                
                SwitchToBehavior(State.InActive);
            }

            TryStartCoroutine(StareCoroutine(_outsideLOS), 0);
        }

        void PlayAmbientSFXNearby() // 0 2
        {
            logger.LogDebug("PlayAmbientSFXNearby");
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCPInstance.AmbientSFX);
        }

        void PlayFakeSoundEffectMinor() // 0 3
        {
            logger.LogDebug("PlaySoundEffectMinor");

            // Filter clips that match inside/outside
            var clips = SCPInstance.MinorSoundEffectSFX
                .Where(clip => clip.name.Length >= 2 &&
                               (clip.name[0] == 'I') == targetPlayer.isInsideFactory)
                .ToArray();

            if (clips.Length == 0)
            {
                logger.LogWarning("No matching sound effects for current environment.");
                return;
            }

            // Pick one randomly
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];

            // Extract metadata from name
            bool is2D = clip.name[1] == '2';
            bool isFar = clip.name.Length > 3 && clip.name[3] == 'F';

            // Choose sound position based on 'F'
            int offset = isFar ? 5 : 0;
            Vector3 pos = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, false, offset).position;

            // Play the sound
            GameObject soundObj = Instantiate(SCPInstance.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();
            source.spatialBlend = is2D ? 0f : 1f;
            source.clip = clip;
            source.Play();

            GameObject.Destroy(soundObj, clip.length);
        }

        void WallBloodMessage() // 0 4
        {
            logger.LogDebug("WallBloodMessage");
            throw new NotImplementedException();
        }

        void PlayBellSFX() // 0 5
        {
            logger.LogDebug("PlayBellSFX");
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCPInstance.BellSFX);
        }

        #endregion

        #region UnCommon

        void Jumpscare() // 1 0
        {
            logger.LogDebug("Jumpscare");

            Transform? posTransform = SCPInstance.ChoosePositionInFrontOfPlayer(5f);
            if (posTransform == null) { return; }

            IEnumerator JumpscareCoroutine(Vector3 pos)
            {
                float runSpeed = 20f;
                float disappearTime = 5f;

                yield return null;

                SwitchToBehavior(State.Chasing);
                SCPInstance.Teleport(pos);
                SCPInstance.creatureAnimator.SetBool("armsCrossed", false);
                SCPInstance.agent.speed = runSpeed;
                SCPInstance.facePlayer = true;

                yield return new WaitForSeconds(disappearTime);

                SwitchToBehavior(State.InActive);
            }

            TryStartCoroutine(JumpscareCoroutine(posTransform.position), 1);
        }

        void BlockDoor() // 1 1
        {
            logger.LogDebug("BlockDoor");
            float doorDistance = 10f;
            float blockPosOffset = 1f;

            DoorLock[] doorLocks = GetDoorLocksNearbyPosition(targetPlayer.transform.position, doorDistance).ToArray();
            if (doorLocks.Length == 0) { logger.LogDebug("Cant find any doors near player"); return; }
            int index = UnityEngine.Random.Range(0, doorLocks.Length);
            DoorLock doorLock = doorLocks[index];

            var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
            Vector3 blockPos = RoundManager.Instance.GetNavMeshPosition(steelDoorObj.transform.position + Vector3.forward * blockPosOffset);

            IEnumerator BlockDoorCoroutine(Vector3 blockPos)
            {
                float disappearDistance = 15f;
                float disappearTime = 15f;

                yield return null;

                SwitchToBehavior(State.Manifesting);
                SCPInstance.Teleport(blockPos);
                SCPInstance.SetDestinationToPosition(blockPos);
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                SCPInstance.facePlayer = true;

                float elapsedTime = 0f;
                while (elapsedTime < disappearTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;
                    if (Vector3.Distance(SCPInstance.transform.position, targetPlayer.transform.position) > disappearDistance)
                    {
                        break;
                    }

                    SwitchToBehavior(State.InActive);
                }
            }

            TryStartCoroutine(BlockDoorCoroutine(blockPos), 1);
        }

        void StalkPlayer() // 1 2
        {
            logger.LogDebug("StalkPlayer");

            Transform? teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, true);
            if (teleportTransform == null) { logger.LogDebug("Cant find any node near player"); return; }

            IEnumerator StalkCoroutine(Vector3 teleportPos)
            {
                float playerDistanceToDisappear = 3f;

                yield return null;

                SwitchToBehavior(State.Stalking);
                SCPInstance.Teleport(teleportPos);
                SCPInstance.SetDestinationToPosition(teleportPos);
                SCPInstance.creatureAnimator.SetBool("armsCrossed", false);

                while (Vector3.Distance(targetPlayer.transform.position, SCPInstance.transform.position) > playerDistanceToDisappear)
                {
                    yield return new WaitForSeconds(0.2f);

                    if (targetPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset))
                    {
                        logger.LogDebug("In LOS, disappearing...");
                        yield return new WaitForSeconds(1f);
                        FlickerLights();
                        break;
                    }
                }

                SwitchToBehavior(State.InActive);
            }

            TryStartCoroutine(StalkCoroutine(teleportTransform.position), 1);
        }

        void PlayFakeSoundEffectMajor() // 1 3
        {
            logger.LogDebug("PlayFakeSoundEffectMajor");

            // Filter clips that match inside/outside
            var clips = SCPInstance.MajorSoundEffectSFX
                .Where(clip => clip.name.Length >= 2 &&
                               (clip.name[0] == 'I') == targetPlayer.isInsideFactory)
                .ToArray();

            if (clips.Length == 0)
            {
                logger.LogWarning("No matching sound effects for current environment.");
                return;
            }

            // Pick one randomly
            var clip = clips[UnityEngine.Random.Range(0, clips.Length)];

            // Extract metadata from name
            bool is2D = clip.name[1] == '2';
            bool isFar = clip.name.Length > 3 && clip.name[3] == 'F';

            // Choose sound position based on 'F'
            int offset = isFar ? 5 : 0;
            Vector3 pos = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, false, offset).position;

            // Play the sound
            GameObject soundObj = Instantiate(SCPInstance.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();
            source.spatialBlend = is2D ? 0f : 1f;
            source.clip = clip;
            source.Play();

            GameObject.Destroy(soundObj, clip.length);
        }


        void ShowFakeShipLeavingDisplayTip() // 1 4
        {
            logger.LogDebug("ShowFakeShipLeavingDisplayTip");
        }

        void SpawnFakeBody() // 1 5
        {
            logger.LogDebug("SpawnFakeBody");
        }

        void SlowWalkToPlayer() // Use arms crossed // 1 6
        {
            logger.LogDebug("SlowWalkToPlayer");

            Transform? teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, false, 3);
            if (teleportTransform == null) {logger.LogDebug("Cant find node to teleport to"); return; }

            IEnumerator SlowWalkCoroutine(Vector3 teleportPos)
            {
                yield return null;

                SCPInstance.Teleport(teleportPos);
                SCPInstance.agent.speed = 1f;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                SwitchToBehavior(State.Chasing);

                while (true)
                {
                    yield return new WaitForSeconds(0.2f);

                    if (targetPlayer.HasLineOfSightToPosition(transform.position + Vector3.up * LOSOffset))
                    {
                        yield return new WaitForSeconds(3f);
                        FlickerLights();
                        break;
                    }
                }

                SwitchToBehavior(State.InActive);
            }

            TryStartCoroutine(SlowWalkCoroutine(teleportTransform.position), 1);
        }

        void MimicEnemy() // 1 7
        {
            logger.LogDebug("MimicEnemy");
            // See MimicableEnemies.txt
        }

        #endregion

        #region Rare

        void MimicEnemyChase() // 2 0
        {
            // See MimicableEnemies.txt
            logger.LogDebug("MimicEnemyChase");
        }

        void MimicPlayer() // 2 1
        {
            logger.LogDebug("MimicPlayer");
        }

        void ChasePlayer() // Use arms down, faster // 2 2
        {
            logger.LogDebug("ChasePlayer");
        }

        void SpawnGhostGirl() // DressGirl // 2 3
        {
            logger.LogDebug("SpawnGhostGirl");
        }

        void TurnOffAllLights() // 2 4
        {
            logger.LogDebug("TurnOffAllLights");
        }

        void SpawnFakeLandMineUnderPlayer() // 2 5
        {
            logger.LogDebug("SpawnFakeLandMineUnderPlayer");
        }

        void SpawnMultipleFakeBodies() // 2 6
        {
            logger.LogDebug("SpawnMultipleFakeBodies");
        }

        #endregion

        #region Miscellaneous

        public void PlaySoundAtPosition(Vector3 pos, AudioClip clip, bool randomize = true, bool spatial3D = true)
        {
            GameObject soundObj = Instantiate(SCPInstance.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();

            if (randomize)
            {
                source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            }

            if (!spatial3D)
            {
                source.spatialBlend = 0f;
            }

            source.clip = clip;
            source.Play();
            GameObject.Destroy(soundObj, source.clip.length);
        }

        public void PlaySoundAtPosition(Vector3 pos, AudioClip[] clips, bool randomize = true, bool spatial3D = true)
        {
            GameObject soundObj = Instantiate(SCPInstance.SoundObjectPrefab, pos, Quaternion.identity);
            AudioSource source = soundObj.GetComponent<AudioSource>();

            if (randomize)
            {
                source.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            }

            if (!spatial3D)
            {
                source.spatialBlend = 0f;
            }

            int index = UnityEngine.Random.Range(0, clips.Length);
            source.clip = clips[index];
            source.Play();
            GameObject.Destroy(soundObj, source.clip.length);
        }

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

        #endregion
    }
}
