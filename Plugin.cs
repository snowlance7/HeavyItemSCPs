using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Dawn;
using Dusk;
using GameNetcodeStuff;
using HarmonyLib;
using PSCPLibrary;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace HeavyItemSCPs
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(DawnLib.PLUGIN_GUID)]
    [BepInDependency(SnowyLib.MyPluginInfo.PLUGIN_GUID)]
    [BepInDependency(PSCPLibrary.MyPluginInfo.PLUGIN_GUID)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin PluginInstance { get; private set; } = null!;
        public static ManualLogSource logger { get; private set; } = null!;
        public static DuskMod Mod { get; private set; } = null!;
        public static SCPDatabase SCPDatabase { get; private set; } = null!;

        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static PlayerControllerB localPlayer { get { return StartOfRound.Instance.localPlayerController; } }
        public static PlayerControllerB? PlayerFromId(ulong id) { return StartOfRound.Instance.allPlayerScripts.Where(x => x.actualClientId == id).FirstOrDefault(); }
        public static bool IsServerOrHost { get { return NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost; } }

        private void Awake()
        {
            if (PluginInstance == null)
            {
                PluginInstance = this;
            }

            logger = PluginInstance.Logger;

            harmony.PatchAll();

            AssetBundle? mainBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "itemscps_mainassets"));
            Mod = DuskMod.RegisterMod(this, mainBundle);
            Mod.RegisterContentHandlers();

            SCPDatabase = HeavyItemSCPsContentHandler.Instance.HeavyItemSCPsAssets!.SCPDatabase;
            SCPAPI.Register(SCPDatabase);

            InitializeNetworkBehaviours();

            // Finished
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
            logger.LogDebug("Finished initializing network behaviours");
        }
    }
}

/* For later use TODO
Voice.ListenForPhrase("my cool phrase", (message) => {
    Plugin.logger.LogInfo("my cool voice phrase was said!");
});
https://github.com/LoafOrc/VoiceRecognitionAPI/wiki/For-Developers
*/