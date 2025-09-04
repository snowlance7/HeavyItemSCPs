using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using HeavyItemSCPs.Items.SCP178;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AICollisionDetect : MonoBehaviour, IHittable
    {
        public SCP1781AI mainScript;
        public float timeSinceCollision;

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
            timeSinceCollision = 0f;
            mainScript.OnCollideWithPlayer(player);
        }

        bool IHittable.Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            mainScript.HitEnemyOnLocalClient(force, hitDirection, playerWhoHit, playHitSFX, hitID);
            return true;
        }
    }
}
