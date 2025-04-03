using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HeavyItemSCPs.Items.SCP513
{
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

            uncommonEvents.Add(BlockDoor);
            uncommonEvents.Add(StalkPlayer);
            uncommonEvents.Add(MimicMine);
            uncommonEvents.Add(MimicTurret);
            uncommonEvents.Add(MimicSpikeTrap);

            rareEvents.Add(MimicEnemy);
            rareEvents.Add(FakeMimicPlayer);
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

        #endregion

        #region UnCommon

        void BlockDoor()
        {

        }

        void StalkPlayer()
        {

        }

        void MimicMine()
        {

        }

        void MimicTurret()
        {

        }

        void MimicSpikeTrap()
        {

        }

        #endregion

        #region Rare

        void MimicEnemy()
        {

        }

        void FakeMimicPlayer()
        {

        }

        void ChasePlayer()
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
