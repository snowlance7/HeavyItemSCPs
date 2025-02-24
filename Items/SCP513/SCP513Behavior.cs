﻿using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP178;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static HeavyItemSCPs.Plugin;
using HeavyItemSCPs.Items.SCP513;

namespace HeavyItemSCPs.Items.SCP513
{
    // TODO: Make sure 513-1 follows the player even if they go to another moon
    internal class SCP513Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public static SCP513Behavior? Instance { get; private set; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemAudio;
        public AudioClip[] BellSFX;
        public GameObject SCP513_1Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public List<SCP513_1AI> BellManInstances = [];
        public NetworkList<ulong> HauntedPlayers = new NetworkList<ulong>();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-178 in the scene. Removing this one.");
                if (!IsServerOrHost) { return; }
                NetworkObject.Despawn(true);
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-178");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;

                if (!IsServerOrHost) { return; }

                foreach (var bellMan in BellManInstances)
                {
                    bellMan.NetworkObject.Despawn(true);
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            if (buttonDown)
            {
                RingBell();
            }
        }

        public override void DiscardItem()
        {
            float fallDistance = startFallingPosition.y - targetFloorPosition.y;
            logger.LogDebug("FallDistance: " + fallDistance); // TODO: Use this to figure out how far it should fall to make the bell go off
        }

        public void RingBell()
        {
            RoundManager.PlayRandomClip(ItemAudio, BellSFX);

            if (!IsServerOrHost) { return; }

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled) { continue; }
                if (HauntedPlayers.Contains(player.actualClientId)) { continue; }
                if (Vector3.Distance(transform.position, player.bodyParts[0].transform.position) <= ItemAudio.maxDistance)
                {
                    HauntedPlayers.Add(player.actualClientId);

                    if (StartOfRound.Instance.inShipPhase || StartOfRound.Instance.shipIsLeaving) { continue; }
                    SpawnBellMan(player.actualClientId);
                }
            }
        }

        public void SpawnBellMan(ulong targetPlayerClientId)
        {
            GameObject bellManObj = Instantiate(SCP513_1Prefab, Vector3.zero, Quaternion.identity);
            SCP513_1AI bellMan = bellManObj.GetComponent<SCP513_1AI>();
            bellMan.NetworkObject.Spawn(true);
            RoundManager.Instance.SpawnedEnemies.Add(bellMan);
            BellManInstances.Add(bellMan);
            bellMan.ChangeTargetPlayerClientRpc(targetPlayerClientId);
        }
    }

    [HarmonyPatch]
    public class SCP513Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.OnShipLandedMiscEvents))]
        public static void OnShipLandedMiscEventsPostfix()
        {
            try
            {
                if (!IsServerOrHost) { return; }
                if (SCP513Behavior.Instance == null) { return; }

                foreach (var clientId in SCP513Behavior.Instance.HauntedPlayers)
                {
                    PlayerControllerB player = PlayerFromId(clientId);
                    if (player == null || !player.isPlayerControlled) { continue; }
                    SCP513Behavior.Instance.SpawnBellMan(clientId);
                }
            }
            catch (System.Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayerServerRpc))]
        public static void KillPlayerServerRpcPostfix(PlayerControllerB __instance)
        {
            try
            {
                if (!IsServerOrHost) { return; }
                if (SCP513Behavior.Instance == null) { return; }

                if (SCP513Behavior.Instance.HauntedPlayers.Contains(__instance.actualClientId))
                {
                    SCP513Behavior.Instance.HauntedPlayers.Remove(__instance.actualClientId);
                }
            }
            catch (System.Exception e)
            {
                LoggerInstance.LogError(e);
                return;
            }
        }
    }
}
