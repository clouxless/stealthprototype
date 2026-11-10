using UnityEngine;
using UnityEngine.Rendering;

namespace StealthProto
{
    /// <summary>
    /// Placeholder effects made of flat unlit lines. Everything spawned here is named PH_FX_*
    /// so it is never mistaken for final VFX (see ASSETS.md).
    /// </summary>
    public static class FX
    {
        static Material lineMaterial;

        public static Material LineMaterial
        {
            get
            {
                if (lineMaterial == null)
                {
                    lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "PH_FX_LineMaterial" };
                }
                return lineMaterial;
            }
        }

        public static LineRenderer CreateLine(Transform parent, string name, Color color, float width)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.material = LineMaterial;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            return lr;
        }

        public static void Line(Vector3 a, Vector3 b, Color color, float width, float duration)
        {
            var lr = CreateLine(null, "PH_FX_Line", color, width);
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            Object.Destroy(lr.gameObject, duration);
        }

        public static void Arc(Vector3 center, Vector3 forward, float radius, float arcDegrees, Color color, float duration)
        {
            const int steps = 12;
            var lr = CreateLine(null, "PH_FX_Arc", color, 0.08f);
            lr.positionCount = steps + 1;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(-arcDegrees * 0.5f, arcDegrees * 0.5f, i / (float)steps);
                lr.SetPosition(i, center + Quaternion.Euler(0f, a, 0f) * forward * radius);
            }
            Object.Destroy(lr.gameObject, duration);
        }

        /// <summary>An expanding ring for a noise, clipped to the noise's SoundGrid space.</summary>
        public static void NoiseRing(Vector3 position, float radius, float loudness, int space = SoundGrid.Unknown)
        {
            var go = new GameObject("PH_FX_NoiseRing"); // stays at the world origin: the ring mesh is in world space
            var ring = go.AddComponent<NoiseRingFX>();
            ring.Init(position, radius, space, new Color(0.6f, 0.9f, 1f, Mathf.Lerp(0.15f, 0.5f, loudness)));
        }
    }
}
