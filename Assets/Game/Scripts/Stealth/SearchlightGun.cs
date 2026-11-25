using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A gun mounted on a searchlight (SwivelLight, track or track-aim), worked by the guard who owns the light.
    /// Get caught in the beam (Lit in it) and it locks on: the light leaves its path and follows you
    /// (SwivelLight.follow, a little slower than a run), and
    /// while the owner is alert and you're still in the beam it fires fully automatic from the
    /// lamp, straight down the beam: a short wind-up, then a round every fireInterval. A round hits
    /// unless something solid is between the lamp and you. Out of the beam for releaseAfter
    /// seconds, or the owner not alert any more, and it lets go and goes back to its path.
    /// </summary>
    public class SearchlightGun : MonoBehaviour
    {
        public SwivelLight swivel;
        public StealthLight beam;
        public GuardBrain owner;
        public Transform target;
        [Tooltip("Where the rounds come from (the lamp in the guard's hands). Default: the light itself.")]
        public Transform muzzle;
        [Tooltip("Seconds from lock-on to the first round.")]
        public float windup = 0.5f;
        [Tooltip("Seconds between rounds (fully automatic).")]
        public float fireInterval = 0.1f;
        public int damage = 1;
        public float noiseRadius = 18f;
        [Tooltip("Seconds out of the beam before it lets go.")]
        public float releaseAfter = 0.6f;

        public bool Locked { get; private set; }

        LightExposure exposure;
        PlayerMotor motor;
        Health targetHealth;
        float fireTimer, outTimer;

        void Start()
        {
            if (target == null) return;
            exposure = target.GetComponent<LightExposure>();
            motor = target.GetComponent<PlayerMotor>();
            targetHealth = target.GetComponent<Health>();
        }

        void Update()
        {
            if (swivel == null || beam == null || owner == null || target == null) return;
            bool ownerUp = owner.State != GuardState.Down;
            bool alive = targetHealth == null || !targetHealth.IsDead;
            Vector3 sample = exposure != null ? exposure.SamplePoint : target.position + Vector3.up;
            bool inBeam = alive && beam.TierAt(sample, target) == LightTier.Lit;

            if (!Locked)
            {
                if (!inBeam || !ownerUp) return;
                Locked = true;
                swivel.follow = target;
                fireTimer = windup;
                outTimer = 0f;
            }

            outTimer = inBeam ? 0f : outTimer + Time.deltaTime;
            bool ownerAlert = owner.State == GuardState.Alert || owner.State == GuardState.Suspicious; // still reacting
            if (!ownerUp || !alive || outTimer >= releaseAfter || (!ownerAlert && !inBeam))
            {
                Locked = false;
                swivel.follow = null;
                return;
            }

            if (!inBeam || owner.State != GuardState.Alert) return;
            fireTimer -= Time.deltaTime;
            if (fireTimer > 0f) return;
            fireTimer += fireInterval;
            Fire();
        }

        void Fire()
        {
            Vector3 origin = muzzle != null ? muzzle.position : beam.transform.position;
            Vector3 aim = target.position + Vector3.up * (motor != null ? motor.CenterHeight : 1.1f);
            Vector3 end = aim;
            if (!Occlusion.Blocked(origin, aim, target, muzzle != null ? muzzle : swivel.transform))
            {
                if (targetHealth != null)
                    targetHealth.TakeDamage(new DamageInfo { amount = damage, point = aim, direction = (aim - origin).normalized, source = owner.gameObject });
            }
            else if (Occlusion.Raycast(origin, (aim - origin).normalized, 40f, muzzle != null ? muzzle : swivel.transform, target, out RaycastHit hit))
            {
                end = hit.point;
            }
            FX.Line(origin, end, new Color(1f, 0.35f, 0.2f, 0.9f), 0.04f, 0.05f);
            NoiseSystem.Emit(origin, noiseRadius, 1f, owner, SoundKind.Gunshot, gameObject);
        }
    }
}
