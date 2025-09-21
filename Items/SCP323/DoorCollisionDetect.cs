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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public SCP323_1AI mainScript;
        public AudioClip doorWooshSFX;
        public AudioClip metalDoorSmashSFX;
        public AudioClip bashSFX;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        DoorLock? doorLock;
        public bool triggering;
        float timeInTrigger = 0f;
        const float doorBashForce = 35f;
        const float despawnDoorAfterBashTime = 3f;

        void OnTriggerStay(Collider other) // InteractTrigger
        {
            if (mainScript.currentBehaviourStateIndex != (int)SCP323_1AI.State.Hunting) { return; }
            if (triggering || !other.CompareTag("InteractTrigger")) { return; }

            doorLock = other.gameObject.GetComponent<DoorLock>();
            if (doorLock == null || doorLock.isDoorOpened)
            {
                return;
            }
            GameObject steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
            GameObject? doorMesh = steelDoorObj.transform.Find("DoorMesh")?.gameObject;
            if (doorMesh != null)
            {
                timeInTrigger += Time.fixedDeltaTime;
                if (!(timeInTrigger <= 1f))
                {
                    triggering = true;
                    timeInTrigger = 0f;
                    other.tag = "Untagged";
                    mainScript.inSpecialAnimation = true;
                    mainScript.creatureAnimator.SetTrigger("punch");
                }
            }
        }

        public void BashDoor()
        {
            if (doorLock == null)
            {
                mainScript.inSpecialAnimation = false;
                return;
            }
            GameObject steelDoorObj = doorLock.transform.parent.transform.parent.gameObject;
            GameObject? doorMesh = steelDoorObj.transform.Find("DoorMesh")?.gameObject;
            if (doorMesh == null)
            {
                mainScript.inSpecialAnimation = false;
                return;
            }
            GameObject flyingDoorPrefab = new GameObject("FlyingDoor");
            BoxCollider tempCollider = flyingDoorPrefab.AddComponent<BoxCollider>();
            tempCollider.isTrigger = true;
            tempCollider.size = new Vector3(1f, 1.5f, 3f);
            flyingDoorPrefab.AddComponent<DoorPlayerCollisionDetect>();
            AudioSource tempAS = flyingDoorPrefab.AddComponent<AudioSource>();
            tempAS.spatialBlend = 1f;
            tempAS.maxDistance = 60f;
            tempAS.rolloffMode = AudioRolloffMode.Linear;
            tempAS.volume = 1f;
            GameObject flyingDoor = Instantiate(flyingDoorPrefab, doorLock.transform.position, doorLock.transform.rotation);
            doorMesh.transform.SetParent(flyingDoor.transform);
            Destroy(flyingDoorPrefab);
            Rigidbody rb = flyingDoor.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.useGravity = true;
            rb.isKinematic = true;
            Vector3 doorForward = flyingDoor.transform.position + flyingDoor.transform.right * 2f;
            Vector3 doorBackward = flyingDoor.transform.position - flyingDoor.transform.right * 2f;
            Vector3 direction;
            if (Vector3.Distance(doorForward, base.transform.position) < Vector3.Distance(doorBackward, base.transform.position))
            {
                direction = (doorBackward - doorForward).normalized;
                flyingDoor.transform.position = flyingDoor.transform.position - flyingDoor.transform.right;
            }
            else
            {
                direction = (doorForward - doorBackward).normalized;
                flyingDoor.transform.position = flyingDoor.transform.position + flyingDoor.transform.right;
            }
            Vector3 upDirection = base.transform.TransformDirection(Vector3.up).normalized * 0.1f;
            Vector3 playerHitDirection = (direction + upDirection).normalized;
            flyingDoor.GetComponent<DoorPlayerCollisionDetect>().force = playerHitDirection * doorBashForce;
            rb.isKinematic = false;
            rb.AddForce(direction * doorBashForce, ForceMode.Impulse);
            AudioSource doorAudio = flyingDoor.GetComponent<AudioSource>();
            doorAudio.PlayOneShot(bashSFX, 1f);
            switch (RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name)
            {
                case "Level1Flow":
                case "Level1FlowExtraLarge":
                case "Level1Flow3Exits":
                case "Level3Flow":
                    doorAudio.PlayOneShot(metalDoorSmashSFX, 0.8f);
                    break;
            }
            doorAudio.PlayOneShot(doorWooshSFX, 1f);
            triggering = false;
            doorLock = null;
            mainScript.inSpecialAnimation = false;
            Destroy(flyingDoor, despawnDoorAfterBashTime);
        }
    }
}