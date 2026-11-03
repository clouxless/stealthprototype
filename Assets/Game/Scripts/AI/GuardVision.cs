using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// 3D vision: a horizontal cone with separate look-up and look-down limits (so perches above
    /// a guard are safe up close), line-of-sight against three body points, and light level.
    /// A target in deep shadow is nearly invisible beyond a few metres.
    /// </summary>
    public class GuardVision : MonoBehaviour
    {
        public Transform eye;
        public Transform target;

        [Header("Cone")]
        public float viewDistance = 13f;
        public float halfAngle = 35f;
        [Tooltip("Guards rarely look up. Targets steeper than this above the eye are unseen.")]
        public float lookUpLimit = 20f;
        public float lookDownLimit = 65f;
        [Tooltip("Within this radius the guard notices the target regardless of facing.")]
        public float peripheralRadius = 1.8f;

        [Header("Light")]
        [Tooltip("Inside this range light level doesn't matter.")]
        public float closeRange = 2.5f;
        [Tooltip("Up to this range a target in shadow is partially visible.")]
        public float darkSightDistance = 6f;
        [Range(0f, 1f)] public float nearDarkVisibility = 0.55f;

        static readonly Vector3[] BodyPoints = { new Vector3(0f, 0.35f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 1.65f, 0f) };

        LightExposure targetExposure;
        Health targetHealth;

        /// <summary>Widened while alert.</summary>
        public float AngleMultiplier { get; set; } = 1f;
        /// <summary>0..1 how well the target is seen this frame.</summary>
        public float Visibility { get; private set; }
        /// <summary>0 at the eye, 1 at max view distance.</summary>
        public float NormalizedDistance { get; private set; } = 1f;
        public Vector3 LastSeenPosition { get; private set; }
        public float EffectiveHalfAngle => halfAngle * AngleMultiplier;

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
            Vector3 toTarget = target.position + Vector3.up * 1.1f - eyePos;
            float distance = toTarget.magnitude;
            NormalizedDistance = Mathf.Clamp01(distance / viewDistance);
            if (distance > viewDistance) return 0f;

            if (distance > peripheralRadius)
            {
                Vector3 flatForward = eye.forward;
                flatForward.y = 0f;
                Vector3 flatTo = toTarget;
                flatTo.y = 0f;
                if (Vector3.Angle(flatForward, flatTo) > EffectiveHalfAngle) return 0f;

                float vertical = Mathf.Atan2(toTarget.y, flatTo.magnitude) * Mathf.Rad2Deg;
                if (vertical > lookUpLimit || vertical < -lookDownLimit) return 0f;
            }

            int visibleParts = 0;
            foreach (var p in BodyPoints)
                if (!Occlusion.Blocked(eyePos, target.TransformPoint(p), target, transform)) visibleParts++;
            if (visibleParts == 0) return 0f;
            float partFactor = visibleParts / (float)BodyPoints.Length;

            float exposure = targetExposure != null ? targetExposure.Exposure : 1f;
            float darkFloor = distance <= closeRange ? 1f : distance <= darkSightDistance ? nearDarkVisibility : 0f;
            float lightFactor = Mathf.Lerp(darkFloor, 1f, exposure);

            float distanceFactor = 1f - Mathf.InverseLerp(closeRange, viewDistance, distance) * 0.6f;
            return Mathf.Clamp01(partFactor * lightFactor * distanceFactor);
        }
    }
}
