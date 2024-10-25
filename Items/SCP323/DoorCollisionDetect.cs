using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP323
{
    internal class DoorCollisionDetect : MonoBehaviour
    {
        private static ManualLogSource logger = LoggerInstance;
        public SCP323_1AI mainScript = null!;

        public bool triggering;
        public float timeSinceHitPlayer;

        void OnTriggerEnter(Collider other) // InteractTrigger
        {
            if (!triggering && other.CompareTag("InteractTrigger"))
            {
                var doorLock = other.gameObject.GetComponent<DoorLock>();
                if (doorLock != null)
                {
                    triggering = true;
                    mainScript.BeginBashDoor(doorLock);
                }
            }
        }
    }
}
