using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using static HeavyItemSCPs.Plugin;
using HarmonyLib;
using GameNetcodeStuff;

namespace SCPItems
{
    internal class SCP500Compatibility
    {
        private static readonly BepInEx.Logging.ManualLogSource logger = LoggerInstance;

        private static bool? _enabled;

        internal static bool enabled
        {
            get
            {
                if (_enabled == null)
                {
                    _enabled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("ProjectSCP.SCP500");
                }
                return (bool)_enabled;
            }
        }

        internal static bool IsLocalPlayerAffectedBySCP500
        {
            get
            {
                if (config427_500Compatibility.Value && enabled)
                {
                    return LocalPlayerAffectedBySCP500();
                }
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static bool LocalPlayerAffectedBySCP500()
        {
            return SCP500.SCP500Controller.LocalPlayerAffectedBySCP500;
        }
    }
}