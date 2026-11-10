using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// Enforces ASSETS.md: lists placeholder usage, and blocks release (non-development) builds
    /// while any build scene still uses placeholders.
    /// </summary>
    public class PlaceholderAudit : IPreprocessBuildWithReport
    {
        const string PlaceholderFolder = "Assets/_Placeholder/";
        const string Prefix = "PH_";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var findings = AuditBuildScenes();
            if (findings.Count == 0) return;

            bool development = (report.summary.options & BuildOptions.Development) != 0;
            string message = $"[PH] {findings.Count} placeholder reference(s) in build scenes:\n" +
                             string.Join("\n", findings.Take(40));
            if (development)
            {
                Debug.LogWarning(message + "\nDevelopment build: allowed.");
                return;
            }
            throw new BuildFailedException(message + "\nRelease builds may not contain placeholders. See ASSETS.md.");
        }

        [MenuItem("Stealth Prototype/Audit Placeholders", priority = 20)]
        static void AuditMenu()
        {
            var findings = AuditBuildScenes();
            findings.AddRange(AuditOpenScene());
            if (findings.Count == 0)
            {
                Debug.Log("[PH] No placeholders found in build scenes or the open scene.");
                return;
            }
            Debug.LogWarning($"[PH] {findings.Count} placeholder reference(s):\n" + string.Join("\n", findings));
        }

        static bool IsPlaceholderPath(string path) =>
            path.StartsWith(PlaceholderFolder) || Path.GetFileName(path).StartsWith(Prefix);

        static List<string> AuditBuildScenes()
        {
            var findings = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled) continue;
                string path = scene.path;
                if (IsPlaceholderPath(path)) findings.Add($"{path}: the scene itself is a placeholder");

                foreach (var dep in AssetDatabase.GetDependencies(path, true))
                    if (dep != path && IsPlaceholderPath(dep)) findings.Add($"{path}: uses {dep}");

                // Scene-embedded objects (meshes, GameObjects) named PH_*. Scenes are text-serialised by default.
                if (File.Exists(path))
                {
                    int count = File.ReadLines(path).Count(l => l.TrimStart().StartsWith("m_Name: " + Prefix));
                    if (count > 0) findings.Add($"{path}: {count} object(s) named {Prefix}*");
                }
            }
            return findings;
        }

        static List<string> AuditOpenScene()
        {
            var findings = new List<string>();
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith(Prefix)) findings.Add($"{scene.name}: object {GetPath(t)}");
                    foreach (var r in t.GetComponents<Renderer>())
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            string mp = AssetDatabase.GetAssetPath(m);
                            if (!string.IsNullOrEmpty(mp) && IsPlaceholderPath(mp))
                                findings.Add($"{scene.name}: {GetPath(t)} uses {mp}");
                        }
                    }
                }
            }
            return findings;
        }

        static string GetPath(Transform t) => t.parent == null ? t.name : GetPath(t.parent) + "/" + t.name;
    }
}
