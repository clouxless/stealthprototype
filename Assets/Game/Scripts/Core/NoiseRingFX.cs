using UnityEngine;

namespace StealthProto
{
    /// <summary>Expanding, fading placeholder ring showing a noise radius.</summary>
    public class NoiseRingFX : MonoBehaviour
    {
        const int Segments = 40;
        const float Duration = 0.45f;
        LineRenderer lr;
        float radius;
        float age;
        Color color;

        public void Init(float r, Color c)
        {
            radius = r;
            color = c;
            lr = FX.CreateLine(transform, "PH_FX_NoiseRingLine", c, 0.05f);
            lr.loop = true;
            lr.positionCount = Segments;
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

        void Refresh(float k)
        {
            float r = Mathf.Lerp(0.2f, radius, Mathf.Sqrt(k));
            Vector3 p = transform.position;
            for (int i = 0; i < Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                lr.SetPosition(i, p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            Color c = color;
            c.a *= 1f - k;
            lr.startColor = lr.endColor = c;
        }
    }
}
