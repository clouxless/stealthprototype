using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// The inside of a vent or floor grate, for sound. Ducts are one-way:
    ///   - Noise from outside never gets in or travels through, so a duct never connects two
    ///     spaces (SoundGrid leaves its inside out of the air it floods).
    ///   - Noise made inside gets out. In a vent it is carried to the vent's openings and heard
    ///     there, at full range from each. In a floor grate it is a regular radius into the space
    ///     above the grate.
    /// The box is axis-aligned, centred on this transform.
    /// </summary>
    public class SoundDuct : MonoBehaviour
    {
        public enum DuctKind { Vent, Grate }

        public DuctKind kind = DuctKind.Vent;
        [Tooltip("World-space size of the duct's inside, centred on this transform (axis-aligned).")]
        public Vector3 size = Vector3.one;
        [Tooltip("Vents: world points just outside each opening, at floor level. A noise inside comes out at every one.")]
        public Vector3[] openings = new Vector3[0];
        [Tooltip("Grates: height of the floor surface the grate sits in. A noise inside is heard from just above it.")]
        public float surfaceY;

        public static readonly List<SoundDuct> All = new List<SoundDuct>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public Bounds Box => new Bounds(transform.position, size);

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            SoundGrid.Invalidate();
        }

        void OnDisable()
        {
            All.Remove(this);
            SoundGrid.Invalidate();
        }

        /// <summary>The duct containing this point (checked a little above it too, for feet on a duct floor).</summary>
        public static SoundDuct Find(Vector3 point)
        {
            foreach (var d in All)
            {
                var box = d.Box;
                if (box.Contains(point) || box.Contains(point + Vector3.up * 0.3f)) return d;
            }
            return null;
        }

        /// <summary>
        /// Where a noise made at this point is heard from: the point itself, a vent's openings, or
        /// the floor above a grate.
        /// </summary>
        public static void ResolveOrigins(Vector3 point, List<Vector3> into)
        {
            into.Clear();
            var duct = Find(point);
            if (duct == null)
            {
                into.Add(point);
                return;
            }
            if (duct.kind == DuctKind.Grate)
            {
                into.Add(new Vector3(point.x, duct.surfaceY, point.z));
                return;
            }
            if (duct.openings == null || duct.openings.Length == 0)
            {
                into.Add(point);
                return;
            }
            into.AddRange(duct.openings);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(transform.position, size);
            if (openings == null) return;
            foreach (var o in openings) Gizmos.DrawWireSphere(o, 0.25f);
        }
    }
}
