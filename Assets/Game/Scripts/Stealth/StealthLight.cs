using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Marks a Light as one that can reveal the player, and gives it crisp, readable tiers.
    ///
    /// Spot lights get a stepped cookie: a bright core (Lit) inside a dimmer ring (Dim), with hard
    /// edges, so what you see on the floor matches the tier exactly. Point lights are Lit within
    /// litFraction of their range and Dim out to their range. Geometry between the light and the
    /// point blocks it (Dark).
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class StealthLight : MonoBehaviour
    {
        public static readonly List<StealthLight> All = new List<StealthLight>();

        [Tooltip("Fraction of the spot cone's radius (or point light's range) that is Lit. The rest is Dim.")]
        [Range(0.1f, 1f)] public float litFraction = 0.6f;
        [Tooltip("Spot only: beyond this fraction of the range the whole beam is at most Dim (the far end of a flashlight).")]
        [Range(0.1f, 1f)] public float litRangeFraction = 0.75f;
        [Tooltip("Spot only: brightness of the Dim ring relative to the core.")]
        [Range(0.05f, 1f)] public float dimBrightness = 0.3f;
        [Tooltip("Spot only: generate the stepped cookie and harden the cone edge.")]
        public bool steppedCookie = true;
        [Tooltip("Directional lights only: the tier they give where not blocked.")]
        public LightTier directionalTier = LightTier.Dim;

        [Tooltip("Colliders under this transform never block this light (e.g. the guard carrying a flashlight).")]
        public Transform ignoreRoot;

        Light lightComponent;
        static readonly Dictionary<int, Texture2D> CookieCache = new Dictionary<int, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            CookieCache.Clear();
        }

        void Awake()
        {
            lightComponent = GetComponent<Light>();
            if (steppedCookie && lightComponent.type == LightType.Spot)
            {
                lightComponent.cookie = SteppedCookie(litFraction, dimBrightness);
                lightComponent.innerSpotAngle = Mathf.Max(1f, lightComponent.spotAngle - 2f); // hard outer edge
            }
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>The tier this light gives a world point. target's colliders don't block it.</summary>
        public LightTier TierAt(Vector3 point, Transform target)
        {
            if (lightComponent == null || !lightComponent.enabled || lightComponent.intensity <= 0f) return LightTier.Dark;

            Vector3 lightPos = transform.position;
            LightTier tier;
            switch (lightComponent.type)
            {
                case LightType.Directional:
                    tier = directionalTier;
                    lightPos = point - transform.forward * 60f;
                    break;

                case LightType.Point:
                {
                    float d = Vector3.Distance(lightPos, point);
                    if (d > lightComponent.range) return LightTier.Dark;
                    tier = d <= lightComponent.range * litFraction ? LightTier.Lit : LightTier.Dim;
                    break;
                }

                case LightType.Spot:
                {
                    Vector3 toPoint = point - lightPos;
                    float d = toPoint.magnitude;
                    if (d > lightComponent.range) return LightTier.Dark;
                    float halfAngle = lightComponent.spotAngle * 0.5f;
                    float angle = Vector3.Angle(transform.forward, toPoint);
                    if (angle > halfAngle) return LightTier.Dark;
                    // Radius across the cone (0 = axis, 1 = edge). tan matches how a cookie projects.
                    float r = Mathf.Tan(angle * Mathf.Deg2Rad) / Mathf.Tan(halfAngle * Mathf.Deg2Rad);
                    bool core = steppedCookie ? r <= litFraction : true;
                    tier = core && d <= lightComponent.range * litRangeFraction ? LightTier.Lit : LightTier.Dim;
                    break;
                }

                default:
                    return LightTier.Dark;
            }

            if (Occlusion.Blocked(lightPos, point, target, ignoreRoot)) return LightTier.Dark;
            return tier;
        }

        /// <summary>The brightest tier any StealthLight gives this point.</summary>
        public static LightTier BrightestAt(Vector3 point, Transform target)
        {
            LightTier best = LightTier.Dark;
            for (int i = 0; i < All.Count && best != LightTier.Lit; i++)
            {
                LightTier t = All[i].TierAt(point, target);
                if (t > best) best = t;
            }
            return best;
        }

        /// <summary>Procedural placeholder cookie: a full-bright core, a dim ring, hard edges.</summary>
        static Texture2D SteppedCookie(float coreFraction, float dim)
        {
            int key = Mathf.RoundToInt(coreFraction * 100f) * 1000 + Mathf.RoundToInt(dim * 100f);
            if (CookieCache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PH_SteppedCookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float v = r <= coreFraction ? 1f : r <= 0.97f ? dim : 0f;
                    pixels[y * size + x] = new Color(v, v, v, v);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            CookieCache[key] = tex;
            return tex;
        }
    }
}
