using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Two guards trading a beat (a shift change). One walks the patrol route, the other stands at
    /// the post. When relief is due, the guard at the post walks to the meeting point; when he gets
    /// there the patrolling guard joins him, they stand a moment, then swap: the newcomer takes the
    /// patrol (starting at handoverStartIndex, or handoverRoute if set) and the other walks back up to the post. Then it
    /// repeats every reliefInterval with roles swapped.
    ///
    /// Nothing happens until the player steps into startZone: the first relief starts startDelay
    /// later, so it always plays out while they're there. It pauses while either guard is off
    /// patrol (suspicious, searching, alert) and stops for good if either is down.
    ///
    /// If the patrolling guard is down, the post guard finds out when he comes to relieve him (at
    /// once if he's already on his way): he walks to the patrol spot, finds nobody there, and raises
    /// the alarm (full alert, radioed).
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
        [Tooltip("Seconds between reliefs, after the first.")]
        public float reliefInterval = 45f;
        [Tooltip("Seconds they stand together before swapping.")]
        public float handoverTime = 3f;
        [Tooltip("Where on patrolRoute the newcomer starts after the handover.")]
        public int handoverStartIndex;
        [Tooltip("Optional: the route the newcomer takes after the handover instead of patrolRoute (every swap).")]
        public PatrolRoute handoverRoute;
        [Tooltip("Optional: from the first swap on, the post is here instead (the guard going up walks to it, and later reliefs start from it).")]
        public PatrolRoute handoverPostRoute;
        public Transform player;
        [Tooltip("World-space box; the first relief starts when the player steps into it.")]
        public Bounds startZone;
        public float startDelay = 1.5f;

        public enum Phase { Waiting, Relieving, Meeting, Handover }
        public Phase CurrentPhase { get; private set; } = Phase.Waiting;
        /// <summary>True once the player has reached startZone.</summary>
        public bool Started { get; private set; }
        public Vector3 PostPoint => postRoute.GetPoint(0).position;

        /// <summary>
        /// Who's on duty at the post (and so working its searchlight): the post guard while he's
        /// standing there, and the relief from the moment he sets off until the swap. Null in the
        /// gap after a swap while the new post guard walks up.
        /// </summary>
        public GuardBrain PostOnDuty
        {
            get
            {
                if (patrolling == null || onPost == null || !enabled) return null;
                if (CurrentPhase != Phase.Waiting) return onPost;
                return onPost.State == GuardState.Patrol && onPost.route == postRoute && onPost.StandingAt(PostPoint, 1.5f) ? onPost : null;
            }
        }

        float timer;
        bool partnerLost;

        void Start()
        {
            if (patrolling != null) patrolling.AssignRoute(patrolRoute);
            if (onPost != null) onPost.AssignRoute(postRoute);
        }

        void Update()
        {
            if (patrolling == null || onPost == null) return;
            if (onPost.State == GuardState.Down) { enabled = false; return; }
            if (patrolling.State == GuardState.Down)
            {
                // Relief due (or under way): he finds nobody there and goes to see.
                if (!Started && player != null && startZone.Contains(player.position)) { Started = true; timer = startDelay; }
                bool due = CurrentPhase != Phase.Waiting || (Started && (timer -= Time.deltaTime) <= 0f);
                if (!due) return;
                Vector3 spot = patrolRoute.GetPoint(0).position;
                if (!partnerLost)
                {
                    if (onPost.State != GuardState.Patrol) return; // busy: he'll go when he's back
                    partnerLost = true;
                    onPost.AssignRoute(patrolRoute); // goes to where his partner should be
                    return;
                }
                if (onPost.State == GuardState.Alert) { enabled = false; return; } // already on to you
                if (onPost.State != GuardState.Patrol) return; // busy: carries on to the spot after
                if ((onPost.transform.position - spot).sqrMagnitude > 2.5f * 2.5f) return;
                onPost.RaiseAlarm(spot);
                enabled = false;
                return;
            }
            bool bothOnPatrol = patrolling.State == GuardState.Patrol && onPost.State == GuardState.Patrol;
            Vector3 meet = meetRoute.GetPoint(0).position;

            switch (CurrentPhase)
            {
                case Phase.Waiting:
                    if (!Started)
                    {
                        if (player == null || !startZone.Contains(player.position)) return;
                        Started = true;
                        timer = startDelay;
                    }
                    if (!bothOnPatrol) return;
                    timer -= Time.deltaTime;
                    if (timer > 0f) return;
                    onPost.AssignRoute(meetRoute); // walks down to meet him
                    CurrentPhase = Phase.Relieving;
                    break;

                case Phase.Relieving:
                    if (!onPost.StandingAt(meet, 1.5f)) return;
                    patrolling.AssignRoute(partnerRoute);
                    CurrentPhase = Phase.Meeting;
                    break;

                case Phase.Meeting:
                    if (!patrolling.StandingAt(partnerRoute.GetPoint(0).position, 1.5f)) return;
                    timer = handoverTime;
                    CurrentPhase = Phase.Handover;
                    break;

                case Phase.Handover:
                    if (!bothOnPatrol) return;
                    timer -= Time.deltaTime;
                    if (timer > 0f) return;
                    if (handoverRoute != null) onPost.AssignRoute(handoverRoute);
                    else onPost.AssignRoute(patrolRoute, handoverStartIndex);
                    if (handoverPostRoute != null) postRoute = handoverPostRoute;
                    patrolling.AssignRoute(postRoute);
                    (patrolling, onPost) = (onPost, patrolling);
                    timer = reliefInterval;
                    CurrentPhase = Phase.Waiting;
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
