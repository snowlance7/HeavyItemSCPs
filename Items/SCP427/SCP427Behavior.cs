using BepInEx.Logging;
using GameNetcodeStuff;
using LethalLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UIElements;
using HarmonyLib;
using static HeavyItemSCPs.Plugin;
using SCPItems;
using Unity.Netcode.Components;
using HeavyItemSCPs.Items.SCP178;

namespace HeavyItemSCPs.Items.SCP427
{
    internal class SCP427Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemSFX;
        public AudioClip PassiveTransformationSFX;
        public AudioClip FullTransformationSFX;
        public Animator itemAnimator;
        public GameObject SCP4271Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static SCP427Behavior? Instance { get; private set; }

        int hashOpen;

        public static float localPlayerHoldTime;
        public static float localPlayerDamageResist;
        //bool transformingEntity = false;

        public static Dictionary<EnemyAI, float> EnemyHoldTimes = new Dictionary<EnemyAI, float>();

        float timeSinceLastHeal;
        float timeSpawned;

        bool playedPassiveTransformationSound;

        bool isOpen;

        EnemyAI? enemyHeldBy;

        // Configs

        public float timeToTransform;
        int healthPerSecondOpen;
        float lootBugTransformTime;
        float birdTransformTime;
        float otherEnemyTransformTime;

        public override void Start()
        {
            base.Start();

            hashOpen = Animator.StringToHash("open");

            timeToTransform = configTimeToTransform.Value;
            healthPerSecondOpen = configHealthPerSecondOpen.Value;
            lootBugTransformTime = configHoarderBugTransformTime.Value;
            birdTransformTime = configBaboonHawkTransformTime.Value;
            otherEnemyTransformTime = configOtherEnemyTransformTime.Value;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Instance != null && Instance != this)
            {
                logger.LogDebug("There is already a SCP-427 in the scene. Removing this one.");
                return;
            }
            Instance = this;
            logger.LogDebug("Finished spawning SCP-427");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void Update()
        {
            base.Update();

            timeSpawned += Time.deltaTime;

            if (Instance != this)
            {
                if (IsServerOrHost && timeSpawned > 3f)
                {
                    NetworkObject.Despawn(true);
                }
                return;
            }

            if (StartOfRound.Instance.inShipPhase) { return; }

            timeSinceLastHeal += Time.deltaTime;

            //logger.LogDebug($"Time held by local player: {timeSCP427HeldByLocalPlayer}");

            if (playerHeldBy == null || playerHeldBy != localPlayer)
            {
                if (localPlayerHoldTime > 0f)
                {
                    localPlayerHoldTime -= Time.deltaTime * 0.5f;
                }
                playedPassiveTransformationSound = false;
            }

            if (playerHeldBy != null) // Held by local player
            {
                itemAnimator.SetBool(hashOpen, isOpen);

                // If player is local player, then continue
                if (playerHeldBy != localPlayer) { return; }

                // Heal player
                HealPlayer(healthPerSecondOpen);

                if (isOpen && timeToTransform > 0)
                {
                    if (SCP500Compatibility.IsLocalPlayerAffectedBySCP500) { return; }

                    // Increasing time held by local player
                    localPlayerHoldTime += Time.deltaTime;

                    // Play passive transformation sound
                    if (localPlayerHoldTime >= timeToTransform / 2 && localPlayerHoldTime <= timeToTransform / 2 + 1f && !playedPassiveTransformationSound)
                    {
                        logger.LogDebug("Playing 1/2 transform sound");
                        ItemSFX.PlayOneShot(PassiveTransformationSFX, 1f);
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
                        TransformPlayer(playerHeldBy);
                    }
                }
            }
            else if (enemyHeldBy != null) // Held by enemy
            {
                if (!(IsServerOrHost)) { return; }

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

                if (enemyHeldBy.enemyType.name == "BaboonHawk")
                {
                    if (birdTransformTime > 0)
                    {
                        if (EnemyHoldTimes[enemyHeldBy] >= birdTransformTime)
                        {
                            logger.LogDebug("Transforming bird");
                            TransformEnemy(enemyHeldBy, SCP4271AI.MaterialVariants.BaboonHawk);
                        }
                    }
                }
                else if (enemyHeldBy.enemyType.name == "HoarderBug")
                {
                    if (lootBugTransformTime > 0)
                    {
                        if (EnemyHoldTimes[enemyHeldBy] >= lootBugTransformTime)
                        {
                            logger.LogDebug("Transforming bug");
                            TransformEnemy(enemyHeldBy, SCP4271AI.MaterialVariants.Hoarderbug);
                        }
                    }
                }
                else
                {
                    if (otherEnemyTransformTime > 0)
                    {
                        if (EnemyHoldTimes[enemyHeldBy] >= otherEnemyTransformTime)
                        {
                            logger.LogDebug("Transforming enemy");
                            TransformEnemy(enemyHeldBy, SCP4271AI.MaterialVariants.None);
                        }
                    }
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);

            isOpen = !StartOfRound.Instance.inShipPhase && buttonDown;

            if (isOpen) ItemSFX.Play();
            else ItemSFX.Stop();
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
            hasBeenHeld = true;
            enemyHeldBy = enemy;
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
        }

        public void TransformPlayer(PlayerControllerB player)
        {
            logger.LogDebug("Transforming player");
            StartCoroutine(TransformPlayerCoroutine(player));
        }

        private IEnumerator TransformPlayerCoroutine(PlayerControllerB player)
        {
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Unknown, 3);
            ItemSFX.PlayOneShot(FullTransformationSFX, 1f);

            yield return new WaitForSeconds(4f);

            if (player.isPlayerDead)
            {
                Vector3 spawnPos = player.deadBody.transform.position;

                player.deadBody.DeactivateBody(setActive: false); // TODO: Test this
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
            ItemSFX.PlayOneShot(FullTransformationSFX, 1f);

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
            if (IsServerOrHost)
            {
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
    }

    [HarmonyPatch]
    internal class SCP427Patches
    {
        private static ManualLogSource logger = LoggerInstance;

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
            if (SCP427Behavior.Instance == null) { return; }
            if (SCP427Behavior.Instance.playerHeldBy == null) { return; }
            if (SCP427Behavior.Instance.playerHeldBy != localPlayer) { return; }
            if (SCP427Behavior.localPlayerHoldTime < SCP427Behavior.Instance.timeToTransform * 0.75f) { return; }

            int initialDamage = damageNumber;
            damageNumber = (int)(damageNumber * (1 - SCP427Behavior.localPlayerDamageResist));
            logger.LogDebug($"SCP-427: Resisting {SCP427Behavior.localPlayerDamageResist * 100f}% damage, {initialDamage} -> {damageNumber}"); // TODO: Test this
        }
    }
}