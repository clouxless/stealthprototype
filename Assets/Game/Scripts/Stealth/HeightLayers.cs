using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Height as named layers (floor, platform, catwalk) instead of look-up/look-down angles.
    /// Guards see their own layer and the layer below; they look up only while searching.
    /// Put one in a scene to change the boundaries; without one, the defaults apply.
    /// </summary>
    public class HeightLayers : MonoBehaviour
    {
        [Tooltip("Feet heights (world Y) where each layer above the first starts, ascending.")]
        public float[] boundaries = { 0.75f, 2.75f };
        public string[] names = { "Floor", "Platform", "Catwalk" };

        static HeightLayers instance;
        static readonly float[] DefaultBoundaries = { 0.75f, 2.75f };
        static readonly string[] DefaultNames = { "Floor", "Platform", "Catwalk" };

        void OnEnable() => instance = this;
        void OnDisable()
        {
            if (instance == this) instance = null;
        }

        static float[] Boundaries => instance != null && instance.boundaries != null ? instance.boundaries : DefaultBoundaries;

        /// <summary>Layer index for a feet height: 0 = lowest.</summary>
        public static int LayerOf(float y)
        {
            var b = Boundaries;
            int layer = 0;
            for (int i = 0; i < b.Length; i++)
                if (y >= b[i]) layer = i + 1;
            return layer;
        }

        public static string NameOf(int layer)
        {
            var n = instance != null && instance.names != null ? instance.names : DefaultNames;
            return layer >= 0 && layer < n.Length ? n[layer] : $"Layer {layer}";
        }
    }
}
