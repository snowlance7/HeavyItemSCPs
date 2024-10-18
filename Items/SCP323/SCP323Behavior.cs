using BepInEx.Logging;
using GameNetcodeStuff;
using LethalLib.Modules;
using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP323
{
    internal class SCP323Behavior : PhysicsProp, IVisibleThreat
    {

        // scale 0.31

        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable 0649

#pragma warning restore 0649

        readonly Vector3 posOffsetWearing = new Vector3(0.05f, -0.5f, -0.25f);
        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -63f, 0f);
        readonly Vector3 posOffsetShoving = new Vector3(-0.28f, -0.75f, -0.08f);
        readonly Vector3 rotOffsetShoving = new Vector3(-90f, -63f, 0f);
        readonly Vector3 posOffsetHolding = new Vector3(0.05f, -0.55f, -0.15f);
        readonly Vector3 rotOffsetHolding = new Vector3(-60f, -90f, 0f);

        float timeSinceInsanityIncrease;
        bool attaching;
        bool skullOn;
        PlayerControllerB? lastPlayerHeldBy;

        // Config variables
        float distanceToIncreaseInstanity; // default 10 or 15
        float insanityNearby; // default 5
        float insanityHolding; // default 10
        float insanityWearing; // default 10

        public ThreatType type => ThreatType.Player;

        public override void Start()
        {
            base.Start();

            // TEMP VALUES
            distanceToIncreaseInstanity = 10f;
            insanityNearby = 5f;
            insanityHolding = 10f;
            insanityWearing = 10f;
        }

        public override void Update()
        {
            base.Update();

            if (PlayerIsTargetable(localPlayer) && Vector3.Distance(transform.position, localPlayer.transform.position) < distanceToIncreaseInstanity)
            {
                timeSinceInsanityIncrease += Time.unscaledDeltaTime;

                if (timeSinceInsanityIncrease > 10f)
                {
                    timeSinceInsanityIncrease = 0f;

                    if (playerHeldBy != null)
                    {
                        lastPlayerHeldBy = playerHeldBy;
                        if (localPlayer == playerHeldBy)
                        {
                            if (skullOn)
                            {
                                playerHeldBy.insanityLevel += insanityWearing;
                            }
                            else
                            {
                                playerHeldBy.insanityLevel += insanityHolding;
                            }

                            if (playerHeldBy.insanityLevel >= 50)
                            {
                                AttemptTransformLocalPlayer();
                            }
                        }
                    }
                    else
                    {
                        localPlayer.insanityLevel += insanityNearby;
                    }
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (!attaching && playerHeldBy != null)
            {
                Wear(buttonDown);
                playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
                skullOn = buttonDown;
                playerHeldBy.activatingItem = buttonDown;
            }
        }

        void Wear(bool buttonDown)
        {
            if (buttonDown)
            {
                SetOffsets(posOffsetWearing, rotOffsetWearing);
            }
            else
            {
                SetOffsets(posOffsetHolding, rotOffsetHolding);
            }
        }

        bool PlayerIsTargetable(PlayerControllerB player)
        {
            if (player != null && player.isPlayerControlled && !player.isPlayerDead && player.inAnimationWithEnemy == null!)
            {
                return true;
            }

            return false;
        }

        void SetOffsets(Vector3 position, Vector3 rotation)
        {
            itemProperties.positionOffset = position;
            itemProperties.rotationOffset = rotation;
        }

        void AttemptTransformLocalPlayer()
        {
            if (!StartOfRound.Instance.shipIsLeaving && (!StartOfRound.Instance.inShipPhase || !(StartOfRound.Instance.testRoom == null)) && !attaching)
            {
                playerHeldBy.SwitchToItemSlot(0, this);
                if (!isPocketed)
                {
                    attaching = true;
                    playerHeldBy.activatingItem = true;
                    TransformPlayerServerRpc();
                }
            }
        }

        void DoTransformationAnimation()
        {
            attaching = true;
            SetOffsets(posOffsetShoving, rotOffsetShoving);
            playerHeldBy.playerBodyAnimator.SetBool("HoldMask", true);

            try
            {
                if (playerHeldBy.currentVoiceChatAudioSource == null)
                {
                    StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                }
                if (playerHeldBy.currentVoiceChatAudioSource != null)
                {
                    playerHeldBy.currentVoiceChatAudioSource.GetComponent<AudioLowPassFilter>().lowpassResonanceQ = 3f;
                    OccludeAudio component = playerHeldBy.currentVoiceChatAudioSource.GetComponent<OccludeAudio>();
                    component.overridingLowPass = true;
                    component.lowPassOverride = 300f;
                    playerHeldBy.voiceMuffledByEnemy = true;
                }
            }
            catch (Exception arg)
            {
                logger.LogError($"Caught exception while attempting to muffle player voice from SCP-323 item: {arg}");
            }

            StartCoroutine(DoTransformationAnimationCoroutine());
        }

        IEnumerator DoTransformationAnimationCoroutine()
        {
            yield return new WaitForSecondsRealtime(5f);

            if (lastPlayerHeldBy != null && !lastPlayerHeldBy.isPlayerDead)
            {
                FinishTransformation();
            }
            else
            {
                CancelTransformation();
            }
        }

        void FinishTransformation()
        {
            lastPlayerHeldBy!.voiceMuffledByEnemy = false;
            lastPlayerHeldBy.playerBodyAnimator.SetBool("HoldMask", false);
            SetOffsets(posOffsetHolding, rotOffsetHolding);
            lastPlayerHeldBy.activatingItem = false;
            lastPlayerHeldBy.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Bludgeoning); // This despawns the item too?
            attaching = false;

            if (IsServerOrHost)
            {
                SpawnSCP3231(lastPlayerHeldBy.transform.position);
            }
        }

        void CancelTransformation()
        {
            lastPlayerHeldBy!.voiceMuffledByEnemy = false;
            lastPlayerHeldBy.playerBodyAnimator.SetBool("HoldMask", false);
            SetOffsets(posOffsetHolding, rotOffsetHolding);
            attaching = false;
            skullOn = false;
        }

        void SpawnSCP3231(Vector3 spawnPos)
        {
            if (IsServerOrHost)
            {
                EnemyType scp = SCPItems.SCPEnemiesList.Where(x => x.enemyType.name == "SCP323_1Enemy").FirstOrDefault().enemyType;
                RoundManager.Instance.SpawnEnemyGameObject(spawnPos, Quaternion.identity.y, 0, scp);
            }
        }

        // IVisibleThreat Settings

        ThreatType IVisibleThreat.type => ThreatType.Item;

        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }

        int IVisibleThreat.GetInterestLevel()
        {
            return 0;
        }

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            if (skullOn)
            {
                return 50;
            }
            return 1;
        }

        Transform IVisibleThreat.GetThreatLookTransform()
        {
            return base.transform;
        }

        Transform IVisibleThreat.GetThreatTransform()
        {
            return base.transform;
        }

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            return Vector3.zero;
        }

        float IVisibleThreat.GetVisibility()
        {
            if (isPocketed)
            {
                return 0f;
            }
            return 1f;
        }

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        void TransformPlayerServerRpc()
        {
            if (IsServerOrHost)
            {
                TransformPlayerClientRpc();
            }
        }

        [ClientRpc]
        void TransformPlayerClientRpc()
        {
            lastPlayerHeldBy = playerHeldBy;
            DoTransformationAnimation();
        }
    }
}