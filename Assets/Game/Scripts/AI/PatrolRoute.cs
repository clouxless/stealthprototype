using UnityEngine;

namespace StealthProto
{
    /// <summary>Waypoints are this object's children. A waypoint's forward is where the guard looks while waiting.</summary>
    public class PatrolRoute : MonoBehaviour
    {
        [Tooltip("Loop back to the first point, or walk back and forth.")]
        public bool loop = true;
        public float waitTime = 1.5f;
        [Tooltip("Turn to face each waypoint's forward direction while waiting there.")]
        public bool faceWaypointDirection = true;

        public int Count => transform.childCount;
        public Transform GetPoint(int index) => transform.GetChild(index);

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
