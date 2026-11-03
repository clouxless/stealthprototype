using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Marks a Light as one that can reveal the player. Exposure is an approximation of the
    /// rendered light (range falloff, spot cone, hard occlusion), tuned for readability rather
    /// than physical accuracy.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class StealthLight : MonoBehaviour
    {
        public static readonly List<StealthLight> All = new List<StealthLight>();

        [Tooltip("Multiplier on this light's contribution to player exposure.")]
        public float weight = 1f;

        [Tooltip("Colliders under this transform never block this light (e.g. the guard carrying a flashlight).")]
        public Transform ignoreRoot;

        Light lightComponent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Awake() => lightComponent = GetComponent<Light>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>0..weight contribution at a world point. target's colliders are ignored for occlusion.</summary>
        public float ExposureAt(Vector3 point, Transform target)
        {
            if (lightComponent == null || !lightComponent.enabled || lightComponent.intensity <= 0f) return 0f;

            Vector3 lightPos = transform.position;
            float attenuation;
            switch (lightComponent.type)
            {
                case LightType.Directional:
                    attenuation = 1f;
                    lightPos = point - transform.forward * 60f;
                    break;
                case LightType.Point:
                    attenuation = RangeAttenuation(Vector3.Distance(lightPos, point));
                    break;
                case LightType.Spot:
                    attenuation = RangeAttenuation(Vector3.Distance(lightPos, point)) * SpotAttenuation(point - lightPos);
                    break;
                default:
                    return 0f;
            }

            if (attenuation <= 0f) return 0f;
            if (Occlusion.Blocked(lightPos, point, target, ignoreRoot)) return 0f;
            return attenuation * weight;
        }

        float RangeAttenuation(float distance)
        {
            float range = lightComponent.range;
            if (distance >= range) return 0f;
            float x = distance / range;
            return Mathf.Clamp01(1f - x * x);
        }

        float SpotAttenuation(Vector3 toPoint)
        {
            float angle = Vector3.Angle(transform.forward, toPoint);
            float outer = lightComponent.spotAngle * 0.5f;
            float inner = Mathf.Min(lightComponent.innerSpotAngle * 0.5f, outer - 0.01f);
            if (angle >= outer) return 0f;
            if (angle <= inner) return 1f;
            return 1f - (angle - inner) / (outer - inner);
        }
    }
}
