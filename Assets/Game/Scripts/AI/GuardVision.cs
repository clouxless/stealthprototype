using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// 3D vision: a horizontal cone, height layers (a guard sees its own layer and the one below,
    /// and looks up only while searching), line of sight against the body, and the target's
    /// light tier. Each tier has one fixed sight distance, the same for every guard.
    /// Inside that distance the target is seen; beyond it, not at all.
    /// </summary>
    public class GuardVision : MonoBehaviour
    {
        public Transform eye;
        public Transform target;

        [Header("Cone")]
        [Tooltip("Cone length. Also the sight distance for a Lit target.")]
        public float viewDistance = 13f;
        public float halfAngle = 35f;
        [Tooltip("How many height layers below its own a guard can see.")]
        public int layersDown = 1;
        [Tooltip("Outside the cone, within this radius, the guard can sense a target that isn't sneaking.")]
        public float peripheralRadius = 1.5f;
        [Tooltip("How strongly that out-of-cone sense registers (0..1). Sneaking targets are never sensed this way.")]
        [Range(0f, 1f)] public float peripheralStrength = 0.3f;

        [Header("Light tiers: fixed sight distances")]
        public float darkSightDistance = 2.5f;
        public float dimSightDistance = 6.5f;
        [Tooltip("Within this distance a seen target registers at full speed; it slows toward the tier's limit.")]
        public float closeRange = 2.5f;

        static readonly Vector3[] BodyPoints = { new Vector3(0f, 0.35f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 1.65f, 0f) };

        LightExposure targetExposure;
        Health targetHealth;
        PlayerMotor targetMotor;

        public int OwnLayer => HeightLayers.LayerOf(transform.position.y);

        /// <summary>Whether this guard can currently see things on a height layer.</summary>
        public bool CanSeeLayer(int layer)
        {
            int own = OwnLayer;
            if (layer < own - layersDown) return false;
            return layer <= own || LookUp;
        }

        /// <summary>Set by the brain while searching: the guard checks higher layers too.</summary>
        public bool LookUp { get; set; }
        /// <summary>Widened while alert.</summary>
        public float AngleMultiplier { get; set; } = 1f;
        /// <summary>0..1 how well the target is seen this frame.</summary>
        public float Visibility { get; private set; }
        /// <summary>0 at the eye, 1 at max view distance.</summary>
        public float NormalizedDistance { get; private set; } = 1f;
        public Vector3 LastSeenPosition { get; private set; }
        public float EffectiveHalfAngle => halfAngle * AngleMultiplier;
        /// <summary>Centre of the target's body (lower when prone).</summary>
        public Vector3 TargetCenter => target.position + Vector3.up * (targetMotor != null ? targetMotor.CenterHeight : 1.1f);

        public float SightDistance(LightTier tier) => tier switch
        {
            LightTier.Lit => viewDistance,
            LightTier.Dim => Mathf.Min(dimSightDistance, viewDistance),
            _ => Mathf.Min(darkSightDistance, viewDistance)
        };

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
            Visibility = Evaluate();
            if (Visibility > 0f) LastSeenPosition = target.position;
        }

        float Evaluate()
        {
            if (target == null || eye == null) return 0f;
            if (targetHealth != null && targetHealth.IsDead) return 0f;

            Vector3 eyePos = eye.position;
            Vector3 toTarget = TargetCenter - eyePos;
            float distance = toTarget.magnitude;
            NormalizedDistance = Mathf.Clamp01(distance / viewDistance);
            if (distance > viewDistance) return 0f;

            Vector3 flatForward = eye.forward;
            flatForward.y = 0f;
            Vector3 flatTo = toTarget;
            flatTo.y = 0f;
            if (!CanSeeLayer(HeightLayers.LayerOf(target.position.y))) return 0f;
            bool inCone = Vector3.Angle(flatForward, flatTo) <= EffectiveHalfAngle;

            // Outside the cone the guard can only *sense* someone close who isn't sneaking.
            // A sneaking player behind a guard is never sensed: that's what makes takedowns possible.
            float senseFactor = 1f;
            if (!inCone)
            {
                bool sneaking = targetMotor != null && targetMotor.IsQuiet;
                if (distance > peripheralRadius || sneaking) return 0f;
                senseFactor = peripheralStrength;
            }

            // Line of sight to the body (prone bodies are long and low: low cover hides them).
            Vector3[] points = targetMotor != null ? targetMotor.VisibilityPoints : BodyPoints;
            int visibleParts = 0;
            foreach (var p in points)
                if (!Occlusion.Blocked(eyePos, target.TransformPoint(p), target, transform)) visibleParts++;
            if (visibleParts == 0) return 0f;
            float partFactor = visibleParts / (float)points.Length;

            LightTier tier = targetExposure != null ? targetExposure.Tier : LightTier.Lit;
            float sight = SightDistance(tier);
            if (distance > sight) return 0f;

            float distanceFactor = 1f - Mathf.InverseLerp(closeRange, sight, distance) * 0.6f;
            return Mathf.Clamp01(partFactor * distanceFactor * senseFactor);
        }
    }
}
