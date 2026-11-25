using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Marks a collider (or everything under this object) as not climbable: you can't clamber onto it,
    /// jump-clamber onto it, or catch its edge from a jump. You can still walk onto it the ordinary
    /// way. Used on the stairs, so the only way up is from the foot.
    /// </summary>
    public class NoClamber : MonoBehaviour
    {
        public static bool On(Collider c) => c != null && c.GetComponentInParent<NoClamber>() != null;
    }
}
