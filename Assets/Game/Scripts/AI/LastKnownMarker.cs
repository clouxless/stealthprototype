using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Shows where hunting guards think you are: a ghost outline at the last known position,
    /// visible while any guard has lost sight of you and is heading there or searching it
    /// (after Splinter Cell Conviction / Shattered Dimensions). Placeholder lines, PH_FX_*.
    /// </summary>
    public class LastKnownMarker : MonoBehaviour
    {
        public Color color = new Color(1f, 0.7f, 0.15f, 0.8f);
        [Tooltip("Hunt points closer than this share one marker.")]
        public float mergeDistance = 2f;
        public int maxMarkers = 4;

        readonly List<LineRenderer[]> pool = new List<LineRenderer[]>();
        readonly List<Vector3> points = new List<Vector3>();
        const int RingSegments = 24;

        void LateUpdate()
        {
            points.Clear();
            foreach (var g in GuardBrain.All)
            {
                if (!g.IsHunting) continue;
                Vector3 p = g.HuntPoint;
                bool merged = false;
                foreach (var q in points)
                    if ((q - p).sqrMagnitude < mergeDistance * mergeDistance) { merged = true; break; }
                if (!merged && points.Count < maxMarkers) points.Add(p);
            }

            while (pool.Count < points.Count) pool.Add(CreateMarker(pool.Count));
            for (int i = 0; i < pool.Count; i++)
            {
                bool on = i < points.Count;
                foreach (var lr in pool[i]) lr.enabled = on;
                if (on) Place(pool[i], points[i]);
            }
        }

        LineRenderer[] CreateMarker(int index)
        {
            var ring = FX.CreateLine(transform, $"PH_FX_LastKnownRing{index}", color, 0.06f);
            ring.loop = true;
            ring.positionCount = RingSegments;
            // A standing outline the height of the player: two sides and a head.
            var body = FX.CreateLine(transform, $"PH_FX_LastKnownBody{index}", color, 0.04f);
            body.positionCount = 5;
            return new[] { ring, body };
        }

        void Place(LineRenderer[] marker, Vector3 p)
        {
            float pulse = 0.5f + 0.1f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                marker[0].SetPosition(i, p + new Vector3(Mathf.Cos(a) * pulse, 0.05f, Mathf.Sin(a) * pulse));
            }
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 side = cam != null ? Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized * 0.25f : Vector3.right * 0.25f;
            marker[1].SetPosition(0, p - side + Vector3.up * 0.05f);
            marker[1].SetPosition(1, p - side + Vector3.up * 1.45f);
            marker[1].SetPosition(2, p + Vector3.up * 1.75f);
            marker[1].SetPosition(3, p + side + Vector3.up * 1.45f);
            marker[1].SetPosition(4, p + side + Vector3.up * 0.05f);
        }
    }
}
