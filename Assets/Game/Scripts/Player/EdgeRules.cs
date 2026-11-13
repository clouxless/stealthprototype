using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Shared rules for edges. A drop is "deadly" when nothing solid lies below before a
    /// FallHazard (water, a bottomless pit) or within maxDepth. You never walk or dash off an
    /// edge: walking off one hangs you from it (PlayerContextActions), a dash stops at it or
    /// jumps a gap, and you can't let go of a ledge over a deadly drop.
    /// </summary>
    public static class EdgeRules
    {
        static readonly RaycastHit[] Hits = new RaycastHit[32];

        /// <summary>Solid ground straight below a point (triggers ignored), within maxDistance.</summary>
        public static bool GroundBelow(Vector3 point, float maxDistance, Transform ignore, out RaycastHit hit)
            => Occlusion.Raycast(point, Vector3.down, maxDistance, ignore, null, out hit);

        /// <summary>True if falling from this point would end in a FallHazard or nothing at all.</summary>
        public static bool DeadlyBelow(Vector3 point, Transform ignore, float maxDepth = 60f)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.1f, Vector3.down, Hits, maxDepth, ~0, QueryTriggerInteraction.Collide);
            float bestSolid = float.MaxValue, bestHazard = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = Hits[i].collider;
                if (ignore != null && c.transform.IsChildOf(ignore)) continue;
                if (c.isTrigger)
                {
                    if (c.GetComponent<FallHazard>() != null) bestHazard = Mathf.Min(bestHazard, Hits[i].distance);
                }
                else bestSolid = Mathf.Min(bestSolid, Hits[i].distance);
            }
            if (bestSolid == float.MaxValue) return true;
            return bestHazard < bestSolid;
        }
    }
}
