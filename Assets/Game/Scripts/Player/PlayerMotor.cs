using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Snappy top-down movement: high acceleration, near-instant turning, a short dash with
    /// i-frames, and crawling (prone). There is one walking speed and no sneak button: walking
    /// is silent, and guards only notice you by sight. The dash is the noisy move.
    /// Crawling's detection rules live in LightExposure.SeenTier and the prone VisibilityPoints.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        [Tooltip("The one walking speed. Walking is silent.")]
        public float moveSpeed = 4.5f;
        public float aimSpeed = 2.6f;
        public float acceleration = 70f;
        public float deceleration = 80f;
        [Tooltip("Degrees per second. Very high = Hades-style instant facing.")]
        public float turnSpeed = 1800f;

        [Header("Prone (C / gamepad B)")]
        public float proneSpeed = 1f;
        public float proneHeight = 0.6f;
        [Tooltip("Name of the child holding the character visuals, rotated to lie flat when prone.")]
        public string visualRootName = "PH_Mannequin";

        [Header("Dash")]
        public float dashDistance = 3f;
        public float dashDuration = 0.15f;
        public float dashCooldown = 0.6f;

        [Header("Noise")]
        public float dashNoiseRadius = 6f;

        [Header("Edges")]
        [Tooltip("Drops at least this deep stop you at the edge. Walk into one and you hang from it (PlayerContextActions); dash into one and you stop, or jump the gap if there's a platform in range.")]
        public float edgeDrop = 1.7f;
        public bool edgeGuard = true;

        [Header("Misc")]
        public float gravity = -30f;
        public float lungeDecay = 45f;
        [Tooltip("Movement is relative to this transform's yaw (the camera rig).")]
        public Transform cameraYawReference;

        CharacterController controller;
        PlayerInputReader input;
        Health health;
        PlayerContextActions context;
        Vector3 velocity;
        Vector3 lunge;
        Vector3 dashDirection;
        float verticalVelocity;
        float dashTimer;
        float dashCooldownTimer;
        Vector3? facingOverride;

        public bool IsDashing => dashTimer > 0f;
        /// <summary>Outward normal of the edge you're walking into, if any (zero otherwise).</summary>
        public Vector3 EdgeNormal { get; private set; }
        /// <summary>How long you've been walking into an edge.</summary>
        public float EdgePushTime { get; private set; }
        public bool IsProne { get; private set; }
        /// <summary>Height of the body's centre above the feet (aim point for guards).</summary>
        public float CenterHeight => IsProne ? 0.25f : 1.1f;
        /// <summary>Local-space points guards test line of sight against.</summary>
        public Vector3[] VisibilityPoints => IsProne ? PronePoints : StandingPoints;
        static readonly Vector3[] StandingPoints = { new Vector3(0f, 0.35f, 0f), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 1.65f, 0f) };
        static readonly Vector3[] PronePoints = { new Vector3(0f, 0.2f, -0.6f), new Vector3(0f, 0.22f, 0f), new Vector3(0f, 0.25f, 0.7f) };
        /// <summary>Locks movement input (first-person look mode).</summary>
        public bool MovementLocked { get; set; }
        /// <summary>Locks movement input during attack swings.</summary>
        public bool AttackLock { get; set; }
        /// <summary>Locks movement input while Focus is held.</summary>
        public bool FocusLock { get; set; }
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

        float standHeight;
        Transform visualRoot;
        readonly Collider[] standCheck = new Collider[8];

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();
            health = GetComponent<Health>();
            context = GetComponent<PlayerContextActions>();
            standHeight = controller.height;
            visualRoot = transform.Find(visualRootName);
        }

        /// <summary>Go prone or stand up. Standing needs head room.</summary>
        public bool SetProne(bool prone)
        {
            if (prone == IsProne) return true;
            if (!prone && !CanStand()) return false;
            IsProne = prone;
            float h = prone ? proneHeight : standHeight;
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);
            if (visualRoot != null)
            {
                // Lie face down along the facing direction, centred on the collider.
                visualRoot.localRotation = prone ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                visualRoot.localPosition = prone ? new Vector3(0f, 0.13f, -0.85f) : Vector3.zero;
            }
            return true;
        }

        bool CanStand()
        {
            float r = controller.radius;
            Vector3 p1 = transform.position + Vector3.up * (r + 0.05f);
            Vector3 p2 = transform.position + Vector3.up * (standHeight - r);
            int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r * 0.95f, standCheck, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!standCheck[i].transform.IsChildOf(transform)) return false;
            return true;
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
            if (!MovementLocked && !AttackLock && !FocusLock)
            {
                wish = CameraRelative(input.Move);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
            }

            if (input.ProneToggled && !IsDashing && controller.isGrounded) SetProne(!IsProne);
            float maxSpeed = IsProne ? proneSpeed : input.AimHeld ? aimSpeed : moveSpeed;

            // Dash
            dashCooldownTimer -= dt;
            if (input.DashPressed && !MovementLocked && !IsProne && !IsDashing && dashCooldownTimer <= 0f)
            {
                dashDirection = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
                dashTimer = dashDuration;
                dashCooldownTimer = dashDuration + dashCooldown;
                if (health != null) health.Invulnerable = true;
                transform.rotation = Quaternion.LookRotation(dashDirection);
                if (dashNoiseRadius > 0f)
                    NoiseSystem.Emit(transform.position, dashNoiseRadius, 0.5f, null, SoundKind.Movement, gameObject);
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

            // Edges: never walk or dash off a real drop.
            bool pushingEdge = false;
            if (edgeGuard && controller.isGrounded)
            {
                Vector3 flat = new Vector3(horizontal.x, 0f, horizontal.z);
                if (flat.sqrMagnitude > 0.0001f && OverDrop(transform.position + flat * dt, flat.normalized, out Vector3 outward))
                {
                    if (IsDashing)
                    {
                        // A dash into a gap jumps it if there's somewhere to land; otherwise it stops here.
                        Vector3 dir = dashDirection;
                        EndDash();
                        if (context != null && context.TryDashJump(dir)) return;
                    }
                    if (!IsDashing && Vector3.Dot(wish, outward) > 0.7f) // pushing out over it, not just along it
                    {
                        pushingEdge = true;
                        EdgeNormal = outward;
                    }
                    // Slide along the edge, or stop.
                    flat = Vector3.ProjectOnPlane(flat, outward);
                    if (flat.sqrMagnitude > 0.0001f && OverDrop(transform.position + flat * dt, flat.normalized, out _))
                        flat = Vector3.zero;
                    horizontal = new Vector3(flat.x, horizontal.y, flat.z);
                    velocity = Vector3.ProjectOnPlane(velocity, outward);
                    lunge = Vector3.zero;
                }
            }
            EdgePushTime = pushingEdge ? EdgePushTime + dt : 0f;
            if (!pushingEdge) EdgeNormal = Vector3.zero;

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
        }

        void EndDash()
        {
            dashTimer = 0f;
            velocity = Vector3.zero;
            if (health != null) health.Invulnerable = false;
        }

        /// <summary>
        /// Would feet at this position be over a drop of at least edgeDrop? If so, outward is the
        /// edge's horizontal normal (pointing out over the drop).
        /// </summary>
        public bool OverDrop(Vector3 feet, Vector3 direction, out Vector3 outward)
        {
            outward = direction;
            Vector3 probe = feet + direction * 0.1f;
            if (EdgeRules.GroundBelow(probe + Vector3.up * 0.4f, 0.4f + edgeDrop, transform, out _)) return false;
            // Find the edge's face: cast back toward us from out over the drop, just under the top.
            Vector3 outside = probe + direction * 0.6f + Vector3.down * 0.12f;
            if (Occlusion.Raycast(outside, -direction, 1.2f, transform, null, out RaycastHit face) && Mathf.Abs(face.normal.y) < 0.3f)
            {
                Vector3 n = face.normal;
                n.y = 0f;
                if (n.sqrMagnitude > 0.01f) outward = n.normalized;
            }
            return true;
        }
    }
}
