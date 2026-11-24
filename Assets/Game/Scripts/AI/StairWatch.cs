using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Keeps a stair entrance covered during a shift change (ShiftChange): at every moment either
    /// the post's searchlight is on it or the patrolling guard's flashlight is.
    ///
    /// - The searchlight belongs to whoever is on duty at the post (ShiftChange.PostOnDuty): it's
    ///   his eyes (GuardVision.searchlights), so its Lit beam spots you at once.
    /// - While the patroller is anywhere but his watch point, the light is held on the stair
    ///   entrance (its sweep's start end, K = 0).
    /// - When he's settled at the watch point (facing the entrance, his flashlight on it), the light
    ///   is let go and sweeps the ledge once, out and back. He doesn't leave the watch point until
    ///   it's back on the entrance.
    /// - With nobody on duty (after a swap, while the new post guard walks up), the light is parked
    ///   at the far end (K = 1), off the entrance, and the patroller doesn't wait for it. That's the
    ///   gap to follow the old patroller up the stairs.
    /// </summary>
    public class StairWatch : MonoBehaviour
    {
        public ShiftChange shift;
        public SwivelLight searchlight;
        public StealthLight beam;
        [Tooltip("The patrol route waypoint where the patroller stands watching the stair entrance.")]
        public int watchIndex;
        [Tooltip("Seconds he stands at the watch point before the light is let go.")]
        public float settleTime = 1f;

        GuardBrain lastOwner;
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

        bool Watching(GuardBrain g) => g != null && g == shift.patrolling && g.route == shift.patrolRoute
                                       && g.RouteIndex == watchIndex;

        bool MayLeave(GuardBrain g, int index)
        {
            if (shift == null || !shift.enabled || searchlight == null) return true;
            if (g != shift.patrolling || g.route != shift.patrolRoute || index != watchIndex) return true;
            if (shift.PostOnDuty == null) return true;
            return sweepDone;
        }

        void Update()
        {
            if (shift == null || searchlight == null) return;

            // Eyes: the searchlight is the on-duty guard's, and nobody else's.
            GuardBrain owner = shift.enabled ? shift.PostOnDuty : null;
            if (owner != lastOwner)
            {
                if (lastOwner != null && lastOwner.vision != null) lastOwner.vision.searchlights = new StealthLight[0];
                if (owner != null && owner.vision != null && beam != null) owner.vision.searchlights = new[] { beam };
                lastOwner = owner;
            }

            if (owner == null)
            {
                Park(1f);
                if (!shift.enabled) enabled = false;
                return;
            }

            GuardBrain p = shift.patrolling;
            bool atWatch = Watching(p) && p.StandingAt(shift.patrolRoute.GetPoint(watchIndex).position, 0.6f);
            settled = atWatch ? settled + Time.deltaTime : 0f;

            if (!Watching(p))
            {
                // He's walked on: reset for his next visit and hold the entrance.
                sweeping = sawFarEnd = sweepDone = false;
                Park(0f);
                return;
            }
            if (sweepDone) { Park(0f); return; }
            if (!sweeping)
            {
                // Only let go once it's actually on the entrance and he's watching it.
                if (settled >= settleTime && searchlight.K <= 0.01f) { sweeping = true; searchlight.hold = false; }
                else Park(0f);
                return;
            }
            if (searchlight.K >= 0.99f) sawFarEnd = true;
            if (sawFarEnd && searchlight.K <= 0.01f) { sweepDone = true; Park(0f); }
        }

        void Park(float k)
        {
            searchlight.hold = true;
            searchlight.holdK = k;
        }
    }
}
