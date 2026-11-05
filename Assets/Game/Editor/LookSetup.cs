using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// The game's look: the world renders in black and white, except reds and ambers, which are kept
    /// for guard state (suspicious amber, alert red). A global URP volume with a Hue vs Saturation
    /// curve does it: zero saturation for every hue outside the red-amber band. So nothing else in
    /// the world should be red or amber (lights and materials are neutral). The debug HUD is drawn
    /// after post-processing, so it keeps its colours.
    /// Scene builders call Apply after creating the camera.
    /// </summary>
    public static class LookSetup
    {
        const string Folder = "Assets/Game/Rendering";
        const string ProfilePath = Folder + "/BlackAndWhite.asset";

        public static void Apply(Camera cam)
        {
            var profile = EnsureProfile();
            var go = new GameObject("GlobalVolume_BlackAndWhite");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            EnsureRendererPostProcessing();
        }

        /// <summary>
        /// A URP renderer with no post-process data silently skips all post-processing, so the
        /// black-and-white volume does nothing. Give the active renderer the package's default data.
        /// </summary>
        static void EnsureRendererPostProcessing()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) return;
            var list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            if (list == null) return;
            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null) continue;
                var so = new SerializedObject(data);
                var post = so.FindProperty("postProcessData");
                if (post == null || post.objectReferenceValue != null) continue;
                var defaults = AssetDatabase.LoadAssetAtPath<Object>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (defaults == null)
                {
                    Debug.LogWarning("[PH] Couldn't find URP's default PostProcessData; enable Post-processing on your URP renderer by hand.");
                    return;
                }
                post.objectReferenceValue = defaults;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log($"[PH] Enabled post-processing on {data.name}.");
            }
        }

        static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game", "Rendering");
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            if (!profile.TryGet(out ColorAdjustments color))
            {
                color = profile.Add<ColorAdjustments>(true);
                color.name = "ColorAdjustments";
                AssetDatabase.AddObjectToAsset(color, profile);
            }
            color.active = true;
            color.saturation.overrideState = false; // the curve below does the desaturation
            color.contrast.Override(15f); // a little punch, so light pools read crisply

            if (!profile.TryGet(out ColorCurves curves))
            {
                curves = profile.Add<ColorCurves>(true);
                curves.name = "ColorCurves";
                AssetDatabase.AddObjectToAsset(curves, profile);
            }
            curves.active = true;
            // Hue 0..1 around the wheel; 0.5 leaves saturation alone, 0 removes it. Keep red through
            // amber (hue 0 to 0.1, and the reds wrapping round from 0.95), grey out everything else.
            var keys = new[]
            {
                new Keyframe(0.00f, 0.5f),
                new Keyframe(0.10f, 0.5f),
                new Keyframe(0.125f, 0f),
                new Keyframe(0.92f, 0f),
                new Keyframe(0.95f, 0.5f),
            };
            curves.hueVsSat.Override(new TextureCurve(keys, 0.5f, true, new Vector2(0f, 1f)));

            EditorUtility.SetDirty(curves);
            EditorUtility.SetDirty(color);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
