using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// A box in the level that takes over the camera while the player is inside it (CameraRig blends
    /// in and out). The shot follows the player's progress through the zone, measured along
    /// progressStart -> progressEnd, not a timer, so the player sets the pace.
    ///
    /// One shape covers the three kinds of shot:
    /// - Track: eye relative to the player, the same at both ends (moves alongside you).
    /// - Crane: eye relative to the player, different at each end (pulls back and up as you go).
    /// - Fixed: eye in the world (stays put), looking at the player (turns to keep you in frame).
    /// Cinematic shots drop back to top-down whenever a guard is suspicious or worse (CameraRig).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CameraZone : MonoBehaviour
    {
        public enum Kind { Track, Crane, Fixed }

        public Kind kind = Kind.Track;
        [Tooltip("Higher wins where zones overlap.")]
        public int priority;
        [Tooltip("Seconds to blend in, and out again when you leave.")]
        public float blendTime = 1.2f;

        [Header("Progress (world points)")]
        public Vector3 progressStart;
        public Vector3 progressEnd;
        [Tooltip("Shapes progress: 1 = even, 2+ = slow to start then faster (a crane that starts slowly).")]
        public float easePower = 1f;

        [Header("Eye")]
        [Tooltip("Eye offsets are from the player's feet (Track, Crane); otherwise world positions (Fixed).")]
        public bool eyeRelativeToPlayer = true;
        public Vector3 eyeStart = new Vector3(8f, 3f, 0f);
        public Vector3 eyeEnd = new Vector3(8f, 3f, 0f);

        [Header("Look at")]
        [Tooltip("Look offsets are from the player's feet; otherwise world positions.")]
        public bool lookRelativeToPlayer = true;
        public Vector3 lookStart = new Vector3(0f, 1.2f, 0f);
        public Vector3 lookEnd = new Vector3(0f, 1.2f, 0f);

        [Header("Lens")]
        public float fovStart = 45f;
        public float fovEnd = 45f;

        public static readonly List<CameraZone> All = new List<CameraZone>();
        BoxCollider box;

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 point)
        {
            if (box == null) box = GetComponent<BoxCollider>();
            Vector3 local = transform.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>0..1 along the zone, eased.</summary>
        public float Progress(Vector3 player)
        {
            Vector3 axis = progressEnd - progressStart;
            float t = axis.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(player - progressStart, axis) / axis.sqrMagnitude) : 0f;
            return Mathf.Pow(t, Mathf.Max(0.01f, easePower));
        }

        /// <summary>The shot for a player at this position.</summary>
        public void Evaluate(Vector3 player, out Vector3 eye, out Quaternion rotation, out float fov)
        {
            float t = Progress(player);
            Vector3 e = Vector3.Lerp(eyeStart, eyeEnd, t);
            Vector3 l = Vector3.Lerp(lookStart, lookEnd, t);
            eye = eyeRelativeToPlayer ? player + e : e;
            Vector3 look = lookRelativeToPlayer ? player + l : l;
            Vector3 dir = look - eye;
            rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;
            fov = Mathf.Lerp(fovStart, fovEnd, t);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawLine(progressStart, progressEnd);
            if (!eyeRelativeToPlayer) Gizmos.DrawWireSphere(eyeStart, 0.5f);
        }
    }
}
