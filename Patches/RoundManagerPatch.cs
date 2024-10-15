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
    [HarmonyPatch(typeof(RoundManager))]
    internal class RoundManagerPatch
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        private static bool isSCPDungeon = false;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(RoundManager.SpawnScrapInLevel))]
        public static void SpawnScrapInLevelPrefix(RoundManager __instance) // SCPFlow
        {
            string dungeonName = __instance.dungeonGenerator.Generator.DungeonFlow.name;

            if (dungeonName == "SCPFlow")
            {
                logger.LogDebug("SCPFlow detected");
                foreach (var scpItem in SCPItems.SCPItemsList)
                {
                    if (scpItem.scpDungeonRarity == -1) { continue; }
                    SpawnableItemWithRarity item = __instance.currentLevel.spawnableScrap.Where(x => x.spawnableItem.name == scpItem.item.name).FirstOrDefault();

                    if (item != null)
                    {
                        scpItem.currentLevelRarity = item.rarity;
                        item.rarity = scpItem.scpDungeonRarity;
                        logger.LogDebug($"Rarity for {scpItem.item.name} set to {item.rarity} from {scpItem.currentLevelRarity}");
                    }
                }

                isSCPDungeon = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(RoundManager.SpawnScrapInLevel))]
        public static void SpawnScrapInLevelPostfix(RoundManager __instance) // SCPFlow
        {
            if (isSCPDungeon)
            {
                logger.LogDebug("Resetting rarities");
                isSCPDungeon = false;

                foreach (var scpItem in SCPItems.SCPItemsList)
                {
                    if (scpItem.scpDungeonRarity == -1) { continue; }
                    SpawnableItemWithRarity item = __instance.currentLevel.spawnableScrap.Where(x => x.spawnableItem.name == scpItem.item.name).FirstOrDefault();

                    if (item != null)
                    {
                        item.rarity = scpItem.currentLevelRarity;
                        logger.LogDebug($"Rarity for {scpItem.item.name} reset to {item.rarity} from {scpItem.scpDungeonRarity}");
                    }
                }
            }
        }
    }
}
