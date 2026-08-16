using GameNetcodeStuff;
using HarmonyLib;
using PSCPLibrary;
using PSCPLibrary.Interfaces;
using SnowyLib;
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.SCP.SCP323
{
    // UPDATE: When trying to add rotation and moving again, look at SoccerBall item code first
    internal class SCP323Behavior : PhysicsProp, ISingletonItem, IVisibleThreat, ISCP
    {
        // scale 0.31
        // scaleonwendigo 0.12
        SCPInfo ISCP.SCPInfo => info;
        public static SCP323Behavior? Instance { get; private set; }

        public SCPInfo info = null!;
        public MeshRenderer renderer = null!;
        public GameObject SCP3231Prefab = null!;
        public Transform turnCompass = null!;

        public static AttachState testState = AttachState.None;

        readonly Vector3 posOffsetWearing = new Vector3(-0.275f, -0.15f, -0.05f);
        readonly Vector3 posOffsetShoving = new Vector3(-0.3f, 0.17f, -0.075f);
        readonly Vector3 posOffsetHolding = new Vector3(-0.23f, 0.05f, -0.16f);

        readonly Vector3 rotOffsetWearing = new Vector3(-55f, -60f, 0f);
        readonly Vector3 rotOffsetShoving = new Vector3(-90f, -60f, 0f);
        readonly Vector3 rotOffsetHolding = new Vector3(-60f, -90f, 0f);

        Vector3 posOffsetWendigo = new Vector3(-0.125f, 0.075f, -0.18f);
        Vector3 rotOffsetWendigo = new Vector3(125f, 10f, 3f);

        bool attaching;
        bool skullOn;
        Coroutine? transformingCoroutine;
        float timeSinceSpawn;
        public float madness;

        public SCP323_1AI AttachedToWendigo = null!;

        // Config variables
        const float distanceToIncreaseInsanity = 5f;
        public static bool showInsanity { get; private set; } = false;

        public ThreatType type => ThreatType.Player;

        [InitConfig]
        public static void InitConfigs()
        {
            showInsanity = PluginInstance.Config.Bind("SCP-323 Options", "SCP-323 | Show insanity", false, "Blur the players vision when they are near SCP-323 based on their insanity.").Value;
        }

        public enum AttachState
        {
            None,
            Wearing,
            Transforming
        }

        public override void ActivatePhysicsTrigger(Collider other)
        {
            logger.LogDebug("Collided with skull");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this) { return; }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (Instance == this)
                Instance = null;
        }

        void BaseUpdate()
        {
            if (currentUseCooldown >= 0f)
            {
                currentUseCooldown -= Time.deltaTime;
            }
            if (base.IsOwner)
            {
                if (isBeingUsed && itemProperties.requiresBattery)
                {
                    if (insertedBattery.charge > 0f)
                    {
                        if (!itemProperties.itemIsTrigger)
                        {
                            insertedBattery.charge -= Time.deltaTime / itemProperties.batteryUsage;
                        }
                    }
                    else if (!insertedBattery.empty)
                    {
                        insertedBattery.empty = true;
                        if (isBeingUsed)
                        {
                            Debug.Log("Use up batteries local");
                            isBeingUsed = false;
                            UseUpBatteries();
                            UseUpItemBatteriesServerRpc();
                        }
                    }
                }
                if (!wasOwnerLastFrame)
                {
                    wasOwnerLastFrame = true;
                }
            }
            else if (wasOwnerLastFrame)
            {
                wasOwnerLastFrame = false;
            }
            if (!isHeld && parentObject == null)
            {
                if (fallTime < 1f)
                {
                    reachedFloorTarget = false;
                    FallWithCurve();
                    if (base.transform.localPosition.y - targetFloorPosition.y < 0.05f && !hasHitGround)
                    {
                        PlayDropSFX();
                        OnHitGround();
                    }
                    return;
                }
                if (!reachedFloorTarget)
                {
                    if (!hasHitGround)
                    {
                        PlayDropSFX();
                        OnHitGround();
                    }
                    reachedFloorTarget = true;
                    if (floorYRot == -1)
                    {
                        base.transform.rotation = Quaternion.Euler(itemProperties.restingRotation.x, base.transform.eulerAngles.y, itemProperties.restingRotation.z);
                    }
                    else
                    {
                        base.transform.rotation = Quaternion.Euler(itemProperties.restingRotation.x, (float)(floorYRot + itemProperties.floorYOffset) + 90f, itemProperties.restingRotation.z);
                    }
                }
                base.transform.localPosition = targetFloorPosition;
            }
            else if (isHeld || isHeldByEnemy)
            {
                reachedFloorTarget = false;
            }
        }

        public override void Update()
        {
            BaseUpdate();

            timeSinceSpawn += Time.deltaTime;

            madness = Mathf.Max(madness, localPlayer.insanityLevel);

            //if (localPlayer == player) { logger.LogDebug("madness: " + madness); } // TODO: TEST AND TWEAK MADNESS VALUES

            if (PlayerIsTargetable(localPlayer) && Vector3.Distance(transform.position, localPlayer.transform.position) < distanceToIncreaseInsanity)
            {
                if (playerHeldBy != null && localPlayer == playerHeldBy)
                {
                    if (skullOn)
                    {
                        madness += Time.deltaTime * 1f;
                    }
                    else
                    {
                        madness += Time.deltaTime * 0.5f;
                    }

                    if (showInsanity)
                    {
                        localPlayer.playersManager.fearLevel = Mathf.Max(madness / localPlayer.maxInsanityLevel, localPlayer.playersManager.fearLevel);
                    }

                    if (madness >= localPlayer.maxInsanityLevel)
                    {
                        localPlayer.insanityLevel = 0f;
                        madness = 0f;
                        AttemptTransformLocalPlayer();
                        return;
                    }
                }
                else
                {
                    madness += Time.deltaTime * 0.1f;
                }
            }
            else
            {
                madness -= Time.deltaTime * 0.5f;
            }

            madness = Mathf.Clamp(madness, 0f, localPlayer.maxInsanityLevel);
            localPlayer.insanityLevel = madness;
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
                    RoundManager.Instance.SpawnedEnemies.Remove(AttachedToWendigo);
                    AttachedToWendigo.NetworkObject.Despawn(true);
                }

                //MeshObj.SetActive(true);
                transform.localScale = new Vector3(0.31f, 0.31f, 0.31f);
                AttachedToWendigo = null!;
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            /*if (Utils.testing)
            {
                playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
                skullOn = buttonDown;
                playerHeldBy.activatingItem = buttonDown;
                return;
            }*/
            
            if (playerHeldBy != null)
            {
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

        void ChangeAttachState(AttachState newState)
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

        void AttemptTransformLocalPlayer()
        {
            if (!StartOfRound.Instance.shipIsLeaving && (!StartOfRound.Instance.inShipPhase || !(StartOfRound.Instance.testRoom == null)) && !attaching)
            {
                logger.LogDebug("Attempting to transform local player.");
                int itemSlotIndex = localPlayer.ItemSlots.IndexOf(this);
                localPlayer.SwitchToItemSlot(itemSlotIndex, this);
                if (!isPocketed)
                {
                    attaching = true;
                    localPlayer.activatingItem = true;
                    TransformPlayerServerRpc();
                }
            }
        }

        void DoTransformationAnimation(PlayerControllerB player)
        {
            logger.LogDebug("Doing transformation animation.");
            attaching = true;
            ChangeAttachState(AttachState.Transforming);
            player.playerBodyAnimator.SetBool("HoldMask", true);

            try
            {
                if (player.currentVoiceChatAudioSource == null)
                {
                    StartOfRound.Instance.RefreshPlayerVoicePlaybackObjects();
                }
                if (player.currentVoiceChatAudioSource != null)
                {
                    player.currentVoiceChatAudioSource.GetComponent<AudioLowPassFilter>().lowpassResonanceQ = 3f;
                    OccludeAudio component = player.currentVoiceChatAudioSource.GetComponent<OccludeAudio>();
                    component.overridingLowPass = true;
                    component.lowPassOverride = 300f;
                    player.voiceMuffledByEnemy = true;
                }
            }
            catch (Exception arg)
            {
                logger.LogError($"Caught exception while attempting to muffle player voice from SCP-323 item: {arg}");
            }
            
            logger.LogDebug("Starting transformation animation coroutine.");

            IEnumerator DoTransformationAnimationCoroutine(PlayerControllerB player)
            {
                logger.LogDebug("Doing transformation animation coroutine.");
                yield return new WaitForSecondsRealtime(5f);

                player.DropAllHeldItems();

                Vector3 spawnPos = player.transform.position;
                player.KillPlayer(Vector3.zero, false, CauseOfDeath.Bludgeoning);

                yield return new WaitForSeconds(1f);

                if (player != null)
                {
                    if (player.isPlayerDead)
                    {
                        FinishTransformation(spawnPos);
                    }
                    StopTransformation(player);
                }
            }

            StartCoroutine(DoTransformationAnimationCoroutine(player));
        }

        void FinishTransformation(Vector3 spawnPos)
        {
            logger.LogDebug("Finishing transformation.");

            if (IsServerOrHost)
            {
                logger.LogDebug("Spawning SCP-323-1.");
                SpawnSCP3231(spawnPos);
                if (NetworkObject != null && NetworkObject.IsSpawned)
                {
                    Instance = null;
                    NetworkObject.Despawn(true);
                }
            }
        }

        void StopTransformation(PlayerControllerB player)
        {
            logger.LogDebug("Stopping transformation.");
            if (player != null)
            {
                player.activatingItem = false;
                player.voiceMuffledByEnemy = false;
                player.playerBodyAnimator.SetBool("HoldMask", false);
            }
            ChangeAttachState(AttachState.None);
            attaching = false;
            skullOn = false;
        }

        void SpawnSCP3231(Vector3 spawnPos)
        {
            if (IsServerOrHost)
            {
                GameObject scpObj = Instantiate(SCP3231Prefab, spawnPos, Quaternion.identity);
                SCP323_1AI scp = scpObj.GetComponent<SCP323_1AI>();
                scp.NetworkObject.Spawn(destroyWithScene: true);
                RoundManager.Instance.SpawnedEnemies.Add(scp);
            }
        }

        #region IVisibleThreat Settings

        ThreatType IVisibleThreat.type
        {
            get
            {
                return ThreatType.Item;
            }
        }

        int IVisibleThreat.SendSpecialBehaviour(int id)
        {
            return 0;
        }

        int IVisibleThreat.GetInterestLevel()
        {
            return 1;
        }

        int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
        {
            if (skullOn)
            {
                return 999999999;
            }
            return 999999999;
        }

        Transform IVisibleThreat.GetThreatLookTransform()
        {
            if (playerHeldBy != null)
            {
                return playerHeldBy.gameplayCamera.transform;
            }
            return base.transform;
        }

        Transform IVisibleThreat.GetThreatTransform()
        {
            if (playerHeldBy != null)
            {
                return playerHeldBy.transform;
            }
            return base.transform;
        }

        Vector3 IVisibleThreat.GetThreatVelocity()
        {
            if (playerHeldBy != null)
            {
                return Vector3.Normalize((playerHeldBy.serverPlayerPosition - playerHeldBy.oldPlayerPosition) * 100f);
            }
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

        public GrabbableObject? GetHeldObject()
        {
            return null;
        }

        public bool IsThreatDead()
        {
            return false;
        }

        #endregion

        // RPCs

        [ServerRpc(RequireOwnership = false)]
        void TransformPlayerServerRpc()
        {
            if (!IsServerOrHost) { return; }
            TransformPlayerClientRpc();
        }

        [ClientRpc]
        void TransformPlayerClientRpc()
        {
            logger.LogDebug("In SCP323Behavior.TransformPlayerClientRpc");
            DoTransformationAnimation(playerHeldBy);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangeAttachStateServerRpc(AttachState state)
        {
            if (!IsServerOrHost) { return; }
            ChangeAttachStateClientRpc(state);
        }

        [ClientRpc]
        void ChangeAttachStateClientRpc(AttachState state)
        {
            ChangeAttachState(state);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangeSizeServerRpc(float size)
        {
            if (!IsServerOrHost) { return; }
            ChangeSizeClientRpc(size);
        }

        [ClientRpc]
        public void ChangeSizeClientRpc(float size)
        {
            transform.localScale = new Vector3(size, size, size);
        }
    }

    [HarmonyPatch]
    internal class SCP323Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BaboonBirdAI), nameof(BaboonBirdAI.SetAggressiveMode))]
        public static void SetAggressiveModePrefix(BaboonBirdAI __instance, ref int mode)
        {
            try
            {
                if (SCP323Behavior.Instance != null && SCP323Behavior.Instance.playerHeldBy != null && __instance.focusedThreat != null)
                {
                    if (__instance.focusedThreat.type == ThreatType.Player)
                    {
                        if (__instance.focusedThreat.threatScript == null) { return; }
                        if (__instance.focusedThreat.threatScript.GetThreatTransform() == null) { return; }
                        if (__instance.focusedThreat.threatScript.GetThreatTransform() == SCP323Behavior.Instance.playerHeldBy.transform)
                        {
                            mode = 1;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.SetMovingTowardsTargetPlayer))]
        public static bool SetMovingTowardsTargetPlayerPrefix(EnemyAI __instance, PlayerControllerB playerScript)
        {
            try
            {
                if (__instance is not MaskedPlayerEnemy) { return true; }
                if (SCP323Behavior.Instance != null && SCP323Behavior.Instance.playerHeldBy != null && playerScript == SCP323Behavior.Instance.playerHeldBy)
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                logger.LogError(e);
                return true;
            }
            return true;
        }
    }
}