using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public GameObject MeshObj;
        public GameObject SCP3231Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

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
        Coroutine? transformingCoroutine;
        float timeSpawned = 0f;

        public SCP323_1AI AttachedToWendigo = null!;

        // Config variables
        float distanceToIncreaseInstanity => config323DistanceToIncreaseInsanity.Value;
        int insanityHolding => config323InsanityHolding.Value;
        int insanityWearing => config323InsanityWearing.Value;
        int insanityToTransform => config323InsanityToTransform.Value;
        bool showInsanity => config323ShowInsanity.Value;

        public ThreatType type => ThreatType.Player;

        public enum AttachState
        {
            None,
            Wearing,
            Transforming
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-513 in the scene. Removing this one.");
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-513");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
                Utils.FreezePlayer(localPlayer, false);
            }
        }

        public override void Update()
        {
            base.Update();

            timeSpawned += Time.deltaTime;

            if (Instance != this)
            {
                grabbable = false;
                if (IsServerOrHost && timeSpawned > 3f)
                {
                    NetworkObject.Despawn(true);
                }
                return;
            }

            if (PlayerIsTargetable(localPlayer) && Vector3.Distance(transform.position, localPlayer.transform.position) < distanceToIncreaseInstanity)
            {
                if (showInsanity) // TODO: Test this
                {
                    playerHeldBy.playersManager.fearLevel = playerHeldBy.insanityLevel / 50;
                }

                timeSinceInsanityIncrease += Time.unscaledDeltaTime;

                if (timeSinceInsanityIncrease > 10f)
                {
                    timeSinceInsanityIncrease = 0f;

                    if (playerHeldBy != null)
                    {
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
                        }
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

            if (Utils.testing)
            {
                playerHeldBy.playerBodyAnimator.SetBool("HoldMask", buttonDown);
                skullOn = buttonDown;
                playerHeldBy.activatingItem = buttonDown;
                return;
            }
            
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

        void AttemptTransformLocalPlayer()
        {
            logger.LogDebug("Attempting to transform local player.");
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
            if (transformingCoroutine != null)
            {
                StopCoroutine(transformingCoroutine);
                transformingCoroutine = null;
            }
            transformingCoroutine = StartCoroutine(DoTransformationAnimationCoroutine(player)); // TODO: Test this
        }

        IEnumerator DoTransformationAnimationCoroutine(PlayerControllerB player)
        {
            logger.LogDebug("Doing transformation animation coroutine.");
            yield return new WaitForSecondsRealtime(5f);

            player.DropAllHeldItemsAndSync();

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

        void FinishTransformation(Vector3 spawnPos) // TODO: Test this
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

        // IVisibleThreat Settings

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
            return true;
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
            logger.LogDebug("In SCP323Behavior.TransformPlayerClientRpc");
            DoTransformationAnimation(playerHeldBy);
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

    [HarmonyPatch]
    internal class SCP323Patches
    {
        private static ManualLogSource logger = LoggerInstance;

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