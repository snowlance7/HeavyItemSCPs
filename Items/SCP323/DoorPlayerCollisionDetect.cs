using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP323
{
    internal class DoorPlayerCollisionDetect : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;

        float doorFlyingTime = 3f;

        bool hitPlayer = false;
        bool isActive = true;
        public Vector3 force;
        int damage;

        public void Start()
        {
            damage = config3231DoorBashDamage.Value;
            StartCoroutine(DisableAfterDelay());
        }
        
        void OnTriggerEnter(Collider other)
        {
            if (isActive && !hitPlayer && other.CompareTag("Player"))
            {
                PlayerControllerB player = other.GetComponent<PlayerControllerB>();
                logger.LogDebug("Door hit player " + player.playerUsername);
                player.DamagePlayer(damage, true, true, CauseOfDeath.Inertia, 0, false, force);
                hitPlayer = true;
            }
        }

        private IEnumerator DisableAfterDelay()
        {
            yield return new WaitForSeconds(doorFlyingTime);
            isActive = false;
        }
    }
}
