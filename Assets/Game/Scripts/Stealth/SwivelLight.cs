using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A swivel spotlight: turns this transform side to side about the vertical axis, a fixed
    /// sweep every time (deterministic). Put it on a spot Light with a StealthLight; where the
    /// beam hits the floor is Lit. Use phase to put two lights out of step so a gap opens.
    /// </summary>
    public class SwivelLight : MonoBehaviour
    {
        [Tooltip("Total sweep, degrees (half each side of the starting direction).")]
        public float sweepAngle = 80f;
        [Tooltip("Seconds for one sweep from one side to the other.")]
        public float sweepTime = 8f;
        [Tooltip("0..1 offset into the cycle.")]
        [Range(0f, 1f)] public float phase;
        [Tooltip("Seconds it rests at each end of the sweep.")]
        public float pauseAtEnds = 0.5f;

        Quaternion baseRotation;

        void Awake() => baseRotation = transform.rotation;

        void Update()
        {
            // One cycle = across, pause, back, pause. Eased so it slows into each end (readable).
            float cycle = 2f * (sweepTime + pauseAtEnds);
            float t = Mathf.Repeat(Time.time / cycle + phase, 1f) * cycle;
            float k;
            if (t < sweepTime) k = t / sweepTime;
            else if (t < sweepTime + pauseAtEnds) k = 1f;
            else if (t < 2f * sweepTime + pauseAtEnds) k = 1f - (t - sweepTime - pauseAtEnds) / sweepTime;
            else k = 0f;
            k = k * k * (3f - 2f * k);
            float yaw = Mathf.Lerp(-sweepAngle * 0.5f, sweepAngle * 0.5f, k);
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * baseRotation;
        }
    }
}
