using System.Collections.Generic;
using Downstream.Boat;
using Downstream.Cameras;
using Downstream.Input;
using Downstream.Items;
using Downstream.Race;
using Downstream.UI;
using Downstream.Water;
using Downstream.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downstream.Editor
{
    /// <summary>
    /// Builds the first playable greybox from code, so the repository needs no hand-written scene
    /// YAML: a URP pipeline asset set up for the water shader, the lighting from the design doc's
    /// rendering rules, a 1.5 km test river with terraced block-kit banks, a boat and camera prefab,
    /// a post-processing volume, and a scene that races 8 boats (you plus 7 AI). Run it once after
    /// opening the project: Downstream > Create Greybox Race Scene, then press Play. It is safe to
    /// run again: assets are updated in place and the scene is rebuilt.
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        private const string Root = "Assets/Downstream";
        private const string SettingsDir = Root + "/Settings";
        private const string TrackDir = Root + "/Tracks/Greybox";
        private const string MegaKitDir = Root + "/Art/Vendor/StylizedNatureMegaKit/Models";
        private const string HdriPath = Root + "/Art/Vendor/PolyHaven/kloofendal_48d_partly_cloudy_puresky_4k.hdr";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SceneDir = Root + "/Scenes";

        // Design doc, Colour: calm ground (grass, earth, sand, turquoise water); pop accents only on
        // boats, gear and anything you race for. Materials are physically based, roughness 0.6-0.9 on
        // land; only water shines.
        private static readonly Color Grass = new Color(0.36f, 0.50f, 0.26f);
        private static readonly Color Earth = new Color(0.60f, 0.50f, 0.36f);
        private static readonly Color Rock = new Color(0.52f, 0.51f, 0.48f);
        private static readonly Color Sand = new Color(0.34f, 0.44f, 0.24f); // floodable meadow: darker grass, not beach
        private static readonly Color Stones = new Color(0.55f, 0.53f, 0.47f);
        private static readonly Color Tomato = new Color(0.86f, 0.27f, 0.20f);
        private static readonly Color Cliff = new Color(0.36f, 0.37f, 0.40f);
        private static readonly Color FogColour = new Color(0.76f, 0.84f, 0.91f); // the sky at the horizon: far land dissolves into it
        private static readonly Color Cream = new Color(0.93f, 0.88f, 0.76f);
        private static readonly Color Sunflower = new Color(0.98f, 0.78f, 0.22f);
        private static readonly Color SkyBlue = new Color(0.35f, 0.70f, 0.95f);

        [MenuItem("Downstream/Create Greybox Race Scene")]
        public static void CreateGreyboxScene()
        {
            // Open the empty scene first: NewScene(Single) unloads every asset nothing references yet,
            // which turns assets loaded or created before it (the river, the prefab components) into
            // dead references that serialize as None.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolders();
            EnsureUrp();

            var river = LoadOrCreate<RiverDefinition>(TrackDir + "/GreyboxRiver.asset", r => r.ApplyGreyboxDefaults());
            // The greybox river is generated content: re-apply the current defaults so the scene follows the code.
            river.ApplyGreyboxDefaults();
            EditorUtility.SetDirty(river);
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            var waterShader = Shader.Find("Downstream/River Water");
            if (waterShader == null)
            {
                Debug.LogError("[Downstream] Shader 'Downstream/River Water' did not compile; falling back to URP Lit for the water.");
                waterShader = litShader;
            }
            var grassMat = EnsureMaterial(TrackDir + "/GreyboxGrass.mat", litShader, Grass, 0.22f);
            // Bank terrace faces read as shaded grass slopes, not an earth road; earth stays on the channel lip.
            var earthMat = EnsureMaterial(TrackDir + "/GreyboxEarth.mat", litShader, Earth, 0.15f);
            var slopeMat = EnsureMaterial(TrackDir + "/GreyboxSlope.mat", litShader, new Color(0.36f, 0.50f, 0.26f), 0.2f);
            var propShader = Shader.Find("Downstream/Greybox Prop") ?? litShader;
            // River boulders are wet rock: darker and bluer than the dry rocks on the banks.
            var rockMat = PropMaterial(TrackDir + "/GreyboxRock.mat", propShader, new Color(0.34f, 0.36f, 0.40f), new Color(0.50f, 0.52f, 0.55f), new Color(0.18f, 0.20f, 0.24f));
            var groundShader = Shader.Find("Downstream/Greybox Ground") ?? litShader;
            var bedMat = EnsureMaterial(TrackDir + "/GreyboxBed.mat", groundShader, Stones, 0.35f);
            if (bedMat.HasProperty("_StoneColor")) { bedMat.SetColor("_StoneColor", Stones); bedMat.SetColor("_GrassColor", Sand); bedMat.SetColor("_SandColor", new Color(0.30f, 0.26f, 0.20f)); /* wet earth at the waterline, darker than the grass */ bedMat.SetColor("_EarthColor", Earth); bedMat.SetColor("_CliffColor", Cliff); }
            GroundTextures(bedMat);
            var waterMat = EnsureMaterial(TrackDir + "/GreyboxWater.mat", waterShader, Color.white, 0.94f);
            ConfigureWaterMaterial(waterMat);
            var itemMat = EnsureMaterial(TrackDir + "/GreyboxItem.mat", litShader, SkyBlue, 0.35f);
            // The old greybox bank material stays on disk for anyone still referencing it.
            EnsureMaterial(TrackDir + "/GreyboxBank.mat", litShader, Grass, 0.22f);

            var boatPrefab = CreateBoatPrefab(propShader, litShader);
            var cameraPrefab = CreateCameraPrefab();
            var post = EnsurePostProfile();
            var sky = EnsureSkyMaterial();

            var sun = CreateLighting(sky);
            CreateVolume(post);

            var water = new GameObject("Greybox Water", typeof(MeshFilter), typeof(MeshRenderer), typeof(GreyboxWaterMesh), typeof(RiverFieldGizmos));
            water.GetComponent<MeshRenderer>().sharedMaterial = waterMat;
            var waterMesh = water.GetComponent<GreyboxWaterMesh>();
            var waterSo = new SerializedObject(waterMesh);
            waterSo.FindProperty("_bedMaterial").objectReferenceValue = bedMat;
            waterSo.ApplyModifiedPropertiesWithoutUndo();
            waterMesh.BuildBanks(river, grassMat, earthMat, rockMat, slopeMat);
            SaveGeneratedMeshes(water.transform, TrackDir);
            var gizmos = new SerializedObject(water.GetComponent<RiverFieldGizmos>());
            gizmos.FindProperty("_river").objectReferenceValue = river;
            gizmos.ApplyModifiedPropertiesWithoutUndo();

            // Set dressing: hills, trees, rocks, reeds, flowers and story props, built at runtime.
            var dressing = new GameObject("Greybox Dressing", typeof(GreyboxDressing));
            WireDressing(dressing.GetComponent<GreyboxDressing>(), river, groundShader, litShader);

            var itemView = new GameObject("Items", typeof(ItemWorldView));
            var itemSo = new SerializedObject(itemView.GetComponent<ItemWorldView>());
            itemSo.FindProperty("_material").objectReferenceValue = itemMat;
            itemSo.FindProperty("_whirlMaterial").objectReferenceValue = EnsureSprayMaterial(litShader);
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            var director = new GameObject("Race Director", typeof(LocalPlayerJoin), typeof(RaceDirector), typeof(GreyboxRaceHud));
            var so = new SerializedObject(director.GetComponent<RaceDirector>());
            so.FindProperty("_river").objectReferenceValue = river;
            so.FindProperty("_boatPrefab").objectReferenceValue = boatPrefab;
            so.FindProperty("_cameraPrefab").objectReferenceValue = cameraPrefab;
            so.FindProperty("_waterMesh").objectReferenceValue = waterMesh;
            so.FindProperty("_players").objectReferenceValue = director.GetComponent<LocalPlayerJoin>();
            so.FindProperty("_itemView").objectReferenceValue = itemView.GetComponent<ItemWorldView>();
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();
            foreach (var field in new[] { "_river", "_boatPrefab", "_cameraPrefab" })
            {
                if (so.FindProperty(field).objectReferenceValue == null)
                    Debug.LogError($"[Downstream] Race Director lost its {field} reference; the race will not start.", director);
            }
            var hudSo = new SerializedObject(director.GetComponent<GreyboxRaceHud>());
            hudSo.FindProperty("_director").objectReferenceValue = director.GetComponent<RaceDirector>();
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            string scenePath = SceneDir + "/GreyboxRace.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AddToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Downstream] Greybox race scene created at {scenePath}. Press Play: keyboard or any gamepad drives boat 1.");
        }

        /// <summary>Creates the dressing's shared materials (a few tints each so the SRP batcher keeps the draw count down) and assigns them.</summary>
        private static void WireDressing(GreyboxDressing dressing, RiverDefinition river, Shader groundShader, Shader litShader)
        {
            string dir = TrackDir + "/Dressing";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(TrackDir, "Dressing");
            var hills = EnsureMaterial(dir + "/Hills.mat", groundShader, Grass, 0.2f);
            if (hills.HasProperty("_GrassColor")) { hills.SetColor("_GrassColor", new Color(0.33f, 0.47f, 0.24f)); hills.SetColor("_StoneColor", Stones); hills.SetColor("_SandColor", Sand); hills.SetColor("_EarthColor", Earth); hills.SetColor("_CliffColor", Cliff); }
            GroundTextures(hills);
            var propShader = Shader.Find("Downstream/Greybox Prop") ?? litShader;
            var canopies = new[]
            {
                PropMaterial(dir + "/Canopy1.mat", propShader, new Color(0.34f, 0.54f, 0.25f), new Color(0.56f, 0.74f, 0.33f), new Color(0.20f, 0.34f, 0.18f)),
                PropMaterial(dir + "/Canopy2.mat", propShader, new Color(0.40f, 0.58f, 0.26f), new Color(0.64f, 0.78f, 0.34f), new Color(0.22f, 0.36f, 0.17f)),
                PropMaterial(dir + "/Canopy3.mat", propShader, new Color(0.31f, 0.49f, 0.30f), new Color(0.50f, 0.70f, 0.40f), new Color(0.18f, 0.32f, 0.20f)),
                PropMaterial(dir + "/Canopy4.mat", propShader, new Color(0.46f, 0.60f, 0.28f), new Color(0.70f, 0.80f, 0.36f), new Color(0.26f, 0.38f, 0.18f)),
                PropMaterial(dir + "/Canopy5.mat", propShader, new Color(0.50f, 0.58f, 0.22f), new Color(0.76f, 0.80f, 0.30f), new Color(0.28f, 0.36f, 0.14f)),
            };
            var pines = new[]
            {
                PropMaterial(dir + "/Pine1.mat", propShader, new Color(0.20f, 0.40f, 0.30f), new Color(0.36f, 0.58f, 0.40f), new Color(0.12f, 0.26f, 0.20f)),
                PropMaterial(dir + "/Pine2.mat", propShader, new Color(0.24f, 0.44f, 0.27f), new Color(0.42f, 0.62f, 0.36f), new Color(0.14f, 0.28f, 0.18f)),
                PropMaterial(dir + "/Pine3.mat", propShader, new Color(0.18f, 0.36f, 0.26f), new Color(0.32f, 0.54f, 0.36f), new Color(0.10f, 0.22f, 0.17f)),
            };
            var flowers = new[]
            {
                EnsureMaterial(dir + "/FlowerSunflower.mat", litShader, Sunflower, 0.3f),
                EnsureMaterial(dir + "/FlowerTomato.mat", litShader, Tomato, 0.3f),
                EnsureMaterial(dir + "/FlowerBlossom.mat", litShader, new Color(0.96f, 0.62f, 0.76f), 0.3f),
                EnsureMaterial(dir + "/FlowerSky.mat", litShader, SkyBlue, 0.3f),
            };
            var lantern = EnsureMaterial(dir + "/Lantern.mat", litShader, new Color(1f, 0.85f, 0.45f), 0.4f);
            if (lantern.HasProperty("_EmissionColor"))
            {
                lantern.EnableKeyword("_EMISSION");
                lantern.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                lantern.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.35f) * 2.5f);
            }
            var so = new SerializedObject(dressing);
            so.FindProperty("_river").objectReferenceValue = river;
            so.FindProperty("_hills").objectReferenceValue = hills;
            SetArray(so.FindProperty("_canopies"), canopies);
            SetArray(so.FindProperty("_pines"), pines);
            so.FindProperty("_trunk").objectReferenceValue = PropMaterial(dir + "/Trunk.mat", propShader, new Color(0.40f, 0.29f, 0.20f), new Color(0.50f, 0.38f, 0.27f), new Color(0.26f, 0.18f, 0.12f));
            so.FindProperty("_rock").objectReferenceValue = PropMaterial(dir + "/Rock.mat", propShader, new Color(0.50f, 0.50f, 0.48f), new Color(0.68f, 0.68f, 0.64f), new Color(0.30f, 0.31f, 0.32f));
            so.FindProperty("_plank").objectReferenceValue = EnsureMaterial(dir + "/Plank.mat", litShader, new Color(0.62f, 0.46f, 0.30f), 0.2f);
            so.FindProperty("_shrine").objectReferenceValue = EnsureMaterial(dir + "/Shrine.mat", litShader, new Color(0.86f, 0.80f, 0.68f), 0.2f);
            so.FindProperty("_reed").objectReferenceValue = EnsureMaterial(dir + "/Reed.mat", litShader, new Color(0.45f, 0.58f, 0.25f), 0.2f);
            SetArray(so.FindProperty("_flowers"), flowers);
            so.FindProperty("_tent").objectReferenceValue = EnsureMaterial(dir + "/Tent.mat", litShader, Tomato, 0.25f);
            so.FindProperty("_post").objectReferenceValue = EnsureMaterial(dir + "/Post.mat", litShader, new Color(0.50f, 0.38f, 0.26f), 0.15f);
            so.FindProperty("_lantern").objectReferenceValue = lantern;
            so.FindProperty("_spray").objectReferenceValue = EnsureSprayMaterial(litShader);
            // CC0 vendor models when they are on disk; the block props stay as the fallback.
            SetObjects(so.FindProperty("_vendorBroadleaf"), Vendor("CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5", "TwistedTree_1", "TwistedTree_2", "TwistedTree_3"));
            SetObjects(so.FindProperty("_vendorPines"), Vendor("Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5"));
            SetObjects(so.FindProperty("_vendorBushes"), Vendor("Bush_Common", "Bush_Common_Flowers"));
            SetObjects(so.FindProperty("_vendorRocks"), Vendor("Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3"));
            SetObjects(so.FindProperty("_vendorGrass"), Vendor("Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Short", "Grass_Wispy_Tall", "Clover_1", "Fern_1"));
            SetObjects(so.FindProperty("_vendorFlowers"), Vendor("Flower_3_Group", "Flower_4_Group", "Plant_1", "Plant_7", "Mushroom_Common"));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A three-tone prop material: sides, lit tops and shaded undersides.</summary>
        private static Material PropMaterial(string path, Shader shader, Color side, Color top, Color under)
        {
            var mat = EnsureMaterial(path, shader, side, 0.15f);
            if (mat.HasProperty("_TopColor")) { mat.SetColor("_TopColor", top); mat.SetColor("_ShadeColor", under); }
            return mat;
        }

        private const string PolyHavenTextures = Root + "/Art/Vendor/PolyHaven/Textures";

        /// <summary>Wires the CC0 ground textures (grass, meadow, earth, rock) when they are on disk.</summary>
        private static void GroundTextures(Material mat)
        {
            if (!mat.HasProperty("_UseTextures")) return;
            Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(PolyHavenTextures + "/" + name + "_1k.jpg");
            var grass = Tex("aerial_grass_rock_diff");
            if (grass == null) { mat.SetFloat("_UseTextures", 0f); return; }
            mat.SetFloat("_UseTextures", 1f);
            mat.SetTexture("_GrassMap", grass);
            mat.SetTexture("_GrassNormal", Tex("aerial_grass_rock_nor_gl"));
            mat.SetTexture("_MeadowMap", Tex("leafy_grass_diff"));
            mat.SetTexture("_MeadowNormal", Tex("leafy_grass_nor_gl"));
            mat.SetTexture("_EarthMap", Tex("forrest_ground_01_diff"));
            mat.SetTexture("_EarthNormal", Tex("forrest_ground_01_nor_gl"));
            mat.SetTexture("_RockMap", Tex("rock_face_diff"));
            mat.SetTexture("_RockNormal", Tex("rock_face_nor_gl"));
            mat.SetFloat("_TexScale", 6f);
            mat.SetFloat("_TintStrength", 0.6f);
            mat.SetFloat("_NormalStrength", 0.9f);
            EditorUtility.SetDirty(mat);
        }

        private static GameObject[] Vendor(params string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(MegaKitDir + "/" + n + ".fbx");
                if (go != null) list.Add(go);
            }
            return list.ToArray();
        }

        private static void SetObjects(SerializedProperty property, Object[] items)
        {
            property.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        private static void SetArray(SerializedProperty property, Material[] items)
        {
            property.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        /// <summary>Rendering rules: one warm key sun from the upper left, a cool sky fill, teal-tinted shadow, light haze.</summary>
        private static Light CreateLighting(Material sky)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1.0f; // the sky fill keeps shadows cool, never grey-black
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.5f;
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = sky;
            if (sky != null && sky.HasProperty("_SunDirection")) sky.SetVector("_SunDirection", -sun.transform.forward);
            // Sky-driven fill: cool in the shadows, refreshed at runtime by the dressing (DynamicGI.UpdateEnvironment).
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.62f; // the HDRI sky is bright: keep grass and rock from blowing out
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 256;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColour;
            if (sky != null && sky.shader != null && (sky.shader.name == "Skybox/Panoramic" || sky.shader.name == "Downstream/Greybox Sky HDRI") && sky.GetTexture("_MainTex") is Texture2D hdri && hdri.isReadable)
            {
                // Measure the HDRI: the sun is the brightest patch, the fog colour is the band just above the horizon.
                AnalyseSky(hdri, out float sunAzimuth, out float sunElevation, out Color horizon);
                // Turn the panorama so its sun sits where the design wants the key: upper left, our usual -38 degrees.
                const float wantAzimuth = -38f;
                sky.SetFloat("_Rotation", Mathf.Repeat(wantAzimuth - sunAzimuth, 360f));
                sun.transform.rotation = Quaternion.Euler(sunElevation, wantAzimuth, 0f);
                sun.intensity = 1.2f;
                RenderSettings.fogColor = horizon;
                if (sky.HasProperty("_HorizonHaze")) sky.SetColor("_HorizonHaze", horizon);
            }
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 1800f; // the far ground reaches 3.2 km: distant hills show as faint silhouettes before dissolving
            return sun;
        }

        /// <summary>
        /// Reads a lat-long HDRI (Unity's panoramic mapping: u = 0.5 - atan2(z, x) / 2pi, v = 1 - acos(y) / pi):
        /// the sun's azimuth and elevation (degrees, as a light's Euler angles) and the mean colour of the
        /// band just above the horizon.
        /// </summary>
        private static void AnalyseSky(Texture2D hdri, out float sunAzimuth, out float sunElevation, out Color horizon)
        {
            var px = hdri.GetPixels();
            int w = hdri.width, h = hdri.height;
            float best = -1f; int bx = 0, by = 0;
            var band = Color.black; int bandCount = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                if (lum > best) { best = lum; bx = x; by = y; }
                float v = (y + 0.5f) / h;
                if (v > 0.505f && v < 0.56f) { band += c; bandCount++; }
            }
            float u = (bx + 0.5f) / w, vv = (by + 0.5f) / h;
            float lon = (0.5f - u) * 2f * Mathf.PI;      // atan2(z, x)
            float lat = (1f - vv) * Mathf.PI;            // acos(y)
            var dir = new Vector3(Mathf.Cos(lon) * Mathf.Sin(lat), Mathf.Cos(lat), Mathf.Sin(lon) * Mathf.Sin(lat));
            sunAzimuth = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // yaw of the direction toward the sun
            sunElevation = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            // A light's forward points away from the sun: yaw flips by 180 degrees.
            sunAzimuth = Mathf.Repeat(sunAzimuth + 180f, 360f);
            if (sunAzimuth > 180f) sunAzimuth -= 360f;
            horizon = bandCount > 0 ? band / bandCount : new Color(0.76f, 0.84f, 0.91f);
            horizon = new Color(Mathf.Clamp01(horizon.r), Mathf.Clamp01(horizon.g), Mathf.Clamp01(horizon.b), 1f);
        }

        private static void CreateVolume(VolumeProfile profile)
        {
            var go = new GameObject("Global Volume", typeof(Volume));
            var volume = go.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static VolumeProfile EnsurePostProfile()
        {
            string path = SettingsDir + "/DownstreamPost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.35f);
            bloom.intensity.Override(0.26f);
            bloom.scatter.Override(0.72f);
            var colour = GetOrAdd<ColorAdjustments>(profile);
            colour.postExposure.Override(0.05f);
            colour.contrast.Override(18f);
            colour.saturation.Override(6f);
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.45f);
            var balance = GetOrAdd<WhiteBalance>(profile);
            balance.temperature.Override(8f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>The painted sky: gradient bands that meet the fog colour at the horizon, a soft sun, noise clouds.</summary>
        private static Material EnsureSkyMaterial()
        {
            string path = SettingsDir + "/DownstreamSky.mat";
            var shader = Shader.Find("Downstream/Greybox Sky") ?? Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            // A real sky when the CC0 HDRI is on disk: lat-long panorama, with the sun and horizon measured from it.
            var hdri = AssetDatabase.LoadAssetAtPath<Texture2D>(HdriPath);
            var panoramic = Shader.Find("Downstream/Greybox Sky HDRI") ?? Shader.Find("Skybox/Panoramic");
            if (hdri != null && panoramic != null)
            {
                mat.shader = panoramic;
                mat.SetTexture("_MainTex", hdri);
                mat.SetFloat("_Mapping", 1f);   // latitude-longitude
                mat.SetFloat("_ImageType", 0f); // 360 degrees
                mat.SetFloat("_Exposure", 0.82f);
                mat.SetFloat("_Rotation", 0f);
                if (mat.HasProperty("_MaxBrightness")) mat.SetFloat("_MaxBrightness", 5f);
                if (mat.HasProperty("_HazeHeight")) { mat.SetFloat("_HazeHeight", 0.035f); mat.SetFloat("_HazeStrength", 0.5f); }
                if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", Color.white); // the panoramic shader's default tint is half grey
                EditorUtility.SetDirty(mat);
                return mat;
            }
            mat.shader = shader;
            if (mat.HasProperty("_ZenithColor"))
            {
                mat.SetColor("_ZenithColor", new Color(0.22f, 0.40f, 0.78f));
                mat.SetColor("_HorizonColor", FogColour);
                mat.SetColor("_GroundColor", new Color(0.68f, 0.77f, 0.86f));
                mat.SetFloat("_HorizonPower", 3.2f);
                mat.SetColor("_SunColor", new Color(1f, 0.95f, 0.85f));
                mat.SetFloat("_SunSize", 1400f);
                mat.SetFloat("_SunHalo", 0.35f);
                mat.SetFloat("_CloudCut", 0.52f);
                mat.SetFloat("_CloudSoft", 0.18f);
                mat.SetFloat("_CloudScale", 0.42f);
                mat.SetFloat("_CloudHeight", 0.14f);
                mat.SetColor("_CloudShade", new Color(0.72f, 0.78f, 0.88f));
                mat.SetFloat("_CloudAmount", 0.85f);
            }
            else
            {
                mat.SetFloat("_SunSize", 0.035f);
                mat.SetFloat("_SunSizeConvergence", 5f);
                mat.SetFloat("_AtmosphereThickness", 0.72f);
                mat.SetColor("_SkyTint", new Color(0.50f, 0.63f, 0.84f));
                mat.SetColor("_GroundColor", FogColour);
                mat.SetFloat("_Exposure", 1.15f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureSprayMaterial(Shader fallback)
        {
            var shader = Shader.Find("Downstream/Greybox Spray") ?? fallback;
            var mat = EnsureMaterial(PrefabDir + "/GreyboxSpray.mat", shader, Color.white, 0f);
            if (mat.HasProperty("_SkyTint")) mat.SetColor("_SkyTint", new Color(0.90f, 0.95f, 1f));
            return mat;
        }

        private static BoatView CreateBoatPrefab(Shader propShader, Shader litShader)
        {
            string path = PrefabDir + "/GreyboxBoat.prefab";
            var hullMat = PropMaterial(PrefabDir + "/GreyboxHull.mat", propShader, Tomato, Color.Lerp(Tomato, Color.white, 0.32f), Tomato * 0.55f);
            var trimMat = PropMaterial(PrefabDir + "/GreyboxTrim.mat", propShader, Cream, new Color(0.99f, 0.96f, 0.88f), new Color(0.60f, 0.55f, 0.45f));
            var skinMat = PropMaterial(PrefabDir + "/GreyboxPilot.mat", propShader, new Color(0.93f, 0.76f, 0.60f), new Color(0.98f, 0.86f, 0.72f), new Color(0.62f, 0.46f, 0.36f));
            var paddleMat = PropMaterial(PrefabDir + "/GreyboxPaddle.mat", propShader, new Color(0.56f, 0.40f, 0.26f), new Color(0.70f, 0.54f, 0.36f), new Color(0.34f, 0.24f, 0.16f));
            var wakeShader = Shader.Find("Downstream/Greybox Spray") ?? litShader;
            var wakeMat = EnsureMaterial(PrefabDir + "/GreyboxWake.mat", wakeShader, new Color(1f, 1f, 1f, 0.9f), 0f);
            var sprayMat = EnsureSprayMaterial(litShader);

            var hullMesh = EnsureMesh(PrefabDir + "/GreyboxHullMesh.asset", () => BlockBoat.Hull("GreyboxHull"));
            var deckMesh = EnsureMesh(PrefabDir + "/GreyboxDeckMesh.asset", () => BlockBoat.Deck("GreyboxDeck"));
            var headMesh = EnsureMesh(PrefabDir + "/GreyboxPilotMesh.asset", () => BlockBoat.Head("GreyboxPilotHead"));
            var torsoMesh = EnsureMesh(PrefabDir + "/GreyboxVestMesh.asset", () => BlockBoat.Torso("GreyboxTorso"));
            var seatMesh = EnsureMesh(PrefabDir + "/GreyboxSeatMesh.asset", () => BlockBoat.Seat("GreyboxSeat"));
            var helmetMesh = EnsureMesh(PrefabDir + "/GreyboxHelmetMesh.asset", () => BlockBoat.HelmetOnHead("GreyboxHelmet"));
            var armMesh = EnsureMesh(PrefabDir + "/GreyboxArmMesh.asset", () => BlockBoat.Arm("GreyboxArm"));
            var paddleMesh = EnsureMesh(PrefabDir + "/GreyboxPaddleMesh.asset", () => BlockBoat.PaddleCentred("GreyboxPaddle"));
            var gogglesMesh = EnsureMesh(PrefabDir + "/GreyboxGogglesMesh.asset", () => BlockBoat.Goggles("GreyboxGoggles"));
            var helmetTrimMesh = EnsureMesh(PrefabDir + "/GreyboxHelmetTrimMesh.asset", () => BlockBoat.HelmetTrim("GreyboxHelmetTrim"));
            var vestTrimMesh = EnsureMesh(PrefabDir + "/GreyboxVestTrimMesh.asset", () => BlockBoat.VestTrim("GreyboxVestTrim"));
            var legsMesh = EnsureMesh(PrefabDir + "/GreyboxLegsMesh.asset", () => BlockBoat.Legs("GreyboxLegs"));
            var handMesh = EnsureMesh(PrefabDir + "/GreyboxHandMesh.asset", () => BlockBoat.Hand("GreyboxHand"));
            var sleeveMesh = EnsureMesh(PrefabDir + "/GreyboxSleeveMesh.asset", () => BlockBoat.Sleeve("GreyboxSleeve"));
            var darkMat = PropMaterial(PrefabDir + "/GreyboxDark.mat", propShader, new Color(0.16f, 0.17f, 0.20f), new Color(0.26f, 0.27f, 0.31f), new Color(0.08f, 0.09f, 0.11f));

            var root = new GameObject("GreyboxBoat");
            Renderer Part(string name, Mesh mesh, Material mat)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                return r;
            }
            var hull = Part("Hull", hullMesh, hullMat);
            Part("Deck", deckMesh, trimMat);
            Part("Seat", seatMesh, trimMat);
            // Articulated pilot: hips -> torso -> (neck -> head + helmet), shoulders -> arms, chest -> paddle.
            Renderer Child(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos)
            {
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                return r;
            }
            var hips = new GameObject("Pilot").transform;
            hips.SetParent(root.transform, false);
            hips.localPosition = BlockBoat.Hips + new Vector3(0f, 0.1f, 0f);
            hips.localScale = Vector3.one * BlockBoat.PilotScale;
            var torsoR = Child(hips, "Torso", torsoMesh, trimMat, Vector3.zero);
            var torso = torsoR.transform;
            var headR = Child(torso, "Head", headMesh, skinMat, BlockBoat.NeckFromHips);
            var head = headR.transform;
            var helmet = Child(head, "Helmet", helmetMesh, hullMat, Vector3.zero);
            Child(head, "Goggles", gogglesMesh, darkMat, Vector3.zero);
            Child(head, "HelmetTrim", helmetTrimMesh, trimMat, Vector3.zero);
            var vestTrim = Child(torso, "VestTrim", vestTrimMesh, hullMat, Vector3.zero);
            Child(hips, "Legs", legsMesh, darkMat, Vector3.zero);
            var armL = Child(torso, "ArmL", armMesh, skinMat, BlockBoat.ShoulderL).transform;
            var armR = Child(torso, "ArmR", armMesh, skinMat, BlockBoat.ShoulderR).transform;
            var sleeveL = Child(armL, "Sleeve", sleeveMesh, hullMat, Vector3.zero);
            var sleeveR = Child(armR, "Sleeve", sleeveMesh, hullMat, Vector3.zero);
            var paddle = Child(torso, "Paddle", paddleMesh, paddleMat, BlockBoat.GripFromHips).transform;
            Child(paddle, "HandL", handMesh, darkMat, new Vector3(-BlockBoat.PaddleGripHalf, 0f, 0f));
            Child(paddle, "HandR", handMesh, darkMat, new Vector3(BlockBoat.PaddleGripHalf, 0f, 0f));
            var animator = root.AddComponent<BoatAnimator>();
            animator.Configure(torso, head, paddle, armL, armR);
            // The sim does all collision through one query proxy; views carry no colliders.
            var effects = root.AddComponent<BoatEffects>();
            effects.Configure(wakeMat, sprayMat);
            var view = root.AddComponent<BoatView>();
            var so = new SerializedObject(view);
            so.FindProperty("_hull").objectReferenceValue = hull.transform;
            so.FindProperty("_effects").objectReferenceValue = effects;
            so.FindProperty("_animator").objectReferenceValue = animator;
            var livery = so.FindProperty("_livery");
            livery.arraySize = 5;
            livery.GetArrayElementAtIndex(0).objectReferenceValue = hull;
            livery.GetArrayElementAtIndex(1).objectReferenceValue = helmet;
            livery.GetArrayElementAtIndex(2).objectReferenceValue = vestTrim;
            livery.GetArrayElementAtIndex(3).objectReferenceValue = sleeveL;
            livery.GetArrayElementAtIndex(4).objectReferenceValue = sleeveR;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<BoatView>();
        }

        private static ChaseCamera CreateCameraPrefab()
        {
            string path = PrefabDir + "/ChaseCamera.prefab";
            var go = new GameObject("ChaseCamera", typeof(Camera), typeof(AudioListener), typeof(ChaseCamera));
            var camera = go.GetComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.farClipPlane = 3000f; // inside the far ground, past the end of the fog
            camera.nearClipPlane = 0.2f;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; // on top of the pipeline's MSAA
            data.antialiasingQuality = AntialiasingQuality.High;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ChaseCamera>();
        }

        /// <summary>Creates the URP assets if the project has none, and sets what the water shader needs.</summary>
        private static void EnsureUrp()
        {
            string rendererPath = SettingsDir + "/DownstreamRenderer.asset";
            string pipelinePath = SettingsDir + "/DownstreamURP.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            renderer.renderingMode = RenderingMode.ForwardPlus;
            EnsureSsao(renderer);
            // The water refracts the opaque texture and fades against the depth texture.
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 320f;
            pipeline.shadowCascadeCount = 4;
            // No public setter for soft shadows; write the serialized field.
            var pipelineSo = new SerializedObject(pipeline);
            pipelineSo.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineSo.ApplyModifiedPropertiesWithoutUndo();
            pipeline.mainLightShadowmapResolution = 4096;
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);

            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                Debug.Log("[Downstream] Created and assigned a URP pipeline asset (Forward+, depth and opaque textures, HDR, 4x MSAA).");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Screen-space ambient occlusion: the contact shading that makes blocks sit on the ground.
        /// The URP feature type is internal, so it is created by name and wired through the serialized lists.</summary>
        private static void EnsureSsao(UniversalRendererData renderer)
        {
            var type = System.Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
            if (type == null)
            {
                Debug.LogWarning("[Downstream] URP's ScreenSpaceAmbientOcclusion feature type was not found; skipping SSAO.");
                return;
            }
            ScriptableRendererFeature feature = null;
            foreach (var f in renderer.rendererFeatures) if (f != null && f.GetType() == type) feature = f;
            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "Screen Space Ambient Occlusion";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                var so = new SerializedObject(renderer);
                var list = so.FindProperty("m_RendererFeatures");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = feature.GetInstanceID();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var fso = new SerializedObject(feature);
            var settings = fso.FindProperty("m_Settings");
            if (settings != null)
            {
                void SetF(string name, float v) { var p = settings.FindPropertyRelative(name); if (p != null) p.floatValue = v; }
                void SetB(string name, bool v) { var p = settings.FindPropertyRelative(name); if (p != null) p.boolValue = v; }
                SetF("Intensity", 0.6f);
                SetF("Radius", 0.45f);
                SetF("Falloff", 160f);
                SetF("DirectLightingStrength", 0.25f);
                SetB("Downsample", true);
                SetB("AfterOpaque", false);
                fso.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            init?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Temperate highlands water (design doc, Biomes): clear green over stones, peaty in the pools.</summary>
        private static void ConfigureWaterMaterial(Material mat)
        {
            if (!mat.HasProperty("_ShallowColor")) return;
            mat.SetColor("_ShallowColor", new Color(0.58f, 0.84f, 0.78f));
            mat.SetColor("_DeepColor", new Color(0.11f, 0.33f, 0.37f));
            mat.SetVector("_Absorption", new Vector4(2.2f, 1.2f, 0.95f, 0f));
            mat.SetFloat("_Refraction", 0.035f);
            mat.SetFloat("_EdgeFade", 0.35f);
            mat.SetFloat("_RippleScale", 0.25f);
            mat.SetFloat("_RippleStrength", 0.25f);
            mat.SetFloat("_FlowSpeed", 0.35f);
            mat.SetFloat("_LaneStretch", 1.5f);
            mat.SetFloat("_SpecularStrength", 2f);
            mat.SetColor("_ZenithColor", new Color(0.50f, 0.68f, 0.90f));
            mat.SetColor("_HorizonColor", new Color(0.85f, 0.90f, 0.95f));
            mat.SetFloat("_Fresnel", 1f);
            mat.SetFloat("_FoamScale", 0.22f);
            mat.SetFloat("_FoamFlowStart", 8.5f);
            mat.SetFloat("_FoamFlowFull", 13f);
            mat.SetFloat("_SlowSpeed", 2.5f);
            mat.SetFloat("_FastSpeed", 9f);
            mat.SetFloat("_FoamSlope", 0.12f);
            EditorUtility.SetDirty(mat);
        }

        /// <summary>Creates or updates a material so re-running the builder applies the current look.</summary>
        private static Material EnsureMaterial(string path, Shader shader, Color color, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.shader != shader) mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D EnsureTexture(string path, System.Func<Texture2D> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            var tex = build();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        private static Mesh EnsureMesh(string path, System.Func<Mesh> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var mesh = build();
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            // Keep the asset (and the references to it) and replace its contents.
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>Meshes built in memory would vanish on scene reload; store each as an asset named after it.</summary>
        private static void SaveGeneratedMeshes(Transform root, string dir)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || EditorUtility.IsPersistent(mesh)) continue;
                string path = $"{dir}/{mesh.name}.asset";
                var saved = EnsureMesh(path, () => mesh);
                filter.sharedMesh = saved;
            }
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == scenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { SettingsDir, Root + "/Tracks", TrackDir, PrefabDir, SceneDir })
            {
                if (AssetDatabase.IsValidFolder(dir)) continue;
                int slash = dir.LastIndexOf('/');
                AssetDatabase.CreateFolder(dir.Substring(0, slash), dir.Substring(slash + 1));
            }
        }
    }
}
