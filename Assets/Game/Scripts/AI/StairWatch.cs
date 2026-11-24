using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Keeps a stair foot covered during a shift change (ShiftChange): while it's in step, at every
    /// moment either a sweeping searchlight is on the foot or the patrolling guard's flashlight is.
    ///
    /// - The patroller's route has a watch point facing the foot. When he's settled there, the
    ///   light sweeps out across the ledge and back; he doesn't leave until it's back on the foot.
    /// - While he walks the rest of his beat, the light rests on the foot (its sweep's start end,
    ///   K = 0) until he's back at the watch point.
    /// - Out of step (the light sweeps freely, he doesn't wait for it) whenever nobody's on duty at
    ///   the post (after a swap, while the new post guard walks up: the gap to slip up the stairs),
    ///   or either guard is off patrol (suspicious, searching, alert), or the shift change is over.
    ///
    /// The light's eyes stay with whoever works it (GuardVision.searchlights, set in the builder).
    /// </summary>
    public class StairWatch : MonoBehaviour
    {
        public ShiftChange shift;
        public SwivelLight searchlight;
        [Tooltip("The patrol route waypoint where the patroller stands watching the stair foot.")]
        public int watchIndex;
        [Tooltip("Seconds he stands at the watch point before the light is let go.")]
        public float settleTime = 1f;

        float settled;
        bool sweeping, sawFarEnd, sweepDone;

        void Start()
        {
            if (shift == null) return;
            Hook(shift.patrolling);
            Hook(shift.onPost);
        }

        void Hook(GuardBrain g)
        {
            if (g != null) g.mayLeavePoint = index => MayLeave(g, index);
        }

        bool InStep()
        {
            if (shift == null || !shift.enabled || searchlight == null) return false;
            GuardBrain p = shift.patrolling;
            return p != null && p.State == GuardState.Patrol && shift.PostOnDuty != null;
        }

        bool HeadingToWatch(GuardBrain g) => g == shift.patrolling && g.route == shift.patrolRoute && g.RouteIndex == watchIndex;

        bool MayLeave(GuardBrain g, int index)
        {
            if (!InStep() || g != shift.patrolling || g.route != shift.patrolRoute || index != watchIndex) return true;
            return sweepDone;
        }

        void Update()
        {
            if (searchlight == null) return;
            if (!InStep())
            {
                searchlight.hold = false;
                sweeping = sawFarEnd = sweepDone = false;
                settled = 0f;
                return;
            }

            GuardBrain p = shift.patrolling;
            if (!HeadingToWatch(p))
            {
                // Walking his beat (or off meeting the relief): rest on the foot.
                sweeping = sawFarEnd = sweepDone = false;
                settled = 0f;
                Hold();
                return;
            }

            bool atWatch = p.StandingAt(shift.patrolRoute.GetPoint(watchIndex).position, 0.6f);
            settled = atWatch ? settled + Time.deltaTime : 0f;
            if (sweepDone) { Hold(); return; }
            if (!sweeping)
            {
                // Let go once it's on the foot and he's settled, watching it.
                if (settled >= settleTime && searchlight.K <= 0.01f) { sweeping = true; searchlight.hold = false; }
                else Hold();
                return;
            }
            if (searchlight.K >= 0.99f) sawFarEnd = true;
            if (sawFarEnd && searchlight.K <= 0.01f) { sweepDone = true; Hold(); }
        }

        void Hold()
        {
            searchlight.hold = true;
            searchlight.holdK = 0f;
        }
    }
}
