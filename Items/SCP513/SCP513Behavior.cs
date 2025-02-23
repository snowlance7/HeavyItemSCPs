using HeavyItemSCPs.Items.SCP323;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HeavyItemSCPs.Items.SCP513
{
    // TODO: Make sure 513-1 follows the player even if they go to another moon
    internal class SCP513Behavior
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public AudioSource ItemAudio;
        public AudioClip[] BellSFX;
        public GameObject SCP513_1Prefab;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }
}
