using UnityEngine;

namespace HeavyItemSCPs.SCP.SCP178
{
    internal class SCP1781PartBehavior : PhysicsProp
    {
        public MeshRenderer renderer = null!;
        public GameObject scanNode = null!;

        float timeSinceSpawn;

        public override void Update()
        {
            base.Update();

            timeSinceSpawn += Time.deltaTime;

            if (timeSinceSpawn < 5f) { return; }

            EnableMesh(SCP178Behavior.Instance != null && SCP178Behavior.Instance.wearingOnLocalClient);
        }

        public void EnableMesh(bool enable)
        {
            renderer.enabled = enable;
            scanNode.SetActive(enable);
        }
    }
}
