using System.Collections.Generic;
using BehaviorLLM.Core.Perception;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Everything about a guard the game knows and the model may be told: how hurt it is, which
    /// section it answers for, and what it is currently doing. The last two are read by the
    /// availability and options providers, so this component is what turns "Guard_02 covers the
    /// cell block" into a different action menu.
    /// </summary>
    [AddComponentMenu("PrisonYard/Guard State")]
    public class GuardState : MonoBehaviour
    {
        [Tooltip("Health the guard starts with. Below half it stops being offered Chase-like " +
                 "actions and gets Retreat instead.")]
        public int maxHealth = 100;

        [Tooltip("Current health. Watch this while playing: crossing below half changes which " +
                 "actions the guard is offered, without the prompt changing.")]
        public int currentHealth = 100;

        [Tooltip("Health restored per second while resting in the infirmary. 0 disables healing.")]
        public float healPerSecond = 6f;

        [Tooltip("Which section this guard answers for. Its patrol points come from here, and a " +
                 "radio call about this section interrupts it. The warden's Reinforce changes it.")]
        public PrisonSection assignedSection = PrisonSection.Yard;

        [Tooltip("Read-only: what the guard is doing right now. Shown to the AI as part of its own " +
                 "status, so its last decision is part of the next prompt.")]
        public string activity = "On duty";

        private ModularVisionModule vision;

        /// <summary>The prisoner this guard is walking somewhere, or null.</summary>
        public PrisonerState Escorting { get; set; }

        /// <summary>True while the guard is fit enough to deal with trouble rather than withdraw.</summary>
        public bool IsHealthy => currentHealth * 2 >= maxHealth;

        /// <summary>Shown to the model through this object's context bindings.</summary>
        public string HealthReport => $"{currentHealth}/{maxHealth}";

        /// <summary>The guard's eyes, so other code can ask what it can see.</summary>
        public ModularVisionModule Vision => vision != null ? vision : (vision = GetComponent<ModularVisionModule>());

        private void Awake()
        {
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
            vision = GetComponent<ModularVisionModule>();
        }

        private void Update()
        {
            if (healPerSecond <= 0f || currentHealth >= maxHealth) return;
            if (!string.Equals(activity, "Recovering", System.StringComparison.Ordinal)) return;

            healAccumulator += healPerSecond * Time.deltaTime;
            int whole = Mathf.FloorToInt(healAccumulator);
            if (whole <= 0) return;
            healAccumulator -= whole;
            currentHealth = Mathf.Min(maxHealth, currentHealth + whole);
        }

        private float healAccumulator;

        /// <summary>Hurts the guard. The director's key and prisoner fights both call this.</summary>
        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Max(0, currentHealth - amount);
        }

        /// <summary>True when this guard can see the given object right now.</summary>
        public bool Sees(Transform target)
        {
            return Vision != null && Vision.Sees(target);
        }

        /// <summary>
        /// Fills the list with every prisoner this guard can currently see within
        /// <paramref name="maxDistance"/> metres, nearest first. Both the availability and the
        /// options providers ask this, so "who can I reach?" is answered once, the same way.
        /// </summary>
        public void CollectVisiblePrisoners(List<PrisonerState> into, float maxDistance)
        {
            into.Clear();
            if (Vision == null) return;

            var visible = Vision.VisibleObjects;
            for (int i = 0; i < visible.Count; i++)
            {
                LLMContextObject obj = visible[i];
                if (obj == null) continue;

                PrisonerState prisoner = obj.GetComponent<PrisonerState>();
                if (prisoner == null) continue;
                if (Vector3.Distance(transform.position, prisoner.transform.position) > maxDistance) continue;
                into.Add(prisoner);
            }

            into.Sort((a, b) => Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));
        }
    }
}
