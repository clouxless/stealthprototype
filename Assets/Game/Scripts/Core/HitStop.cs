using UnityEngine;

namespace StealthProto
{
    /// <summary>Brief global freeze on impactful hits. A big part of top-down action game feel.</summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop instance;
        float resumeAt;

        public static void Do(float seconds)
        {
            if (seconds <= 0f) return;
            if (instance == null)
            {
                var go = new GameObject("PH_HitStop");
                instance = go.AddComponent<HitStop>();
            }
            instance.resumeAt = Mathf.Max(instance.resumeAt, Time.unscaledTime + seconds);
            Time.timeScale = 0f;
        }

        void Update()
        {
            if (Time.timeScale == 0f && Time.unscaledTime >= resumeAt) Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            if (instance == this) Time.timeScale = 1f;
        }
    }
}
