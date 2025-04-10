using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

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
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public SCP513_1AI Instance {  get; set; }
        PlayerControllerB targetPlayer;
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
            Chasing
        }

        public HallucinationManager(SCP513_1AI script, PlayerControllerB _targetPlayer)
        {
            Instance = script;
            targetPlayer = _targetPlayer;
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

            uncommonEvents.Add(BlockDoor);
            uncommonEvents.Add(StalkPlayer);
            uncommonEvents.Add(MimicHazard);
            uncommonEvents.Add(PlayFakeSoundEffectMajor);
            uncommonEvents.Add(ShowFakeShipLeavingDisplayTip);
            uncommonEvents.Add(SpawnFakeBody);
            uncommonEvents.Add(SlowWalkToPlayer);
            uncommonEvents.Add(MimicEnemy);

            rareEvents.Add(MimicEnemyChase);
            rareEvents.Add(MimicPlayer);
            rareEvents.Add(ChasePlayer);
            rareEvents.Add(SpawnGhostGirl);
            rareEvents.Add(TurnOffAllLights);
            rareEvents.Add(SpawnFakeLandMineUnderPlayer);
        }

        public void RunRandomEvent(int eventCategory)
        {
            int index;

            switch (eventCategory)
            {
                case 0:
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
                    commonEvents[index]?.Invoke();
                    break;
                case 1:
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
                    uncommonEvents[index]?.Invoke();
                    break;
                case 2:
                    StopAllCoroutines();
                    index = UnityEngine.Random.Range(0, commonEvents.Count);
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

        #region Common

        void Jumpscare()
        {
            float runSpeed = 15f;

            SwitchToBehavior(State.Manifesting);
            Instance.Teleport(GetPositionBehindPlayer(targetPlayer, 1f));
            Instance.creatureAnimator.SetBool("armsCrossed", true);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);

            IEnumerator DisappearAfterLOS()
            {
                try
                {
                    yield return null;

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
                finally
                {
                    if (Instance.currentBehaviourStateIndex == (int)State.Manifesting)
                    {
                        SwitchToBehavior(State.Active);
                    }
                }
            }

            StartCoroutine(DisappearAfterLOS());
        }

        void FlickerLights()
        {
            RoundManager.Instance.FlickerLights(true, true);
            GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.9f);
        }

        void Stare()
        {
            float stareTime = 15f;

            GameObject[] aiNodes = targetPlayer.isInsideFactory ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
            Transform? outsideLOS = TryFindingHauntPosition(targetPlayer, aiNodes, false);
            if (outsideLOS == null) { return; }

            Instance.Teleport(outsideLOS.position);
            SwitchToBehavior(State.Manifesting);
            Instance.creatureAnimator.SetBool("armsCrossed", true);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);


            IEnumerator StareCoroutine()
            {
                try
                {
                    yield return null;

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
                finally
                {
                    if (Instance.currentBehaviourStateIndex == (int)State.Manifesting)
                    {
                        SwitchToBehavior(State.Active);
                    }
                }
            }

            StartCoroutine(StareCoroutine());
        }

        void PlayAmbientSFXNearby()
        {
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            Instance.Teleport(pos);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);
        }

        void PlaySoundEffectMinor()
        {
            Vector3 pos = RoundManager.Instance.GetClosestNode(targetPlayer.transform.position, !targetPlayer.isInsideFactory).position;
            Instance.Teleport(pos);
            RoundManager.PlayRandomClip(Instance.creatureSFX, Instance.AmbientSFX);
        }

        void WallBloodMessage()
        {

        }

        void PlayBellSFX()
        {

        }

        #endregion

        #region UnCommon

        void BlockDoor()
        {

        }

        void StalkPlayer()
        {

        }

        void MimicHazard()
        {

        }

        void PlayFakeSoundEffectMajor()
        {

        }

        void ShowFakeShipLeavingDisplayTip()
        {

        }

        void SpawnFakeBody()
        {
            
        }

        void SlowWalkToPlayer() // Use arms crossed
        {

        }

        void MimicEnemy()
        {
            // See MimicableEnemies.txt
        }

        #endregion

        #region Rare

        void MimicEnemyChase()
        {
            // See MimicableEnemies.txt
        }

        void MimicPlayer()
        {

        }

        void ChasePlayer() // Use arms down, faster
        {

        }

        void SpawnGhostGirl() // DressGirl
        {

        }

        void TurnOffAllLights()
        {

        }

        void SpawnFakeLandMineUnderPlayer()
        {

        }

        #endregion

        #region Miscellaneous

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
