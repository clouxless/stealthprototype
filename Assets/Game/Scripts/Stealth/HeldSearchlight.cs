using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// The searchlight a guard holds (a prop): it turns every frame to point at the pool cast by
    /// `source`, an unseen light straight above the floor (a track SwivelLight pointing down) that
    /// makes the round pool and does the Lit test. So the pool reads as a crisp circle while the
    /// guard is seen swivelling the lamp that seems to throw it. SearchlightGun fires from here.
    /// </summary>
    public class HeldSearchlight : MonoBehaviour
    {
        public Transform source;
        [Tooltip("World height of the floor the pool lies on.")]
        public float floorY;

        void LateUpdate()
        {
            if (source == null) return;
            Vector3 dir = new Vector3(source.position.x, floorY, source.position.z) - transform.position;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }
    }
}
