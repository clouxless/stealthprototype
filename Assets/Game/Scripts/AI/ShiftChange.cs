using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Two guards trading a beat (a shift change). One walks the patrol route, the other stands at
    /// the post. Every reliefInterval, the guard at the post walks to the meeting point; when he
    /// gets there the patrolling guard joins him, they stand a moment, then swap: the newcomer
    /// takes the patrol and the other walks back up to the post. Then it repeats with roles swapped.
    ///
    /// The first relief starts as soon as the player enters startZone (so it happens while they're
    /// there), or after reliefInterval if they never do. It pauses while either guard is off patrol
    /// (suspicious, searching, alert) and stops for good if either is down.
    /// </summary>
    public class ShiftChange : MonoBehaviour
    {
        public GuardBrain patrolling;
        public GuardBrain onPost;
        public PatrolRoute patrolRoute;
        [Tooltip("One point: where the guard off patrol stands.")]
        public PatrolRoute postRoute;
        [Tooltip("One point: where the guard coming off the post stops to meet the other.")]
        public PatrolRoute meetRoute;
        [Tooltip("One point, a step from meetRoute's, facing it: where the patrolling guard joins him.")]
        public PatrolRoute partnerRoute;
        public float reliefInterval = 35f;
        [Tooltip("Seconds they stand together before swapping.")]
        public float handoverTime = 3f;
        public Transform player;
        [Tooltip("World-space box; the first relief starts when the player steps into it.")]
        public Bounds startZone;
        public float startDelay = 1.5f;

        enum Phase { Waiting, Relieving, Meeting, Handover }
        Phase phase = Phase.Waiting;
        float timer;
        bool started;

        void Start()
        {
            timer = reliefInterval;
            if (patrolling != null) patrolling.AssignRoute(patrolRoute);
            if (onPost != null) onPost.AssignRoute(postRoute);
        }

        void Update()
        {
            if (patrolling == null || onPost == null) return;
            if (patrolling.State == GuardState.Down || onPost.State == GuardState.Down) { enabled = false; return; }
            bool bothOnPatrol = patrolling.State == GuardState.Patrol && onPost.State == GuardState.Patrol;
            Vector3 meet = meetRoute.GetPoint(0).position;

            switch (phase)
            {
                case Phase.Waiting:
                    if (!started && player != null && startZone.Contains(player.position))
                    {
                        started = true;
                        timer = Mathf.Min(timer, startDelay);
                    }
                    if (!bothOnPatrol) return;
                    timer -= Time.deltaTime;
                    if (timer > 0f) return;
                    started = true;
                    onPost.AssignRoute(meetRoute); // walks down to meet him
                    phase = Phase.Relieving;
                    break;

                case Phase.Relieving:
                    if (!onPost.StandingAt(meet, 1.5f)) return;
                    patrolling.AssignRoute(partnerRoute);
                    phase = Phase.Meeting;
                    break;

                case Phase.Meeting:
                    if (!patrolling.StandingAt(partnerRoute.GetPoint(0).position, 1.5f)) return;
                    timer = handoverTime;
                    phase = Phase.Handover;
                    break;

                case Phase.Handover:
                    if (!bothOnPatrol) return;
                    timer -= Time.deltaTime;
                    if (timer > 0f) return;
                    onPost.AssignRoute(patrolRoute);
                    patrolling.AssignRoute(postRoute);
                    (patrolling, onPost) = (onPost, patrolling);
                    timer = reliefInterval;
                    phase = Phase.Waiting;
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.5f);
            Gizmos.DrawWireCube(startZone.center, startZone.size);
        }
    }
}
