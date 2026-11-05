using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// The game's look: the world renders in black and white (a global URP volume with saturation
    /// at -100). The debug HUD is drawn after post-processing, so it keeps its colours.
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
            color.saturation.Override(-100f);
            color.contrast.Override(15f); // a little punch, so light pools read crisply

            EditorUtility.SetDirty(color);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
