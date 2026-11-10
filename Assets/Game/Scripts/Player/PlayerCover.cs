using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Cover cling (MGS1-style), on a held button: Shift / gamepad LB.
    ///   - Hold it next to a tall wall (or walk into one while holding it) to press your back to it.
    ///     Let go to step off.
    ///   - The stick slides you along the wall. Left and right are read in the normal top-down frame,
    ///     so they don't change when the camera swings. You stop at the wall's end.
    ///   - Near the end of the wall (the last swingZone metres, short of the very edge) the camera
    ///     swings low to look along the wall and round the corner (CameraRig.coverSwing).
    ///   - Knock: hold melee to show the knock's noise radius, release to knock. Letting go of Shift
    ///     first cancels it.
    ///   - Dash or crawl also leave cover.
    /// In a vent or floor grate (SoundDuct) the same hold-melee / release knock works without
    /// Shift; moving cancels it. Its noise comes out at the vent's openings, or above the grate.
    /// The player is still fully visible to guards in cover; it's for seeing and luring, not hiding.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(CharacterController))]
    public class PlayerCover : MonoBehaviour
    {
        [Tooltip("How far past the body a wall can be to cling to it.")]
        public float reach = 0.6f;
        public float coverSpeed = 1.6f;
        [Tooltip("The camera swings when the wall ends within this many metres of you (you stop short of the very edge).")]
        public float swingZone = 1f;
        public float knockRadius = 10f;
        [Range(0f, 1f)] public float knockLoudness = 0.5f;
        public float knockCooldown = 0.8f;
        [Tooltip("Wall probe heights. All must hit for a wall to count as cover (no clinging to crates).")]
        public float[] probeHeights = { 1.0f, 1.6f };

        public bool InCover { get; private set; }
        /// <summary>-1 or +1 when stopped at the left/right end of the wall (relative to the wall tangent), else 0.</summary>
        public int AtEdge { get; private set; }
        /// <summary>True while near an end of the wall, with the camera swung round the corner.</summary>
        public bool Swinging { get; private set; }
        /// <summary>True while melee is held to line up a knock.</summary>
        public bool PreviewingKnock { get; private set; }
        public Vector3 WallNormal => wallNormal;

        public string Prompt
        {
            get
            {
                if (PreviewingKnock) return InCover ? "Release LMB: knock  |  let go of Shift: cancel" : "Release LMB: knock  |  move: cancel";
                if (InCover) return Swinging ? "Corner view  |  hold LMB: knock" : "In cover  |  hold LMB: knock";
                if (inDuct) return "Hold LMB: knock";
                return null;
            }
        }

        PlayerMotor motor;
        PlayerInputReader input;
        CharacterController controller;
        PlayerContextActions context;
        NoisePreview preview;
        CameraRig rig;
        Vector3 wallNormal;
        float knockTimer;
        float reenterTimer;
        int lastMoveSide;
        bool inDuct;

        static readonly Vector3[] Compass =
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
            new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized, new Vector3(-1f, 0f, -1f).normalized
        };

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<CharacterController>();
            context = GetComponent<PlayerContextActions>();
            preview = GetComponent<NoisePreview>();
            if (preview == null) preview = gameObject.AddComponent<NoisePreview>();
        }

        void Start() => rig = FindAnyObjectByType<CameraRig>();

        void OnDisable()
        {
            if (InCover) Exit();
            CancelKnock();
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

            if (InCover)
            {
                UpdateCover(dt);
                return;
            }

            inDuct = SoundDuct.Find(transform.position) != null;
            if (inDuct) UpdateDuctKnock();
            else
            {
                if (PreviewingKnock) CancelKnock();
                TryEnter();
            }
        }

        // ---------------------------------------------------------------- entering

        void TryEnter()
        {
            if (!input.CoverHeld || reenterTimer > 0f) return;
            bool blocked = motor.ExternalControl || motor.IsDashing || motor.IsProne || motor.AttackLock || motor.FocusLock
                           || motor.MovementLocked || !controller.isGrounded || input.AimHeld
                           || (context != null && context.Busy) || (rig != null && rig.IsFirstPerson);
            if (blocked) return;

            // The wall you're walking into first, then the one you're facing, then the nearest.
            Vector3 wish = motor.CameraRelative(input.Move);
            wish.y = 0f;
            bool found = false;
            Vector3 bestNormal = default;
            float bestDistance = float.MaxValue;
            if (wish.sqrMagnitude > 0.25f && ProbeWall(transform.position, wish.normalized, out bestNormal, out bestDistance))
                found = true;
            else if (ProbeWall(transform.position, Flat(transform.forward), out bestNormal, out bestDistance))
                found = true;
            else
            {
                foreach (var dir in Compass)
                {
                    if (!ProbeWall(transform.position, dir, out Vector3 n, out float d) || d >= bestDistance) continue;
                    found = true;
                    bestNormal = n;
                    bestDistance = d;
                }
            }
            if (!found) return;

            InCover = true;
            wallNormal = bestNormal;
            lastMoveSide = 0;
            motor.ExternalControl = true;
            Snap(bestDistance);
            transform.rotation = Quaternion.LookRotation(wallNormal);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
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

        /// <summary>Metres from the body's centre to where the wall ends on that side, or infinity if it's beyond the swing zone.</summary>
        float DistanceToEnd(int side, Vector3 tangent)
        {
            const float step = 0.1f;
            for (float d = step; d <= swingZone + 0.05f; d += step)
                if (!ProbeWall(transform.position + tangent * (side * d), -wallNormal, out _, out _)) return d;
            return float.PositiveInfinity;
        }

        // ---------------------------------------------------------------- in cover

        void UpdateCover(float dt)
        {
            bool leave = !input.CoverHeld || input.DashPressed || input.ProneToggled || (rig != null && rig.IsFirstPerson);
            if (leave)
            {
                Exit(); // letting go of Shift while lining up a knock cancels it
                return;
            }

            // The rig's yaw doesn't change when the camera swings, so this stays the top-down frame.
            Vector3 wish = motor.CameraRelative(input.Move);
            wish.y = 0f;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal);
            float along = Vector3.Dot(wish, tangent);
            int side = along > 0.2f ? 1 : along < -0.2f ? -1 : 0;
            if (side != 0) lastMoveSide = side;

            if (side != 0)
            {
                // Is there still wall a little ahead of us on that side?
                Vector3 ahead = transform.position + tangent * (side * (controller.radius + 0.1f));
                if (ProbeWall(ahead, -wallNormal, out _, out _))
                {
                    AtEdge = 0;
                    controller.Move(tangent * (along * coverSpeed * dt) + Vector3.down * (2f * dt));
                }
                else AtEdge = side;
            }
            else if (AtEdge != 0)
            {
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
            tangent = Vector3.Cross(Vector3.up, wallNormal);

            // Corner view: swing when the wall ends within the swing zone.
            bool nearPos = DistanceToEnd(1, tangent) <= swingZone;
            bool nearNeg = DistanceToEnd(-1, tangent) <= swingZone;
            int swingSide = nearPos && nearNeg ? (lastMoveSide != 0 ? lastMoveSide : 1) : nearPos ? 1 : nearNeg ? -1 : 0;
            Swinging = swingSide != 0;
            if (rig != null) rig.SetCoverView(Swinging, transform.position, wallNormal, tangent * swingSide);

            // Knock: hold melee to line it up, release to knock.
            if (input.MeleePressed && knockTimer <= 0f) PreviewingKnock = true;
            if (PreviewingKnock)
            {
                preview.Show(transform.position, knockRadius);
                if (!input.MeleeHeld) Knock();
            }
        }

        // ---------------------------------------------------------------- in a vent or grate

        void UpdateDuctKnock()
        {
            if (input.MeleePressed && knockTimer <= 0f && !motor.IsDashing) PreviewingKnock = true;
            if (!PreviewingKnock) return;
            if (input.Move.sqrMagnitude > 0.12f || input.DashPressed)
            {
                CancelKnock();
                return;
            }
            preview.Show(transform.position, knockRadius);
            if (!input.MeleeHeld) Knock();
        }

        void Knock()
        {
            PreviewingKnock = false;
            preview.Hide();
            knockTimer = knockCooldown;
            Vector3 point = transform.position;
            NoiseSystem.Emit(point, knockRadius, knockLoudness, null, SoundKind.Knock, gameObject);
            // A deliberate sound: always show its reach, even with the F2 noise rings off.
            if (!NoiseSystem.ShowNoise && knockLoudness < NoiseSystem.CriticalLoudness)
            {
                var origins = new System.Collections.Generic.List<Vector3>();
                SoundDuct.ResolveOrigins(point, origins);
                foreach (var o in origins)
                    FX.NoiseRing(o + Vector3.up * 0.05f, knockRadius, knockLoudness, SoundGrid.SpaceAt(o));
            }
            CameraRig.Shake(0.04f);
        }

        void CancelKnock()
        {
            PreviewingKnock = false;
            if (preview != null) preview.Hide();
        }

        void Exit()
        {
            InCover = false;
            Swinging = false;
            AtEdge = 0;
            CancelKnock();
            reenterTimer = 0.3f;
            if (motor != null) motor.ExternalControl = false;
            if (rig != null) rig.SetCoverView(false, transform.position, wallNormal, Vector3.zero);
        }
    }
}
