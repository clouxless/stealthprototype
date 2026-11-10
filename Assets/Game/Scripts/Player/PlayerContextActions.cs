using System.Collections;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// The context button (E / gamepad Y). Shows a prompt when an action is available and runs it:
    /// 1. Takedown: close behind a guard who isn't alert. Hold the button through a choke that takes
    ///    a few seconds (Arkham-style). You're locked in place and exposed the whole time; let go
    ///    early and the guard breaks free, alerted. The struggle makes a little noise.
    /// 2. Clamber / Climb: facing a ledge between minClimbHeight and maxClimbHeight with room to
    ///    stand on top. Up to clamberMaxHeight is a quick clamber, above that a slower climb.
    /// 3. Hang (MGS2-style): at a drop of at least minHangDrop with nothing in front, lower yourself
    ///    over the edge. Shimmy along it with the stick, E to climb back up, Space / C to let go.
    ///    Grip runs out after gripTime and you fall.
    /// Priority: takedown, then clamber/climb, then hang.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(CharacterController))]
    public class PlayerContextActions : MonoBehaviour
    {
        public enum ContextAction { None, Takedown, Clamber, Climb, Hang }
        /// <summary>Which surface sets the light tier while hanging (open design question).</summary>
        public enum HangLightSource { FloorBelow, Ledge }

        [Header("Takedown")]
        public float takedownRange = 1.8f;
        [Tooltip("The player must be roughly facing the guard.")]
        public float takedownFacingAngle = 75f;
        public float takedownStepTime = 0.12f;
        [Tooltip("Seconds the button must be held to finish the choke.")]
        public float takedownHoldTime = 2.5f;
        [Tooltip("Grace period after letting go before the guard breaks free.")]
        public float releaseGrace = 0.15f;
        public float struggleNoiseRadius = 2.5f;
        public float struggleNoiseInterval = 0.6f;
        public float takedownNoiseRadius = 1.5f;

        [Header("Climb")]
        public float minClimbHeight = 0.5f;
        public float clamberMaxHeight = 1.6f;
        public float maxClimbHeight = 4.2f;
        [Tooltip("How far in front of the player a wall can be.")]
        public float wallReach = 0.9f;
        public float clamberTime = 0.3f;
        public float climbTimePerMetre = 0.16f;
        public float climbNoiseRadius = 2f;

        [Header("Ledge hang")]
        [Tooltip("Smallest drop past an edge that offers a hang.")]
        public float minHangDrop = 1.7f;
        [Tooltip("Feet below the ledge top while hanging (hands on the edge).")]
        public float hangDepth = 2.05f;
        [Tooltip("Seconds you can hang before your grip gives out.")]
        public float gripTime = 6f;
        public float shimmySpeed = 1.2f;
        public float hangEnterTime = 0.3f;
        public HangLightSource hangLight = HangLightSource.FloorBelow;

        public ContextAction Available { get; private set; }
        public bool Busy { get; private set; }
        /// <summary>0..1 while a takedown choke is in progress, otherwise -1.</summary>
        public float TakedownProgress { get; private set; } = -1f;
        /// <summary>World position to draw the prompt at.</summary>
        public Vector3 PromptPosition { get; private set; }
        public string PromptLabel => Available switch
        {
            ContextAction.Takedown => "Takedown (hold)",
            ContextAction.Clamber => "Clamber",
            ContextAction.Climb => "Climb",
            ContextAction.Hang => "Hang off edge",
            _ => null
        };

        public bool Hanging { get; private set; }
        /// <summary>Grip left while hanging, 1..0.</summary>
        public float GripFraction { get; private set; }

        struct Edge
        {
            public Vector3 normal;   // horizontal, pointing out over the drop
            public Vector3 face;     // a point on the outer face, just under the top
            public float topY;
            public Vector3 hangPoint;
        }

        struct Ledge
        {
            public Vector3 inward;      // horizontal direction into the wall
            public float wallDistance;  // from the player's centre line to the wall face
            public Vector3 standPoint;  // where the player's feet end up
            public float height;
        }

        PlayerMotor motor;
        PlayerInputReader input;
        CharacterController controller;
        CameraRig rig;
        GuardBrain takedownTarget;
        GuardBrain grabbed;
        Ledge ledge;
        Edge edge;
        LightExposure exposure;
        PlayerCover cover;
        PlayerGrateCrawl grate;
        readonly Collider[] overlap = new Collider[16];
        static readonly float[] WallProbeHeights = { 0.4f, 1.0f, 1.5f };

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<CharacterController>();
            exposure = GetComponent<LightExposure>();
            cover = GetComponent<PlayerCover>();
            grate = GetComponent<PlayerGrateCrawl>();
        }

        void Start() => rig = FindAnyObjectByType<CameraRig>();

        void OnDisable()
        {
            if (grabbed != null) grabbed.BreakFree(transform.position);
            grabbed = null;
            if (motor != null) motor.ExternalControl = false;
            if (controller != null) controller.enabled = true;
            if (exposure != null) exposure.SampleOverride = null;
            Hanging = false;
            Busy = false;
            TakedownProgress = -1f;
        }

        void Update()
        {
            Available = ContextAction.None;
            if (grate != null && grate.BlocksContext) return; // floor grates take the button
            // In cover, only climbing the wall you're pressed against is offered.
            bool inCover = cover != null && cover.InCover;
            if (Busy || motor.IsDashing || (motor.ExternalControl && !inCover) || motor.AttackLock || motor.IsProne) return;
            if (rig != null && rig.IsFirstPerson) return;

            takedownTarget = inCover ? null : FindTakedownTarget();
            Vector3 facing = inCover ? -cover.WallNormal : transform.forward;
            if (takedownTarget != null)
            {
                Available = ContextAction.Takedown;
                PromptPosition = takedownTarget.transform.position + Vector3.up * 2.1f;
            }
            else if (controller.isGrounded && FindLedge(facing, out ledge))
            {
                Available = ledge.height <= clamberMaxHeight ? ContextAction.Clamber : ContextAction.Climb;
                PromptPosition = ledge.standPoint + Vector3.up * 0.4f;
            }
            else if (!inCover && controller.isGrounded && FindEdge(out edge))
            {
                Available = ContextAction.Hang;
                PromptPosition = new Vector3(edge.face.x, edge.topY + 0.4f, edge.face.z);
            }

            if (Available == ContextAction.None || !input.InteractPressed) return;
            if (inCover) cover.Release();
            if (Available == ContextAction.Takedown) StartCoroutine(TakedownRoutine(takedownTarget));
            else if (Available == ContextAction.Hang) StartCoroutine(HangRoutine(edge));
            else StartCoroutine(ClimbRoutine(ledge));
        }

        // ---------------------------------------------------------------- takedown

        GuardBrain FindTakedownTarget()
        {
            GuardBrain best = null;
            float bestDistance = takedownRange;
            Vector3 chest = transform.position + Vector3.up * 1.2f;
            foreach (var g in GuardBrain.All)
            {
                if (g.State == GuardState.Down) continue;
                Vector3 to = g.transform.position - transform.position;
                if (Mathf.Abs(to.y) > 0.8f) continue;
                to.y = 0f;
                float d = to.magnitude;
                if (d > bestDistance) continue;
                if (d > 0.3f && Vector3.Angle(transform.forward, to) > takedownFacingAngle) continue;
                if (!g.CanBeTakenDown(transform.position)) continue;
                if (Occlusion.Blocked(chest, g.transform.position + Vector3.up * 1.2f, transform, g.transform)) continue;
                best = g;
                bestDistance = d;
            }
            return best;
        }

        IEnumerator TakedownRoutine(GuardBrain guard)
        {
            Busy = true;
            motor.ExternalControl = true;
            grabbed = guard;
            guard.Grab();

            // Step in behind the guard (CharacterController.Move keeps us out of walls).
            Vector3 behind = guard.transform.position - guard.transform.forward * 0.7f;
            behind.y = transform.position.y;
            Vector3 start = transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / takedownStepTime)
            {
                Vector3 target = Vector3.Lerp(start, behind, t);
                controller.Move(target - transform.position);
                FaceTowards(guard.transform.position);
                yield return null;
            }
            FaceTowards(guard.transform.position);

            // The choke: keep holding the button.
            float held = 0f;
            float released = 0f;
            float noiseTimer = 0f;
            TakedownProgress = 0f;
            while (held < takedownHoldTime)
            {
                float dt = Time.deltaTime;
                if (input.InteractHeld) released = 0f;
                else released += dt;

                if (released > releaseGrace)
                {
                    // Let go too early: the guard breaks free and raises the alarm.
                    grabbed = null;
                    TakedownProgress = -1f;
                    guard.BreakFree(transform.position);
                    motor.ExternalControl = false;
                    Busy = false;
                    yield break;
                }

                held += dt;
                TakedownProgress = held / takedownHoldTime;
                PromptPosition = guard.transform.position + Vector3.up * 2.1f;
                noiseTimer -= dt;
                if (noiseTimer <= 0f)
                {
                    noiseTimer = struggleNoiseInterval;
                    NoiseSystem.Emit(transform.position, struggleNoiseRadius, 0.3f, null, SoundKind.Struggle, gameObject);
                    CameraRig.Shake(0.05f);
                }
                yield return null;
            }

            TakedownProgress = -1f;
            grabbed = null;
            guard.CompleteTakedown(gameObject);
            HitStop.Do(0.1f);
            CameraRig.Shake(0.2f);
            NoiseSystem.Emit(transform.position, takedownNoiseRadius, 0.3f, null, SoundKind.Impact, gameObject);

            yield return new WaitForSeconds(0.15f);
            motor.ExternalControl = false;
            Busy = false;
        }

        void FaceTowards(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
        }

        // ---------------------------------------------------------------- climb

        bool FindLedge(Vector3 forward, out Ledge result)
        {
            result = default;
            Vector3 origin = transform.position;
            forward.y = 0f;
            forward.Normalize();

            // 1. A roughly vertical surface in front of us.
            RaycastHit wall = default;
            bool foundWall = false;
            foreach (float h in WallProbeHeights)
            {
                if (!Occlusion.Raycast(origin + Vector3.up * h, forward, wallReach, transform, null, out RaycastHit hit)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.4f) continue;
                if (hit.collider.GetComponentInParent<Health>() != null) continue; // not people
                wall = hit;
                foundWall = true;
                break;
            }
            if (!foundWall) return false;

            Vector3 inward = -wall.normal;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.01f) return false;
            inward.Normalize();
            float wallDistance = Vector3.Dot(wall.point - origin, inward);

            // 2. A walkable top surface just past the wall face, within climbing height.
            Vector3 probe = wall.point + inward * 0.35f;
            probe.y = origin.y + maxClimbHeight + 0.3f;
            float probeLength = maxClimbHeight + 0.3f - minClimbHeight;
            if (!Occlusion.Raycast(probe, Vector3.down, probeLength, transform, null, out RaycastHit top)) return false;
            if (top.normal.y < 0.7f) return false;
            float height = top.point.y - origin.y;
            if (height < minClimbHeight || height > maxClimbHeight) return false;

            // 3. Room to stand up there.
            Vector3 stand = new Vector3(probe.x, top.point.y, probe.z) + inward * 0.2f;
            float radius = controller.radius;
            Vector3 p1 = stand + Vector3.up * (radius + 0.05f);
            Vector3 p2 = stand + Vector3.up * (controller.height - radius);
            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!overlap[i].transform.IsChildOf(transform)) return false;

            result = new Ledge { inward = inward, wallDistance = wallDistance, standPoint = stand, height = height };
            return true;
        }

        IEnumerator ClimbRoutine(Ledge l)
        {
            Busy = true;
            motor.ExternalControl = true;
            controller.enabled = false;
            transform.rotation = Quaternion.LookRotation(l.inward);

            bool clamber = l.height <= clamberMaxHeight;
            Vector3 start = transform.position;
            Vector3 hug = start + l.inward * Mathf.Max(0f, l.wallDistance - controller.radius - 0.02f);
            Vector3 raised = new Vector3(hug.x, l.standPoint.y + 0.05f, hug.z);
            float riseTime = clamber ? clamberTime * 0.6f : 0.15f + climbTimePerMetre * l.height;
            float overTime = clamber ? clamberTime * 0.4f : 0.2f;

            yield return Move(start, hug, 0.05f);
            yield return Move(hug, raised, riseTime);
            yield return Move(raised, l.standPoint, overTime);

            controller.enabled = true;
            NoiseSystem.Emit(transform.position, climbNoiseRadius, 0.3f, null, SoundKind.Movement, gameObject);
            motor.ExternalControl = false;
            Busy = false;
        }

        // ---------------------------------------------------------------- ledge hang

        bool FindEdge(out Edge result)
        {
            result = default;
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return false;
            forward.Normalize();
            float radius = controller.radius;

            // 1. Nothing in front at chest height, and a real drop just ahead.
            if (Occlusion.Raycast(origin + Vector3.up * 1.2f, forward, radius + 0.8f, transform, null, out _)) return false;
            Vector3 beyond = origin + forward * (radius + 0.45f) + Vector3.up * 0.3f;
            if (Occlusion.Raycast(beyond, Vector3.down, minHangDrop + 0.3f, transform, null, out _)) return false;

            // 2. The outer face of what we stand on: cast back towards us from out over the drop.
            Vector3 outside = origin + forward * (radius + 0.8f) + Vector3.down * 0.12f; // decks can be thin
            if (!Occlusion.Raycast(outside, -forward, 1.2f, transform, null, out RaycastHit face)) return false;
            if (Mathf.Abs(face.normal.y) > 0.3f) return false;
            Vector3 normal = face.normal;
            normal.y = 0f;
            normal.Normalize();
            if (Vector3.Dot(normal, forward) < 0.6f) return false;

            // 3. The top of the edge.
            if (!FindTop(face.point, normal, out float topY)) return false;
            if (Mathf.Abs(topY - origin.y) > 0.3f) return false;

            Vector3 hang = new Vector3(face.point.x, topY - hangDepth, face.point.z) + normal * (radius + 0.05f);
            if (!BodyFits(hang)) return false;

            result = new Edge { normal = normal, face = face.point, topY = topY, hangPoint = hang };
            return true;
        }

        /// <summary>Walkable top just inside a face point.</summary>
        bool FindTop(Vector3 facePoint, Vector3 normal, out float topY)
        {
            topY = 0f;
            Vector3 above = facePoint - normal * 0.15f + Vector3.up * 0.8f;
            if (!Occlusion.Raycast(above, Vector3.down, 1.5f, transform, null, out RaycastHit top)) return false;
            if (top.normal.y < 0.7f) return false;
            topY = top.point.y;
            return true;
        }

        bool BodyFits(Vector3 feet)
        {
            float radius = controller.radius;
            Vector3 p1 = feet + Vector3.up * (radius + 0.05f);
            Vector3 p2 = feet + Vector3.up * (controller.height - radius);
            int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius * 0.95f, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!overlap[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        /// <summary>Is the ledge still there for hanging at this position? Updates the edge if so.</summary>
        bool EdgeAt(Vector3 hangPoint, ref Edge e)
        {
            Vector3 probe = hangPoint + Vector3.up * (hangDepth - 0.12f);
            if (!Occlusion.Raycast(probe, -e.normal, controller.radius + 0.4f, transform, null, out RaycastHit face)) return false;
            if (Mathf.Abs(face.normal.y) > 0.3f) return false;
            Vector3 normal = face.normal;
            normal.y = 0f;
            normal.Normalize();
            if (!FindTop(face.point, normal, out float topY) || Mathf.Abs(topY - e.topY) > 0.2f) return false;
            if (!BodyFits(hangPoint)) return false;
            e.normal = normal;
            e.face = face.point;
            e.topY = topY;
            e.hangPoint = new Vector3(face.point.x, topY - hangDepth, face.point.z) + normal * (controller.radius + 0.05f);
            return true;
        }

        IEnumerator HangRoutine(Edge e)
        {
            Busy = true;
            motor.ExternalControl = true;
            controller.enabled = false;

            // Over the edge and down, ending up facing the wall.
            Vector3 start = transform.position;
            Vector3 over = new Vector3(e.hangPoint.x, start.y, e.hangPoint.z);
            transform.rotation = Quaternion.LookRotation(e.normal);
            yield return Move(start, over, hangEnterTime * 0.4f);
            transform.rotation = Quaternion.LookRotation(-e.normal);
            yield return Move(over, e.hangPoint, hangEnterTime * 0.6f);
            NoiseSystem.Emit(transform.position + Vector3.up * hangDepth, climbNoiseRadius, 0.3f, null, SoundKind.Movement, gameObject);

            Hanging = true;
            float grip = gripTime;
            bool climbUp = false;
            while (true)
            {
                float dt = Time.deltaTime;
                grip -= dt;
                GripFraction = Mathf.Clamp01(grip / gripTime);
                PromptPosition = new Vector3(e.face.x, e.topY + 0.4f, e.face.z);
                if (exposure != null)
                    exposure.SampleOverride = hangLight == HangLightSource.Ledge
                        ? new Vector3(e.face.x, e.topY + 0.05f, e.face.z) - e.normal * 0.3f
                        : (Vector3?)null;

                if (grip <= 0f || input.DashPressed || input.ProneToggled) break;
                if (input.InteractPressed)
                {
                    climbUp = true;
                    break;
                }

                // Shimmy along the edge.
                Vector3 tangent = Vector3.Cross(Vector3.up, e.normal);
                Vector3 wish = motor.CameraRelative(input.Move);
                float along = Vector3.Dot(wish, tangent);
                if (Mathf.Abs(along) > 0.2f && dt > 0f)
                {
                    Vector3 next = transform.position + tangent * (Mathf.Clamp(along, -1f, 1f) * shimmySpeed * dt);
                    Edge candidate = e;
                    if (EdgeAt(next, ref candidate))
                    {
                        e = candidate;
                        transform.position = Vector3.MoveTowards(next, e.hangPoint, 1f * dt);
                        transform.rotation = Quaternion.LookRotation(-e.normal);
                    }
                }
                yield return null;
            }

            Hanging = false;
            GripFraction = 0f;
            if (exposure != null) exposure.SampleOverride = null;

            if (climbUp)
            {
                Vector3 stand = new Vector3(e.face.x, e.topY, e.face.z) - e.normal * (controller.radius + 0.25f);
                if (BodyFits(stand))
                {
                    Vector3 raised = new Vector3(transform.position.x, e.topY + 0.05f, transform.position.z);
                    yield return Move(transform.position, raised, 0.15f + climbTimePerMetre * hangDepth);
                    yield return Move(raised, stand, 0.2f);
                    NoiseSystem.Emit(transform.position, climbNoiseRadius, 0.3f, null, SoundKind.Movement, gameObject);
                }
                // No room up there: just let go.
            }

            controller.enabled = true;
            motor.ExternalControl = false;
            Busy = false;
        }

        IEnumerator Move(Vector3 from, Vector3 to, float duration)
        {
            if (duration <= 0f)
            {
                transform.position = to;
                yield break;
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float k = t * t * (3f - 2f * t);
                transform.position = Vector3.Lerp(from, to, k);
                yield return null;
            }
            transform.position = to;
        }
    }
}
