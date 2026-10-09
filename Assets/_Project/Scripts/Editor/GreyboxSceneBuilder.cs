using Downstream.Boat;
using Downstream.Cameras;
using Downstream.Input;
using Downstream.Items;
using Downstream.Race;
using Downstream.UI;
using Downstream.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Downstream.Editor
{
    /// <summary>
    /// Builds the first playable greybox from code, so the repository needs no hand-written scene
    /// YAML: a URP pipeline asset, a 1.5 km test river, a boat and camera prefab, and a scene that
    /// races 8 boats (you plus 7 AI). Run it once after opening the project:
    /// Downstream > Create Greybox Race Scene, then press Play.
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        private const string Root = "Assets/_Project";
        private const string SettingsDir = Root + "/Settings";
        private const string TrackDir = Root + "/Tracks/Greybox";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SceneDir = Root + "/Scenes";

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
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            var waterShader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ?? litShader;
            var bankMat = LoadOrCreateMaterial(TrackDir + "/GreyboxBank.mat", litShader, new Color(0.47f, 0.62f, 0.35f));
            var waterMat = LoadOrCreateMaterial(TrackDir + "/GreyboxWater.mat", waterShader, Color.white);
            var hullMat = LoadOrCreateMaterial(PrefabDir + "/GreyboxHull.mat", litShader, new Color(0.95f, 0.45f, 0.25f));

            var boatPrefab = CreateBoatPrefab(hullMat);
            var cameraPrefab = CreateCameraPrefab();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f); // warm key from the upper left (design doc)

            var water = new GameObject("Greybox Water", typeof(MeshFilter), typeof(MeshRenderer), typeof(GreyboxWaterMesh), typeof(RiverFieldGizmos));
            water.GetComponent<MeshRenderer>().sharedMaterial = waterMat;
            var waterMesh = water.GetComponent<GreyboxWaterMesh>();
            waterMesh.BuildBanks(river, bankMat);
            var gizmos = new SerializedObject(water.GetComponent<RiverFieldGizmos>());
            gizmos.FindProperty("_river").objectReferenceValue = river;
            gizmos.ApplyModifiedPropertiesWithoutUndo();

            var itemMat = LoadOrCreateMaterial(TrackDir + "/GreyboxItem.mat", litShader, Color.white);
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

        private static BoatView CreateBoatPrefab(Material hullMat)
        {
            string path = PrefabDir + "/GreyboxBoat.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<BoatView>(path);
            if (existing != null) return existing;

            var root = new GameObject("GreyboxBoat");
            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(root.transform, false);
            hull.transform.localScale = new Vector3(2f, 0.6f, 4f);
            hull.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Pilot";
            cabin.transform.SetParent(root.transform, false);
            cabin.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            cabin.transform.localPosition = new Vector3(0f, 0.8f, -0.6f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = hullMat;
            // The sim does all collision through one query proxy; views carry no colliders.
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            root.AddComponent<BoatView>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<BoatView>();
        }

        private static ChaseCamera CreateCameraPrefab()
        {
            string path = PrefabDir + "/ChaseCamera.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<ChaseCamera>(path);
            if (existing != null) return existing;

            var go = new GameObject("ChaseCamera", typeof(Camera), typeof(AudioListener), typeof(ChaseCamera));
            go.GetComponent<Camera>().fieldOfView = 70f;
            go.GetComponent<Camera>().farClipPlane = 2000f;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ChaseCamera>();
        }

        /// <summary>Creates and assigns a URP pipeline asset if the project has none yet.</summary>
        private static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset) return;

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
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            Debug.Log("[Downstream] Created and assigned a URP pipeline asset. Set the renderer to Forward+ in " + rendererPath + ".");
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

        private static Material LoadOrCreateMaterial(string path, Shader shader, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
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
