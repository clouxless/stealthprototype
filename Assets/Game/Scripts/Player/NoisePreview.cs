using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Shows where a noise would be heard before you make it (the knock preview): a faint filled
    /// circle of its radius on the floor and a brighter outline, with sealed-off spaces cut out.
    /// A noise inside a vent is previewed at each of the vent's openings. Placeholder look ([PH]).
    /// </summary>
    public class NoisePreview : MonoBehaviour
    {
        public Color fillColor = new Color(0.6f, 0.9f, 1f, 0.12f);
        public Color outlineColor = new Color(0.6f, 0.9f, 1f, 0.6f);
        public float outlineWidth = 0.08f;
        [Tooltip("Rebuild the shape when the noise moves this far.")]
        public float rebuildDistance = 0.1f;

        public bool Showing { get; private set; }

        MeshFilter mf;
        Vector3 lastPosition;
        float lastRadius = -1f;
        readonly List<Vector3> origins = new List<Vector3>();

        void Awake()
        {
            // A root object at the world origin: the mesh is built in world space.
            mf = NoiseShape.CreateRenderer(null, "PH_FX_NoisePreview");
            mf.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (mf == null) return;
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(mf.gameObject);
        }

        void OnDisable() => Hide();

        /// <summary>Show the reach of a noise made at this point (call every frame while previewing).</summary>
        public void Show(Vector3 position, float radius)
        {
            if (mf == null) return;
            if (!Showing || radius != lastRadius || (position - lastPosition).sqrMagnitude > rebuildDistance * rebuildDistance)
            {
                lastPosition = position;
                lastRadius = radius;
                SoundDuct.ResolveOrigins(position, origins);
                NoiseShape.Begin();
                foreach (var o in origins)
                {
                    Vector3 floor = o + Vector3.up * 0.04f;
                    int space = SoundGrid.SpaceAt(o);
                    NoiseShape.Disc(floor, radius, space, fillColor);
                    NoiseShape.Ring(floor + Vector3.up * 0.01f, radius, outlineWidth, space, outlineColor);
                }
                NoiseShape.End(mf.sharedMesh);
            }
            Showing = true;
            mf.gameObject.SetActive(true);
        }

        public void Hide()
        {
            Showing = false;
            if (mf != null) mf.gameObject.SetActive(false);
        }
    }
}
