using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Patches;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.ProBuilder.Csg;
using UnityEngine.UIElements;
using static HeavyItemSCPs.Plugin;
using static UnityEngine.VFX.VisualEffectControlTrackController;

namespace HeavyItemSCPs.Items.SCP513
{
    internal class HallucinationManager : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public static HallucinationManager Instance { get; private set; }
        public SCP513_1AI SCPInstance {  get; set; }
        public PlayerControllerB targetPlayer;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static HashSet<GrabbableObject> overrideShotguns = [];

        delegate void MethodDelegate();

        List<MethodDelegate> commonEvents = new List<MethodDelegate>();
        List<MethodDelegate> uncommonEvents = new List<MethodDelegate>();
        List<MethodDelegate> rareEvents = new List<MethodDelegate>();

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
            commonEvents.Add(PlayAmbientSFXNearby);
            commonEvents.Add(PlayFakeSoundEffectMinor);
            //commonEvents.Add(WallBloodMessage);
            commonEvents.Add(PlayBellSFX);
            commonEvents.Add(HideHazard);
            commonEvents.Add(Stare);
            logger.LogDebug("CommonEvents: " + commonEvents.Count);

            uncommonEvents.Add(FarStare);
            uncommonEvents.Add(Jumpscare);
            uncommonEvents.Add(BlockDoor);
            uncommonEvents.Add(StalkPlayer);
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
            rareEvents.Add(SpawnFakeHazardsAroundPlayer);
            rareEvents.Add(SpawnMultipleFakeBodies);
            rareEvents.Add(ForceSuicide);
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
        bool wasInterrupted = false;

        private bool TryStartCoroutine(IEnumerator coroutineMethod, int tier)
        {
            if (activeCoroutine != null)
            {
                if (tier < currentCoroutineTier)
                {
                    logger.LogDebug("A higher priority coroutine is already running, don't start this one");
                    return false;
                }

                // Interrupt current coroutine
                wasInterrupted = true;
                StopCoroutine(activeCoroutine);
                activeCoroutine = null;
                currentCoroutineTier = -1;
            }

            wasInterrupted = false;
            activeCoroutine = StartCoroutine(WrapCoroutine(coroutineMethod, tier));
            currentCoroutineTier = tier;
            return true;
        }

        private IEnumerator WrapCoroutine(IEnumerator coroutineMethod, int tier)
        {
            yield return StartCoroutine(coroutineMethod);

            // Only clear and switch behavior if not interrupted
            if (tier == currentCoroutineTier && !wasInterrupted)
            {
                SwitchToBehavior(State.InActive);
                activeCoroutine = null;
                currentCoroutineTier = -1;
            }
        }


        #region Common

        public void FlickerLights() // 0 0
        {
            logger.LogDebug("FlickerLights");
            if (!targetPlayer.isInsideFactory) { return; }
            RoundManager.Instance.FlickerLights(true, true);
            localPlayer.JumpToFearLevel(0.9f);
        }

