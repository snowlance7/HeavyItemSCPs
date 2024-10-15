using ES3Types;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs
{
    public static class SCPItems
    {
        public static List<SCPItem> SCPItemsList = new List<SCPItem>();
        public static List<SCPEnemy> SCPEnemiesList = new List<SCPEnemy>();

        public static void Load(string path, ObjectClass objClass, string? levelRarities = null, string? customLevelRarities = null, int scpDungeonRarity = 100, int minValue = 0, int maxValue = 0, int? maxSpawns = null, bool tabletop = false, bool general = false, bool small = false)
        {
            Item SCP = ModAssets.LoadAsset<Item>(path);
            if (SCP == null) { LoggerInstance.LogError($"Error: Couldnt get item with path {path} in assets"); return; }
            LoggerInstance.LogDebug($"Got {SCP.name} prefab");

            SCP.minValue = minValue;
            SCP.maxValue = Mathf.Max(minValue, maxValue);

            SCPItem scpItem = new SCPItem();
            scpItem.item = SCP;
            scpItem.MaxSpawns = maxSpawns;
            scpItem.scpDungeonRarity = scpDungeonRarity;
            scpItem.ObjectClass = objClass;
            scpItem.TabletopItem = tabletop;
            scpItem.GeneralItem = general;
            scpItem.SmallItem = small;
            SCPItemsList.Add(scpItem);

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP.spawnPrefab);
            LethalLib.Modules.Utilities.FixMixerGroups(SCP.spawnPrefab);
            LethalLib.Modules.Items.RegisterScrap(SCP, GetLevelRarities(levelRarities), GetCustomLevelRarities(customLevelRarities));
        }

        public static void LoadShopItem(string path, ObjectClass objClass, int price)
        {
            Item SCP = ModAssets.LoadAsset<Item>(path);
            if (SCP == null) { LoggerInstance.LogError($"Error: Couldnt get item with path {path} from assets"); return; }
            LoggerInstance.LogDebug($"Got {SCP.name} prefab");

            SCPItem scp = new SCPItem();
            scp.item = SCP;
            scp.ObjectClass = objClass;
            scp.price = price;
            SCPItemsList.Add(scp);

            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(SCP.spawnPrefab);
            LethalLib.Modules.Utilities.FixMixerGroups(SCP.spawnPrefab);
            LethalLib.Modules.Items.RegisterShopItem(SCP, price);
        }

        public static void LoadEnemy(string path, string TNPath, string TKPath, string? levelRarities = null, string? customLevelRarities = null, int scpDungeonRarity = 100, ObjectClass objClass = ObjectClass.Unknown)
        {
            EnemyType enemy = ModAssets.LoadAsset<EnemyType>(path);
            if (enemy == null) { LoggerInstance.LogError($"Error: Couldnt get enemy with path {path} from assets"); return; }
            LoggerInstance.LogDebug($"Got {enemy.name} prefab");
            if (levelRarities == null && customLevelRarities == null) { enemy.spawningDisabled = true; }
            TerminalNode terminalNode = ModAssets.LoadAsset<TerminalNode>(TNPath);
            TerminalKeyword terminalKeyword = ModAssets.LoadAsset<TerminalKeyword>(TKPath);
            if (terminalNode == null) { LoggerInstance.LogError($"Error: Couldnt get terminal node from path {TNPath} in assets"); return; }
            if (terminalKeyword == null) { LoggerInstance.LogError($"Error: Couldnt get terminal keyword from path {TKPath} in assets"); return; }

            SCPEnemy scp = new SCPEnemy();
            scp.ObjectClass = objClass;
            scp.enemyType = enemy;
            scp.SCPDungeonRarity = scpDungeonRarity;
            SCPEnemiesList.Add(scp);

            LoggerInstance.LogDebug($"Registering {enemy.name} enemy network prefab...");
            LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(enemy.enemyPrefab);
            LoggerInstance.LogDebug($"Registering {enemy.name} enemy...");
            LethalLib.Modules.Enemies.RegisterEnemy(enemy, GetLevelRarities(levelRarities), GetCustomLevelRarities(customLevelRarities), terminalNode, terminalKeyword);
        }
    }

    public class SCPEnemy
    {
        public string enemyName { get { return enemyType.enemyName; } }
        public ObjectClass ObjectClass;
        public EnemyType enemyType = null!;
        public int SCPDungeonRarity;
        public int currentLevelRarity;
    }

    public class SCPItem
    {
        public Item item = null!;
        public int? MaxSpawns;
        public ObjectClass ObjectClass;

        public bool TabletopItem = false;
        public bool GeneralItem = false;
        public bool SmallItem = false;

        public int amountInCurrentLevel = 0;

        public int scpDungeonRarity = 100;
        public int currentLevelRarity;

        public int price;
    }
    public enum ObjectClass
    {
        Unknown,
        Safe,
        Euclid,
        Keter
    }
}
