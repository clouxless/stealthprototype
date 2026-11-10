using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A straight crawlspace under a floor grate (Arkham-style). Stand over it and press the
    /// context button to drop in, crawl along it, and press again to climb out anywhere there's
    /// room to stand. See PlayerGrateCrawl. start and end are world points on the crawlspace's
    /// floor, along its centre line.
    /// </summary>
    public class GrateRun : MonoBehaviour
    {
        public static readonly List<GrateRun> All = new List<GrateRun>();

        public Vector3 start;
        public Vector3 end;
        [Tooltip("Height of the walkable floor on top of the grate.")]
        public float surfaceY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public float Length => Vector3.Distance(start, end);
        public Vector3 Direction => (end - start).normalized;
        public Vector3 PointAt(float along) => start + Direction * Mathf.Clamp(along, 0f, Length);

        public Vector3 SurfacePointAt(float along)
        {
            Vector3 p = PointAt(along);
            p.y = surfaceY;
            return p;
        }

        /// <summary>Distance along the run of the point nearest to p (unclamped), and the horizontal distance to the line.</summary>
        public float Project(Vector3 p, out float lateral)
        {
            Vector3 dir = end - start;
            dir.y = 0f;
            dir.Normalize();
            Vector3 v = p - start;
            v.y = 0f;
            float along = Vector3.Dot(v, dir);
            lateral = (v - dir * along).magnitude;
            return along;
        }

        /// <summary>The grate run the player's feet are standing over, if any.</summary>
        public static bool Find(Vector3 feet, float reach, out GrateRun run, out float along)
        {
            foreach (var r in All)
            {
                if (Mathf.Abs(feet.y - r.surfaceY) > 0.4f) continue;
                float a = r.Project(feet, out float lateral);
                if (lateral > reach || a < -0.3f || a > r.Length + 0.3f) continue;
                run = r;
                along = Mathf.Clamp(a, 0f, r.Length);
                return true;
            }
            run = null;
            along = 0f;
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 0.2f);
            Gizmos.DrawWireSphere(end, 0.2f);
        }
    }
}
