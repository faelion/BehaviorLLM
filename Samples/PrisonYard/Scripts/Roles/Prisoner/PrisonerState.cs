using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>How a prisoner behaves when nobody is looking. This is a gate, not a suggestion.</summary>
    public enum PrisonerTemperament
    {
        /// <summary>Serves quietly. Never offered Fight or Sneak.</summary>
        Compliant,
        /// <summary>Picks fights when unwatched. The only temperament offered Fight.</summary>
        Hothead,
        /// <summary>Tests the doors when unwatched. The only temperament offered Sneak.</summary>
        Escapee
    }

    /// <summary>
    /// Everything about a prisoner the game knows. Two fields here decide most of what the model
    /// is allowed to do: <see cref="temperament"/>, which is fixed, and
    /// <see cref="ObservedByGuard"/>, which the game recomputes every frame by asking each guard's
    /// vision module whether it can see this prisoner.
    ///
    /// That second one is the sample's version of StealthGuard's lesson. Asking the model "are you
    /// being watched? if not, you may misbehave" is a two-condition rule, and those were measured
    /// to be ignored. Computing it and removing the action instead is exact and free.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prisoner State")]
    public class PrisonerState : MonoBehaviour
    {
        [Tooltip("Health the prisoner starts with. Fights take it down; the infirmary brings it back.")]
        public int maxHealth = 100;

        [Tooltip("Current health. A hurt prisoner is what a guard escorts to the infirmary.")]
        public int currentHealth = 100;

        [Tooltip("How this prisoner behaves when unwatched. Only a Hothead is ever offered Fight, " +
                 "and only an Escapee is ever offered Sneak, no matter what the prompt says.")]
        public PrisonerTemperament temperament = PrisonerTemperament.Compliant;

        [Tooltip("Whether this prisoner is carrying something they should not. Gives them Hide, and " +
                 "is what a guard's Search is looking for.")]
        public bool hasContraband;

        [Tooltip("Read-only: what the prisoner is doing right now. Guards see this through their " +
                 "vision, so 'Fighting' is visible to anyone watching.")]
        public string activity = "Idle";

        [Tooltip("Read-only while playing: whether any guard can see this prisoner at this moment. " +
                 "Recomputed every frame from the guards' vision modules. Watch it in the inspector " +
                 "to understand why an action did or did not appear.")]
        [SerializeField] private bool observedByGuard;

        private GuardState[] guards;
        private float busyUntil;

        /// <summary>True while at least one guard can see this prisoner.</summary>
        public bool ObservedByGuard => observedByGuard;

        /// <summary>The guard walking this prisoner somewhere, or null.</summary>
        public GuardState EscortedBy { get; set; }

        /// <summary>True while a fight, a conversation or an escort is still playing out.</summary>
        public bool IsBusy => Time.time < busyUntil;

        /// <summary>Shown to the model through this object's context bindings.</summary>
        public string HealthReport => $"{currentHealth}/{maxHealth}";

        private void Awake()
        {
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
            RefreshGuards();
        }

        /// <summary>Re-finds the guards whose vision is polled. Call after spawning one at runtime.</summary>
        public void RefreshGuards()
        {
            guards = FindObjectsByType<GuardState>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            observedByGuard = false;
            if (guards == null) return;

            for (int i = 0; i < guards.Length; i++)
            {
                if (guards[i] == null || !guards[i].Sees(transform)) continue;
                observedByGuard = true;
                break;
            }
        }

        /// <summary>Occupies the prisoner for a while, so a fight or a talk lasts more than a frame.</summary>
        public void BusyFor(float seconds)
        {
            busyUntil = Mathf.Max(busyUntil, Time.time + Mathf.Max(0f, seconds));
        }

        /// <summary>Hurts the prisoner. Fights call this on both participants.</summary>
        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Max(0, currentHealth - amount);
        }
    }
}
