using System.Collections.Generic;
using UnityEditor;

namespace Downstream.Editor.Provenance
{
    /// <summary>Which assets need a provenance record, and which ones block a release build.</summary>
    public static class ProvenanceRules
    {
        /// <summary>Only authored content needs a record: art, audio, text. Code, settings and packages do not.</summary>
        public static bool NeedsRecord(string path)
        {
            if (!path.StartsWith("Assets/")) return false;
            if (path.StartsWith("Assets/Downstream/Scripts/") || path.StartsWith("Assets/Downstream/Tests/") || path.StartsWith("Assets/Downstream/Settings/")) return false;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".png": case ".psd": case ".tga": case ".exr": case ".jpg": case ".tif":
                case ".fbx": case ".blend": case ".obj":
                case ".wav": case ".ogg": case ".mp3": case ".bank":
                case ".ttf": case ".otf":
                case ".mat": case ".prefab": case ".anim": case ".controller":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Returns every asset reachable from the given roots that has no record or is not approved.</summary>
        public static List<string> FindBlockingAssets(ProvenanceDatabase db, IEnumerable<string> rootPaths)
        {
            var blocking = new List<string>();
            var seen = new HashSet<string>();
            foreach (var root in rootPaths)
            {
                foreach (var dep in AssetDatabase.GetDependencies(root, true))
                {
                    if (!seen.Add(dep) || !NeedsRecord(dep)) continue;
                    var record = db != null ? db.Find(AssetDatabase.AssetPathToGUID(dep)) : null;
                    if (record == null || record.Status != ProvenanceStatus.Approved || string.IsNullOrEmpty(record.Approver))
                        blocking.Add(dep);
                }
            }
            blocking.Sort(System.StringComparer.Ordinal);
            return blocking;
        }

        public static ProvenanceDatabase LoadDatabase() =>
            AssetDatabase.LoadAssetAtPath<ProvenanceDatabase>(ProvenanceDatabase.DefaultPath);
    }
}
