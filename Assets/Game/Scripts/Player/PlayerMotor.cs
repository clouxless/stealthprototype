using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Snappy top-down movement: high acceleration, near-instant turning, a short dash with
    /// i-frames, and a slow silent sneak. Running makes footstep noise guards can hear.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        public float runSpeed = 5f;
        public float sneakSpeed = 2f;
        public float aimSpeed = 2.6f;
        public float acceleration = 70f;
        public float deceleration = 80f;
        [Tooltip("Degrees per second. Very high = Hades-style instant facing.")]
        public float turnSpeed = 1800f;

        [Header("Dash")]
        public float dashDistance = 3f;
        public float dashDuration = 0.15f;
        public float dashCooldown = 0.6f;

        [Header("Noise")]
        public float footstepNoiseRadius = 6f;
        public float footstepInterval = 0.32f;
        public float dashNoiseRadius = 6f;

        [Header("Misc")]
        public float gravity = -30f;
        public float lungeDecay = 45f;
        [Tooltip("Movement is relative to this transform's yaw (the camera rig).")]
        public Transform cameraYawReference;

        CharacterController controller;
        PlayerInputReader input;
        Health health;
        Vector3 velocity;
        Vector3 lunge;
        Vector3 dashDirection;
        float verticalVelocity;
        float dashTimer;
        float dashCooldownTimer;
        float footstepTimer;
        Vector3? facingOverride;

        public bool IsDashing => dashTimer > 0f;
        public bool IsSneaking { get; private set; }
        /// <summary>Locks movement input (first-person look mode).</summary>
        public bool MovementLocked { get; set; }
        /// <summary>Locks movement input during attack swings.</summary>
        public bool AttackLock { get; set; }
        /// <summary>Another component (climb, takedown) is moving the player. The motor does nothing.</summary>
        public bool ExternalControl
        {
            get => externalControl;
            set
            {
                externalControl = value;
                velocity = Vector3.zero;
                lunge = Vector3.zero;
                verticalVelocity = 0f;
            }
        }
        bool externalControl;
        public Vector3 Velocity => velocity;
        public float Speed => new Vector3(velocity.x, 0f, velocity.z).magnitude;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();
            health = GetComponent<Health>();
        }

        void OnDisable()
        {
            if (health != null) health.Invulnerable = false;
            dashTimer = 0f;
        }

        public void SetFacingOverride(Vector3? direction) => facingOverride = direction;
        public void AddLunge(Vector3 impulse) => lunge += impulse;

        public Vector3 CameraRelative(Vector2 stick)
        {
            float yaw = cameraYawReference != null ? cameraYawReference.eulerAngles.y : 0f;
            return Quaternion.Euler(0f, yaw, 0f) * new Vector3(stick.x, 0f, stick.y);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || externalControl) return;

            Vector3 wish = Vector3.zero;
            if (!MovementLocked && !AttackLock)
            {
                wish = CameraRelative(input.Move);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
            }

            IsSneaking = input.SneakHeld && !IsDashing;
            float maxSpeed = input.AimHeld ? aimSpeed : IsSneaking ? sneakSpeed : runSpeed;

            // Dash
            dashCooldownTimer -= dt;
            if (input.DashPressed && !MovementLocked && !IsDashing && dashCooldownTimer <= 0f)
            {
                dashDirection = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
                dashTimer = dashDuration;
                dashCooldownTimer = dashDuration + dashCooldown;
                if (health != null) health.Invulnerable = true;
                transform.rotation = Quaternion.LookRotation(dashDirection);
                NoiseSystem.Emit(transform.position, dashNoiseRadius, 0.5f);
            }

            Vector3 horizontal;
            if (IsDashing)
            {
                dashTimer -= dt;
                horizontal = dashDirection * (dashDistance / dashDuration);
                if (dashTimer <= 0f)
                {
                    if (health != null) health.Invulnerable = false;
                    velocity = dashDirection * maxSpeed;
                }
            }
            else
            {
                Vector3 target = wish * maxSpeed;
                float rate = target.sqrMagnitude >= velocity.sqrMagnitude ? acceleration : deceleration;
                velocity = Vector3.MoveTowards(velocity, target, rate * dt);
                horizontal = velocity;
            }

            horizontal += lunge;
            lunge = Vector3.MoveTowards(lunge, Vector3.zero, lungeDecay * dt);

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * dt);

            // Facing
            Vector3 face = facingOverride ?? (!IsDashing && wish.sqrMagnitude > 0.01f ? wish : Vector3.zero);
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), turnSpeed * dt);
            }

            // Footsteps: sneaking is silent, running is not.
            footstepTimer -= dt;
            if (!IsSneaking && !IsDashing && Speed > sneakSpeed + 0.5f && footstepTimer <= 0f)
            {
                footstepTimer = footstepInterval;
                NoiseSystem.Emit(transform.position, footstepNoiseRadius * Mathf.Clamp01(Speed / runSpeed), 0.4f);
            }
        }
    }
}
