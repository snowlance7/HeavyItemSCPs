using SnowyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static HeavyItemSCPs.Plugin;

// lens distortion -0.35
// chromatic aberration 3.5
// color 0.5 0 1.2 / 127.5 0 306

namespace HeavyItemSCPs.SCP.SCP178
{
    internal class SCP1783DVision : MonoBehaviour
    {
        public static SCP1783DVision Instance;

        private GameObject camera_object;

        public static bool wearingGlasses = false;

        private GameObject normal_filter;

        private Volume normal_volume;

        private Volume glasses_volume;

        private GameObject glasses_filter;

        private float glasses_response_time = 1f;

        private float glasses_timer = 0f;

        private bool initiated = false;

        public Material highlightMaterial;
        public GameObject lungObject;
        public Material lungMaterial;

        public Dictionary<GameObject, Material> highlightedScrap = new Dictionary<GameObject, Material>();
        float timeSinceIntervalUpdate = 0f;

        public static float lensDistortion = -0.2f;
        public static float chromaticAberration = 3f;
        public static string colorTint = "500,0,500";

        [InitConfig]
        public static void InitConfigs()
        {
            lensDistortion = PluginInstance.Config.Bind("SCP-178 Options", "SCP-178 | Lens Distortion", -0.2f, "Changes the lens distortion effect of the 3D glasses.").Value;
            chromaticAberration = PluginInstance.Config.Bind("SCP-178 Options", "SCP-178 | Chromatic Aberration", 3f, "Changes the chromatic aberration effect of the 3D glasses.").Value;
            colorTint = PluginInstance.Config.Bind("SCP-178 Options", "SCP-178 | Color Tint", "500,0,500", "Changes the RGB color tint effect of the 3D glasses.").Value;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(this);
            }
        }

        public static void Load()
        {
            GameObject gameObject = new GameObject("SCP1783DController");
            gameObject.AddComponent<SCP1783DVision>();
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            logger.LogDebug("SCP1783DController loaded");
        }

        public void Init()
        {
            camera_object = localPlayer.gameplayCamera.gameObject;
            normal_filter = GameObject.Find("CustomPass");
            normal_volume = GameObject.Find("VolumeMain").GetComponent<Volume>();
            glasses_filter = UnityEngine.Object.Instantiate(GameObject.Find("VolumeMain"));
            glasses_filter.name = "3DGlassesVolume";

            glasses_filter.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            glasses_volume = glasses_filter.GetComponent<Volume>();
            VolumeProfile profile = glasses_volume.profile;
            profile.name = "3DGlassesProfile";
            profile.components.Clear();

            // Add 3D effect overrides
            ChromaticAberration chromatic = profile.Add<ChromaticAberration>(true);
            chromatic.intensity.max = 5f;
            chromatic.intensity.value = chromaticAberration;

            LensDistortion _lensDistortion = profile.Add<LensDistortion>(true);
            _lensDistortion.intensity.value = lensDistortion;

            ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.colorFilter.value = GetColor(colorTint);  // Red-blue tint for 3D effect

            glasses_filter.SetActive(value: true);
            glasses_volume.weight = 0f;
            initiated = true;


            highlightMaterial = HeavyItemSCPsContentHandler.Instance.SCP178.HighlightMaterial;
        }

        public void Update()
        {
            if (!initiated) { return; }
            if (wearingGlasses)
            {
                glasses_timer += Time.deltaTime;
            }
            else
            {
                glasses_timer -= Time.deltaTime;
            }
            if (glasses_timer <= 0f)
            {
                glasses_timer = 0f;
            }
            if (glasses_timer >= glasses_response_time)
            {
                glasses_timer = glasses_response_time;
            }
            if (localPlayer != null)
            {
                normal_volume.weight = (glasses_response_time - glasses_timer) / glasses_response_time;
                glasses_volume.weight = glasses_timer / glasses_response_time;
            }

            timeSinceIntervalUpdate += Time.deltaTime;

            if (timeSinceIntervalUpdate >= 0.2f)
            {
                timeSinceIntervalUpdate = 0f;
                DoIntervalUpdate();
            }
        }

        void DoIntervalUpdate()
        {
            if (wearingGlasses)
            {
                HighlightScrap();
            }
        }

        void HighlightScrap()
        {
            List<GameObject> scrapList = new List<GameObject>();

            RaycastHit[] hits = Physics.RaycastAll(localPlayer.playerEye.transform.position, localPlayer.playerEye.transform.forward, 30f, LayerMask.GetMask("Props"));

            foreach (RaycastHit hit in hits)
            {
                GameObject prop = hit.collider.gameObject;
                if (prop != null && prop.TryGetComponent<GrabbableObject>(out GrabbableObject grabbableObject))
                {
                    if (grabbableObject.itemProperties.isScrap)
                    {
                        scrapList.Add(prop);
                    }
                }
            }

            foreach (GameObject scrap in highlightedScrap.Keys.ToList())
            {
                if (!scrapList.Contains(scrap))
                {
                    if (scrap != null)
                    {
                        MeshRenderer renderer = scrap.GetComponentInChildren<MeshRenderer>();
                        if (renderer != null)
                        {
                            renderer.material = highlightedScrap[scrap];
                        }
                    }
                    highlightedScrap.Remove(scrap);
                }
            }

            foreach (GameObject scrap in scrapList)
            {
                if (!highlightedScrap.ContainsKey(scrap) && scrap != null)
                {
                    MeshRenderer renderer = scrap.GetComponentInChildren<MeshRenderer>();
                    if (renderer != null)
                    {
                        highlightedScrap.Add(scrap, renderer.material);
                        renderer.material = highlightMaterial;
                    }
                }
            }
        }

        public void Enable3DVision(bool enable)
        {
            logger.LogDebug("Enabling 3D Vision");
            if (!(localPlayer == null) && !(camera_object == null))
            {
                wearingGlasses = enable;
                if (wearingGlasses)
                {
                    normal_filter.SetActive(value: false);

                    LungProp lung = FindObjectsOfType<LungProp>().Where(x => x.isLungDocked).FirstOrDefault();
                    if (lung != null)
                    {
                        lungObject = lung.gameObject;
                        lungMaterial = lungObject.GetComponentInChildren<MeshRenderer>().material;
                        lungObject.GetComponentInChildren<MeshRenderer>().material = highlightMaterial;
                    }
                }
                else
                {
                    normal_filter.SetActive(value: true);

                    if (lungObject != null)
                    {
                        lungObject.GetComponentInChildren<MeshRenderer>().material = lungMaterial;
                    }

                    if (highlightedScrap != null && highlightedScrap.Count > 0)
                    {
                        foreach (GameObject scrap in highlightedScrap.Keys.ToList())
                        {
                            if (scrap != null)
                            {
                                MeshRenderer renderer = scrap.GetComponentInChildren<MeshRenderer>();
                                if (renderer != null)
                                {
                                    renderer.material = highlightedScrap[scrap];
                                }
                            }
                        }
                        highlightedScrap.Clear();
                    }
                }
            }
        }
        
        public static Color GetColor(string colorString)
        {
            try
            {
                string[] rgb = colorString.Split(',');
                return new Color(float.Parse(rgb[0].Trim()) / 255f, float.Parse(rgb[1].Trim()) / 255f, float.Parse(rgb[2].Trim()) / 255f);
            }
            catch (Exception e)
            {
                logger.LogError(e);
                return new Color(1.9f, 0f, 3f);
            }
        }
    }
}