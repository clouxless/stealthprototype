using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// Makes sure a URP asset is active. Projects not made from the Universal 3D template have URP
    /// installed but no asset assigned, so every URP material renders magenta. The scene builders
    /// call this first; it can also be run on its own from the menu.
    /// </summary>
    public static class UrpSetup
    {
        const string Folder = "Assets/Settings";
        const string AssetPath = Folder + "/PH_URP_Asset.asset";
        const string RendererPath = Folder + "/PH_URP_Renderer.asset";

        [MenuItem("Stealth Prototype/Set Up URP", priority = 10)]
        static void Menu()
        {
            bool changed = EnsureActive();
            EditorUtility.DisplayDialog("URP", changed
                ? "Created and assigned a URP asset (Assets/Settings/PH_URP_Asset). Rebuild your scene."
                : "A URP asset is already active. Nothing to do.", "OK");
        }

        /// <summary>Returns true if it had to create or assign an asset.</summary>
        public static bool EnsureActive()
        {
            bool qualityHasPipeline = QualitySettings.renderPipeline != null;
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset && (!qualityHasPipeline || QualitySettings.renderPipeline is UniversalRenderPipelineAsset))
                return false;

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Settings");

                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, AssetPath);

                // Point lights need shadows for light pools to be blocked by geometry.
                var so = new SerializedObject(asset);
                SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // per pixel
                SetBool(so, "m_AdditionalLightShadowsSupported", true);
                SetBool(so, "m_MainLightShadowsSupported", true);
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            // A quality level with its own pipeline overrides the default, so point those at ours too.
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != asset)
                    QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PH] Assigned {AssetPath} as the active render pipeline.");
            return true;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }
    }
}
