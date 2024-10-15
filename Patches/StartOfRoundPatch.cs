using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using static HeavyItemSCPs.Plugin;
using UnityEngine;
using System.Diagnostics.CodeAnalysis;

namespace HeavyItemSCPs.Patches
{
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(StartOfRound.firstDayAnimation))]
        public static void firstDayAnimationPostfix()
        {
            logger.LogDebug("First day started");

            // Setting up itemgroups

            if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
            {
                ItemGroup TabletopItems = Resources.FindObjectsOfTypeAll<ItemGroup>().Where(x => x.name == "TabletopItems").First();
                ItemGroup GeneralItemClass = Resources.FindObjectsOfTypeAll<ItemGroup>().Where(x => x.name == "GeneralItemClass").First();
                ItemGroup SmallItems = Resources.FindObjectsOfTypeAll<ItemGroup>().Where(x => x.name == "SmallItems").First();
                logger.LogDebug("Got itemgroups");

                // Setting up items

                foreach(var scpItem in SCPItems.SCPItemsList)
                {
                    if (scpItem.TabletopItem || scpItem.GeneralItem || scpItem.SmallItem)
                    {
                        Item item = LethalLib.Modules.Items.LethalLibItemList.Where(x => x.name == scpItem.item.name).First();
                        item.spawnPositionTypes.Clear();

                        if (item != null)
                        {
                            if (scpItem.TabletopItem) { item.spawnPositionTypes.Add(TabletopItems); }
                            if (scpItem.GeneralItem) { item.spawnPositionTypes.Add(GeneralItemClass); }
                            if (scpItem.SmallItem) { item.spawnPositionTypes.Add(SmallItems); }
                        }
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(StartOfRound.OnShipLandedMiscEvents))]
        public static void OnShipLandedMiscEventsPostfix() // TEST THIS
        {
            try
            {
                if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
                {
                    foreach (var scpItem in SCPItems.SCPItemsList)
                    {
                        scpItem.amountInCurrentLevel = 0;
                    }

                    List<GrabbableObject> allItems = UnityEngine.Object.FindObjectsOfType<GrabbableObject>().ToList();

                    allItems.RemoveAll(x => !SCPItems.SCPItemsList.Any(y => y.item.name == x.itemProperties.name));
                    //allItems = allItems.OrderBy(x => x.isInShipRoom || x.hasBeenHeld).ToList();
                    List<GrabbableObject> itemsInShip = allItems.Where(x => x.isInShipRoom || x.hasBeenHeld).ToList();
                    List<GrabbableObject> itemsNotInShip = allItems.Where(x => !x.isInShipRoom && !x.hasBeenHeld).ToList();

                    foreach (var item in SCPItems.SCPItemsList)
                    {
                        item.amountInCurrentLevel = itemsInShip.Count(x => x.itemProperties.name == item.item.name);
                    }

                    foreach (var item in itemsNotInShip)
                    {
                        SCPItem scp = SCPItems.SCPItemsList.Where(x => x.item.name == item.itemProperties.name).First();

                        if (scp.MaxSpawns > 0)
                        {
                            if (scp.amountInCurrentLevel < scp.MaxSpawns)
                            {
                                scp.amountInCurrentLevel++;
                            }
                            else
                            {
                                NetworkObject networkObject = item.gameObject.GetComponent<NetworkObject>();

                                if (networkObject != null && networkObject.IsSpawned)
                                {
                                    item.gameObject.GetComponent<NetworkObject>().Despawn(true);
                                }
                                else
                                {
                                    logger.LogDebug("Item prop '" + item.gameObject.name + "' was not spawned, did not have a NetworkObject component or wasnt the server! Skipped despawning and destroyed it instead.");
                                    UnityEngine.Object.Destroy(item.gameObject);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError("Error in StartOfRoundPatch/OnShipLandedMiscEventsPostfix: " + e);
                return;
            }
        }
    }
}
