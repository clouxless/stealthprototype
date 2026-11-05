using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Trigger volume under the waterline. A player who falls in is downed, which restarts the
    /// scene through PlayerLife. Keeps trying while the player is inside, in case they fell in
    /// during dash i-frames.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterHazard : MonoBehaviour
    {
        void Reset() => GetComponent<BoxCollider>().isTrigger = true;
        void Awake() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerStay(Collider other)
        {
            var health = other.GetComponentInParent<Health>();
            if (health == null || health.IsDead || other.GetComponentInParent<PlayerMotor>() == null) return;
            health.TakeDamage(new DamageInfo { amount = health.Current, point = other.transform.position, source = gameObject });
        }
    }
}
