using UnityEngine;

namespace StealthProto
{
    /// <summary>How fast a guard's awareness meter fills. Ordered slowest to fastest.</summary>
    public enum DetectRate { None = 0, VerySlow = 1, Slow = 2, Fast = 3, Instant = 4 }

    /// <summary>
    /// The guard vision model (design/guard-vision/guard-vision-spec.md). Three overlapping zones,
    /// measured with 3D distance from the eye and the horizontal angle off the guard's facing:
    ///   Sight cone (70 degrees, out to Far): A = Mid..Far, B = Near..Mid, C = within Near
    ///   Peripheral (within Mid, out to 90 degrees each side): D = Near..Mid, E = within Near
    ///   Noise (the whole Far circle, behind too): F = the rest
    /// Everything needs line of sight. The player's state picks a row of the rate table: light on
    /// the floor under their feet (Lit / Dim / Dark), movement (still, walking, dashing, crawling)
    /// and height relative to the guard (below, equal, above). Of the zones the player is in, the
    /// fastest rate counts. Above the guard is never seen unless the guard is searching (LookUp).
    /// </summary>
    public class GuardVision : MonoBehaviour
    {
        public Transform eye;
        public Transform target;

        [Header("Distances (named in the design; tune here)")]
        public float nearDistance = 3f;
        public float midDistance = 6f;
        [Tooltip("Far: the end of the sight cone and the edge of the noise zone.")]
        public float viewDistance = 13f;

        [Header("Angles")]
        [Tooltip("Half the sight cone (70 degrees across).")]
        public float halfAngle = 35f;
        [Tooltip("Peripheral zone: this far either side of facing, within Mid.")]
        public float peripheralHalfAngle = 90f;

        [Header("Meter fill times (seconds from empty to full)")]
        public float fastTime = 1f;
        public float slowTime = 4f;
        public float verySlowTime = 10f;

        [Header("Searchlights")]
        [Tooltip("Lights this guard operates (swivel spotlights). A player standing in one's Lit beam is spotted at once (zone S), wherever the guard's own eyes point.")]
        public StealthLight[] searchlights = new StealthLight[0];

        [Header("Player movement")]
        [Tooltip("Below this speed a standing player counts as still.")]
        public float stillSpeed = 0.2f;

        static readonly Vector3[] BodyPoints = { new Vector3(0f, 0.35f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 1.65f, 0f) };

        // ---------------------------------------------------------------- rate table
        // Zones in order A B C D E F. Letters: I Instant, F Fast, S Slow, V Very slow, N None.
        // Rows: Still, Walking, Dashing, Crawling. Lit has a second table for a player below the guard.
        enum Move { Still = 0, Walking = 1, Dashing = 2, Crawling = 3 }

        static readonly string[] LitSame  = { "FIISFS", "FIISFS", "IIIFIF", "SFFVSN" };
        static readonly string[] LitBelow = { "IIISFF", "IIISFF", "IIIFII", "FIIVSS" };
        static readonly string[] Dim      = { "SFFVFN", "SFFVFN", "FIISIN", "VSSNSN" };
        static readonly string[] Dark     = { "NVSNFN", "NSFNFN", "NFININ", "NNVNSN" };

        static DetectRate Parse(char c) => c switch
        {
            'I' => DetectRate.Instant,
            'F' => DetectRate.Fast,
            'S' => DetectRate.Slow,
            'V' => DetectRate.VerySlow,
            _ => DetectRate.None
        };

        /// <summary>The table's rate for one zone (0..5 = A..F).</summary>
        static DetectRate TableRate(LightTier light, Move move, bool below, int zone)
        {
            string[] rows = light == LightTier.Lit ? (below ? LitBelow : LitSame)
                          : light == LightTier.Dim ? Dim : Dark;
            return Parse(rows[(int)move][zone]);
        }

        // ---------------------------------------------------------------- state

        LightExposure targetExposure;
        Health targetHealth;
        PlayerMotor targetMotor;

        public int OwnLayer => HeightLayers.LayerOf(transform.position.y);

        /// <summary>Whether this guard can currently see things on a height layer: its own and any below; above only while LookUp.</summary>
        public bool CanSeeLayer(int layer) => layer <= OwnLayer || LookUp;

        /// <summary>Set by the brain while searching: the guard checks higher layers too.</summary>
        public bool LookUp { get; set; }
        /// <summary>Widens the sight cone (alert).</summary>
        public float AngleMultiplier { get; set; } = 1f;
        public float EffectiveHalfAngle => halfAngle * AngleMultiplier;

