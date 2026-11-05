using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Cover cling (MGS1-style). Push into a tall wall for a moment to press your back against it.
    /// While in cover:
    ///   - move along the wall with the stick; you stop at its end
    ///   - at an end, keep pushing that way to peek: the view slides around the corner
    ///   - LMB / gamepad X knocks on the wall, a noise that lures guards to that spot
    ///   - push away from the wall, dash, or go prone to leave
    /// The player is still fully visible to guards in cover; it's for seeing, not hiding.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(CharacterController))]
    public class PlayerCover : MonoBehaviour
    {
        [Tooltip("Seconds of pushing into a wall before you press against it.")]
        public float pressTime = 0.25f;
        [Tooltip("How far past the body a wall can be to cling to it.")]
        public float reach = 0.35f;
        public float coverSpeed = 1.6f;
        [Tooltip("How far the camera slides around a corner while peeking.")]
        public float peekDistance = 4f;
        public float knockRadius = 7f;
        [Range(0f, 1f)] public float knockLoudness = 0.5f;
        public float knockCooldown = 0.8f;
        [Tooltip("Wall probe heights. All must hit for a wall to count as cover (no clinging to crates).")]
        public float[] probeHeights = { 1.0f, 1.6f };

        public bool InCover { get; private set; }
        /// <summary>-1 or +1 when stopped at the left/right end of the wall (relative to the wall tangent), else 0.</summary>
        public int AtEdge { get; private set; }
        public bool Peeking { get; private set; }
        public Vector3 WallNormal => wallNormal;
        public string Prompt => !InCover ? null : Peeking ? "Peeking  |  LMB knock" : "In cover  |  LMB knock";

        PlayerMotor motor;
        PlayerInputReader input;
        CharacterController controller;
        PlayerContextActions context;
        CameraRig rig;
        Vector3 wallNormal;
        float pressTimer;
        float knockTimer;
        float reenterTimer;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<CharacterController>();
            context = GetComponent<PlayerContextActions>();
        }

        void Start() => rig = FindAnyObjectByType<CameraRig>();

        void OnDisable()
        {
            if (InCover) Exit();
        }

        /// <summary>Leave cover because another action (a climb) takes over.</summary>
        public void Release()
        {
            if (InCover) Exit();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            knockTimer -= dt;
            reenterTimer -= dt;

            if (InCover) UpdateCover(dt);
            else TryEnter(dt);
        }

        // ---------------------------------------------------------------- entering

        void TryEnter(float dt)
        {
            bool blocked = motor.ExternalControl || motor.IsDashing || motor.IsProne || motor.AttackLock || motor.FocusLock
                           || motor.MovementLocked || !controller.isGrounded || input.AimHeld
                           || (context != null && context.Busy) || (rig != null && rig.IsFirstPerson) || reenterTimer > 0f;
            Vector3 wish = motor.CameraRelative(input.Move);
            if (blocked || wish.sqrMagnitude < 0.25f)
            {
                pressTimer = 0f;
                return;
            }
            wish.y = 0f;
            wish.Normalize();

            if (!ProbeWall(transform.position, wish, out Vector3 normal, out float distance) || Vector3.Dot(wish, -normal) < 0.7f)
            {
                pressTimer = 0f;
                return;
            }

            pressTimer += dt;
            if (pressTimer < pressTime) return;

            InCover = true;
            pressTimer = 0f;
            wallNormal = normal;
            motor.ExternalControl = true;
            Snap(distance);
            transform.rotation = Quaternion.LookRotation(wallNormal);
        }

        /// <summary>A tall, roughly vertical, non-character wall in direction dir at every probe height.</summary>
        bool ProbeWall(Vector3 feet, Vector3 dir, out Vector3 normal, out float distance)
        {
            normal = default;
            distance = 0f;
            float maxDist = controller.radius + reach;
            Vector3 sum = Vector3.zero;
            foreach (float h in probeHeights)
            {
                if (!Occlusion.Raycast(feet + Vector3.up * h, dir, maxDist, transform, null, out RaycastHit hit)) return false;
                if (Mathf.Abs(hit.normal.y) > 0.3f) return false;
                if (hit.collider.GetComponentInParent<Health>() != null) return false;
                sum += hit.normal;
                distance = Mathf.Max(distance, hit.distance);
            }
            sum.y = 0f;
            if (sum.sqrMagnitude < 0.01f) return false;
            normal = sum.normalized;
            return true;
        }

        /// <summary>Move so the body touches the wall.</summary>
        void Snap(float distanceToWall)
        {
            float gap = distanceToWall - controller.radius - 0.02f;
            controller.Move(-wallNormal * gap + Vector3.down * 0.02f); // a nudge down keeps isGrounded true
        }

        // ---------------------------------------------------------------- in cover

        void UpdateCover(float dt)
        {
            Vector3 wish = motor.CameraRelative(input.Move);
            wish.y = 0f;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool leave = input.DashPressed || input.ProneToggled || Vector3.Dot(wish, wallNormal) > 0.6f
                         || (rig != null && rig.IsFirstPerson);
            if (leave)
            {
                Exit();
                return;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal);
            float along = Vector3.Dot(wish, tangent);
            int side = along > 0.2f ? 1 : along < -0.2f ? -1 : 0;

            Peeking = false;
            if (side != 0)
            {
                // Is there still wall a little ahead of us on that side?
                Vector3 ahead = transform.position + tangent * (side * (controller.radius + 0.1f));
                if (ProbeWall(ahead, -wallNormal, out _, out _))
                {
                    AtEdge = 0;
                    controller.Move(tangent * (along * coverSpeed * dt) + Vector3.down * (2f * dt));
                }
                else
                {
                    AtEdge = side;
                    Peeking = true;
                }
            }
            else if (AtEdge != 0)
            {
                // Still at the end we stopped at, just not pushing.
                Vector3 ahead = transform.position + tangent * (AtEdge * (controller.radius + 0.1f));
                if (ProbeWall(ahead, -wallNormal, out _, out _)) AtEdge = 0;
            }

            // Follow the wall and stay pressed against it; let go if it's gone.
            if (!ProbeWall(transform.position, -wallNormal, out Vector3 normal, out float distance))
            {
                Exit();
                return;
            }
            wallNormal = normal;
            Snap(distance);
            transform.rotation = Quaternion.LookRotation(wallNormal);

            if (rig != null) rig.SetPeekOffset(Peeking ? tangent * (AtEdge * peekDistance) : Vector3.zero);

            if (input.MeleePressed && knockTimer <= 0f) Knock();
        }

        void Knock()
        {
            knockTimer = knockCooldown;
            Vector3 point = transform.position + Vector3.up * 1.1f - wallNormal * (controller.radius + 0.02f);
            NoiseSystem.Emit(point, knockRadius, knockLoudness, null, SoundKind.Knock, gameObject);
            // A deliberate sound: always show its reach, even with the F2 noise rings off.
            if (!NoiseSystem.ShowNoise && knockLoudness < NoiseSystem.CriticalLoudness)
                FX.NoiseRing(new Vector3(point.x, transform.position.y + 0.05f, point.z), knockRadius, knockLoudness);
            CameraRig.Shake(0.04f);
        }

        void Exit()
        {
            InCover = false;
            Peeking = false;
            AtEdge = 0;
            reenterTimer = 0.3f;
            if (motor != null) motor.ExternalControl = false;
            if (rig != null) rig.SetPeekOffset(Vector3.zero);
        }
    }
}
