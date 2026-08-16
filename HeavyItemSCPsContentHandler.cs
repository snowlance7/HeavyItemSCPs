using Dusk;
using PSCPLibrary;
using UnityEngine;

namespace HeavyItemSCPs
{
    public class HeavyItemSCPsContentHandler : ContentHandler<HeavyItemSCPsContentHandler>
    {
        public class HeavyItemSCPsAssetsAssets(DuskMod mod, string filePath) : AssetBundleLoader<HeavyItemSCPsAssetsAssets>(mod, filePath)
        {
            [LoadFromBundle("HeavyItemSCPsNetworkHandler.prefab")]
            public GameObject NetworkHandlerPrefab { get; private set; } = null!;

            [LoadFromBundle("SCPDatabase.asset")]
            public SCPDatabase SCPDatabase { get; private set; } = null!;
        }
        public HeavyItemSCPsAssetsAssets? HeavyItemSCPsAssets;

        public class SCP178Assets(DuskMod mod, string filePath) : AssetBundleLoader<SCP178Assets>(mod, filePath)
        {
            [LoadFromBundle("OverlayMaterial.mat")]
            public Material HighlightMaterial { get; private set; } = null!;
        }
        public SCP178Assets? SCP178;

        public class SCP323Assets(DuskMod mod, string filePath) : AssetBundleLoader<SCP323Assets>(mod, filePath) { }
        public SCP323Assets? SCP323;

        public class SCP427Assets(DuskMod mod, string filePath) : AssetBundleLoader<SCP427Assets>(mod, filePath) { }
        public SCP427Assets? SCP427;

        public class SCP513Assets(DuskMod mod, string filePath) : AssetBundleLoader<SCP513Assets>(mod, filePath)
        {
            [LoadFromBundle("SCP513_1.prefab")]
            public GameObject SCP513_1Prefab { get; private set; } = null!;
        }
        public SCP513Assets? SCP513;

        public HeavyItemSCPsContentHandler(DuskMod mod) : base(mod)
        {
            RegisterContent("heavyitemscps_assets", out HeavyItemSCPsAssets);

            RegisterContent("scp178", out SCP178);
            RegisterContent("scp323", out SCP323);
            RegisterContent("scp427", out SCP427);
            RegisterContent("scp513", out SCP513);
        }
    }

}