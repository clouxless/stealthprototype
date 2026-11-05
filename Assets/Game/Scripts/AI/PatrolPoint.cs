using UnityEngine;

namespace StealthProto
{
    /// <summary>Optional on a PatrolRoute waypoint: its own wait time. 0 = walk straight through (a rounding point).</summary>
    public class PatrolPoint : MonoBehaviour
    {
        public float wait;
    }
}
