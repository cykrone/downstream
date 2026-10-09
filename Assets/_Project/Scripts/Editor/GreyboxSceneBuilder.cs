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
        private const string Root = "Assets/_Project";
        private const string SettingsDir = Root + "/Settings";
        private const string TrackDir = Root + "/Tracks/Greybox";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SceneDir = Root + "/Scenes";

        // Design doc, Colour: calm ground (grass, earth, sand, turquoise water); pop accents only on
        // boats, gear and anything you race for. Materials are physically based, roughness 0.6-0.9 on
        // land; only water shines.
        private static readonly Color Grass = new Color(0.42f, 0.58f, 0.30f);
        private static readonly Color Earth = new Color(0.56f, 0.44f, 0.32f);
        private static readonly Color Rock = new Color(0.52f, 0.51f, 0.48f);
        private static readonly Color Sand = new Color(0.40f, 0.50f, 0.27f); // floodable meadow: darker grass, not beach
        private static readonly Color Stones = new Color(0.55f, 0.53f, 0.47f);
        private static readonly Color Tomato = new Color(0.86f, 0.27f, 0.20f);
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
            var earthMat = EnsureMaterial(TrackDir + "/GreyboxEarth.mat", litShader, Earth, 0.15f);
            var rockMat = EnsureMaterial(TrackDir + "/GreyboxRock.mat", litShader, Rock, 0.2f);
            var groundShader = Shader.Find("Downstream/Greybox Ground") ?? litShader;
            var bedMat = EnsureMaterial(TrackDir + "/GreyboxBed.mat", groundShader, Stones, 0.35f);
            if (bedMat.HasProperty("_StoneColor")) { bedMat.SetColor("_StoneColor", Stones); bedMat.SetColor("_GrassColor", Sand); bedMat.SetColor("_SandColor", new Color(0.80f, 0.74f, 0.58f)); }
            var waterMat = EnsureMaterial(TrackDir + "/GreyboxWater.mat", waterShader, Color.white, 0.94f);
            ConfigureWaterMaterial(waterMat);
            var hullMat = EnsureMaterial(PrefabDir + "/GreyboxHull.mat", litShader, Tomato, 0.32f);
            var pilotMat = EnsureMaterial(PrefabDir + "/GreyboxPilot.mat", litShader, Sunflower, 0.3f);
            var itemMat = EnsureMaterial(TrackDir + "/GreyboxItem.mat", litShader, SkyBlue, 0.35f);
            // The old greybox bank material stays on disk for anyone still referencing it.
            EnsureMaterial(TrackDir + "/GreyboxBank.mat", litShader, Grass, 0.22f);

            var boatPrefab = CreateBoatPrefab(hullMat, pilotMat);
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
            waterMesh.BuildBanks(river, grassMat, earthMat, rockMat);
            SaveGeneratedMeshes(water.transform, TrackDir);
            var gizmos = new SerializedObject(water.GetComponent<RiverFieldGizmos>());
            gizmos.FindProperty("_river").objectReferenceValue = river;
            gizmos.ApplyModifiedPropertiesWithoutUndo();

            var itemView = new GameObject("Items", typeof(ItemWorldView));
            var itemSo = new SerializedObject(itemView.GetComponent<ItemWorldView>());
            itemSo.FindProperty("_material").objectReferenceValue = itemMat;
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

        /// <summary>Rendering rules: one warm key sun from the upper left, a cool sky fill, teal-tinted shadow, light haze.</summary>
        private static Light CreateLighting(Material sky)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.80f);
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f; // the sky fill keeps shadows cool, never grey-black
            sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.68f, 0.90f) * 0.8f;
            RenderSettings.ambientEquatorColor = new Color(0.56f, 0.64f, 0.62f) * 0.7f;
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.33f, 0.27f) * 0.55f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.81f, 0.90f);
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 750f;
            return sun;
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
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.28f);
            var colour = GetOrAdd<ColorAdjustments>(profile);
            colour.postExposure.Override(0f);
            colour.contrast.Override(5f);
            colour.saturation.Override(6f);
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

        /// <summary>A scattered sky: small sun, cool tint, no gradient wash.</summary>
        private static Material EnsureSkyMaterial()
        {
            string path = SettingsDir + "/DownstreamSky.mat";
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetFloat("_SunSize", 0.035f);
            mat.SetFloat("_SunSizeConvergence", 5f);
            mat.SetFloat("_AtmosphereThickness", 0.85f);
            mat.SetColor("_SkyTint", new Color(0.52f, 0.64f, 0.82f));
            mat.SetColor("_GroundColor", new Color(0.42f, 0.50f, 0.42f));
            mat.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static BoatView CreateBoatPrefab(Material hullMat, Material pilotMat)
        {
            string path = PrefabDir + "/GreyboxBoat.prefab";
            var hullMesh = EnsureMesh(PrefabDir + "/GreyboxHullMesh.asset", () => BlockMeshes.BevelledBox(new Vector3(2f, 0.6f, 4f), 0.12f, "GreyboxHull"));
            var pilotMesh = EnsureMesh(PrefabDir + "/GreyboxPilotMesh.asset", () => BlockMeshes.BevelledBox(new Vector3(0.7f, 0.9f, 0.7f), 0.1f, "GreyboxPilot"));

            var root = new GameObject("GreyboxBoat");
            var hull = new GameObject("Hull", typeof(MeshFilter), typeof(MeshRenderer));
            hull.transform.SetParent(root.transform, false);
            hull.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            hull.GetComponent<MeshFilter>().sharedMesh = hullMesh;
            hull.GetComponent<MeshRenderer>().sharedMaterial = hullMat;
            var pilot = new GameObject("Pilot", typeof(MeshFilter), typeof(MeshRenderer));
            pilot.transform.SetParent(root.transform, false);
            pilot.transform.localPosition = new Vector3(0f, 0.8f, -0.6f);
            pilot.GetComponent<MeshFilter>().sharedMesh = pilotMesh;
            pilot.GetComponent<MeshRenderer>().sharedMaterial = pilotMat;
            // The sim does all collision through one query proxy; views carry no colliders.
            root.AddComponent<BoatView>();

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
            camera.farClipPlane = 2000f;
            camera.nearClipPlane = 0.2f;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // MSAA comes from the pipeline asset
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
            // The water refracts the opaque texture and fades against the depth texture.
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 160f;
            pipeline.shadowCascadeCount = 3;
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
            mat.SetFloat("_FoamFlowStart", 4f);
            mat.SetFloat("_FoamFlowFull", 7f);
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
