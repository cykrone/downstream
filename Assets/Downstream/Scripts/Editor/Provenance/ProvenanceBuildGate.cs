using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Downstream.Editor.Provenance
{
    /// <summary>
    /// Fails any non-development player build that would ship an asset without an approved
    /// provenance record, so a forgotten placeholder can never reach players (the design doc's
    /// Clair Obscur lesson). Development builds only warn. Addressables content builds run outside
    /// the player build, so CI also runs <see cref="CheckFromCommandLine"/> before them.
    /// </summary>
    public sealed class ProvenanceBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            bool development = (report.summary.options & BuildOptions.Development) != 0;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var blocking = ProvenanceRules.FindBlockingAssets(ProvenanceRules.LoadDatabase(), scenes);
            if (blocking.Count == 0) return;

            string message = $"{blocking.Count} asset(s) lack an approved provenance record:\n  " + string.Join("\n  ", blocking.Take(50));
            if (development)
            {
                Debug.LogWarning("[Provenance] " + message);
                return;
            }
            throw new BuildFailedException("[Provenance] Release build blocked. " + message);
        }

        /// <summary>CI entry point: -executeMethod Downstream.Editor.Provenance.ProvenanceBuildGate.CheckFromCommandLine</summary>
        public static void CheckFromCommandLine()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var blocking = ProvenanceRules.FindBlockingAssets(ProvenanceRules.LoadDatabase(), scenes);
            foreach (var path in blocking) Debug.LogError("[Provenance] Not approved: " + path);
            EditorApplication.Exit(blocking.Count == 0 ? 0 : 1);
        }
    }
}
