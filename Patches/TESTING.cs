using BepInEx.Logging;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP427;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            //logger.LogDebug($"Instanity: {localPlayer.insanityLevel}");
            //logger.LogDebug($"Drunkness: {localPlayer.drunkness}");
            //logger.LogDebug($"Fear: {localPlayer.playersManager.fearLevel}");

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

            //logger.LogDebug("Done");

            /*foreach(var dungeon in RoundManager.Instance.dungeonFlowTypes) // SCPFlow
            {
                logger.LogDebug(dungeon.dungeonFlow.GetType().ToString());
                logger.LogDebug(dungeon.dungeonFlow.name);
            }*/
            //string name = RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name;
            //logger.LogDebug(name);
        }

        /*[HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            logger.LogDebug(msg);

            switch (args[0])
            {
                case "/outside":
                    //UnityEngine.Object.FindObjectOfType<SCP4271AI>().SetEnemyOutsideClientRpc(bool.Parse(args[1]));
                    break;
                default:
                    break;
            }
        }*/

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