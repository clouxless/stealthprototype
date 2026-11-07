using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Trigger volume below a pit or ledge. A player who falls into it is downed, which restarts the
    /// scene through PlayerLife. Keeps trying while the player is inside, in case they fell in
    /// during dash i-frames.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class FallHazard : MonoBehaviour
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
