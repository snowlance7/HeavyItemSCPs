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

namespace HeavyItemSCPs.Items.SCP427
{
    internal class SCP427Behavior : PhysicsProp
    {
        private static ManualLogSource logger = LoggerInstance;

        public static float timeSCP427HeldByLocalPlayer = 0f;
        public static bool transformingEntity = false;

        //public static Dictionary<PlayerControllerB, float> PlayerHoldTimes = new Dictionary<PlayerControllerB, float>(); // TODO: Make this work with network
        public static Dictionary<HoarderBugAI, float> LootBugHoldTimes = new Dictionary<HoarderBugAI, float>(); // TODO: Make sure this work with network
        public static Dictionary<BaboonBirdAI, float> BirdHoldTimes = new Dictionary<BaboonBirdAI, float>(); // TODO: Make sure this work with network

        bool enableOpenNecklace;
        float timeToTransform;
        float transformOpenMultiplier;
        int healthPerSecond;
        int healthPerSecondOpen;
        float lootBugTransformTime;
        float birdTransformTime;
        float timeToSpawnSCP4271;
        bool spawnAfterPickup;
        int maxSpawns;
        bool inInventoryCounts;

        float timeOnGround = 0f;
        float timeSinceLastHeal = 0f;
        float timeSinceLastHealOpen = 0f;

        bool playedPassiveTransformationSound = false;

        public AudioSource ItemSFX;

        public AudioClip PassiveTransformationSFX;
        public AudioClip FullTransformationSFX;

        public GameObject Orb;
        public Animator itemAnimator;

        bool open = false;


        public override void Start()
        {
            base.Start();
            enableOpenNecklace = configEnableOpenNecklace.Value;
            timeToTransform = configTimeToTransform.Value;
            transformOpenMultiplier = configTransformOpenMultiplier.Value;
            healthPerSecond = configHealthPerSecondHolding.Value;
            healthPerSecondOpen = configHealthPerSecondOpen.Value;
            lootBugTransformTime = configHoarderBugTransformTime.Value;
            birdTransformTime = configBaboonHawkTransformTime.Value;
            timeToSpawnSCP4271 = configTimeToSpawnSCP4271.Value;
            spawnAfterPickup = configContinueSpawningAfterPickup.Value;
            maxSpawns = configMaxSpawns.Value;
            inInventoryCounts = configIncreaseTimeWhenInInventory.Value;

            if (enableOpenNecklace) { itemProperties.toolTips = ["Open [LMB]"]; }
        }

