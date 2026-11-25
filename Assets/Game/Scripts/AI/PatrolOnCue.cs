using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A scripted walk on a cue: the guard keeps his first route (a post) until the player steps
    /// into cueZone, then walks `walk` point by point. When he reaches its last point he takes up
    /// `thenRoute` for good. If he's busy (suspicious, searching) he picks it up when he's back on
    /// patrol. One-shot; stops if he's down.
    /// </summary>
    public class PatrolOnCue : MonoBehaviour
    {
        public GuardBrain guard;
        public PatrolRoute walk;
        public PatrolRoute thenRoute;
        public Transform player;
        [Tooltip("World-space box: the walk starts when the player steps into it.")]
        public Bounds cueZone;
        public float cueDelay;

        enum Phase { Waiting, Delay, Walking, Done }
        Phase phase;
        float timer;

        void Update()
        {
            if (guard == null || walk == null || walk.Count == 0) { enabled = false; return; }
            if (guard.State == GuardState.Down) { enabled = false; return; }
            switch (phase)
            {
                case Phase.Waiting:
                    if (player == null || !cueZone.Contains(player.position)) return;
                    timer = cueDelay;
                    phase = Phase.Delay;
                    break;
                case Phase.Delay:
                    timer -= Time.deltaTime;
                    if (timer > 0f) return;
                    guard.AssignRoute(walk);
                    phase = Phase.Walking;
                    break;
                case Phase.Walking:
                    int last = walk.Count - 1;
                    if (guard.route != walk || guard.RouteIndex != last || !guard.StandingAt(walk.GetPoint(last).position, 1.2f)) return;
                    if (thenRoute != null) guard.AssignRoute(thenRoute);
                    phase = Phase.Done;
                    enabled = false;
                    break;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(cueZone.center, cueZone.size);
        }
    }
}
