using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace StealthProto
{
    public enum GuardState { Patrol, Suspicious, Alert, Search, Down }

    /// <summary>
    /// Guard state machine.
    /// Patrol: walk the route, sweep the flashlight at waypoints.
    /// Suspicious: something was glimpsed or heard. Pause, turn, go look.
    /// Alert: player confirmed. Radio nearby guards, chase, shoot with a visible wind-up.
    /// Search: lost the player. Sweep around the last known position, then go back to patrol.
    /// Down: taken out.
    /// The flashlight colour shows the state: warm white, amber, red.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public class GuardBrain : MonoBehaviour, INoiseListener
    {
        public static readonly List<GuardBrain> All = new List<GuardBrain>();

        [Header("References")]
        public PatrolRoute route;
        public GuardVision vision;
        public Transform head;
        public Light flashlight;
        public Renderer[] bodyRenderers;

        [Header("Movement")]
        public float walkSpeed = 1.8f;
        public float investigateSpeed = 2.6f;
        public float chaseSpeed = 5.5f;
        public float turnSpeed = 360f;

        [Header("Awareness")]
        public float detectRate = 2f;
        public float searchDetectMultiplier = 2f;
        public float decayRate = 0.12f;
        [Range(0f, 1f)] public float suspiciousThreshold = 0.3f;
        public float reactionTime = 0.45f;
        public float suspiciousDuration = 7f;
        public float loseSightTime = 2.5f;
        public float searchDuration = 12f;
        public float searchRadius = 6f;
        public float radioRadius = 22f;
        [Tooltip("Attacks from more than this angle off the guard's facing count as from behind.")]
        public float takedownAngle = 100f;

        [Header("Head sweep")]
        public float sweepAngle = 45f;
        public float sweepSpeed = 1.3f;

        [Header("Shooting")]
        public int fireDamage = 1;
        public float fireRange = 14f;
        public float fireWindup = 0.5f;
        public float fireCooldown = 1.1f;
        [Range(0f, 1f)] public float hitChance = 0.75f;
        public float gunNoiseRadius = 15f;

        [Header("State colours")]
        public Color patrolColor = new Color(1f, 0.93f, 0.78f);
        public Color suspiciousColor = new Color(1f, 0.7f, 0.15f);
        public Color alertColor = new Color(1f, 0.15f, 0.1f);

        public GuardState State { get; private set; } = GuardState.Patrol;
        public float Awareness { get; private set; }
        public event Action<GuardState> StateChanged;
        public Vector3 ListenPosition => transform.position;

        public Color CurrentColor => State switch
        {
            GuardState.Suspicious => suspiciousColor,
            GuardState.Search => suspiciousColor,
            GuardState.Alert => alertColor,
            _ => patrolColor
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        NavMeshAgent agent;
        Health health;
        LineRenderer telegraph;
        MaterialPropertyBlock block;
        Color bodyColor = Color.white;

        int routeIndex;
        int routeStep = 1;
        float waitTimer;
        float stateTimer;
        float reactTimer;
        float lostTimer;
        float lastSeenTime = -999f;
        float staggerTimer;
        float hitFlashTimer;
        float fireTimer;
        float windupTimer;
        bool windingUp;
        float flashBaseIntensity;
        Vector3 investigatePoint;
        Vector3 lastKnown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            if (vision == null) vision = GetComponent<GuardVision>();
            if (flashlight != null) flashBaseIntensity = flashlight.intensity;
            block = new MaterialPropertyBlock();
            if (bodyRenderers != null && bodyRenderers.Length > 0 && bodyRenderers[0] != null)
            {
                var mat = bodyRenderers[0].sharedMaterial;
                if (mat != null && mat.HasProperty(BaseColorId)) bodyColor = mat.GetColor(BaseColorId);
            }
            telegraph = FX.CreateLine(transform, "PH_FX_AimTelegraph", new Color(1f, 0.1f, 0.1f, 0.5f), 0.025f);
            telegraph.positionCount = 2;
            telegraph.enabled = false;
        }

        void OnEnable()
        {
            All.Add(this);
            NoiseSystem.Register(this);
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            All.Remove(this);
            NoiseSystem.Unregister(this);
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void Start()
        {
            // The agent is left disabled in the scene so it binds after the runtime NavMesh bake.
            agent.enabled = true;
            agent.Warp(transform.position);
            if (!agent.isOnNavMesh) Debug.LogWarning($"{name} is not on the NavMesh.", this);
            agent.speed = walkSpeed;
            GoToRoutePoint();
        }

        // ---------- helpers ----------

        bool AgentReady => agent.enabled && agent.isOnNavMesh;
        bool Arrived => AgentReady && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f;

        void SetDestination(Vector3 p)
        {
            if (!AgentReady) return;
            if (agent.hasPath && (agent.destination - p).sqrMagnitude < 0.25f) return;
            agent.SetDestination(p);
        }

        void SetStopped(bool stopped)
        {
            if (AgentReady) agent.isStopped = stopped;
        }

        void SetState(GuardState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        void FaceTowards(Vector3 point, float dt)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * dt);
        }

        /// <summary>Held by the player during a takedown. Frozen and blind until released or downed.</summary>
        public bool IsGrabbed { get; private set; }

        public bool CanBeTakenDown(Vector3 attackerPosition)
        {
            if (State == GuardState.Alert || State == GuardState.Down || IsGrabbed) return false;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            Vector3 toAttacker = attackerPosition - transform.position;
            toAttacker.y = 0f;
            return Vector3.Angle(forward, toAttacker) >= takedownAngle;
        }

        public void Grab()
        {
            IsGrabbed = true;
            windingUp = false;
            telegraph.enabled = false;
            SetStopped(true);
        }

        public void Release()
        {
            if (!IsGrabbed) return;
            IsGrabbed = false;
            if (State != GuardState.Down) SetStopped(false);
        }

        /// <summary>The player let go before the takedown finished: the guard breaks free, alerted.</summary>
        public void BreakFree(Vector3 attackerPosition)
        {
            if (!IsGrabbed) return;
            IsGrabbed = false;
            if (State == GuardState.Down) return;
            Awareness = 1f;
            EnterAlert(attackerPosition, true);
        }

        public void CompleteTakedown(GameObject attacker)
        {
            IsGrabbed = false;
            health.TakeDamage(new DamageInfo
            {
                amount = health.Current,
                point = transform.position + Vector3.up,
                direction = transform.forward,
                source = attacker,
                takedown = true
            });
        }

        // ---------- update ----------

        void Update()
        {
            if (State == GuardState.Down || IsGrabbed) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            vision.AngleMultiplier = State == GuardState.Alert ? 1.35f : 1f;
            vision.Tick();
            UpdateAwareness(dt);

            if (staggerTimer > 0f)
            {
                staggerTimer -= dt;
                if (staggerTimer <= 0f) SetStopped(false);
            }

            switch (State)
            {
                case GuardState.Patrol: TickPatrol(dt); break;
                case GuardState.Suspicious: TickSuspicious(dt); break;
                case GuardState.Alert: TickAlert(dt); break;
                case GuardState.Search: TickSearch(dt); break;
            }

            UpdateHead(dt);
            UpdateVisuals(dt);
        }

        void UpdateAwareness(float dt)
        {
            float v = vision.Visibility;
            if (v > 0f)
            {
                lastSeenTime = Time.time;
                lastKnown = vision.LastSeenPosition;
                float proximity = Mathf.Lerp(2.5f, 0.75f, vision.NormalizedDistance);
                float mult = State == GuardState.Search ? searchDetectMultiplier : 1f;
                Awareness += v * detectRate * proximity * mult * dt;
            }
            else if (State != GuardState.Alert && Time.time - lastSeenTime > 1f)
            {
                Awareness -= decayRate * dt;
            }
            Awareness = Mathf.Clamp01(Awareness);

            if (State == GuardState.Alert) return;
            if (Awareness >= 1f) EnterAlert(lastKnown, true);
            else if (State == GuardState.Patrol && Awareness >= suspiciousThreshold) EnterSuspicious(lastKnown);
            else if (State == GuardState.Suspicious && v > 0f) EnterSuspicious(lastKnown);
        }

        // ---------- patrol ----------

        void TickPatrol(float dt)
        {
            agent.speed = walkSpeed;
            if (route == null || route.Count == 0) return;
            if (!Arrived) return;

            waitTimer += dt;
            if (route.faceWaypointDirection)
            {
                Transform p = route.GetPoint(routeIndex);
                FaceTowards(transform.position + p.forward, dt);
            }
            if (waitTimer >= route.waitTime)
            {
                waitTimer = 0f;
                AdvanceRoute();
                GoToRoutePoint();
            }
        }

        void AdvanceRoute()
        {
            if (route.Count < 2) return;
            if (route.loop)
            {
                routeIndex = (routeIndex + 1) % route.Count;
                return;
            }
            int next = routeIndex + routeStep;
            if (next < 0 || next >= route.Count)
            {
                routeStep = -routeStep;
                next = routeIndex + routeStep;
            }
            routeIndex = next;
        }

        void GoToRoutePoint()
        {
            if (route != null && route.Count > 0) SetDestination(route.GetPoint(routeIndex).position);
        }

        void ReturnToPatrol()
        {
            SetState(GuardState.Patrol);
            SetStopped(false);
            waitTimer = 0f;
            GoToRoutePoint();
        }

        // ---------- suspicious ----------

        void EnterSuspicious(Vector3 point)
        {
            investigatePoint = point;
            stateTimer = 0f;
            if (State == GuardState.Suspicious)
            {
                if (reactTimer <= 0f) SetDestination(point);
                return;
            }
            SetState(GuardState.Suspicious);
            reactTimer = reactionTime;
            SetStopped(true);
        }

        void TickSuspicious(float dt)
        {
            stateTimer += dt;
            if (reactTimer > 0f)
            {
                reactTimer -= dt;
                FaceTowards(investigatePoint, dt);
                if (reactTimer <= 0f)
                {
                    SetStopped(false);
                    agent.speed = investigateSpeed;
                    SetDestination(investigatePoint);
                }
                return;
            }

            agent.speed = investigateSpeed;
            if (stateTimer >= suspiciousDuration && Time.time - lastSeenTime > 2f)
            {
                Awareness = Mathf.Min(Awareness, suspiciousThreshold * 0.5f);
                ReturnToPatrol();
            }
        }

        // ---------- alert ----------

        void EnterAlert(Vector3 point, bool broadcast)
        {
            lastKnown = point;
            lostTimer = 0f;
            Awareness = 1f;
            if (State != GuardState.Alert)
            {
                SetState(GuardState.Alert);
                fireTimer = fireWindup * 0.5f;
                windingUp = false;
            }
            if (staggerTimer <= 0f) SetStopped(false);
            if (broadcast) Radio(point);
        }

        void Radio(Vector3 point)
        {
            float r2 = radioRadius * radioRadius;
            foreach (var g in All)
            {
                if (g == this || g.State == GuardState.Down) continue;
                if ((g.transform.position - transform.position).sqrMagnitude <= r2) g.ReceiveRadio(point);
            }
        }

        public void ReceiveRadio(Vector3 point)
        {
            if (State == GuardState.Down || IsGrabbed) return;
            EnterAlert(point, false);
        }

        void TickAlert(float dt)
        {
            Transform t = vision.target;
            if (t == null) return;
            bool sees = vision.Visibility > 0.02f;
            if (sees)
            {
                lostTimer = 0f;
                lastKnown = t.position;
            }
            else
            {
                lostTimer += dt;
            }

            float distance = Vector3.Distance(transform.position, t.position);
            if (sees && distance <= fireRange)
            {
                SetStopped(true);
                FaceTowards(t.position, dt);
                TickShooting(dt);
            }
            else
            {
                windingUp = false;
                if (staggerTimer <= 0f) SetStopped(false);
                agent.speed = chaseSpeed;
                SetDestination(lastKnown);
            }

            if (lostTimer >= loseSightTime && (Arrived || lostTimer >= loseSightTime * 2f)) EnterSearch();
        }

        Vector3 MuzzlePosition => transform.position + Vector3.up * 1.3f + transform.forward * 0.45f;

        void TickShooting(float dt)
        {
            if (!windingUp)
            {
                fireTimer -= dt;
                if (fireTimer <= 0f)
                {
                    windingUp = true;
                    windupTimer = fireWindup;
                }
                return;
            }
            windupTimer -= dt;
            if (windupTimer <= 0f)
            {
                windingUp = false;
                fireTimer = fireCooldown;
                Shoot();
            }
        }

        void Shoot()
        {
            Transform t = vision.target;
            Vector3 origin = MuzzlePosition;
            Vector3 aimPoint = t.position + Vector3.up * 1.1f;
            var targetHealth = t.GetComponent<Health>();
            bool clear = !Occlusion.Blocked(origin, aimPoint, t, transform);
            bool hit = clear && UnityEngine.Random.value <= hitChance;

            Vector3 end = aimPoint;
            if (hit && targetHealth != null)
            {
                targetHealth.TakeDamage(new DamageInfo
                {
                    amount = fireDamage,
                    point = aimPoint,
                    direction = (aimPoint - origin).normalized,
                    source = gameObject
                });
            }
            else
            {
                Vector3 missPoint = aimPoint + UnityEngine.Random.insideUnitSphere * 1.5f;
                Vector3 dir = (missPoint - origin).normalized;
                end = Occlusion.Raycast(origin, dir, 40f, transform, null, out RaycastHit rh) ? rh.point : origin + dir * 40f;
            }

            FX.Line(origin, end, new Color(1f, 0.35f, 0.2f, 0.9f), 0.05f, 0.07f);
            NoiseSystem.Emit(transform.position, gunNoiseRadius, 1f, this);
        }

        // ---------- search ----------

        void EnterSearch()
        {
            SetState(GuardState.Search);
            stateTimer = 0f;
            waitTimer = 0f;
            Awareness = 0.6f;
            windingUp = false;
            SetStopped(false);
            agent.speed = investigateSpeed;
            SetDestination(lastKnown);
        }

        void TickSearch(float dt)
        {
            stateTimer += dt;
            agent.speed = investigateSpeed;
            if (Arrived)
            {
                waitTimer += dt;
                if (waitTimer > 1.5f)
                {
                    waitTimer = 0f;
                    if (TryRandomNavPoint(lastKnown, searchRadius, out Vector3 p)) SetDestination(p);
                }
            }
            if (stateTimer >= searchDuration)
            {
                Awareness = suspiciousThreshold * 0.5f;
                ReturnToPatrol();
            }
        }

        static bool TryRandomNavPoint(Vector3 center, float radius, out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = center;
            return false;
        }

        // ---------- hearing ----------

        public void HearNoise(Vector3 position, float loudness)
        {
            if (IsGrabbed) return;
            switch (State)
            {
                case GuardState.Down:
                    return;
                case GuardState.Alert:
                    if (loudness >= 0.9f && Time.time - lastSeenTime > 0.5f)
                    {
                        lastKnown = position;
                        lostTimer = 0f;
                    }
                    return;
                case GuardState.Search:
                    lastKnown = position;
                    stateTimer = 0f;
                    SetDestination(position);
                    return;
                default:
                    Awareness = Mathf.Max(Awareness, suspiciousThreshold + 0.1f + 0.3f * loudness);
                    EnterSuspicious(position);
                    return;
            }
        }

        // ---------- damage ----------

        void OnDamaged(DamageInfo info)
        {
            hitFlashTimer = 0.08f;
            if (health.IsDead) return;
            staggerTimer = 0.25f;
            SetStopped(true);
            if (AgentReady) agent.Move(info.direction * 0.4f);
            Vector3 source = info.source != null ? info.source.transform.position : transform.position;
            EnterAlert(source, true);
        }

        void OnDied(DamageInfo info)
        {
            SetState(GuardState.Down);
            windingUp = false;
            telegraph.enabled = false;
            if (agent.enabled) agent.enabled = false;
            if (flashlight != null) flashlight.enabled = false;
            var cone = GetComponentInChildren<VisionConeMesh>();
            if (cone != null) cone.gameObject.SetActive(false);
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            SetBodyColor(bodyColor * 0.4f);
            NoiseSystem.Emit(transform.position, info.takedown ? 2.5f : 6f, 0.6f, this);
            StartCoroutine(Topple(info.direction));
        }

        IEnumerator Topple(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = -transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, direction.normalized);
            Quaternion start = transform.rotation;
            Quaternion end = Quaternion.AngleAxis(90f, axis) * start;
            Vector3 startPos = transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                float k = t * t;
                transform.rotation = Quaternion.Slerp(start, end, k);
                transform.position = startPos + Vector3.up * (0.4f * k);
                yield return null;
            }
            transform.rotation = end;
        }

        // ---------- presentation ----------

        void UpdateHead(float dt)
        {
            if (head == null) return;
            bool sweep = (State == GuardState.Patrol && route != null && Arrived && waitTimer > 0.3f)
                         || (State == GuardState.Search && Arrived)
                         || (State == GuardState.Suspicious && reactTimer <= 0f && Arrived);
            float targetYaw = sweep ? Mathf.Sin(Time.time * sweepSpeed) * sweepAngle : 0f;
            float current = Mathf.DeltaAngle(0f, head.localEulerAngles.y);
            head.localRotation = Quaternion.Euler(0f, Mathf.MoveTowards(current, targetYaw, 120f * dt), 0f);
        }

        void UpdateVisuals(float dt)
        {
            if (flashlight != null)
            {
                flashlight.color = Color.Lerp(flashlight.color, CurrentColor, 1f - Mathf.Exp(-10f * dt));
                float intensity = flashBaseIntensity;
                if (windingUp) intensity *= Mathf.PingPong(Time.time * 20f, 1f) > 0.5f ? 1.3f : 0.6f;
                flashlight.intensity = intensity;
            }

            telegraph.enabled = windingUp && vision.target != null;
            if (telegraph.enabled)
            {
                telegraph.SetPosition(0, MuzzlePosition);
                telegraph.SetPosition(1, vision.target.position + Vector3.up * 1.1f);
            }

            hitFlashTimer -= dt;
            SetBodyColor(hitFlashTimer > 0f ? Color.white : bodyColor);
        }

        void SetBodyColor(Color c)
        {
            if (bodyRenderers == null) return;
            foreach (var r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
