﻿using BepInEx.Logging;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UIElements;
using UnityEngine.VFX;
using static HeavyItemSCPs.Plugin;
using WearableItemsAPI;
using HarmonyLib;

/* bodyparts
 * 0 head
 * 1 right arm
 * 2 left arm
 * 3 right leg
 * 4 left leg
 * 5 chest
 * 6 feet
 * 7 right hip
 * 8 crotch
 * 9 left shoulder
 * 10 right shoulder */

// Pos offset 0, 0.17, 0

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP178Behavior : WearableItem
    {
        private static ManualLogSource logger = LoggerInstance;
        public static SCP178Behavior? Instance { get; private set; }

        float timeSinceDelayedUpdate = 0f;

        public override void Start()
        {
            base.Start();

            if (IsServerOrHost)
            {
                StartCoroutine(DelayedStart());
            }
        }

        IEnumerator DelayedStart()
        {
            yield return new WaitUntil(() => NetworkObject.IsSpawned);

            if (Instance != null && NetworkObject.IsSpawned)
            {
                if (IsServerOrHost)
                {
                    logger.LogDebug("There is already a SCP-178 in the scene. Removing this one.");
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

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
            if (buttonDown)
            {
                Wear();
            }
        }

        public override void Wear()
        {
            base.Wear();
            if (playerWornBy!.drunkness < 0.2f) { playerWornBy.drunkness = 0.2f; }

            StartCoroutine(Enable1781MeshesCoroutine());
            WearServerRpc();

            SCP1783DVision.Instance.Enable3DVision(true);
        }

        public override void UnWear(bool grabItem = true)
        {
            UnWearServerRpc();
            SCP1781Manager.EnableAll1781MeshesOnLocalClient(false);
            base.UnWear(grabItem);

            SCP1783DVision.Instance.Enable3DVision(false);
        }

        private IEnumerator Enable1781MeshesCoroutine()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            SCP1781Manager.EnableAll1781MeshesOnLocalClient(true);
        }

        // RPCs
        [ServerRpc(RequireOwnership = false)]
        public void WearServerRpc()
        {
            logger.LogDebug("WearServerRpc called.");

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                if (playerWornBy == null) { logger.LogError("playerWornBy is null."); return; }
                playerWornBy.voiceMuffledByEnemy = true;

                if (StartOfRound.Instance.inShipPhase) { return; }

                if (SCP1781Manager.Instance == null)
                {
                    logger.LogDebug("SCP1781Manager instance is null. Creating a new instance.");
                    SCP1781Manager.Init();
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void UnWearServerRpc()
        {
            logger.LogDebug("UnWearServerRpc called.");

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                if (playerWornBy == null) { logger.LogError("playerWornBy is null."); return; }
                playerWornBy.voiceMuffledByEnemy = false;
            }
        }

        [ClientRpc]
        public void EnableMeshesClientRpc(bool enabled) // TODO: Test this to make sure it works and enables the mesh properly
        {
            logger.LogDebug($"EnableMeshesClientRpc called with enabled = {enabled}.");

            if (localPlayer == playerWornBy)
            {
                logger.LogDebug($"localPlayer matches playerWornBy ({playerWornBy}). Enabling/Disabling meshes.");

                SCP1781Manager.EnableAll1781MeshesOnLocalClient(false);
            }
        }
    }

    [HarmonyPatch]
    public class SCP178Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.ConnectClientToPlayerObject))]
        public static void ConnectClientToPlayerObjectPostfix()
        {
            if (configEnableSCP178.Value)
            {
                SCP1783DVision.Instance.Init();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.DespawnPropsAtEndOfRound))]
        public static void DespawnPropsAtEndOfRoundPostfix()
        {
            if (configEnableSCP178.Value && SCP1781Manager.Instance != null)
            {
                Object.Destroy(SCP1781Manager.Instance);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnShipLandedMiscEvents))]
        public static void OnShipLandedMiscEventsPostfix()
        {
            if (SCP1781Manager.Instance == null && SCP178Behavior.Instance != null && SCP178Behavior.Instance.playerWornBy != null)
            {
                SCP1781Manager.Init();
            }
        }
    }
}