        public override void Update()
        {
            base.Update();

            if (StartOfRound.Instance.inShipPhase) { return; }

            timeSinceLastHeal += Time.deltaTime;

            if (!isHeld) { playedPassiveTransformationSound = false; }

            if (isHeld && playerHeldBy != null && playerHeldBy == localPlayer && !isBeingUsed) // Held by local player
            {
                if (isHeld && isPocketed && !inInventoryCounts) { return; }

                if (timeSinceLastHeal > 1f)
                {
                    HealPlayer(healthPerSecond);
                    timeSinceLastHeal = 0f;
                }

                if (timeToTransform != -1 && !transformingEntity && !SCP500Compatibility.IsLocalPlayerAffectedBySCP500)
                {
                    timeSCP427HeldByLocalPlayer += Time.deltaTime;

                    logger.LogDebug($"Time held by local player: {timeSCP427HeldByLocalPlayer}");

                    if (timeSCP427HeldByLocalPlayer >= timeToTransform / 2 && timeSCP427HeldByLocalPlayer <= timeToTransform / 2 + 1f && !playedPassiveTransformationSound)
                    {
                        logger.LogDebug("Playing 1/2 transform sound");
                        ItemSFX.PlayOneShot(PassiveTransformationSFX, 0.5f);
                        playedPassiveTransformationSound = true;
                    }

                    if (timeSCP427HeldByLocalPlayer >= timeToTransform && !StartOfRound.Instance.inShipPhase)
                    {
                        logger.LogDebug("Transforming player");
                        transformingEntity = true;
                        TransformPlayer();
                    }
                }
            }
            else if (isHeldByEnemy) // Held by enemy
            {
                if (!IsServer) { return; }
                HoarderBugAI bug = FindObjectsOfType<HoarderBugAI>().Where(x => x.heldItem.itemGrabbableObject == this).FirstOrDefault();
                BaboonBirdAI bird = FindObjectsOfType<BaboonBirdAI>().Where(x => x.heldScrap == this).FirstOrDefault();

                if (bug != null)
                {
                    hasBeenHeld = true;

                    if (!LootBugHoldTimes.ContainsKey(bug))
                    {
                        LootBugHoldTimes.Add(bug, 0f);
                    }

                    if (timeSinceLastHeal > 1f)
                    {
                        HealEnemy(bug);
                        timeSinceLastHeal = 0f;
                    }

                    if (lootBugTransformTime != -1 && !transformingEntity)
                    {
                        LootBugHoldTimes[bug] += Time.deltaTime;
                        logger.LogDebug($"Loot bug hold time: {LootBugHoldTimes[bug]}");

                        if (LootBugHoldTimes[bug] >= lootBugTransformTime)
                        {
                            logger.LogDebug("Transforming bug");
                            transformingEntity = true;
                            TransformEnemy(bug);
                        }
                    }
                }
                else if (bird != null)
                {
                    hasBeenHeld = true;

                    if (!BirdHoldTimes.ContainsKey(bird))
                    {
                        BirdHoldTimes.Add(bird, 0f);
                    }

                    if (timeSinceLastHeal > 1f)
                    {
                        HealEnemy(bird);
                        timeSinceLastHeal = 0f;
                    }

                    if (birdTransformTime != -1 && !transformingEntity)
                    {
                        BirdHoldTimes[bird] += Time.deltaTime;
                        logger.LogDebug($"Bird hold time: {BirdHoldTimes[bird]}");

                        if (BirdHoldTimes[bird] >= birdTransformTime)
                        {
                            logger.LogDebug("Transforming bird");
                            transformingEntity = true;
                            TransformEnemy(bird);
                        }
                    }
                }
            }
            else // On ground
            {
                if (!IsServer) { return; }
                if (timeToSpawnSCP4271 != -1 && !transformingEntity)
                {
                    if (!spawnAfterPickup && hasBeenHeld) { return; }
                    if (!hasHitGround) { return; }

                    timeOnGround += Time.deltaTime;
                    logger.LogDebug($"Time on ground: {timeOnGround}");

                    if (timeOnGround >= timeToSpawnSCP4271)
                    {
                        if (FindObjectsOfType<SCP427Behavior>().Count() >= maxSpawns) { return; } // TODO: This may cause errors?
                        logger.LogDebug("Spawning SCP-427-1");
                        timeToSpawnSCP4271 += timeToSpawnSCP4271;
                        SpawnSCP4271(transform.position);
                    }
                }
            }
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            base.ItemActivate(used, buttonDown);
            if (buttonDown)
            {
                if (StartOfRound.Instance.inShipPhase) { return; }
                if (!enableOpenNecklace || transformingEntity) { return; }

                if (!open)
                {
                    itemAnimator.SetTrigger("open");
                    open = true;
                    Orb.SetActive(true);
                }
                if (!ItemSFX.isPlaying) { ItemSFX.Play(); }

                timeSinceLastHealOpen += Time.deltaTime;

                if (timeSinceLastHealOpen > 1f)
                {
                    HealPlayer(healthPerSecondOpen);
                    timeSinceLastHealOpen = 0f;
                }

                if (playerHeldBy != null && timeToTransform != -1 && !SCP500Compatibility.IsLocalPlayerAffectedBySCP500)
                {
                    timeSCP427HeldByLocalPlayer += Time.deltaTime * transformOpenMultiplier;
                    logger.LogDebug($"Time SCP-427 Held: {timeSCP427HeldByLocalPlayer}");

                    if (timeSCP427HeldByLocalPlayer >= timeToTransform)
                    {
                        logger.LogDebug("Transforming Local Player");
                        transformingEntity = true;
                        TransformPlayer();
                    }
                }
            }
            else
            {
                // Button was released
                logger.LogDebug("Button was released");
                open = false;
                itemAnimator.SetTrigger("close");
                Orb.SetActive(false);
                ItemSFX.Stop();
            }
        }

