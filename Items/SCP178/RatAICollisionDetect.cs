using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using HeavyItemSCPs.Items.SCP178;

namespace HeavyItemSCPs.Items.SCP178
{
    public class SCP1781AICollisionDetect : MonoBehaviour, IHittable
    {
        public SCP1781AI mainScript;

        private void OnTriggerStay(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                mainScript.OnCollideWithPlayer(other);
            }
        }

        bool IHittable.Hit(int force, Vector3 hitDirection, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            mainScript.HitEnemyOnLocalClient(force, hitDirection, playerWhoHit, playHitSFX, hitID);
            return true;
        }
    }
}
