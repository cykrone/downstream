using UnityEditor;

namespace Downstream.Editor.Provenance
{
    /// <summary>Gives every newly imported content asset a Pending record so nothing slips in unrecorded.</summary>
    public sealed class ProvenanceStamper : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var db = ProvenanceRules.LoadDatabase();
            if (db == null) return;
            bool dirty = false;
            foreach (var path in imported)
            {
                if (!ProvenanceRules.NeedsRecord(path)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (db.Find(guid) != null) continue;
                db.GetOrAdd(guid, path);
                dirty = true;
            }
            for (int i = 0; i < moved.Length; i++)
            {
                var r = db.Find(AssetDatabase.AssetPathToGUID(moved[i]));
                if (r == null) continue;
                r.AssetPath = moved[i];
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(db);
        }
    }
}
