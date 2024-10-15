using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs
{
    public class NetworkHandlerHeavy : NetworkBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        public static NetworkHandlerHeavy Instance { get; private set; }

        public static PlayerControllerB PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts[StartOfRound.Instance.ClientPlayerList[id]]; }

#pragma warning disable 0649
        public Material OverlayMaterial = null!;
#pragma warning restore 0649

        public override void OnNetworkSpawn()
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                if (Instance != null)
                {
                    Instance.gameObject.GetComponent<NetworkObject>().Despawn();
                    logger.LogDebug("Despawned network object");
                }
            }

            Instance = this;
            logger.LogDebug("set instance to this");
            base.OnNetworkSpawn();
            logger.LogDebug("base.OnNetworkSpawn");
        }

        [ServerRpc(RequireOwnership = false)]
        public void ShakePlayerCamerasServerRpc(ScreenShakeType type, float distance, Vector3 position)
        {
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                ShakePlayerCamerasClientRpc(type, distance, position);
            }
        }

        [ClientRpc]
        private void ShakePlayerCamerasClientRpc(ScreenShakeType type, float distance, Vector3 position)
        {
            float num = Vector3.Distance(localPlayer.transform.position, position);
            if (num < distance)
            {
                HUDManager.Instance.ShakeCamera(type);
            }
            else if (num < distance * 2f)
            {
                if ((int)type - 1 >= 0) { HUDManager.Instance.ShakeCamera((ScreenShakeType)((int)type - 1)); }
            }
        }

        [ClientRpc]
        private void GrabObjectClientRpc(ulong id, ulong clientId) // TODO: Figure out how to turn off grab animation
        {
            if (clientId == localPlayer.actualClientId)
            {
                if (localPlayer.ItemSlots.Where(x => x == null).Any())
                {
                    GrabbableObject grabbableItem = NetworkManager.Singleton.SpawnManager.SpawnedObjects[id].gameObject.GetComponent<GrabbableObject>();
                    logger.LogDebug($"Grabbing item with weight: {grabbableItem.itemProperties.weight}");

                    localPlayer.GrabObjectServerRpc(grabbableItem.NetworkObject);
                    grabbableItem.parentObject = localPlayer.localItemHolder;
                    grabbableItem.GrabItemOnClient();
                }
            }
        }

        [ClientRpc]
        private void ChangePlayerSizeClientRpc(ulong clientId, float size)
        {
            PlayerControllerB playerHeldBy = StartOfRound.Instance.allPlayerScripts[StartOfRound.Instance.ClientPlayerList[clientId]];

            playerHeldBy.thisPlayerBody.localScale = new Vector3(size, size, size);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ChangePlayerSizeServerRpc(ulong clientId, float size)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                ChangePlayerSizeClientRpc(clientId, size);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnEnemyServerRpc(string enemyName, Vector3 pos = default, int yRot = default, bool outsideEnemy = false)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                SpawnableEnemyWithRarity enemy = RoundManager.Instance.currentLevel.Enemies.Where(x => x.enemyType.enemyName == enemyName).FirstOrDefault();
                if (enemy == null) { logger.LogError($"Error: Couldnt get {enemyName} to spawn"); return; }
                int index = RoundManager.Instance.currentLevel.Enemies.IndexOf(enemy);

                if (pos != default)
                {
                    RoundManager.Instance.SpawnEnemyOnServer(pos, yRot, index);
                    if (outsideEnemy)
                    {
                        EnemyAI spawnedEnemy = RoundManager.Instance.SpawnedEnemies.Last();
                        spawnedEnemy.SetEnemyOutside(true);
                    }
                    return;
                }

                if (outsideEnemy)
                {
                    var nodes = RoundManager.Instance.outsideAINodes;
                    int nodeIndex = UnityEngine.Random.Range(0, nodes.Length - 1);

                    RoundManager.Instance.SpawnEnemyOnServer(nodes[nodeIndex].transform.position, yRot, index);
                }
                else
                {
                    List<EnemyVent> vents = RoundManager.Instance.allEnemyVents.ToList();
                    logger.LogDebug("Found vents: " + vents.Count);

                    EnemyVent vent = vents[UnityEngine.Random.Range(0, vents.Count - 1)];
                    logger.LogDebug("Selected vent: " + vent);

                    vent.enemyTypeIndex = index;
                    vent.enemyType = enemy.enemyType;
                    logger.LogDebug("Updated vent with enemy type index: " + vent.enemyTypeIndex + " and enemy type: " + vent.enemyType);

                    RoundManager.Instance.SpawnEnemyFromVent(vent);
                    logger.LogDebug("Spawning SCP-956 from vent");
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnItemServerRpc(ulong clientId, string _itemName, int newValue, Vector3 pos, UnityEngine.Quaternion rot, bool grabItem = false)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                Item item = StartOfRound.Instance.allItemsList.itemsList.Where(x => x.itemName == _itemName).FirstOrDefault();
                logger.LogDebug("Got item");

                GameObject obj = UnityEngine.Object.Instantiate(item.spawnPrefab, pos, rot, StartOfRound.Instance.propsContainer);
                if (newValue != 0) { obj.GetComponent<GrabbableObject>().SetScrapValue(newValue); }
                logger.LogDebug($"Spawning item with weight: {obj.GetComponent<GrabbableObject>().itemProperties.weight}");
                obj.GetComponent<NetworkObject>().Spawn();

                if (grabItem)
                {
                    GrabObjectClientRpc(obj.GetComponent<NetworkObject>().NetworkObjectId, clientId);
                    logger.LogDebug("Grabbed obj");
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void DespawnDeadPlayerServerRpc(ulong clientId)
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == clientId).FirstOrDefault();
                if (player == null) { return; }

                UnityEngine.Object.Destroy(player.deadBody.gameObject);
            }
        }
    }

    [HarmonyPatch]
    public class NetworkObjectManager
    {
        static GameObject networkPrefab;
        private static ManualLogSource logger = Plugin.LoggerInstance;

        [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.Start))]
        public static void Init()
        {
            logger.LogDebug("Initializing network prefab...");
            if (networkPrefab != null)
                return;

            networkPrefab = (GameObject)Plugin.ModAssets.LoadAsset("Assets/ModAssets/SharedAssets/NetworkHandlerHeavyItemSCPs.prefab");
            logger.LogDebug("Got networkPrefab");
            //networkPrefab.AddComponent<NetworkHandlerHeavy>();
            //logger.LogDebug("Added component");

            NetworkManager.Singleton.AddNetworkPrefab(networkPrefab);
            logger.LogDebug("Added networkPrefab");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.Awake))]
        static void SpawnNetworkHandler()
        {
            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                var networkHandlerHost = UnityEngine.Object.Instantiate(networkPrefab, Vector3.zero, Quaternion.identity);
                logger.LogDebug("Instantiated networkHandlerHost");
                networkHandlerHost.GetComponent<NetworkObject>().Spawn();
                logger.LogDebug("Spawned network object");
            }
        }
    }
}