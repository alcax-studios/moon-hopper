using System;
using UnityEngine;

namespace Alcax.Combat
{
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public float Knockback;
        public bool Headshot;
        public GameObject Source;
    }

    /// <summary>Hit points for anything that can be hurt (the player and zombies).</summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] float maxHealth = 100f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
        public bool IsDead { get; private set; }

        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action<float> Healed;

        /// <summary>When this returns true, incoming damage is ignored (admin god mode).</summary>
        public Func<bool> DamageBlocked;
        /// <summary>Scales incoming damage (armor). Null = 1.</summary>
        public Func<DamageInfo, float> DamageScale;

        void Awake()
        {
            Current = maxHealth;
        }

        public void SetMax(float value, bool refill = true)
        {
            maxHealth = Mathf.Max(1f, value);
            if (refill) Current = maxHealth;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (IsDead || info.Amount <= 0f) return;
            if (DamageBlocked != null && DamageBlocked()) return;
            if (DamageScale != null) info.Amount *= Mathf.Max(0f, DamageScale(info));
            Current = Mathf.Max(0f, Current - info.Amount);
            Damaged?.Invoke(info);
            if (Current <= 0f)
            {
                IsDead = true;
                Died?.Invoke();
            }
        }

        /// <summary>Bring a dead Health back to full (practice targets).</summary>
        public void Revive()
        {
            IsDead = false;
            Current = maxHealth;
        }

        public bool Heal(float amount)
        {
            if (IsDead || Current >= maxHealth) return false;
            Current = Mathf.Min(maxHealth, Current + amount);
            Healed?.Invoke(amount);
            return true;
        }
    }
}