        /// <summary>This frame's meter rate for the target.</summary>
        public DetectRate Rate { get; private set; }
        /// <summary>Which zone set the rate: 'A'..'F', 'S' for one of this guard's searchlights, or '-' when unseen.</summary>
        public char Zone { get; private set; } = '-';
        /// <summary>1 while the target registers at all this frame, else 0.</summary>
        public float Visibility => Rate == DetectRate.None ? 0f : 1f;
        /// <summary>Seconds to fill the meter from empty at this frame's rate (0 = instant, infinity = never).</summary>
        public float FillTime => Rate switch
        {
            DetectRate.Instant => 0f,
            DetectRate.Fast => fastTime,
            DetectRate.Slow => slowTime,
            DetectRate.VerySlow => verySlowTime,
            _ => float.PositiveInfinity
        };
        /// <summary>0 at the eye, 1 at Far.</summary>
        public float NormalizedDistance { get; private set; } = 1f;
        public Vector3 LastSeenPosition { get; private set; }
        /// <summary>Centre of the target's body (lower when prone).</summary>
        public Vector3 TargetCenter => target.position + Vector3.up * (targetMotor != null ? targetMotor.CenterHeight : 1.1f);

        /// <summary>Furthest anything can be seen (Far).</summary>
        public float MaxRange => viewDistance;
        /// <summary>How far the zones reach at an angle off facing: Far in the cone, Mid in the periphery, Far behind (Noise zone, Lit only).</summary>
        public float RangeAtAngle(float degreesOffAhead)
        {
            float a = Mathf.Abs(degreesOffAhead);
            return a <= EffectiveHalfAngle ? viewDistance : a <= peripheralHalfAngle ? midDistance : viewDistance;
        }

        void Start()
        {
            if (eye == null) eye = transform;
            if (target == null)
            {
                var player = FindAnyObjectByType<PlayerMotor>();
                if (player != null) target = player.transform;
            }
            if (target != null)
            {
                targetExposure = target.GetComponent<LightExposure>();
                targetHealth = target.GetComponent<Health>();
                targetMotor = target.GetComponent<PlayerMotor>();
            }
        }

        public void Tick()
        {
            Rate = Evaluate(out char zone);
            Zone = Rate == DetectRate.None ? '-' : zone;
            if (Rate != DetectRate.None) LastSeenPosition = target.position;
        }

        DetectRate Evaluate(out char zone)
        {
            zone = '-';
            if (target == null || eye == null) return DetectRate.None;
            if (targetHealth != null && targetHealth.IsDead) return DetectRate.None;

            // A searchlight on the player is the guard's eyes: Instant, wherever the guard is looking.
            if (searchlights != null && targetExposure != null)
                foreach (var light in searchlights)
                    if (light != null && light.isActiveAndEnabled && light.TierAt(targetExposure.SamplePoint, target) == LightTier.Lit)
                    {
                        zone = 'S';
                        NormalizedDistance = 1f;
                        return DetectRate.Instant;
                    }

            Vector3 eyePos = eye.position;
            Vector3 toTarget = TargetCenter - eyePos;
            float distance = toTarget.magnitude; // 3D: height counts
            NormalizedDistance = Mathf.Clamp01(distance / viewDistance);
            if (distance > viewDistance) return DetectRate.None;

            // Height: above the guard is unseen unless searching. Below changes the Lit rates.
            int targetLayer = HeightLayers.LayerOf(target.position.y);
            int own = OwnLayer;
            if (targetLayer > own && !LookUp) return DetectRate.None;
            bool below = targetLayer < own;

            // Line of sight to any part of the body (prone bodies are long and low: low cover hides them).
            Vector3[] points = targetMotor != null ? targetMotor.VisibilityPoints : BodyPoints;
            bool anyVisible = false;
            foreach (var p in points)
                if (!Occlusion.Blocked(eyePos, target.TransformPoint(p), target, transform)) { anyVisible = true; break; }
            if (!anyVisible) return DetectRate.None;

            Vector3 flatForward = eye.forward;
            flatForward.y = 0f;
            Vector3 flatTo = toTarget;
            flatTo.y = 0f;
            float off = flatTo.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(flatForward, flatTo);

            LightTier light = targetExposure != null ? targetExposure.Tier : LightTier.Lit;
            Move move = targetMotor == null ? Move.Walking
                      : targetMotor.IsProne ? Move.Crawling
                      : targetMotor.IsDashing ? Move.Dashing
                      : targetMotor.Speed > stillSpeed ? Move.Walking
                      : Move.Still;

            // Every zone the target is in; the fastest rate wins.
            DetectRate best = DetectRate.None;
            int bestZone = 5;
            void Consider(int z)
            {
                DetectRate r = TableRate(light, move, below, z);
                if (r > best) { best = r; bestZone = z; }
            }
            if (off <= EffectiveHalfAngle) Consider(distance < nearDistance ? 2 : distance < midDistance ? 1 : 0);
            if (off <= peripheralHalfAngle && distance < midDistance) Consider(distance < nearDistance ? 4 : 3);
            Consider(5); // the Noise zone covers the whole Far circle
            zone = (char)('A' + bestZone);
            return best;
        }
    }
}
