using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP427;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UIElements;
using static HeavyItemSCPs.Plugin;

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

namespace HeavyItemSCPs.Patches
{
    [HarmonyPatch]
    internal class TESTING : MonoBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;

        private static PlayerControllerB localPlayer { get { return StartOfRound.Instance.localPlayerController; } }

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            logger.LogDebug($"Instanity: {localPlayer.insanityLevel}");
            logger.LogDebug($"Drunkness: {localPlayer.drunkness}");
            logger.LogDebug($"Fear: {localPlayer.playersManager.fearLevel}");

            //RoundManager.Instance.PlayAudibleNoise(localPlayer.transform.position, 50f, 1f, 0, false, 0);

            /*LungProp apparatus = UnityEngine.Object.FindObjectOfType<LungProp>();
            MeshRenderer renderer = apparatus.gameObject.GetComponentInChildren<MeshRenderer>();

            Material outlineMat = ModAssets.LoadAsset<Material>("Assets/ModAssets/SCP178/Materials/OverlayMaterial.mat");

            Material[] originalMaterials = renderer.materials;  // Save original materials
            Material[] newMaterials = new Material[originalMaterials.Length + 1];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                newMaterials[i] = originalMaterials[i];
            }
            newMaterials[originalMaterials.Length] = outlineMat;  // Add the outline material

            renderer.materials = newMaterials;*/

            logger.LogDebug("Done");
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            logger.LogDebug(msg);

            switch (args[0])
            {
                case "/spawn":
                    NetworkHandlerHeavy.Instance.SpawnEnemyServerRpc("SCP-427-1", localPlayer.transform.forward);
                    break;
                case "/testRoom":
                    StartOfRound.Instance.Debug_EnableTestRoomServerRpc(StartOfRound.Instance.testRoom == null);
                    break;
                case "/fear":
                    localPlayer.JumpToFearLevel(int.Parse(args[1]));
                    break;
                case "/playNoise":
                    RoundManager.Instance.PlayAudibleNoise(localPlayer.transform.position, default, 1f, default, default, int.Parse(args[1]));
                    break;
                case "/drunkness":
                    localPlayer.drunkness = float.Parse(args[1]);
                    break;
                case "/getLevels":
                    logger.LogDebug("Getting levels");
                    foreach (var level in StartOfRound.Instance.levels)
                    {
                        logger.LogDebug(level.name);
                    }
                    break; // TODO: Get custom levels so i can add the scp moons as defaults for custom levels config
                case "/getItemTypes":
                    List<ItemGroup> itemGroups = new List<ItemGroup>();
                    foreach(ItemGroup itemGroup in Resources.FindObjectsOfTypeAll<ItemGroup>())
                    {
                        if (!itemGroups.Contains(itemGroup))
                        {
                            itemGroups.Add(itemGroup);
                            logger.LogDebug(itemGroup.name);
                        }
                    }
                    break;
                case "/damageSelf":
                    int damage = int.Parse(args[1]);
                    localPlayer.DamagePlayer(damage);
                    HUDManager.Instance.UpdateHealthUI(localPlayer.health, true);
                    break;
                case "/setHealth":
                    localPlayer.health = int.Parse(args[1]);
                    HUDManager.Instance.UpdateHealthUI(localPlayer.health, true);
                    break;
                case "/SpeedAccelerationEffect":
                    SCP4271AI.SpeedAccelerationEffect = float.Parse(args[1]);
                    break;
                case "/BaseAcceleration":
                    SCP4271AI.BaseAcceleration = float.Parse(args[1]);
                    break;
                case "/SpeedIncreaseRate":
                    SCP4271AI.SpeedIncreaseRate = float.Parse(args[1]);
                    break;
                case "/throwing":
                    SCP4271AI.throwingPlayerEnabled = bool.Parse(args[1]);
                    break;
                default:
                    break;
            }
        }

        public static List<SpawnableEnemyWithRarity> GetEnemies()
        {
            logger.LogDebug("Getting enemies");
            List<SpawnableEnemyWithRarity> enemies = new List<SpawnableEnemyWithRarity>();
            enemies = GameObject.Find("Terminal")
                .GetComponentInChildren<Terminal>()
                .moonsCatalogueList
                .SelectMany(x => x.Enemies.Concat(x.DaytimeEnemies).Concat(x.OutsideEnemies))
                .Where(x => x != null && x.enemyType != null && x.enemyType.name != null)
                .GroupBy(x => x.enemyType.name, (k, v) => v.First())
                .ToList();

            logger.LogDebug($"Enemy types: {enemies.Count}");
            return enemies;
        }
    }
}