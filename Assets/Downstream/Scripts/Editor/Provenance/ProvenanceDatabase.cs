using System;
using System.Collections.Generic;
using UnityEngine;

namespace Downstream.Editor.Provenance
{
    public enum ProvenanceStatus
    {
        Placeholder,
        Pending,
        Approved,
    }

    [Serializable]
    public sealed class ProvenanceRecord
    {
        public string Guid;
        public string AssetPath;
        public string Author;
        public string Tools;
        public bool UsedGenerativeAI;
        public string Approver;
        public string ApprovedOn;
        public ProvenanceStatus Status = ProvenanceStatus.Pending;
    }

    /// <summary>
    /// Who made each shipped asset, with what tools, whether generative AI was involved, and who
    /// approved it (design doc: "Asset provenance"). Keyed by asset GUID so renames and moves keep
    /// their record. One diffable asset, reviewed in pull requests like code. The same data produces
    /// the Steam AI-content disclosure list.
    /// </summary>
    [CreateAssetMenu(menuName = "Downstream/Provenance Database", fileName = "ProvenanceDatabase")]
    public sealed class ProvenanceDatabase : ScriptableObject
    {
        public const string DefaultPath = "Assets/Downstream/Settings/ProvenanceDatabase.asset";

        [SerializeField] private List<ProvenanceRecord> _records = new List<ProvenanceRecord>();

        public IReadOnlyList<ProvenanceRecord> Records => _records;

        public ProvenanceRecord Find(string guid)
        {
            for (int i = 0; i < _records.Count; i++)
                if (_records[i].Guid == guid) return _records[i];
            return null;
        }

        public ProvenanceRecord GetOrAdd(string guid, string path)
        {
            var r = Find(guid);
            if (r != null) return r;
            r = new ProvenanceRecord { Guid = guid, AssetPath = path, Status = ProvenanceStatus.Pending };
            _records.Add(r);
            return r;
        }
    }
}
