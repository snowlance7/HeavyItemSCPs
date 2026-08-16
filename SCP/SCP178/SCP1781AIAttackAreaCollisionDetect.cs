using GameNetcodeStuff;
using UnityEngine;

namespace HeavyItemSCPs.SCP.SCP178
{
    public class SCP1781AIAttackAreaCollisionDetect : MonoBehaviour
    {
        public SCP1781AI mainScript = null!;
        float timeSinceCollision;

        public void Update()
        {
            timeSinceCollision += Time.deltaTime;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Player")) { return; }
            PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
            if (player == null || !player.isPlayerControlled) { return; }
            if (timeSinceCollision < 2f) { return; }
            if (mainScript.currentBehaviorState != SCP1781AI.State.Chasing) { return; }
            timeSinceCollision = 0f;
            mainScript.OnCollideWithPlayer(player);
        }
    }
}