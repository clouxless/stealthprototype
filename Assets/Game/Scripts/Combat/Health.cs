using System;
using UnityEngine;

namespace StealthProto
{
    public struct DamageInfo
    {
        public int amount;
        public Vector3 point;
        public Vector3 direction;
        public GameObject source;
        /// <summary>Silent one-hit stealth takedown.</summary>
        public bool takedown;
    }

    public class Health : MonoBehaviour
    {
        public int maxHealth = 3;

        public int Current { get; private set; }
        public bool IsDead => Current <= 0;
        /// <summary>Set during dash i-frames.</summary>
        public bool Invulnerable { get; set; }

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Died;

        void Awake() => Current = maxHealth;

        /// <returns>True if the damage landed.</returns>
        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || Invulnerable || info.amount <= 0) return false;
            Current = Mathf.Max(0, Current - info.amount);
            Damaged?.Invoke(info);
            if (Current == 0) Died?.Invoke(info);
            return true;
        }
    }
}
