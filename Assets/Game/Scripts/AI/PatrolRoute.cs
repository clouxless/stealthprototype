using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Waypoints are this object's children. A waypoint's forward is where the guard looks while waiting.
    /// Design rule: no straight back-and-forth routes. A guard reaching the end of a line and
    /// spinning 180 degrees surprises the player unfairly, so routes are loops, and dead ends are
    /// rounded off with a few pass-through points (PatrolPoint wait 0) so the turn is gradual.
    /// </summary>
    public class PatrolRoute : MonoBehaviour
    {
        [Tooltip("Loop back to the first point, or walk back and forth.")]
        public bool loop = true;
        public float waitTime = 1.5f;
        [Tooltip("Turn to face each waypoint's forward direction while waiting there.")]
        public bool faceWaypointDirection = true;

        public int Count => transform.childCount;
        public Transform GetPoint(int index) => transform.GetChild(index);

        /// <summary>Seconds to wait at a point: its PatrolPoint override if it has one, else waitTime.</summary>
        public float WaitAt(int index)
        {
            var point = GetPoint(index).GetComponent<PatrolPoint>();
            return point != null ? point.wait : waitTime;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            for (int i = 0; i < Count; i++)
            {
                Transform p = GetPoint(i);
                Gizmos.DrawWireSphere(p.position, 0.25f);
                Gizmos.DrawLine(p.position, p.position + p.forward * 0.8f);
                if (i + 1 < Count) Gizmos.DrawLine(p.position, GetPoint(i + 1).position);
                else if (loop && Count > 1) Gizmos.DrawLine(p.position, GetPoint(0).position);
            }
        }
    }
}
