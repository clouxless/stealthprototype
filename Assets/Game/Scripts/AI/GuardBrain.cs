using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace StealthProto
{
    public enum GuardState { Patrol, Suspicious, Alert, Search, Down }

    /// <summary>
    /// Guard state machine. No dice rolls anywhere: the same stimulus always gets the same
    /// response, and every state change is announced by a visible tell first.
    ///
    /// Patrol: walk the route, fixed pauses and head sweeps at waypoints.
    /// Suspicious: "?" tell (stop, turn), walk the shortest path to the stimulus, one fixed
    ///   look-around, back to the route.
    /// Alert: "!" tell (freeze, face you) before anything else, then radio nearby guards, chase,
    ///   and shoot after a visible wind-up. A shot always hits unless you break line of sight.
    /// Search: lost you. A short fixed pattern at the last known position (shown by a marker),
    ///   then back to patrol. Noise moves the search.
    /// Down: taken out.
    ///
    /// After the first alert the room stays changed: guards remain wary (faster to notice) and
    /// any AlarmLight in the scene switches on for good.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public class GuardBrain : MonoBehaviour, INoiseListener
    {
        public enum Tell { None, Question, Exclamation }

        public static readonly List<GuardBrain> All = new List<GuardBrain>();
        /// <summary>True once any guard has gone fully alert in this scene.</summary>
        public static bool RoomAlerted { get; private set; }
        /// <summary>Fired once, the first time any guard finishes its alert tell.</summary>
        public static event Action RoomAlertRaised;

        [Header("References")]
        public PatrolRoute route;
        public GuardVision vision;
        public Transform head;
        public Light flashlight;
        public Renderer[] bodyRenderers;

        [Header("Movement")]
        public float walkSpeed = 1.4f;
        public float investigateSpeed = 2.6f;
        public float chaseSpeed = 5.5f;
        public float turnSpeed = 360f;

        [Header("Awareness")]
        public float detectRate = 2f;
        public float searchDetectMultiplier = 2f;
        [Tooltip("Detection multiplier for every guard once the room has been alerted.")]
        public float waryDetectMultiplier = 1.5f;
        public float decayRate = 0.12f;
        [Range(0f, 1f)] public float suspiciousThreshold = 0.3f;
        [Tooltip("Takedowns: attacks from more than this angle off the guard's facing count as from behind.")]
        public float takedownAngle = 100f;

        [Header("Tells (seconds)")]
        [Tooltip("\"?\" pause before investigating.")]
        public float reactionTime = 0.6f;
        [Tooltip("\"!\" freeze before chasing and shooting. Your window to break line of sight.")]
        public float alertTellTime = 0.6f;
        [Tooltip("Shorter \"!\" when alerted by radio or by being hit.")]
        public float secondhandTellTime = 0.35f;

        [Header("Investigate and hunt (seconds / metres)")]
        public float lookAroundTime = 3f;
        public float loseSightTime = 1.2f;
        public float searchPointLookTime = 2.2f;
        [Tooltip("Second search point: this far past the last known position, in the direction of the chase.")]
        public float searchStep = 3.5f;
        [Tooltip("Safety net if a point can't be reached.")]
        public float investigateTimeout = 12f;
        public float radioRadius = 22f;

        [Header("Head sweep")]
        public float sweepAngle = 45f;
        public float sweepSpeed = 1.6f;

        [Header("Shooting")]
        public int fireDamage = 1;
        public float fireRange = 14f;
        public float fireWindup = 0.6f;
        public float fireCooldown = 1.1f;
        public float gunNoiseRadius = 15f;

        [Header("State colours")]
        public Color patrolColor = new Color(1f, 0.93f, 0.78f);
        public Color suspiciousColor = new Color(1f, 0.7f, 0.15f);
        public Color alertColor = new Color(1f, 0.15f, 0.1f);

        public GuardState State { get; private set; } = GuardState.Patrol;
        public float Awareness { get; private set; }
        public event Action<GuardState> StateChanged;
        public Vector3 ListenPosition => transform.position;

        /// <summary>The tell currently shown over this guard.</summary>
        public Tell CurrentTell
        {
            get
            {
                if (State == GuardState.Alert && alertTellTimer > 0f) return Tell.Exclamation;
                if (State == GuardState.Suspicious && suspiciousPhase == SuspiciousPhase.React) return Tell.Question;
                return Tell.None;
            }
        }

        /// <summary>True while the guard has lost sight of you and is heading for HuntPoint.</summary>
        public bool IsHunting => State == GuardState.Search
                                 || (State == GuardState.Alert && alertTellTimer <= 0f && lostTimer > 0.25f);
        public Vector3 HuntPoint => State == GuardState.Search && searchIndex < searchPoints.Count ? searchPoints[0] : lastKnown;
        /// <summary>Searching guards look up at higher layers (see GuardVision).</summary>
        public bool IsSearching => State == GuardState.Search;

        public Color CurrentColor => State switch
        {
            GuardState.Suspicious => suspiciousColor,
            GuardState.Search => suspiciousColor,
            GuardState.Alert => alertColor,
            _ => patrolColor
        };

        enum SuspiciousPhase { React, Walk, Look }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SilhouetteColorId = Shader.PropertyToID("_Color");

        NavMeshAgent agent;
        Health health;
        LineRenderer telegraph;
        MaterialPropertyBlock block;
        Color bodyColor = Color.white;

        int routeIndex;
        int routeStep = 1;
        float waitTimer;
        float stateTimer;
        float phaseTimer;
        float lostTimer;
        float lastSeenTime = -999f;
        float staggerTimer;
        float hitFlashTimer;
        float fireTimer;
        float windupTimer;
        bool windingUp;
        float alertTellTimer;
        bool radioPending;
        float flashBaseIntensity;
        float sweepTimer;
        bool wasSweeping;
        SuspiciousPhase suspiciousPhase;
        Vector3 investigatePoint;
        Vector3 lastKnown;
        readonly List<Vector3> searchPoints = new List<Vector3>();
        int searchIndex;
        bool searchLooking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            RoomAlerted = false;
            RoomAlertRaised = null;
            // A restart reloads the scene: the room starts calm again.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single) RoomAlerted = false;
        }

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

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
        }

        // ---------- takedowns ----------

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
            EnterAlert(attackerPosition, true, secondhandTellTime);
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
            vision.LookUp = IsSearching;
            vision.Tick();
            UpdateAwareness(dt);

            if (staggerTimer > 0f)
            {
                staggerTimer -= dt;
                if (staggerTimer <= 0f && alertTellTimer <= 0f) SetStopped(false);
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
                if (RoomAlerted) mult *= waryDetectMultiplier;
                Awareness += v * detectRate * proximity * mult * dt;
            }
            else if (State != GuardState.Alert && Time.time - lastSeenTime > 1f)
            {
                Awareness -= decayRate * dt;
            }
            Awareness = Mathf.Clamp01(Awareness);

            if (State == GuardState.Alert) return;
            if (Awareness >= 1f) EnterAlert(lastKnown, true, alertTellTime);
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
            agent.speed = walkSpeed;
            GoToRoutePoint();
        }

        // ---------- suspicious: "?" tell, go look, fixed look-around, back ----------

        void EnterSuspicious(Vector3 point)
        {
            investigatePoint = point;
            if (State == GuardState.Suspicious)
            {
                // New stimulus: same response, go to the new point (no second "?" pause).
                if (suspiciousPhase != SuspiciousPhase.React)
                {
                    suspiciousPhase = SuspiciousPhase.Walk;
                    phaseTimer = 0f;
                    SetDestination(point);
                }
                return;
            }
            SetState(GuardState.Suspicious);
            stateTimer = 0f;
            suspiciousPhase = SuspiciousPhase.React;
            phaseTimer = 0f;
            SetStopped(true);
        }

        void TickSuspicious(float dt)
        {
            stateTimer += dt;
            phaseTimer += dt;
            agent.speed = investigateSpeed;

            switch (suspiciousPhase)
            {
                case SuspiciousPhase.React:
                    FaceTowards(investigatePoint, dt);
                    if (phaseTimer >= reactionTime)
                    {
                        suspiciousPhase = SuspiciousPhase.Walk;
                        phaseTimer = 0f;
                        SetStopped(false);
                        SetDestination(investigatePoint);
                    }
                    break;

                case SuspiciousPhase.Walk:
                    if (Arrived || phaseTimer >= investigateTimeout)
                    {
                        suspiciousPhase = SuspiciousPhase.Look;
                        phaseTimer = 0f;
                    }
                    break;

                case SuspiciousPhase.Look:
                    if (phaseTimer >= lookAroundTime)
                    {
                        Awareness = Mathf.Min(Awareness, suspiciousThreshold * 0.5f);
                        ReturnToPatrol();
                    }
                    break;
            }
        }

        // ---------- alert: "!" tell, radio, chase, shoot ----------

        void EnterAlert(Vector3 point, bool broadcast, float tellTime)
        {
            lastKnown = point;
            lostTimer = 0f;
            Awareness = 1f;
            if (State != GuardState.Alert)
            {
                SetState(GuardState.Alert);
                alertTellTimer = tellTime;
                radioPending = broadcast;
                windingUp = false;
                fireTimer = 0f;
                SetStopped(true);
                return;
            }
            if (broadcast && alertTellTimer <= 0f) Radio(point);
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
            if (State == GuardState.Alert)
            {
                if (lostTimer > 0f) lastKnown = point;
                return;
            }
            EnterAlert(point, false, secondhandTellTime);
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

            // "!" tell: freeze and face the player before doing anything.
            if (alertTellTimer > 0f)
            {
                alertTellTimer -= dt;
                FaceTowards(lastKnown, dt);
                if (alertTellTimer <= 0f)
                {
                    if (!RoomAlerted)
                    {
                        RoomAlerted = true;
                        RoomAlertRaised?.Invoke();
                    }
                    if (radioPending) Radio(lastKnown);
                    radioPending = false;
                    NoiseSystem.Emit(transform.position, 10f, 0.9f, this, SoundKind.Shout, gameObject);
                    if (staggerTimer <= 0f) SetStopped(false);
                }
                return;
            }

            if (!sees) lostTimer += dt;

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

            if (lostTimer >= loseSightTime) EnterSearch(lastKnown);
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

        /// <summary>Always hits unless line of sight is broken at the moment of firing.</summary>
        void Shoot()
        {
            Transform t = vision.target;
            Vector3 origin = MuzzlePosition;
            Vector3 aimPoint = t.position + Vector3.up * 1.1f;
            Vector3 dir = (aimPoint - origin).normalized;
            Vector3 end = aimPoint;

            if (!Occlusion.Blocked(origin, aimPoint, t, transform))
            {
                var targetHealth = t.GetComponent<Health>();
                if (targetHealth != null)
                {
                    targetHealth.TakeDamage(new DamageInfo
                    {
                        amount = fireDamage,
                        point = aimPoint,
                        direction = dir,
                        source = gameObject
                    });
                }
            }
            else if (Occlusion.Raycast(origin, dir, 40f, transform, t, out RaycastHit hit))
            {
                end = hit.point;
            }

            FX.Line(origin, end, new Color(1f, 0.35f, 0.2f, 0.9f), 0.05f, 0.07f);
            NoiseSystem.Emit(transform.position, gunNoiseRadius, 1f, this, SoundKind.Gunshot, gameObject);
        }

        // ---------- search: short fixed pattern ----------

        void EnterSearch(Vector3 center)
        {
            SetState(GuardState.Search);
            stateTimer = 0f;
            Awareness = 0.6f;
            windingUp = false;
            SetStopped(false);
            agent.speed = investigateSpeed;
            BuildSearchPattern(center);
            searchIndex = 0;
            searchLooking = false;
            phaseTimer = 0f;
            SetDestination(searchPoints[0]);
        }

        /// <summary>The last known position, then one step further the way the chase was heading.</summary>
        void BuildSearchPattern(Vector3 center)
        {
            searchPoints.Clear();
            searchPoints.Add(center);
            Vector3 heading = Flat(center - transform.position, transform.forward);
            if (NavMesh.SamplePosition(center + heading * searchStep, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                searchPoints.Add(hit.position);
        }

        void TickSearch(float dt)
        {
            stateTimer += dt;
            phaseTimer += dt;
            agent.speed = investigateSpeed;

            if (!searchLooking)
            {
                if (Arrived || phaseTimer >= investigateTimeout)
                {
                    searchLooking = true;
                    phaseTimer = 0f;
                }
                return;
            }

            if (phaseTimer < searchPointLookTime) return;
            searchIndex++;
            if (searchIndex >= searchPoints.Count)
            {
                Awareness = suspiciousThreshold * 0.5f;
                ReturnToPatrol();
                return;
            }
            searchLooking = false;
            phaseTimer = 0f;
            SetDestination(searchPoints[searchIndex]);
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
                    if (alertTellTimer <= 0f && loudness >= 0.9f && Time.time - lastSeenTime > 0.5f)
                        lastKnown = position;
                    return;
                case GuardState.Search:
                    // Same response every time: restart the short pattern at the noise.
                    EnterSearch(position);
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
            EnterAlert(source, true, secondhandTellTime);
        }

        void OnDied(DamageInfo info)
        {
            SetState(GuardState.Down);
            windingUp = false;
            alertTellTimer = 0f;
            telegraph.enabled = false;
            if (agent.enabled) agent.enabled = false;
            if (flashlight != null) flashlight.enabled = false;
            var cone = GetComponentInChildren<VisionConeMesh>();
            if (cone != null) cone.gameObject.SetActive(false);
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            SetBodyColor(bodyColor * 0.4f, new Color(0.4f, 0.4f, 0.4f, 0.35f));
            NoiseSystem.Emit(transform.position, info.takedown ? 2.5f : 6f, 0.6f, this, SoundKind.Impact, gameObject);
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
                transform.position = startPos + Vector3.up * (0.3f * k);
                yield return null;
            }
            transform.rotation = end;
        }

        // ---------- presentation ----------

        void UpdateHead(float dt)
        {
            if (head == null) return;
            bool sweep = (State == GuardState.Patrol && route != null && Arrived && waitTimer > 0.3f)
                         || (State == GuardState.Search && searchLooking)
                         || (State == GuardState.Suspicious && suspiciousPhase == SuspiciousPhase.Look);

            // Deterministic: every sweep starts centred and turns the same way.
            if (sweep && !wasSweeping) sweepTimer = 0f;
            wasSweeping = sweep;
            if (sweep) sweepTimer += dt;

            float targetYaw = sweep ? Mathf.Sin(sweepTimer * sweepSpeed) * sweepAngle : 0f;
            float current = Mathf.DeltaAngle(0f, head.localEulerAngles.y);
            head.localRotation = Quaternion.Euler(0f, Mathf.MoveTowards(current, targetYaw, 120f * dt), 0f);
        }

        void UpdateVisuals(float dt)
        {
            if (flashlight != null)
            {
                flashlight.color = Color.Lerp(flashlight.color, CurrentColor, 1f - Mathf.Exp(-10f * dt));
                float intensity = flashBaseIntensity;
                bool flashing = windingUp || (State == GuardState.Alert && alertTellTimer > 0f);
                if (flashing) intensity *= Mathf.PingPong(Time.time * 20f, 1f) > 0.5f ? 1.3f : 0.6f;
                flashlight.intensity = intensity;
            }

            telegraph.enabled = windingUp && vision.target != null;
            if (telegraph.enabled)
            {
                telegraph.SetPosition(0, MuzzlePosition);
                telegraph.SetPosition(1, vision.target.position + Vector3.up * 1.1f);
            }

            hitFlashTimer -= dt;
            Color silhouette = CurrentColor;
            silhouette.a = 0.55f;
            SetBodyColor(hitFlashTimer > 0f ? Color.white : bodyColor, silhouette);
        }

        void SetBodyColor(Color c, Color silhouette)
        {
            if (bodyRenderers == null) return;
            foreach (var r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(SilhouetteColorId, silhouette);
                r.SetPropertyBlock(block);
            }
        }
    }
}
