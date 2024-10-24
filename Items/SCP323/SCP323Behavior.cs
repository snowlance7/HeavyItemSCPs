﻿using BepInEx.Logging;
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
        // scaleonwendigo 0.12

        private static ManualLogSource logger = LoggerInstance;
        public static SCP323Behavior? Instance { get; private set; }

#pragma warning disable 0649
        public GameObject MeshObj = null!;
#pragma warning restore 0649

        public static bool isTesting = false;
        public static AttachState testState = AttachState.None;

        readonly Vector3 posOffsetWearing = new Vector3(-0.275f, -0.15f, -0.05f);
        readonly Vector3 posOffsetShoving = new Vector3(-0.3f, 0.17f, -0.075f);
        readonly Vector3 posOffsetHolding = new Vector3(-0.23f, 0.05f, -0.16f);

        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -60f, 0f);
        readonly Vector3 rotOffsetShoving = new Vector3(-90f, -60f, 0f);
        readonly Vector3 rotOffsetHolding = new Vector3(-60f, -90f, 0f);

        Vector3 posOffsetWendigo = new Vector3(-0.125f, 0.075f, -0.18f);
        Vector3 rotOffsetWendigo = new Vector3(125f, 10f, 3f);

        float timeSinceInsanityIncrease;
        bool attaching;
        bool skullOn;
        PlayerControllerB? lastPlayerHeldBy;
        bool forceTransforming;
        Coroutine? transformingCoroutine;

        public SCP323_1AI AttachedToWendigo = null!;

        // Config variables
        float distanceToIncreaseInstanity; // default 10 or 15
        int insanityNearby; // default 5
        int insanityHolding; // default 10
        int insanityWearing; // default 10
        float forceSwitchChance; // default 0.25
        float forceTransformChance; // default 0.1
        int insanityToForceSwitch; // default 20
        int insanityToForceTransform; // default 35
        int insanityToTransform; // default 50

        public ThreatType type => ThreatType.Player;

        public enum AttachState
        {
            None,
            Wearing,
            Transforming
        }

        public override void Start()
        {
            base.Start();

            distanceToIncreaseInstanity = config323DistanceToIncreaseInsanity.Value;
            insanityNearby = config323InsanityNearby.Value;
            insanityHolding = config323InsanityHolding.Value;
            insanityWearing = config323InsanityWearing.Value;
            forceSwitchChance = config323ForceSwitchChance.Value;
            forceTransformChance = config323ForceTransformChance.Value;
            insanityToForceSwitch = config323InsanityToForceSwitch.Value;
            insanityToForceTransform = config323InsanityToForceTransform.Value;
            insanityToTransform = config323InsanityToTransform.Value;

            StartCoroutine(DelayedStart());
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);

            if (Instance != null && NetworkObject.IsSpawned)
            {
                if (IsServerOrHost)
                {
                    logger.LogDebug("There is already a SCP-323 in the scene. Removing this one.");
                    NetworkObject.Despawn(true);
                }
            }
            else
            {
                Instance = this;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void Update()
        {
            base.Update();

            if (playerHeldBy != null)
            {
                lastPlayerHeldBy = playerHeldBy;
            }

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

                            if (playerHeldBy.insanityLevel >= insanityToTransform)
                            {
                                AttemptTransformLocalPlayer();
                                return;
                            }

                            if (playerHeldBy.insanityLevel >= insanityToForceTransform) // TODO: Test this
                            {
                                if (UnityEngine.Random.Range(0f, 1f) > forceTransformChance)
                                {
                                    AttemptTransformLocalPlayer(forced: true);
                                    return;
                                }
                            }

                            if (playerHeldBy.insanityLevel >= insanityToForceSwitch) // TODO: Test this
                            {
                                if (UnityEngine.Random.Range(0f, 1f) > forceSwitchChance)
                                {
                                    playerHeldBy.SwitchToItemSlot(1, this);
                                    return;
                                }
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

        public override void LateUpdate()
        {
            if (AttachedToWendigo != null)
            {
                transform.rotation = parentObject.rotation;
                transform.Rotate(rotOffsetWendigo);
                transform.position = parentObject.position;
                Vector3 positionOffset = posOffsetWendigo;
                positionOffset = parentObject.rotation * positionOffset;
                transform.position += positionOffset;
                return;
            }

            base.LateUpdate();
        }

        public override void GrabItem()
        {
            base.GrabItem();
            if (AttachedToWendigo != null)
            {
                if (IsServerOrHost && AttachedToWendigo.NetworkObject.IsSpawned)
                {
                    AttachedToWendigo.NetworkObject.Despawn(true);
                }

                MeshObj.SetActive(true);
                transform.localScale = new Vector3(0.31f, 0.31f, 0.31f);
                AttachedToWendigo = null!;
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (isTesting)
            {
                playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
                skullOn = buttonDown;
                playerHeldBy.activatingItem = buttonDown;
                return;
            }
            
            if (playerHeldBy != null)
            {
                if (forceTransforming && !buttonDown) // TODO: Test this
                {
                    StopTransformation();
                }

                if (!attaching)
                {
                    Wear(buttonDown);
                    playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
                    skullOn = buttonDown;
                    playerHeldBy.activatingItem = buttonDown;
                }
            }
        }

        void Wear(bool buttonDown)
        {
            if (buttonDown)
            {
                ChangeAttachState(AttachState.Wearing);
            }
            else
            {
                ChangeAttachState(AttachState.None);
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

        void ChangeAttachState(AttachState newState) // TODO: Test this
        {
            switch (newState)
            {
                case AttachState.None:
                    itemProperties.positionOffset = posOffsetHolding;
                    itemProperties.rotationOffset = rotOffsetHolding;
                    break;
                case AttachState.Wearing:
                    itemProperties.positionOffset = posOffsetWearing;
                    itemProperties.rotationOffset = rotOffsetWearing;
                    break;
                case AttachState.Transforming:
                    itemProperties.positionOffset = posOffsetShoving;
                    itemProperties.rotationOffset = rotOffsetShoving;
                    break;
                default:
                    break;
            }
        }

        void AttemptTransformLocalPlayer(bool forced = false)
        {
            if (!StartOfRound.Instance.shipIsLeaving && (!StartOfRound.Instance.inShipPhase || !(StartOfRound.Instance.testRoom == null)) && !attaching)
            {
                playerHeldBy.SwitchToItemSlot(0, this);
                if (!isPocketed)
                {
                    attaching = true;
                    playerHeldBy.activatingItem = true;
                    TransformPlayerServerRpc(forced);
                }
            }
        }

        void DoTransformationAnimation()
        {
            attaching = true;
            ChangeAttachState(AttachState.Transforming);
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
            
            if (transformingCoroutine != null)
            {
                StopCoroutine(transformingCoroutine);
                transformingCoroutine = null;
            }
            transformingCoroutine = StartCoroutine(DoTransformationAnimationCoroutine()); // TODO: Test this
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
                StopTransformation();
            }
        }

        void FinishTransformation() // TODO: Test this
        {
            lastPlayerHeldBy!.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Bludgeoning); // This despawns the item too?
            StopTransformation();

            if (lastPlayerHeldBy != null && lastPlayerHeldBy.isPlayerDead)
            {
                if (IsServerOrHost)
                {
                    SpawnSCP3231(lastPlayerHeldBy.transform.position);
                    if (NetworkObject.IsSpawned) { NetworkObject.Despawn(); }
                }
            }
        }

        void StopTransformation()
        {
            if (transformingCoroutine != null) // TODO: Test this
            {
                StopCoroutine(transformingCoroutine);
                transformingCoroutine = null;
            }

            if (lastPlayerHeldBy != null)
            {
                if (!lastPlayerHeldBy.isPlayerDead) { lastPlayerHeldBy.DropAllHeldItemsAndSync(); }
                lastPlayerHeldBy.activatingItem = false;
                lastPlayerHeldBy.voiceMuffledByEnemy = false;
                lastPlayerHeldBy.playerBodyAnimator.SetBool("HoldMask", false);
            }
            ChangeAttachState(AttachState.None);
            attaching = false;
            skullOn = false;
            forceTransforming = false;
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

        ThreatType IVisibleThreat.type => ThreatType.Player;

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
                return 100;
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
        void TransformPlayerServerRpc(bool forced = false)
        {
            if (IsServerOrHost)
            {
                TransformPlayerClientRpc(forced);
            }
        }

        [ClientRpc]
        void TransformPlayerClientRpc(bool forced = false)
        {
            forceTransforming = forced;
            lastPlayerHeldBy = playerHeldBy;
            DoTransformationAnimation();
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangeAttachStateServerRpc(AttachState state)
        {
            if (IsServerOrHost)
            {
                ChangeAttachStateClientRpc(state);
            }
        }

        [ClientRpc]
        void ChangeAttachStateClientRpc(AttachState state)
        {
            ChangeAttachState(state);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangeSizeServerRpc(float size)
        {
            if (IsServerOrHost)
            {
                ChangeSizeClientRpc(size);
            }
        }

        [ClientRpc]
        public void ChangeSizeClientRpc(float size)
        {
            transform.localScale = new Vector3(size, size, size);
        }
    }
}

// TODO:
// When scp3231 dies, spawn scp323 on the head of the dead wendigo