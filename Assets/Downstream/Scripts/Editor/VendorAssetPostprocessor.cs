using System.IO;
using UnityEditor;
using UnityEngine;

namespace Downstream.Editor
{
    /// <summary>
    /// Import rules for the CC0 vendor packs under Assets/Downstream/Art/Vendor so they drop straight into
    /// the greybox look: URP Lit with the pack's textures, matte, foliage alpha-clipped and two-sided,
    /// and the sky HDRI imported as a readable lat-long map the scene builder can measure.
    /// </summary>
    public sealed class VendorAssetPostprocessor : AssetPostprocessor
    {
        private const string VendorRoot = "Assets/Downstream/Art/Vendor/";

        private bool IsVendor => assetPath.Replace('\\', '/').StartsWith(VendorRoot);

        // Bump to reimport every vendor asset when these rules change.
        public override uint GetVersion() => 5;

        private static bool IsFoliageName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("leaf") || n.Contains("leaves") || n.Contains("grass") || n.Contains("flower")
                || n.Contains("petal") || n.Contains("clover") || n.Contains("fern") || n.Contains("plant")
                || n.Contains("mushroom");
        }

        private void OnPreprocessModel()
        {
            if (!IsVendor) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.generateSecondaryUV = false;
        }

        private void OnPreprocessTexture()
        {
            if (!IsVendor) return;
            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            if (assetPath.EndsWith(".hdr"))
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = false;
                importer.isReadable = true;
                // Mips with trilinear filtering: the zenith is minified and would sparkle without them; at the
                // horizon the panorama is sampled near 1:1, so mip 0 is what shows there.
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 16;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            bool normal = name.EndsWith("_Normal") || name.Contains("_nor_gl");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaIsTransparency = !normal && IsFoliageName(name);
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.Compressed;
            if (assetPath.Contains("/PolyHaven/")) importer.wrapMode = TextureWrapMode.Repeat;
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsVendor) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null && material.shader != lit) material.shader = lit;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            // The pack names its materials after its textures; fall back to that when the importer found none.
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D " + material.name, new[] { VendorRoot.TrimEnd('/') });
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != material.name) continue;
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null) { material.SetTexture("_BaseMap", tex); break; }
                }
            }

            // The kit ships the twisted trees and plain bushes in autumn red; the valley is summer green.
            if (material.name == "Leaves_TwistedTree" && material.HasProperty("_BaseMap"))
            {
                var green = AssetDatabase.LoadAssetAtPath<Texture2D>(VendorRoot + "StylizedNatureMegaKit/Textures/Leaves_NormalTree_C.png");
                if (green != null) material.SetTexture("_BaseMap", green);
            }

            if (IsFoliageName(material.name))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.45f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
        }
    }
}
