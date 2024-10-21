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
    }
}