        void PlayAmbientSFXNearby() // 0 1
        {
            logger.LogDebug("PlayAmbientSFXNearby");
            Vector3 pos = GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCPInstance.AmbientSFX);
        }

        void PlayFakeSoundEffectMinor() // 0 2
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

        void PlayBellSFX() // 0 3
        {
            logger.LogDebug("PlayBellSFX");
            Vector3 pos = GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            PlaySoundAtPosition(pos, SCPInstance.BellSFX);
        }

        void HideHazard() // 0 4 TODO: Not working
        {
            logger.LogDebug("HideHazard");

            float hideTime = 15f;
            int index = UnityEngine.Random.Range(0, 3);
            index = 0;

            switch (index)
            {
                case 0: // LandMine
                
                Landmine? landmine = Utils.GetClosestGameObjectOfType<Landmine>(targetPlayer.transform.position);
                if (landmine == null) { return; }

                IEnumerator HideLandmineCoroutine(Landmine landmine)
                {
                    try
                    {
                        logger.LogDebug("Hiding landmine");
                        yield return null;
                        landmine.GetComponent<MeshRenderer>().forceRenderingOff = true;
                        
                        float elapsedTime = 0f;
                        while (elapsedTime > hideTime)
                        {
                            yield return new WaitForSeconds(0.2f);
                            elapsedTime += 0.2f;
                            if (landmine.pressMineDebounceTimer > 0f) // TODO: Check this
                            {
                                break;
                            }
                        }
                    }
                    finally
                    {
                        landmine.GetComponent<MeshRenderer>().forceRenderingOff = false;
                    }
                }

                StartCoroutine(HideLandmineCoroutine(landmine));

                break;
                case 1: // Turret // TODO: Hide until turret activates

                    Turret? turret = Utils.GetClosestGameObjectOfType<Turret>(targetPlayer.transform.position);
                    if (turret == null) { return; }

                    IEnumerator HideTurretCoroutine(Turret turret)
                    {
                        try
                        {
                            logger.LogDebug("Hiding turret");
                            yield return null;
                            turret.GetComponent<MeshRenderer>().forceRenderingOff = true;

                            float elapsedTime = 0f;
                            while (elapsedTime > hideTime)
                            {
                                yield return new WaitForSeconds(0.2f);
                                elapsedTime += 0.2f;
                                if (turret.turretMode == TurretMode.Charging) // TODO: Test this
                                {
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            turret.GetComponent<MeshRenderer>().forceRenderingOff = false;
                        }
                    }

                    StartCoroutine(HideTurretCoroutine(turret));

                    break;
                case 2: // SpikeTrap // TODO

                    SpikeRoofTrap? spikeTrap = Utils.GetClosestGameObjectOfType<SpikeRoofTrap>(targetPlayer.transform.position);
                    if (spikeTrap == null) { return; }

                    IEnumerator HideSpikeTrapCoroutine(SpikeRoofTrap spikeTrap)
                    {
                        try
                        {
                            logger.LogDebug("Hiding spike trap");
                            yield return null;
                            spikeTrap.GetComponent<MeshRenderer>().forceRenderingOff = true;

                            float elapsedTime = 0f;
                            while (elapsedTime > hideTime)
                            {
                                yield return new WaitForSeconds(0.2f);
                                elapsedTime += 0.2f;
                            }
                        }
                        finally
                        {
                            spikeTrap.GetComponent<MeshRenderer>().forceRenderingOff = false;
                        }
                    }

                    StartCoroutine(HideSpikeTrapCoroutine(spikeTrap));

                    break;
                default:
                break;
            }
        }

        void Stare() // 0 5
        {
            logger.LogDebug("Stare");

            IEnumerator StareCoroutine()
            {
                float stareTime = 15f;

                yield return null;

                Vector3 outsideLOS = SCPInstance.GetRandomPositionAroundPlayer(5f, 15f);

                SwitchToBehavior(State.Manifesting);
                SCPInstance.Teleport(outsideLOS);
                SCPInstance.facePlayer = true;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(SCPInstance.creatureSFX, SCPInstance.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (SCPInstance.playerHasLOS)
                    {
                        yield return new WaitForSeconds(2.5f);
                        FlickerLights();
                        yield break;
                    }
                }
            }

            TryStartCoroutine(StareCoroutine(), 0);
        }

        #endregion

        #region UnCommon

        void FarStare() // 1 0
        {
            logger.LogDebug("FarStare");

            IEnumerator StareCoroutine()
            {
                float stareTime = 15f;

                yield return null;

                Transform? pos = SCPInstance.TryFindingHauntPosition();
                while (pos == null)
                {
                    yield return new WaitForSeconds(0.2f);
                    pos = SCPInstance.TryFindingHauntPosition();
                }

                SwitchToBehavior(State.Manifesting);
                SCPInstance.Teleport(pos.position);
                SCPInstance.facePlayer = true;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                RoundManager.PlayRandomClip(SCPInstance.creatureSFX, SCPInstance.AmbientSFX);

                float elapsedTime = 0f;

                while (elapsedTime < stareTime)
                {
                    yield return new WaitForSeconds(0.2f);
                    elapsedTime += 0.2f;

                    if (SCPInstance.playerHasLOS)
                    {
                        yield return new WaitForSeconds(2.5f);
                        FlickerLights();
                        yield break;
                    }
                }
            }

            TryStartCoroutine(StareCoroutine(), 1);
        }

        void Jumpscare() // 1 1
        {
            logger.LogDebug("Jumpscare");

            IEnumerator JumpscareCoroutine()
            {
                float runSpeed = 20f;
                float disappearTime = 5f;

                yield return null;

                Transform? pos = SCPInstance.ChoosePositionInFrontOfPlayer(1f, 5f);
                while (pos == null)
                {
                    yield return new WaitForSeconds(0.2f);
                    pos = SCPInstance.ChoosePositionInFrontOfPlayer(1f, 5f);
                }

                SwitchToBehavior(State.Chasing);
                SCPInstance.Teleport(pos.position);
                SCPInstance.creatureAnimator.SetBool("armsCrossed", false);
                SCPInstance.agent.speed = runSpeed;
                SCPInstance.facePlayer = true;

                yield return new WaitForSeconds(disappearTime);
            }

            TryStartCoroutine(JumpscareCoroutine(), 1);
        }

        void BlockDoor() // 1 2
        {
            logger.LogDebug("BlockDoor");

            IEnumerator BlockDoorCoroutine()
            {
                float doorDistance = 10f;
                float blockPosOffset = 0f;
                float disappearDistance = 15f;
                float disappearTime = 15f;

                yield return null;

                DoorLock[] doorLocks = GetDoorLocksNearbyPosition(targetPlayer.transform.position, doorDistance).ToArray();
                while (doorLocks.Length == 0)
                {
                    yield return new WaitForSeconds(1f);
                    doorLocks = GetDoorLocksNearbyPosition(targetPlayer.transform.position, doorDistance).ToArray();
                }

                int index = UnityEngine.Random.Range(0, doorLocks.Length);
                DoorLock doorLock = doorLocks[index];

                var steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
                Vector3 blockPos = RoundManager.Instance.GetNavMeshPosition(steelDoorObj.transform.position + Vector3.forward * blockPosOffset);

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
                    if (Vector3.Distance(SCPInstance.transform.position, targetPlayer.transform.position) > disappearDistance || SCPInstance.currentBehaviourStateIndex != (int)State.Manifesting)
                    {
                        break;
                    }
                }
            }

            TryStartCoroutine(BlockDoorCoroutine(), 1);
        }

        void StalkPlayer() // 1 3
        {
            logger.LogDebug("StalkPlayer");

            IEnumerator StalkCoroutine()
            {
                yield return null;

                Transform? teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, true);
                while (teleportTransform == null)
                {
                    yield return new WaitForSeconds(1f);
                    teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, true);
                }

                Vector3 teleportPos = teleportTransform.position;

                SwitchToBehavior(State.Stalking);
                SCPInstance.Teleport(teleportPos);
                SCPInstance.SetDestinationToPosition(teleportPos);
                SCPInstance.creatureAnimator.SetBool("armsCrossed", false);

                while (SCPInstance.currentBehaviourStateIndex == (int)State.Stalking && targetPlayer.isInsideFactory != SCPInstance.isOutside)
                {
                    yield return new WaitForSeconds(1f);
                }

                FlickerLights();
            }

            TryStartCoroutine(StalkCoroutine(), 1);
        }

        void PlayFakeSoundEffectMajor() // 1 4
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


        void ShowFakeShipLeavingDisplayTip() // 1 5
        {
            logger.LogDebug("ShowFakeShipLeavingDisplayTip");

            TimeOfDay.Instance.shipLeavingEarlyDialogue[0].bodyText = "WARNING! Please return by " + GetClock(TimeOfDay.Instance.normalizedTimeOfDay + 0.1f, TimeOfDay.Instance.numberOfHours, createNewLine: false) + ". A vote has been cast, and the autopilot ship will leave early.";
            HUDManager.Instance.ReadDialogue(TimeOfDay.Instance.shipLeavingEarlyDialogue);
        }

        void SpawnFakeBody() // 1 6
        {
            logger.LogDebug("SpawnFakeBody");

            float radius = 3f;

            int deathAnimation = UnityEngine.Random.Range(0, 8);

            Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 randomOffset = new Vector3(offset2D.x, 0f, offset2D.y);

            GameObject bodyObj = SpawnDeadBody(targetPlayer, targetPlayer.transform.position, 0, deathAnimation, randomOffset); // TODO: Test this
            GameObject.Destroy(bodyObj, 30f);
        }

        void SlowWalkToPlayer() // 1 7 // TODO: Not working
        {
            logger.LogDebug("SlowWalkToPlayer");

            IEnumerator SlowWalkCoroutine()
            {
                yield return null;

                Transform? teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, false, 3);
                while (teleportTransform == null)
                {
                    yield return new WaitForSeconds(1f);
                    teleportTransform = SCPInstance.ChooseClosestNodeToPosition(targetPlayer.transform.position, false, 3);
                }

                Vector3 teleportPos = teleportTransform.position;

                SCPInstance.Teleport(teleportPos);
                SCPInstance.agent.speed = 5f;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", true);
                SwitchToBehavior(State.Chasing);

                while (SCPInstance.currentBehaviourStateIndex == (int)State.Chasing && targetPlayer.isInsideFactory != SCPInstance.isOutside)
                {
                    yield return new WaitForSeconds(5f);
                    FlickerLights();
                }
            }

            TryStartCoroutine(SlowWalkCoroutine(), 1);
        }

        void MimicEnemy() // 1 8
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

            IEnumerator ChaseCoroutine()
            {
                yield return null;

                Vector3 teleportPos = SCPInstance.ChooseFarthestNodeFromPosition(targetPlayer.transform.position).position;

                SCPInstance.Teleport(teleportPos);
                SCPInstance.agent.speed = 10f;
                SCPInstance.creatureAnimator.SetBool("armsCrossed", false);
                SwitchToBehavior(State.Chasing);

                while (SCPInstance.currentBehaviourStateIndex == (int)State.Chasing && targetPlayer.isInsideFactory != SCPInstance.isOutside)
                {
                    yield return new WaitForSeconds(2.5f);
                    FlickerLights();
                }
            }

            TryStartCoroutine(ChaseCoroutine(), 2);
        }

        void SpawnGhostGirl() // DressGirl // 2 3
        {
            logger.LogDebug("SpawnGhostGirl");

            SCPInstance.SpawnGhostGirlServerRpc(targetPlayer.actualClientId);
        }

        void TurnOffAllLights() // 2 4
        {
            logger.LogDebug("TurnOffAllLights");
            FlickerLights();
            RoundManager.Instance.TurnOnAllLights(false);
        }

        void SpawnFakeHazardsAroundPlayer() // 2 5
        {
            logger.LogDebug("SpawnFakeHazardsAroundPlayer");
        }

        void SpawnMultipleFakeBodies() // 2 6
        {
            logger.LogDebug("SpawnMultipleFakeBodies");

            float radius = 3f;
            int minBodies = 5;
            int maxBodies = 10;

            int amount = UnityEngine.Random.Range(minBodies, maxBodies + 1);

            for (int i = 0; i < amount; i++)
            {
                int deathAnimation = UnityEngine.Random.Range(0, 8);

                Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 randomOffset = new Vector3(offset2D.x, 0f, offset2D.y);

                GameObject bodyObj = SpawnDeadBody(targetPlayer, targetPlayer.transform.position, 0, deathAnimation, randomOffset); // TODO: Test this
                GameObject.Destroy(bodyObj, 30f);
            }
        }

        void ForceSuicide() // 2 7
        {
            logger.LogDebug("ForceSuicide");

            bool playerHasShotgun = false;
            bool playerHasKnife = false;
            bool playerHasMask = false;

            foreach (var slot in targetPlayer.ItemSlots)
            {
                if (slot == null) { continue; }
                if (slot.itemProperties.name == "Shotgun")
                {
                    playerHasShotgun = true;
                }
                if (slot.itemProperties.name == "Knife")
                {
                    playerHasKnife = true;
                }
                if (slot.itemProperties.name == "HauntedMask")
                {
                    playerHasMask = true;
                }
            }

            if (!playerHasKnife && !playerHasShotgun && !playerHasMask)
            {
                RunRandomEvent(2);
                return;
            }

            IEnumerator ForceSuicideCoroutine(bool hasShotgun, bool hasMask)
            {
                yield return null;

                if (hasShotgun) // Shotgun
                {
                    Utils.FreezePlayer(targetPlayer, true);
                    targetPlayer.activatingItem = true;
                    ShotgunItem? shotgun = null;

                    foreach (var slot in targetPlayer.ItemSlots)
                    {
                        if (slot == null) { continue; }
                        if (slot.itemProperties.name == "Shotgun")
                        {
                            shotgun = (ShotgunItem)slot;
                        }
                    }

                    if (shotgun == null) { logger.LogError("Couldnt find shotgun"); yield break; }

                    targetPlayer.SwitchToItemSlot(0, shotgun);

                    float duration = 5f;

                    SCPInstance.ShotgunSuicideServerRpc(shotgun.NetworkObject, duration);
                }
                else if (playerHasMask) // Mask
                {

                }
                else // Knife
                {

                }
            }

            TryStartCoroutine(ForceSuicideCoroutine(playerHasShotgun, playerHasMask), 2);
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

        public Transform GetClosestNode(Vector3 pos, bool outside = true)
        {
            GameObject[] nodes;
            if (outside && !TESTING.inTestRoom)
            {
                if (RoundManager.Instance.outsideAINodes == null || RoundManager.Instance.outsideAINodes.Length <= 0)
                {
                    RoundManager.Instance.outsideAINodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
                    logger.LogDebug("Found OutsideAINodes: " + RoundManager.Instance.outsideAINodes.Length);
                }
                nodes = RoundManager.Instance.outsideAINodes;
            }
            else
            {
                if (RoundManager.Instance.insideAINodes == null || RoundManager.Instance.insideAINodes.Length <= 0)
                {
                    RoundManager.Instance.insideAINodes = GameObject.FindGameObjectsWithTag("AINode");
                    logger.LogDebug("Found AINodes: " + RoundManager.Instance.insideAINodes.Length);
                }
                nodes = RoundManager.Instance.insideAINodes;
            }

            logger.LogDebug("Node count: " + nodes.Length);
            float closestDistance = Mathf.Infinity;
            GameObject closestNode = null!;
            
            foreach (var node in nodes)
            {
                float distance = Vector3.Distance(pos, node.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                }
            }

            return closestNode.transform;
        }

        public string GetClock(float timeNormalized, float numberOfHours, bool createNewLine = true)
        {
            string newLine;
            int num = (int)(timeNormalized * (60f * numberOfHours)) + 360;
            int num2 = (int)Mathf.Floor(num / 60);
            if (!createNewLine)
            {
                newLine = " ";
            }
            else
            {
                newLine = "\n";
            }
            string amPM = newLine + "AM";
            if (num2 >= 24)
            {
                return "12:00\nAM";
            }
            if (num2 < 12)
            {
                amPM = newLine + "AM";
            }
            else
            {
                amPM = newLine + "PM";
            }
            if (num2 > 12)
            {
                num2 %= 12;
            }
            int num3 = num % 60;
            string text = $"{num2:00}:{num3:00}".TrimStart('0') + amPM;
            return text;
        }

        public static GameObject SpawnDeadBody(PlayerControllerB deadPlayerController, Vector3 spawnPosition, int causeOfDeath = 0, int deathAnimation = 0, Vector3 positionOffset = default(Vector3))
        {
            float num = 1.32f;
            /*if (positionOffset != Vector3.zero)
            {
                num = 0f;
            }*/
            GameObject bodyObj = GameObject.Instantiate(deadPlayerController.playersManager.playerRagdolls[deathAnimation], spawnPosition + Vector3.up * num + positionOffset, Quaternion.identity);
            DeadBodyInfo deadBody = bodyObj.GetComponent<DeadBodyInfo>();
            deadBody.overrideSpawnPosition = true;
            if (deadPlayerController.physicsParent != null)
            {
                deadBody.SetPhysicsParent(deadPlayerController.physicsParent);
            }
            deadBody.parentedToShip = false;
            deadBody.playerObjectId = (int)deadPlayerController.actualClientId;
            for (int j = 0; j < deadPlayerController.bodyBloodDecals.Length; j++)
            {
                deadBody.bodyBloodDecals[j].SetActive(deadPlayerController.bodyBloodDecals[j].activeSelf);
            }
            ScanNodeProperties componentInChildren = deadBody.gameObject.GetComponentInChildren<ScanNodeProperties>();
            componentInChildren.headerText = "Body of " + deadPlayerController.playerUsername;
            CauseOfDeath causeOfDeath2 = (CauseOfDeath)causeOfDeath;
            componentInChildren.subText = "Cause of death: " + causeOfDeath2;
            deadBody.causeOfDeath = causeOfDeath2;
            if (causeOfDeath2 == CauseOfDeath.Bludgeoning || causeOfDeath2 == CauseOfDeath.Mauling || causeOfDeath2 == CauseOfDeath.Gunshots)
            {
                deadBody.MakeCorpseBloody();
            }
            return bodyObj;
        }

        #endregion
    }

    [HarmonyPatch]
    public class HallucinationManagerPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.LateUpdate))]
        public static bool LateUpdatePrefix(GrabbableObject __instance)
        {
            return !HallucinationManager.overrideShotguns.Contains(__instance);
        }
    }
}
