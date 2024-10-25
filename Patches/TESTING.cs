using BepInEx.Logging;
using DunGen;
using HarmonyLib;
using HeavyItemSCPs.Items.SCP323;
using HeavyItemSCPs.Items.SCP427;
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

namespace HeavyItemSCPs.Patches
{
    [HarmonyPatch]
    internal class TESTING : MonoBehaviour
    {
        private static ManualLogSource logger = Plugin.LoggerInstance;
        public static BoxCollider collider = null!;

        [HarmonyPostfix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.PingScan_performed))]
        public static void PingScan_performedPostFix()
        {
            logger.LogDebug("Insanity: " + localPlayer.insanityLevel);
            BashDoor();

        } // HoarderBug, BaboonHawk

        public static void BashDoor()
        {
            var doorLock = UnityEngine.Object.FindObjectsOfType<DoorLock>()
                .Where(x => Vector3.Distance(x.transform.position, localPlayer.transform.position) < 5f)
                .FirstOrDefault();

            if (doorLock != null)
            {
                var doorObj = doorLock.transform.parent.transform.parent.gameObject;
                var door = doorObj.transform.Find("DoorMesh").gameObject;

                // TODO: Add collider or use doorlock collider on trigger stay thats used in dnspy look at dnspy you doofus
                return;

                Rigidbody rb = door.GetComponent<Rigidbody>() ?? door.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.useGravity = true;
                rb.isKinematic = true;

                // Determine which direction to apply the force
                Vector3 doorForward = door.transform.position + door.transform.right * 2f;
                Vector3 doorBackward = door.transform.position - door.transform.right * 2f;
                Vector3 direction;

                if (Vector3.Distance(doorForward, localPlayer.transform.position) < Vector3.Distance(doorBackward, localPlayer.transform.position))
                {
                    // Player is at front of door
                    direction = (doorBackward - doorForward).normalized;
                    door.transform.position = door.transform.position - door.transform.right;
                }
                else
                {
                    // Player is at back of door
                    direction = (doorForward - doorBackward).normalized;
                    door.transform.position = door.transform.position + door.transform.right;
                }

                // Release the Rigidbody from kinematic state
                rb.isKinematic = false;

                // Add an impulse force to the door
                rb.AddForce(direction * 25f, ForceMode.Impulse);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(HUDManager), nameof(HUDManager.SubmitChat_performed))]
        public static void SubmitChat_performedPrefix(HUDManager __instance)
        {
            string msg = __instance.chatTextField.text;
            string[] args = msg.Split(" ");
            logger.LogDebug(msg);

            switch (args[0])
            {
                case "/size":
                    collider.size = new Vector3(float.Parse(args[1]), float.Parse(args[2]), float.Parse(args[3]));
                    break;
                case "/destroy":
                    var obj = UnityEngine.Object.FindObjectsOfType<GameObject>().Where(x => Vector3.Distance(x.transform.position, localPlayer.transform.position) < 5f).FirstOrDefault();
                    if (obj != null)
                    {
                        NetworkObject netObj = obj.GetComponent<NetworkObject>();
                        if (netObj != null && netObj.IsSpawned)
                        {
                            netObj.Despawn(true);
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(obj);
                        }
                        //obj.gameObject.transform.position = obj.gameObject.transform.position + obj.gameObject.transform.forward * 5f;
                    }
                    break;
                case "/state":
                    SCP323Behavior.isTesting = true;
                    SCP323Behavior.testState = (SCP323Behavior.AttachState)int.Parse(args[1]);
                    SCP323Behavior.Instance?.ChangeAttachStateServerRpc(SCP323Behavior.testState);
                    break;
                case "/enemies":
                    foreach (var enemy in GetEnemies())
                    {
                        logger.LogDebug(enemy.enemyType.name);
                    }
                    break;
                case "/outside":
                    //UnityEngine.Object.FindObjectOfType<SCP4271AI>().SetEnemyOutsideClientRpc(bool.Parse(args[1]));
                    break;
                case "/refresh":
                    RoundManager.Instance.RefreshEnemiesList();
                    HoarderBugAI.RefreshGrabbableObjectsInMapList();
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

        public static void LogChat(string msg)
        {
            HUDManager.Instance.AddChatMessage(msg, "Server");
        }

        public static Vector3 GetSpeed()
        {
            float num3 = localPlayer.movementSpeed / localPlayer.carryWeight;
            if (localPlayer.sinkingValue > 0.73f)
            {
                num3 = 0f;
            }
            else
            {
                if (localPlayer.isCrouching)
                {
                    num3 /= 1.5f;
                }
                else if (localPlayer.criticallyInjured && !localPlayer.isCrouching)
                {
                    num3 *= localPlayer.limpMultiplier;
                }
                if (localPlayer.isSpeedCheating)
                {
                    num3 *= 15f;
                }
                if (localPlayer.movementHinderedPrev > 0)
                {
                    num3 /= 2f * localPlayer.hinderedMultiplier;
                }
                if (localPlayer.drunkness > 0f)
                {
                    num3 *= StartOfRound.Instance.drunknessSpeedEffect.Evaluate(localPlayer.drunkness) / 5f + 1f;
                }
                if (!localPlayer.isCrouching && localPlayer.crouchMeter > 1.2f)
                {
                    num3 *= 0.5f;
                }
                if (!localPlayer.isCrouching)
                {
                    float num4 = Vector3.Dot(localPlayer.playerGroundNormal, localPlayer.walkForce);
                    if (num4 > 0.05f)
                    {
                        localPlayer.slopeModifier = Mathf.MoveTowards(localPlayer.slopeModifier, num4, (localPlayer.slopeModifierSpeed + 0.45f) * Time.deltaTime);
                    }
                    else
                    {
                        localPlayer.slopeModifier = Mathf.MoveTowards(localPlayer.slopeModifier, num4, localPlayer.slopeModifierSpeed / 2f * Time.deltaTime);
                    }
                    num3 = Mathf.Max(num3 * 0.8f, num3 + localPlayer.slopeIntensity * localPlayer.slopeModifier);
                }
            }

            Vector3 vector3 = new Vector3(0f, 0f, 0f);
            int num5 = Physics.OverlapSphereNonAlloc(localPlayer.transform.position, 0.65f, localPlayer.nearByPlayers, StartOfRound.Instance.playersMask);
            for (int i = 0; i < num5; i++)
            {
                vector3 += Vector3.Normalize((localPlayer.transform.position - localPlayer.nearByPlayers[i].transform.position) * 100f) * 1.2f;
            }
            int num6 = Physics.OverlapSphereNonAlloc(localPlayer.transform.position, 1.25f, localPlayer.nearByPlayers, 524288);
            for (int j = 0; j < num6; j++)
            {
                EnemyAICollisionDetect component = localPlayer.nearByPlayers[j].gameObject.GetComponent<EnemyAICollisionDetect>();
                if (component != null && component.mainScript != null && !component.mainScript.isEnemyDead && Vector3.Distance(localPlayer.transform.position, localPlayer.nearByPlayers[j].transform.position) < component.mainScript.enemyType.pushPlayerDistance)
                {
                    vector3 += Vector3.Normalize((localPlayer.transform.position - localPlayer.nearByPlayers[j].transform.position) * 100f) * component.mainScript.enemyType.pushPlayerForce;
                }
            }

            Vector3 vector4 = localPlayer.walkForce * num3 * localPlayer.sprintMultiplier + new Vector3(0f, localPlayer.fallValue, 0f) + vector3;
            vector4 += localPlayer.externalForces;
            return vector4;
        }
    }
}