using UnityEngine;

namespace StealthProto
{
    /// <summary>How lit the player is, 0 (pitch dark) to 1 (fully lit). Guards use this to see.</summary>
    public class LightExposure : MonoBehaviour
    {
        [Tooltip("Local-space points sampled on the body (feet, chest, head).")]
        public Vector3[] samplePoints = { new Vector3(0f, 0.3f, 0f), new Vector3(0f, 1.0f, 0f), new Vector3(0f, 1.6f, 0f) };
        [Range(0f, 1f)] public float ambient = 0.06f;
        public float sampleInterval = 0.05f;
        public float smoothing = 14f;
        [Tooltip("Below this the player counts as hidden in shadow (HUD + tint).")]
        public float hiddenThreshold = 0.3f;

        public float Exposure { get; private set; }
        public float RawExposure { get; private set; }
        public bool IsHidden => Exposure < hiddenThreshold;

        float timer;

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = sampleInterval;
                RawExposure = Sample();
            }
            Exposure = Mathf.Lerp(Exposure, RawExposure, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        }

        public float Sample()
        {
            if (samplePoints.Length == 0) return 1f;
            float total = 0f;
            var lights = StealthLight.All;
            foreach (var local in samplePoints)
            {
                Vector3 p = transform.TransformPoint(local);
                float e = ambient;
                for (int i = 0; i < lights.Count; i++) e += lights[i].ExposureAt(p, transform);
                total += Mathf.Clamp01(e);
            }
            return total / samplePoints.Length;
        }
    }
}
