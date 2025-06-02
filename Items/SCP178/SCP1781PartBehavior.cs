using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static HeavyItemSCPs.Plugin;

namespace HeavyItemSCPs.Items.SCP178
{
    internal class SCP1781PartBehavior : PhysicsProp
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public GameObject PartMesh;
        public GameObject ScanNode;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public override void Update()
        {
            base.Update();

            EnableMesh(SCP178Behavior.Instance != null && SCP178Behavior.Instance.wearingOnLocalClient);
        }

        public void EnableMesh(bool enable)
        {
            PartMesh.SetActive(enable);
            ScanNode.SetActive(enable);
        }
    }
}
