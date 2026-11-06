using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// The player's light tier, decided only by the light reaching the floor under their feet.
    /// Guards use SeenTier: the same, except that crawling counts as one tier darker
    /// (Lit reads as Dim, Dim as Dark). The character's tint shows SeenTier, so it always
    /// says what the guards get.
    /// </summary>
    public class LightExposure : MonoBehaviour
    {
        public float sampleInterval = 0.05f;
        [Tooltip("How far below the feet to look for the floor (long enough to reach the ground when hanging).")]
        public float floorProbeDistance = 12f;

        public LightTier Tier { get; private set; } = LightTier.Dark;
        /// <summary>The floor point last sampled.</summary>
        public Vector3 SamplePoint { get; private set; }
        /// <summary>The tier guards see you at: Tier, one darker while crawling. A muzzle flash is always Lit.</summary>
        public LightTier SeenTier
        {
            get
            {
                if (Time.time < flashUntil) return LightTier.Lit;
                if (motor != null && motor.IsProne && Tier > LightTier.Dark) return Tier - 1;
                return Tier;
            }
        }
        public bool IsHidden => SeenTier == LightTier.Dark;
        PlayerMotor motor;

        void Awake() => motor = GetComponent<PlayerMotor>();

        /// <summary>
        /// When set, the tier is sampled here instead of under the feet (e.g. the ledge surface
        /// while hanging, if that option is chosen). Cleared by whoever set it.
        /// </summary>
        public Vector3? SampleOverride { get; set; }

        float timer;
        float flashUntil;

        /// <summary>Muzzle flash: counts as Lit for a moment, wherever you are.</summary>
        public void Flash(float seconds)
        {
            flashUntil = Mathf.Max(flashUntil, Time.time + seconds);
            Tier = LightTier.Lit;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = sampleInterval;

            if (SampleOverride.HasValue)
            {
                SamplePoint = SampleOverride.Value;
            }
            else
            {
                Vector3 origin = transform.position + Vector3.up * 0.3f;
                if (!Occlusion.Raycast(origin, Vector3.down, floorProbeDistance, transform, null, out RaycastHit hit))
                    return; // nothing below (mid-air): keep the last tier
                SamplePoint = hit.point + hit.normal * 0.05f;
            }
            Tier = Time.time < flashUntil ? LightTier.Lit : StealthLight.BrightestAt(SamplePoint, transform);
        }
    }
}
