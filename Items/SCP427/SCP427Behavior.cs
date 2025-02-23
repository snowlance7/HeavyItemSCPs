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

        public static float timeSCP427HeldByLocalPlayer = 0f;
        //bool transformingEntity = false;

        public static Dictionary<EnemyAI, float> EnemyHoldTimes = new Dictionary<EnemyAI, float>();

        float timeToTransform;
        float timeSinceDecrease;
        int healthPerSecondOpen;
        float lootBugTransformTime;
        float birdTransformTime;
        float otherEnemyTransformTime;
        float timeToSpawnSCP4271;
        int maxSpawns;

        float timeOnGround = 0f;
        float timeSinceLastHeal = 0f;

        bool playedPassiveTransformationSound = false;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemSFX;
        public AudioClip PassiveTransformationSFX;
        public AudioClip FullTransformationSFX;
        public Animator itemAnimator;
        public GameObject SCP4271Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        bool isOpen = false;

        EnemyAI? enemyHeldBy;

        // Object was not thrown because it does not exist on the server.

        public override void Start()
        {
            base.Start();
            timeToTransform = configTimeToTransform.Value;
            healthPerSecondOpen = configHealthPerSecondOpen.Value;
            lootBugTransformTime = configHoarderBugTransformTime.Value;
            birdTransformTime = configBaboonHawkTransformTime.Value;
            otherEnemyTransformTime = configOtherEnemyTransformTime.Value;
            timeToSpawnSCP4271 = configTimeToSpawnSCP4271.Value;
            maxSpawns = configMaxSpawns.Value;
        }

        public override void Update()
        {
            base.Update();

            if (StartOfRound.Instance.inShipPhase) { return; }

            timeSinceLastHeal += Time.deltaTime;
            timeSinceDecrease += Time.deltaTime;

            //logger.LogDebug($"Time held by local player: {timeSCP427HeldByLocalPlayer}");

            if (playerHeldBy == null || playerHeldBy != localPlayer)
            {
                if (timeSinceDecrease > 2.5f && timeSCP427HeldByLocalPlayer > 0f)
                {
                    timeSinceDecrease = 0f;
                    timeSCP427HeldByLocalPlayer -= 1f;
                }
                playedPassiveTransformationSound = false;
            }

            if (playerHeldBy != null) // Held by local player
            {
                // If player is local player, then continue
                if (playerHeldBy != localPlayer) { return; }

                // Heal player
                HealPlayer(healthPerSecondOpen);

                if (timeToTransform != -1 && !SCP500Compatibility.IsLocalPlayerAffectedBySCP500 && isOpen)
                {
                    // Increasing time held by local player
                    timeSCP427HeldByLocalPlayer += Time.deltaTime;

                    //logger.LogDebug($"Time held by local player: {timeSCP427HeldByLocalPlayer}");

                    // Play passive transformation sound
                    if (timeSCP427HeldByLocalPlayer >= timeToTransform / 2 && timeSCP427HeldByLocalPlayer <= timeToTransform / 2 + 1f && !playedPassiveTransformationSound)
                    {
                        logger.LogDebug("Playing 1/2 transform sound");
                        ItemSFX.PlayOneShot(PassiveTransformationSFX, 1f);
                        playedPassiveTransformationSound = true;
                    }

                    // Transform player if time is up
                    if (timeSCP427HeldByLocalPlayer >= timeToTransform)
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
                    HealEnemy(enemyHeldBy);
                    timeSinceLastHeal = 0f;
                }

                if (enemyHeldBy.enemyType.name == "SCP4271Enemy") { return; }

                if (!EnemyHoldTimes.ContainsKey(enemyHeldBy))
                {
                    EnemyHoldTimes.Add(enemyHeldBy, 0f);
                }

                EnemyHoldTimes[enemyHeldBy] += Time.deltaTime;
                logger.LogDebug($"{enemyHeldBy.enemyType.name} hold time: {EnemyHoldTimes[enemyHeldBy]}");

                if (enemyHeldBy.enemyType.name == "BaboonHawk")
                {
                    if (birdTransformTime != -1)
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
                    if (lootBugTransformTime != -1)
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
                    if (otherEnemyTransformTime != -1)
                    {
                        if (EnemyHoldTimes[enemyHeldBy] >= otherEnemyTransformTime)
                        {
                            logger.LogDebug("Transforming enemy");
                            TransformEnemy(enemyHeldBy, SCP4271AI.MaterialVariants.None);
                        }
                    }
                }
            }
            else if (hasHitGround) // On ground
            {
                if (!IsServerOrHost) { return; }
                if (timeToSpawnSCP4271 != -1)
                {
                    if (hasBeenHeld) { return; }

                    timeOnGround += Time.deltaTime;
                    //logger.LogDebug($"Time on ground: {timeOnGround}");

                    if (timeOnGround >= timeToSpawnSCP4271)
                    {
                        if (FindObjectsOfType<SCP4271AI>().Count() >= maxSpawns) { return; }
                        logger.LogDebug("Spawning SCP-427-1");
                        timeToSpawnSCP4271 += timeToSpawnSCP4271;
                        SpawnSCP4271ServerRpc(transform.position, SCP4271AI.MaterialVariants.None);
                    }
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
            if (StartOfRound.Instance.inShipPhase) { return; }
            if (buttonDown)
            {
                OpenNecklace();
                return;
            }
            CloseNecklace();
        }

        public override void OnHitGround()
        {
            base.OnHitGround();
            enemyHeldBy = null;
            CloseNecklace();
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
            CloseNecklace();
        }

        public override void GrabItem()
        {
            base.GrabItem();
            hasBeenHeld = true;
        }

        public override void DiscardItem()
        {
            base.DiscardItem();
            CloseNecklace();
        }

        public void OpenNecklace()
        {
            //logger.LogDebug("Opening necklace");
            itemAnimator.SetTrigger("open");
            ItemSFX.Play();
            //multiplier = transformOpenMultiplier;
            isOpen = true;
        }

        public void CloseNecklace()
        {
            //logger.LogDebug("Closing necklace");
            itemAnimator.SetTrigger("close");
            ItemSFX.Stop();
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

            timeSCP427HeldByLocalPlayer = 0f;
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
        private static void DespawnPropsAtEndOfRoundPostfix()
        {
            logger.LogDebug("In DespawnPropsAtEndOfRoundPatch");

            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                SCP427Behavior.EnemyHoldTimes.Clear();
            }
        }
    }
}