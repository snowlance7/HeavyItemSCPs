using BepInEx.Logging;
using DunGen;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP323;
using HeavyItemSCPs.Items.SCP427;
using HeavyItemSCPs.Items.SCP513;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
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

namespace HeavyItemSCPs
{
    [HarmonyPatch]
    public class TESTING : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        public static int setEventRarity = -1;
        public static int setEventIndex = -1;

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            if (!Utils.testing) { return; }

            /*SpikeRoofTrap spikeTrap = Utils.GetClosestGameObjectOfType<SpikeRoofTrap>(localPlayer.transform.position);
            logger.LogDebug(spikeTrap.name);
            logger.LogDebug(spikeTrap.gameObject.name);
            logger.LogDebug(spikeTrap.transform.name);
            logger.LogDebug(spikeTrap.gameObject.transform.name);
            logger.LogDebug(spikeTrap.gameObject.transform.root.name);
            logger.LogDebug(spikeTrap.transform.root.gameObject.name);*/

            logger.LogDebug("AINodes: " + GameObject.FindGameObjectsWithTag("AINode").Length);
            logger.LogDebug("OutsideAINodes: " + GameObject.FindGameObjectsWithTag("OutsideAINode").Length);


            if (HallucinationManager.Instance == null || setEventRarity == -1 || setEventIndex == -1) { return; }
            HallucinationManager.Instance.RunEvent(setEventRarity, setEventIndex);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            logger.LogDebug(msg);

            switch (args[0])
            {
                case "/allEvents":
                    HallucinationManager.Instance?.RunAllEvents(int.Parse(args[1]));
                    break;
                case "/setEvent":
                    setEventRarity = int.Parse(args[1]);
                    setEventIndex = int.Parse(args[2]);
                    break;
                case "/event":
                    HallucinationManager.Instance?.RunEvent(int.Parse(args[1]), int.Parse(args[2]));
                    break;
                default:
                    Utils.ChatCommand(args);
                    break;
            }
        }
    }
}