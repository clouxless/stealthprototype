using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A swivel spotlight: sweeps back and forth between two ends, a fixed sweep every time
    /// (deterministic), slowing into each end and resting there for pauseAtEnds. Put it on a spot
    /// Light with a StealthLight; where the beam hits the floor is Lit.
    ///
    /// Two ways to sweep:
    /// - Yaw (default): turns side to side about the vertical axis, sweepAngle in all, centred on
    ///   the direction it starts in.
    /// - Track (useTrack): the light slides along a rail between two points, pointing straight
    ///   down, so its pool is a circle sliding over the floor.
    /// - Aim points (useAimPoints): the beam's aim slides along the line aimFrom -> aimTo (world
    ///   points), so it can tilt as well as turn and its pool tracks a path on the floor.
    ///
    /// Something else can take it over: hold parks it at holdK (0 = the start end, 1 = the far end),
    /// travelling there at sweep speed; let go and it sweeps on from where it is. K is where it is
    /// now. Use phase to start two lights out of step so a gap opens.
    /// </summary>
    public class SwivelLight : MonoBehaviour
    {
        [Tooltip("Total sweep, degrees (half each side of the starting direction). Yaw mode only.")]
        public float sweepAngle = 80f;
        [Tooltip("Seconds for one sweep from one end to the other.")]
        public float sweepTime = 11f;
        [Tooltip("0..1 offset into the cycle at start.")]
        [Range(0f, 1f)] public float phase;
        [Tooltip("Seconds it rests at each end of the sweep.")]
        public float pauseAtEnds = 0.5f;

        [Header("Track (instead of yaw): slides along a rail, pointing straight down")]
        [Tooltip("The light moves between trackFrom and trackTo (world points) pointing straight down, so its pool is a circle that slides along the floor.")]
        public bool useTrack;
        public Vector3 trackFrom;
        public Vector3 trackTo;
        [Tooltip("Optional stops in between, in order (a rail that bends): trackFrom -> these -> trackTo. Speed along the rail is even.")]
        public Vector3[] trackStops = new Vector3[0];

        /// <summary>The point a fraction t (0..1) of the way along the rail, by length.</summary>
        Vector3 AlongTrack(float t)
        {
            if (trackStops == null || trackStops.Length == 0) return Vector3.Lerp(trackFrom, trackTo, t);
            int n = trackStops.Length + 2;
            Vector3 P(int i) => i == 0 ? trackFrom : i == n - 1 ? trackTo : trackStops[i - 1];
            float total = 0f;
            for (int i = 1; i < n; i++) total += Vector3.Distance(P(i - 1), P(i));
            float d = t * total;
            for (int i = 1; i < n; i++)
            {
                float seg = Vector3.Distance(P(i - 1), P(i));
                if (d <= seg || i == n - 1) return Vector3.Lerp(P(i - 1), P(i), seg > 0f ? Mathf.Clamp01(d / seg) : 1f);
                d -= seg;
            }
            return trackTo;
        }

        [Header("Aim points (instead of yaw)")]
        public bool useAimPoints;
        public Vector3 aimFrom;
        public Vector3 aimTo;

        [Header("Held by script")]
        [Tooltip("While on, the light goes to holdK and stays there.")]
        public bool hold;
        [Range(0f, 1f)] public float holdK;

        /// <summary>Where along the sweep it is now: 0 = start end (aimFrom), 1 = far end (aimTo). Not eased.</summary>
        public float K => k;
        /// <summary>Counts each arrival at either end of a free sweep (not while held).</summary>
        public int EndsReached { get; private set; }

        Quaternion baseRotation;
        float k;            // 0..1, linear
        int direction = 1;  // +1 toward the far end, -1 back
        float pauseTimer;

        void Awake()
        {
            baseRotation = transform.rotation;
            // Start where the old time-based cycle would have been at phase.
            float cycle = 2f * (sweepTime + pauseAtEnds);
            float t = Mathf.Repeat(phase, 1f) * cycle;
            if (t < sweepTime) { k = t / sweepTime; direction = 1; }
            else if (t < sweepTime + pauseAtEnds) { k = 1f; direction = -1; pauseTimer = sweepTime + pauseAtEnds - t; }
            else if (t < 2f * sweepTime + pauseAtEnds) { k = 1f - (t - sweepTime - pauseAtEnds) / sweepTime; direction = -1; }
            else { k = 0f; direction = 1; pauseTimer = cycle - t; }
            Apply();
        }

        void Update()
        {
            float step = Time.deltaTime / Mathf.Max(0.01f, sweepTime);
            if (hold)
            {
                k = Mathf.MoveTowards(k, holdK, step);
                // Let go and it carries on away from where it was held.
                direction = holdK >= 0.5f ? -1 : 1;
                pauseTimer = 0f;
            }
            else if (pauseTimer > 0f)
            {
                pauseTimer -= Time.deltaTime;
            }
            else
            {
                k += direction * step;
                if (k >= 1f) { k = 1f; direction = -1; pauseTimer = pauseAtEnds; EndsReached++; }
                else if (k <= 0f) { k = 0f; direction = 1; pauseTimer = pauseAtEnds; EndsReached++; }
            }
            Apply();
        }

        void Apply()
        {
            float e = k * k * (3f - 2f * k); // eased, so it slows into each end (readable)
            if (useTrack)
            {
                transform.SetPositionAndRotation(AlongTrack(e), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                return;
            }
            if (useAimPoints)
            {
                Vector3 dir = Vector3.Lerp(aimFrom, aimTo, e) - transform.position;
                if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                return;
            }
            float yaw = Mathf.Lerp(-sweepAngle * 0.5f, sweepAngle * 0.5f, e);
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * baseRotation;
        }
    }
}