        public void TransformPlayer()
        {
            logger.LogDebug("Transforming player");
            StartCoroutine(TransformPlayerCoroutine());
        }

        private IEnumerator TransformPlayerCoroutine()
        {
            PlayerControllerB player = playerHeldBy;
            player.KillPlayer(Vector3.zero, true, CauseOfDeath.Unknown, 3);
            ItemSFX.PlayOneShot(FullTransformationSFX, 1f);

            foreach (var player2 in StartOfRound.Instance.allPlayerScripts)
            {
                if (player2.HasLineOfSightToPosition(player.transform.position) && player2 != player && player2.isPlayerControlled)
                {
                    player2.IncreaseFearLevelOverTime();
                }
            }

            yield return new WaitForSeconds(4f);

            Vector3 spawnPos = player.deadBody.transform.position;

            Destroy(player.deadBody.gameObject);
            SpawnSCP4271(spawnPos);

            transformingEntity = false;
        }

        private void TransformEnemy(EnemyAI enemy)
        {
            logger.LogDebug($"Transforming {enemy.enemyType.enemyName}");
            StartCoroutine(TransformEnemyCoroutine(enemy));
        }

        private IEnumerator TransformEnemyCoroutine(EnemyAI enemy)
        {
            Vector3 spawnPos = enemy.transform.position;

            enemy.KillEnemy();
            ItemSFX.PlayOneShot(FullTransformationSFX, 1f);

            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player.HasLineOfSightToPosition(enemy.transform.position) && player.isPlayerControlled)
                {
                    player.IncreaseFearLevelOverTime();
                }
            }

            yield return new WaitForSecondsRealtime(4f);

            if (enemy.thisNetworkObject.IsSpawned)
            {
                enemy.thisNetworkObject.Despawn();
            }

            SpawnSCP4271(spawnPos);
            transformingEntity = false;
        }

        public void SpawnSCP4271(Vector3 spawnPos)
        {
            logger.LogDebug("Spawning SCP-427-1");

            int index = RoundManager.Instance.currentLevel.Enemies.FindIndex(x => x.enemyType.name == "SCP4271Enemy");
            RoundManager.Instance.SpawnEnemyOnServer(spawnPos, UnityEngine.Random.Range(0f, 360f), index);
        }

        public void HealPlayer(int health)
        {
            int newHealth = playerHeldBy.health + health;

            playerHeldBy.health = Mathf.Clamp(newHealth, 0, 100);
            HUDManager.Instance.UpdateHealthUI(playerHeldBy.health, false);
        }

        public void HealEnemy(EnemyAI enemyToHeal)
        {
            SpawnableEnemyWithRarity spawnableEnemy = RoundManager.Instance.currentLevel.Enemies.Where(x => x.enemyType.enemyName == enemyToHeal.enemyType.enemyName).FirstOrDefault();
            if (spawnableEnemy == null) { logger.LogError("Enemy not found: " + enemyToHeal.enemyType.enemyName); return; }

            int maxHealth = spawnableEnemy.enemyType.enemyPrefab.GetComponent<EnemyAI>().enemyHP;

            if (enemyToHeal.enemyHP < maxHealth && timeSinceLastHeal > 1f)
            {
                enemyToHeal.enemyHP += 1;

                logger.LogDebug($"{enemyToHeal.enemyType.enemyName} HP: {enemyToHeal.enemyHP}/{maxHealth}");

                timeSinceLastHeal = 0f;
            }
        }

        // RPCs
    }

    [HarmonyPatch]
    internal class SCP427Patches
    {
        private static ManualLogSource logger = LoggerInstance;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer))]
        public static void KillPlayerPostfix()
        {
            SCP427Behavior.timeSCP427HeldByLocalPlayer = 0f;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.DespawnPropsAtEndOfRound))]
        private static void DespawnPropsAtEndOfRoundPostfix()
        {
            logger.LogDebug("In DespawnPropsAtEndOfRoundPatch");

            // SCP427
            SCP427Behavior.LootBugHoldTimes.Clear();
            SCP427Behavior.BirdHoldTimes.Clear();
        }
    }
}