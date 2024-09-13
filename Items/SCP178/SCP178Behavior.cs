using BepInEx.Logging;
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

namespace HeavyItemSCPs.Items.SCP178
{
    internal class SCP178Behavior : WearableItem
    {
        private static ManualLogSource logger = LoggerInstance;

        float timeSinceDelayedUpdate = 0f;

        public Material Anaglyph3DMaterial;

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
            if (buttonDown)
            {
                Wear();
            }
        }

        /*public override void Update()
        {
            base.Update();
            timeSinceDelayedUpdate += Time.deltaTime;
            if (timeSinceDelayedUpdate > 1.5f)
            {
                timeSinceDelayedUpdate = 0f;
                DelayedUpdate();

            }
        }*/

        /*public void DelayedUpdate()
        {
            if (playerWornBy != null && playerWornBy == localPlayer)
            {
                foreach(var scp in FindObjectsOfType<SCP1781AI>())
                {
                    if (!scp.meshEnabledOnClient)
                    {
                        scp.EnableEnemyMesh(true);
                    }
                }
            }
        }*/

        public override void Wear()
        {
            base.Wear();
            StartCoroutine(Enable1781MeshesCoroutine());
            WearServerRpc();

            //playerWornBy.drunkness = 0.5f;
            SCP1783DVision.Instance.Enable3DVision(true);
        }

        public override void UnWear(bool grabItem = true)
        {
            UnWearServerRpc();
            base.UnWear(grabItem);

            SCP1783DVision.Instance.Enable3DVision(false);
        }

        private IEnumerator Enable1781MeshesCoroutine()
        {
            yield return new WaitUntil(() => NetworkHandlerHeavy.Instance.Spawned1781Instances.Value);
            yield return new WaitForSecondsRealtime(2f);
            SCP1781Manager.EnableAll1781Meshes(true);
        }

        // RPCs
        [ServerRpc(RequireOwnership = false)]
        public void WearServerRpc()
        {
            logger.LogDebug("WearServerRpc called.");

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                SCP1781Manager.PlayersWearing178.Add(playerWornBy);

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
                SCP1781Manager.PlayersWearing178.Remove(playerWornBy);
                EnableMeshesClientRpc(false);
            }
        }

        [ClientRpc]
        public void EnableMeshesClientRpc(bool enabled) // TODO: Test this to make sure it works and enables the mesh properly
        {
            logger.LogDebug($"EnableMeshesClientRpc called with enabled = {enabled}.");

            if (localPlayer == playerWornBy)
            {
                logger.LogDebug($"localPlayer matches playerWornBy ({playerWornBy}). Enabling/Disabling meshes.");

                SCP1781Manager.EnableAll1781Meshes(false);
            }
        }
    }

    /*[HarmonyPatch]
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
            if (SCP1781Manager.Instance != null)
            {
                Object.Destroy(SCP1781Manager.Instance.gameObject);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnShipLandedMiscEvents))]
        public static void OnShipLandedMiscEventsPostfix()
        {
            if (SCP1781Manager.Instance == null && SCP1781Manager.PlayersWearing178.Count > 0)
            {
                SCP1781Manager.Init();
            }
        }
    }*/
}