using Dawn.Utils;
using GameNetcodeStuff;
using HarmonyLib;
using PSCPLibrary;
using PSCPLibrary.Interfaces;
using SnowyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.SCP.SCP427
{
    internal class SCP427Behavior : PhysicsProp, ISingletonItem, ISCP
    {
        public SCPInfo info = null!;
        public AudioSource audioSource = null!;
        public AudioClip passiveTransformationSFX = null!;
        public AudioClip fullTransformationSFX = null!;
        public Animator animator = null!;
        public GameObject SCP4271Prefab = null!;

        public static SCP427Behavior? Instance { get; private set; }

        SCPInfo ISCP.SCPInfo => info;

        int hashOpen;

        public static float localPlayerHoldTimeMultiplier;
        public static float localPlayerHoldTime;
        public static float localPlayerDamageResist;
        //bool transformingEntity = false;

        public static Dictionary<EnemyAI, float> EnemyHoldTimes = new Dictionary<EnemyAI, float>();

        float timeSinceLastHeal;
        //float timeSpawned;

        bool playedPassiveTransformationSound;

        bool isOpen;

        EnemyAI? enemyHeldBy;

        readonly BoundedRange timeToTransformSpeed = new BoundedRange(15f, 30f);

        const float enemyTimeToTransform = 5f;

        public static float timeToTransform = 60f;
        public static int healthPerSecondOpen = 5;
        public static bool scp500Compatibility = true;

        [InitConfig]
        public static void InitConfigs()
        {
            timeToTransform = PluginInstance.Config.Bind("SCP-427 Options", "SCP-427 | Time to transform", 60f, "How long a player can hold the necklace before they transform into SCP-427-1. Should be higher that 30. Set to -1 to disable transforming.").Value;
            healthPerSecondOpen = PluginInstance.Config.Bind("SCP-427 Options", "SCP-427 | Health per second open", 5, "The health gained per second while opening SCP-427.").Value;
            scp500Compatibility = PluginInstance.Config.Bind("SCP-427 Options", "SCP-427 | SCP-500 compatibility", true, "Whether or not SCP-427 should be compatible with the SCP-500. This will only work if you have the ItemSCPs mod installed. If enabled, it will temporarily halt the transformation timer when holding or using SCP-427 when you take SCP-500.").Value;
        }

        public override void Start()
        {
            base.Start();
            hashOpen = Animator.StringToHash("open");
            PSCPLibrary.SCPEvents.OnSCP500TakenByLocalPlayer.AddListener(SCP500Taken);
        }

        public void SCP500Taken()
        {
            if (!scp500Compatibility) { return; }
            localPlayerHoldTime = 0f;
            localPlayerHoldTimeMultiplier = 0f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this) { return; }
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
                Instance = null;
        }

        public override void Update()
        {
            base.Update();

            if ((StartOfRound.Instance.inShipPhase || !StartOfRound.Instance.shipHasLanded) && !Utils.inTestRoom) { return; }

            timeSinceLastHeal += Time.deltaTime;

            //logger.LogDebug($"Time held by local player: {localPlayerHoldTime}");

            animator.SetBool(hashOpen, isOpen);

            if (playerHeldBy == null || playerHeldBy != localPlayer)
            {
                if (localPlayerHoldTimeMultiplier > 0f)
                {
                    localPlayerHoldTimeMultiplier -= Time.deltaTime / timeToTransformSpeed.Min;
                    localPlayerHoldTimeMultiplier = Mathf.Clamp01(localPlayerHoldTimeMultiplier);
                }

                if (localPlayerHoldTime > 0f)
                {
                    localPlayerHoldTime -= Time.deltaTime * (1 - localPlayerHoldTimeMultiplier);
                }
                else
                {
                    localPlayerHoldTime = 0f;
                }
                playedPassiveTransformationSound = false;
            }
            else // Held by local player
            {
                // Heal player
                HealPlayer(healthPerSecondOpen);

                if (playerHeldBy.health < 100)
                {
                    localPlayerHoldTime = 0f;
                    localPlayerHoldTimeMultiplier = 0f;
                    return;
                }

                if (localPlayerHoldTimeMultiplier < 1f)
                {
                    localPlayerHoldTimeMultiplier += Time.deltaTime / timeToTransformSpeed.Max;
                    localPlayerHoldTimeMultiplier = Mathf.Clamp01(localPlayerHoldTimeMultiplier);
                }

                if (isOpen) { localPlayerHoldTimeMultiplier = 1f; }

                // Increasing time held by local player
                localPlayerHoldTime += Time.deltaTime * localPlayerHoldTimeMultiplier;

                // Play passive transformation sound
                if (localPlayerHoldTime >= timeToTransform / 2 && localPlayerHoldTime <= timeToTransform / 2 + 1f && !playedPassiveTransformationSound)
                {
                    logger.LogDebug("Playing 1/2 transform sound");
                    audioSource.PlayOneShot(passiveTransformationSFX, 1f);
                    playedPassiveTransformationSound = true;
                }

                if (localPlayerHoldTime >= timeToTransform * 0.75f)
                {
                    localPlayer.drunkness = 0.05f;

                    float t = Mathf.InverseLerp(timeToTransform * 0.75f, timeToTransform, localPlayerHoldTime);
                    localPlayerDamageResist = Mathf.Lerp(0.25f, 1f, t);
                }

                // Transform player if time is up
                if (localPlayerHoldTime >= timeToTransform)
                {
                    logger.LogDebug("Transforming player");
                    localPlayerHoldTime = 0f;
                    TransformPlayer(playerHeldBy);
                }

                return;
            }
            if (enemyHeldBy != null) // Held by enemy
            {
                if (!IsServerOrHost) { return; }

                if (timeSinceLastHeal > 1f)
                {
                    timeSinceLastHeal = 0f;
                    HealEnemy(enemyHeldBy);
                }

                if (enemyHeldBy.enemyType.name == "SCP4271Enemy") { return; }

                if (!EnemyHoldTimes.ContainsKey(enemyHeldBy))
                {
                    EnemyHoldTimes.Add(enemyHeldBy, 0f);
                }

                EnemyHoldTimes[enemyHeldBy] += Time.deltaTime;
                //logger.LogDebug($"{enemyHeldBy.enemyType.name} hold time: {EnemyHoldTimes[enemyHeldBy]}");

                if (EnemyHoldTimes[enemyHeldBy] >= enemyTimeToTransform)
                {
                    SCP4271AI.MaterialVariants variant = SCP4271AI.MaterialVariants.None;
                    string logMessage = "Transforming enemy";

                    switch (enemyHeldBy.enemyType.name)
                    {
                        case "BaboonHawk":
                            variant = SCP4271AI.MaterialVariants.BaboonHawk;
                            logMessage = "Transforming bird";
                            break;
                        case "HoarderBug":
                            variant = SCP4271AI.MaterialVariants.Hoarderbug;
                            logMessage = "Transforming bug";
                            break;
                    }

                    logger.LogDebug(logMessage);
                    EnemyHoldTimes[enemyHeldBy] = 0f;
                    TransformEnemy(enemyHeldBy, variant);
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            isOpen = ((!StartOfRound.Instance.inShipPhase && StartOfRound.Instance.shipHasLanded) || Utils.inTestRoom) && buttonDown;

            if (isOpen) audioSource.Play();
            else audioSource.Stop();
        }

        public override void OnHitGround()
        {
            base.OnHitGround();
            enemyHeldBy = null;
            isOpen = false;
        }

        public override void GrabItemFromEnemy(EnemyAI enemy)
        {
            base.GrabItemFromEnemy(enemy);
            enemyHeldBy = enemy;
            logger.LogDebug("Grabbed by " + enemy.enemyType.enemyName);
        }

        public override void PocketItem()
        {
            base.PocketItem();
            isOpen = false;
        }

        public override void DiscardItem()
        {
            base.DiscardItem();
            isOpen = false;
            enemyHeldBy = null;
        }

        public void TransformPlayer(PlayerControllerB player)
        {
            logger.LogDebug("Transforming player");
            player.DropAllHeldItems();
            StartCoroutine(TransformPlayerCoroutine(player));
        }

        private IEnumerator TransformPlayerCoroutine(PlayerControllerB player)
        {
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Unknown, 3);
            audioSource.PlayOneShot(fullTransformationSFX, 1f);

            if (player.deadBody != null)
            {
                player.deadBody.canBeGrabbedBackByPlayers = false;
            }

            yield return new WaitForSeconds(4f);

            if (player.deadBody != null && player.isPlayerDead)
            {
                Vector3 spawnPos = player.deadBody.grabBodyObject.transform.position;
                spawnPos = RoundManager.Instance.GetNavMeshPosition(spawnPos);

                player.deadBody.DeactivateBody(setActive: false);
                SpawnSCP4271ServerRpc(spawnPos, SCP4271AI.MaterialVariants.Player);
            }

            localPlayerHoldTime = 0f;
        }

        private void TransformEnemy(EnemyAI enemy, SCP4271AI.MaterialVariants variant)
        {
            logger.LogDebug($"Transforming {enemy.enemyType.enemyName}");
            enemyHeldBy = null;
            StartCoroutine(TransformEnemyCoroutine(enemy, variant));
        }

        private IEnumerator TransformEnemyCoroutine(EnemyAI enemy, SCP4271AI.MaterialVariants variant)
        {
            Vector3 spawnPos = enemy.transform.position;

            enemy.KillEnemy();
            audioSource.PlayOneShot(fullTransformationSFX, 1f);

            yield return new WaitForSecondsRealtime(4f);

            if (enemy.thisNetworkObject.IsSpawned)
            {
                enemy.thisNetworkObject.Despawn();
            }

            SpawnSCP4271ServerRpc(spawnPos, variant);
        }

        public void HealPlayer(int health)
        {
            if (timeSinceLastHeal > 1f && isOpen)
            {
                timeSinceLastHeal = 0f;
                int newHealth = playerHeldBy.health + healthPerSecondOpen;

                playerHeldBy.MakeCriticallyInjured(false);
                playerHeldBy.health = Mathf.Clamp(newHealth, 0, 100);
                HUDManager.Instance.UpdateHealthUI(playerHeldBy.health, false);
            }
            else if (timeSinceLastHeal > 2.5f && playerHeldBy.health > 20)
            {
                timeSinceLastHeal = 0f;
                int newHealth = playerHeldBy.health + 1;

                playerHeldBy.health = Mathf.Clamp(newHealth, 0, 100);
                HUDManager.Instance.UpdateHealthUI(playerHeldBy.health, false);
            }
        }
        
        public void HealEnemy(EnemyAI enemyToHeal)
        {
            int maxHealth = enemyToHeal.enemyType.enemyPrefab.GetComponent<EnemyAI>().enemyHP;

            if (enemyToHeal.enemyHP < maxHealth && timeSinceLastHeal > 1f)
            {
                int newHealth = enemyToHeal.enemyHP + 1;
                HealEnemyClientRpc(enemyToHeal.thisEnemyIndex, newHealth);

                logger.LogDebug($"{enemyToHeal.enemyType.enemyName} HP: {enemyToHeal.enemyHP}/{maxHealth}");

                timeSinceLastHeal = 0f;
            }
        }

        // RPCs

        [ClientRpc]
        private void HealEnemyClientRpc(int index, int health)
        {
            EnemyAI enemy = RoundManager.Instance.SpawnedEnemies.Where(x => x.thisEnemyIndex == index).FirstOrDefault();
            if (enemy != null) { enemy.enemyHP = health; }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnSCP4271ServerRpc(Vector3 spawnPos, SCP4271AI.MaterialVariants variant)
        {
            if (!IsServerOrHost) { return; }
            logger.LogDebug("Spawning SCP-427-1");

            GameObject scpObj = Instantiate(SCP4271Prefab, spawnPos, Quaternion.identity);
            SCP4271AI scp = scpObj.GetComponent<SCP4271AI>();
            scp.NetworkObject.Spawn(destroyWithScene: true);
            RoundManager.Instance.SpawnedEnemies.Add(scp);

            if (variant != SCP4271AI.MaterialVariants.None)
            {
                logger.LogDebug("Got net obj for SCP-427-1");
                scp.SetMaterialVariantClientRpc(variant);
            }
        }
    }

    [HarmonyPatch]
    internal class SCP427Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.DespawnPropsAtEndOfRound))]
        public static void DespawnPropsAtEndOfRoundPostfix()
        {
            SCP427Behavior.EnemyHoldTimes.Clear();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer))]
        public static void DamagePlayerPrefix(PlayerControllerB __instance, ref int damageNumber)
        {
            try
            {
                if (SCP427Behavior.Instance == null) { return; }
                if (SCP427Behavior.Instance.playerHeldBy == null) { return; }
                if (SCP427Behavior.Instance.playerHeldBy != localPlayer) { return; }
                if (SCP427Behavior.localPlayerHoldTime < SCP427Behavior.timeToTransform * 0.75f) { return; }

                int initialDamage = damageNumber;
                damageNumber = (int)(damageNumber * (1 - SCP427Behavior.localPlayerDamageResist));
                logger.LogDebug($"SCP-427: Resisting {SCP427Behavior.localPlayerDamageResist * 100f}% damage, {initialDamage} -> {damageNumber}");
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return;
            }
        }
    }
}