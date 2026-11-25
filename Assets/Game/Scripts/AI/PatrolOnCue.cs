using UnityEngine;
using UnityEngine.AI;

namespace StealthProto
{
    /// <summary>
    /// A scripted walk on a cue: the guard keeps his first route (a post) until the cue, then walks
    /// `walk` point by point. When he reaches its last point he takes up `thenRoute` for good. If
    /// he's busy (suspicious, searching) he picks it up when he's back on patrol. One-shot; stops if
    /// he's down.
    ///
    /// Two kinds of cue:
    /// - cueZone only: the walk starts when the player steps into the box.
    /// - Rendezvous (meetIndex >= 0): timed so that a player walking straight at full speed meets
    ///   him at walk point meetIndex. While the player is in cueZone, the walk starts as soon as
    ///   the player's time to the meet point (straight-line, at their walk speed) is down to his
    ///   (his path along the NavMesh, at paceSpeed). Then, until he gets there, his walking speed
    ///   is re-set every frame to keep the appointment, between minSpeed and maxSpeed: if you stop
    ///   or dawdle he drops to minSpeed and you meet him further on. After the meet point he walks
    ///   at his normal speed.
    /// </summary>
    public class PatrolOnCue : MonoBehaviour
    {
        public GuardBrain guard;
        public PatrolRoute walk;
        public PatrolRoute thenRoute;
        public Transform player;
        [Tooltip("World-space box: the player has to be in it for the cue.")]
        public Bounds cueZone;
        public float cueDelay;

        [Header("Rendezvous")]
        [Tooltip("Walk point where a player walking straight meets him. -1 = no rendezvous (plain cue).")]
        public int meetIndex = -1;
        [Tooltip("His speed for the cue estimate.")]
        public float paceSpeed = 2.6f;
        public float minSpeed = 1.4f;
        public float maxSpeed = 3.2f;
        [Tooltip("The player's walking speed; 0 = read it from PlayerMotor.")]
        public float playerSpeed;

        [Header("Camera")]
        [Tooltip("Optional: switched off for good once he's on the walk (from walk point 1 on) and has walked past the player, so the view goes back to top-down.")]
        public CameraZone zoneUntilPassed;

        enum Phase { Waiting, Delay, Walking, Done }
        Phase phase;
        float timer;
        float normalWalkSpeed;
        float[] legLengths;   // NavMesh length from walk point i-1 to i (index 0 unused)
        NavMeshPath path;

        void Start()
        {
            if (guard != null) normalWalkSpeed = guard.walkSpeed;
            if (playerSpeed <= 0f && player != null)
            {
                var motor = player.GetComponent<PlayerMotor>();
                playerSpeed = motor != null ? motor.moveSpeed : 4.5f;
            }
            path = new NavMeshPath();
            if (walk != null && meetIndex >= 0)
            {
                legLengths = new float[walk.Count];
                for (int i = 1; i < walk.Count; i++)
                    legLengths[i] = PathLength(walk.GetPoint(i - 1).position, walk.GetPoint(i).position);
            }
        }

        float PathLength(Vector3 from, Vector3 to)
        {
            if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                float d = 0f;
                for (int i = 1; i < path.corners.Length; i++) d += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                return d;
            }
            return Vector3.Distance(from, to);
        }

        Vector3 MeetPoint => walk.GetPoint(meetIndex).position;

        /// <summary>The player's seconds to the meet point, walking straight at full speed.</summary>
        float PlayerEta()
        {
            Vector3 d = MeetPoint - player.position;
            d.y = 0f;
            return d.magnitude / Mathf.Max(0.1f, playerSpeed);
        }

        /// <summary>The guard's path length to the meet point: to his next walk point, then the legs after it.</summary>
        float GuardRemaining(int nextIndex)
        {
            float d = PathLength(guard.transform.position, walk.GetPoint(nextIndex).position);
            for (int i = nextIndex + 1; i <= meetIndex; i++) d += legLengths[i];
            return d;
        }

        void Update()
        {
            if (guard == null || walk == null || walk.Count == 0) { enabled = false; return; }
            if (guard.State == GuardState.Down) { DropZone(); Finish(); return; }
            if (phase >= Phase.Walking && zoneUntilPassed != null && player != null && HasPassedPlayer()) DropZone();
            if (phase == Phase.Done) return;
            bool rendezvous = meetIndex >= 0 && meetIndex < walk.Count && legLengths != null;
            switch (phase)
            {
                case Phase.Waiting:
                    if (player == null || !cueZone.Contains(player.position)) return;
                    if (rendezvous && guard.State == GuardState.Patrol && PlayerEta() > GuardRemaining(0) / paceSpeed) return;
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
                    bool onWalk = guard.route == walk;
                    if (rendezvous && onWalk)
                    {
                        int next = guard.RouteIndex;
                        if (next <= meetIndex && guard.State == GuardState.Patrol)
                        {
                            float eta = Mathf.Max(0.1f, PlayerEta());
                            guard.walkSpeed = Mathf.Clamp(GuardRemaining(next) / eta, minSpeed, maxSpeed);
                        }
                        else guard.walkSpeed = normalWalkSpeed;
                    }
                    if (!onWalk || guard.RouteIndex != last || !guard.StandingAt(walk.GetPoint(last).position, 1.2f)) return;
                    if (thenRoute != null) guard.AssignRoute(thenRoute);
                    Finish();
                    break;
            }
        }

        void Finish()
        {
            if (guard != null && normalWalkSpeed > 0f) guard.walkSpeed = normalWalkSpeed;
            phase = Phase.Done;
            if (zoneUntilPassed == null) enabled = false; // else keep watching for the pass
        }

        /// <summary>On the walk past point 0, and the player is behind him (he's walking away from them).</summary>
        bool HasPassedPlayer()
        {
            if (guard.route == walk && guard.RouteIndex < 1) return false;
            Vector3 toPlayer = player.position - guard.transform.position;
            toPlayer.y = 0f;
            return Vector3.Dot(toPlayer, guard.transform.forward) < -0.5f;
        }

        void DropZone()
        {
            if (zoneUntilPassed != null) zoneUntilPassed.gameObject.SetActive(false);
            zoneUntilPassed = null;
            if (phase == Phase.Done) enabled = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(cueZone.center, cueZone.size);
            if (walk != null && meetIndex >= 0 && meetIndex < walk.Count) Gizmos.DrawWireSphere(walk.GetPoint(meetIndex).position, 0.6f);
        }
    }
}
