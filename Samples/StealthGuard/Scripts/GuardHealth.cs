using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Minimal health for the guard. Exists so the sample has one game-side condition the agent
    /// must respect, which is what <see cref="GuardAvailability"/> turns into a gated action menu.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Health")]
    public class GuardHealth : MonoBehaviour
    {
        [Tooltip("Health the guard starts with and is restored to when the scene reloads.")]
        public int maxHealth = 100;

        [Tooltip("Current health. Watch this while playing: when it crosses below half, Chase " +
                 "disappears from the action menu and Retreat appears, and the guard's behaviour " +
                 "changes without the prompt changing.")]
        public int currentHealth = 100;

        [Tooltip("Health restored per second while the guard is not being hit, so the demo recovers " +
                 "on its own and you can watch the menu flip back. 0 disables regeneration.")]
        public float regenPerSecond = 2f;

        [Tooltip("Seconds without damage before regeneration starts.")]
        public float regenDelaySeconds = 4f;

        private float lastDamageTime = -999f;
        private float regenAccumulator;

        /// <summary>True while the guard is healthy enough to fight rather than withdraw.</summary>
        public bool IsHealthy => currentHealth * 2 >= maxHealth;

        /// <summary>Health as a 0-1 fraction, for display and for the prompt.</summary>
        public float Fraction => maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f;

        private void Awake()
        {
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        }

        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Max(0, currentHealth - amount);
            lastDamageTime = Time.time;
            regenAccumulator = 0f;
        }

        private void Update()
        {
            if (regenPerSecond <= 0f || currentHealth >= maxHealth) return;
            if (Time.time - lastDamageTime < regenDelaySeconds) return;

            regenAccumulator += regenPerSecond * Time.deltaTime;
            int whole = Mathf.FloorToInt(regenAccumulator);
            if (whole <= 0) return;
            regenAccumulator -= whole;
            currentHealth = Mathf.Min(maxHealth, currentHealth + whole);
        }

        /// <summary>Shown to the model through the guard's LLMContextObject data binding.</summary>
        public string HealthReport => $"{currentHealth}/{maxHealth}";
    }
}
