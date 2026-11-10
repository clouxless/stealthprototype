using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Expanding, fading placeholder ring showing a noise radius. It stops at walls that seal the
    /// noise in: only the parts of the circle in the noise's own space are drawn.
    /// </summary>
    public class NoiseRingFX : MonoBehaviour
    {
        const float Duration = 0.45f;
        const float Width = 0.08f;
        MeshFilter mf;
        Vector3 centre;
        float radius;
        int space;
        float age;
        Color color;

        public void Init(Vector3 c, float r, int s, Color col)
        {
            centre = c;
            radius = r;
            space = s;
            color = col;
            mf = NoiseShape.CreateRenderer(transform, "PH_FX_NoiseRingMesh");
            Refresh(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = age / Duration;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            Refresh(k);
        }

        void OnDestroy()
        {
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }

        void Refresh(float k)
        {
            float r = Mathf.Lerp(0.2f, radius, Mathf.Sqrt(k));
            Color c = color;
            c.a *= 1f - k;
            NoiseShape.Begin();
            NoiseShape.Ring(centre, r, Width, space, c, Mathf.Clamp(Mathf.CeilToInt(r * 6f), 16, 96));
            NoiseShape.End(mf.sharedMesh);
        }
    }
}
