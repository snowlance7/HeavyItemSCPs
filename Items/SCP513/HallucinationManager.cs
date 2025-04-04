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
            uncommonEvents.Add(MimicEnemy);

            rareEvents.Add(MimicEnemyChase);
            rareEvents.Add(MimicPlayer);
            rareEvents.Add(ChasePlayer);
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

        public void OnDestroy()
        {
            StopAllCoroutines();
        }

        #region Common

        void Jumpscare()
        {

        }

        void FlickerLights()
        {

        }

        void Stare()
        {
            IEnumerator StareCoroutine()
            {
                try
                {
                    yield return null;
                }
                finally
                {

                }
            }

            StartCoroutine(StareCoroutine());
        }

        void PlayAmbientSFXNearby()
        {

        }

        void PlaySoundEffectMinor()
        {

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
            if (targetPlayer.isInsideFactory)
            {
                for (int i = 0; i < allAINodes.Length; i++)
                {
                    if ((!mustBeInLOS || !Physics.Linecast(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault)) && !targetPlayer.HasLineOfSightToPosition(allAINodes[i].transform.position, 80f, 100, 8f))
                    {
                        Debug.DrawLine(targetPlayer.gameplayCamera.transform.position, allAINodes[i].transform.position, Color.green, 2f);
                        Debug.Log($"Player distance to haunt position: {Vector3.Distance(targetPlayer.transform.position, allAINodes[i].transform.position)}");
                        return allAINodes[i].transform;
                    }
                }
            }
            return null;
        }

        public static GameObject GetRandomAINode(List<GameObject> nodes)
        {
            int randIndex = UnityEngine.Random.Range(0, nodes.Count);
            return nodes[randIndex];
        }

        #endregion
    }
}
