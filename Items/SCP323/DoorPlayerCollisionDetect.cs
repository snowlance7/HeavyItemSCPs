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
                StartCoroutine(AddForceToPlayer(player));
                hitPlayer = true;
            }
        }

        IEnumerator AddForceToPlayer(PlayerControllerB player)
        {
            Rigidbody rb = player.playerRigidbody;
            rb.isKinematic = false;
            rb.velocity = Vector3.zero;
            player.externalForceAutoFade += force;

            yield return new WaitForSeconds(0.5f);
            yield return new WaitUntil(() => player.thisController.isGrounded || player.isInHangarShipRoom);

            rb.isKinematic = true;
        }

        private IEnumerator DisableAfterDelay()
        {
            yield return new WaitForSeconds(doorFlyingTime);
            isActive = false;
        }
    }
}
