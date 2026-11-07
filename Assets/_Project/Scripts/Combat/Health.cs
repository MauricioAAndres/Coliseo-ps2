using System;
using UnityEngine;

namespace Gladiator.Combat
{
    public class Health : MonoBehaviour
    {
        [SerializeField] float max = 100f;

        public float Max => max;
        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;
        public float Normalized => max > 0f ? Current / max : 0f;

        public event Action<float> Damaged;
        public event Action Died;

        void Awake() { Current = max; }

        /// <summary>Milestone 3 hook: max HP from the Resistance stat.</summary>
        public void SetMax(float value, bool refill = true)
        {
            max = Mathf.Max(1f, value);
            Current = refill ? max : Mathf.Min(Current, max);
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount);
            if (Current <= 0f) Died?.Invoke();
        }

        /// <summary>Online copy of another player: show their real health (no events).</summary>
        public void SetNet(float current, float maxValue)
        {
            max = Mathf.Max(1f, maxValue);
            Current = Mathf.Clamp(current, 0f, max);
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Mathf.Min(max, Current + amount);
        }
    }
}
