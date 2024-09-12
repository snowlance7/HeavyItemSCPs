using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using static HeavyItemSCPs.Plugin;
using BepInEx;

// lens distortion -0.35
// chromatic aberration 3.5
// color 0.5 0 1.2 / 127.5 0 306

namespace HeavyItemSCPs.Items.SCP178
{
    internal class SCP1783DVision : MonoBehaviour
    {
        public static SCP1783DVision Instance;
        private static ManualLogSource logger = LoggerInstance;

        private GameObject camera_object;

        private bool wearingGlasses = false;

        private GameObject normal_filter;

        private Volume normal_volume;

        private Volume glasses_volume;

        private GameObject glasses_filter;

        private float glasses_response_time = 1f;

        private float glasses_timer = 0f;

        private bool initiated = false;

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
            chromatic.intensity.value = config178ChromaticAberration.Value;

            LensDistortion lensDistortion = profile.Add<LensDistortion>(true);
            lensDistortion.intensity.value = config178LensDistortion.Value;

            ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.colorFilter.value = GetColor(config178ColorTint.Value);  // Red-blue tint for 3D effect

            glasses_filter.SetActive(value: true);
            glasses_volume.weight = 0f;
            initiated = true;
        }

        private void Update()
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
        }

        public void Enable3DVision(bool enable)
        {
            if (!(localPlayer == null) && !(camera_object == null))
            {
                wearingGlasses = enable;
                if (wearingGlasses)
                {
                    normal_filter.SetActive(value: false);
                }
                else
                {
                    normal_filter.SetActive(value: true);
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
                return new Color(0.5f, 0f, 1.2f);
            }
        } 
    }
}
