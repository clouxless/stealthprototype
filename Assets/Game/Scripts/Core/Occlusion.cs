using UnityEngine;

namespace StealthProto
{
    /// <summary>Line-of-sight helpers that skip the colliders of the characters involved.</summary>
    public static class Occlusion
    {
        static readonly RaycastHit[] Hits = new RaycastHit[32];

        /// <summary>Nearest hit along a ray, ignoring anything under ignoreA / ignoreB and all triggers.</summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            Transform ignoreA, Transform ignoreB, out RaycastHit closest)
        {
            closest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Transform t = Hits[i].collider.transform;
                if (ignoreA != null && t.IsChildOf(ignoreA)) continue;
                if (ignoreB != null && t.IsChildOf(ignoreB)) continue;
                if (Hits[i].distance < best)
                {
                    best = Hits[i].distance;
                    closest = Hits[i];
                    found = true;
                }
            }
            return found;
        }

        /// <summary>True if something solid sits between from and to.</summary>
        public static bool Blocked(Vector3 from, Vector3 to, Transform ignoreA = null, Transform ignoreB = null)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.001f) return false;
            return Raycast(from, delta / distance, distance, ignoreA, ignoreB, out _);
        }
    }
}
