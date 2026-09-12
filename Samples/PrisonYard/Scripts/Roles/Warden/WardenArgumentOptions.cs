using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Supplies the warden's argument values. Lockdown lists only sections that are open,
    /// LiftLockdown only sections that are shut, and Reinforce only sections that actually need
    /// help, so the warden cannot issue an order that would do nothing.
    /// </summary>
    [AddComponentMenu("PrisonYard/Warden Argument Options")]
    [RequireComponent(typeof(WardenAvailability))]
    public class WardenArgumentOptions : MonoBehaviour, IArgumentOptionsProvider
    {
        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private PrisonStatusBoard boardCache;
        private WardenAvailability availabilityCache;

        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());
        private WardenAvailability availability => availabilityCache != null ? availabilityCache : (availabilityCache = GetComponent<WardenAvailability>());

        public bool TryGetArgumentOptions(string actionName, List<string> options)
        {
            if (board == null) return false;

            switch (actionName)
            {
                case PrisonYardIds.Lockdown:
                    return CollectSections(options, locked: false);

                case PrisonYardIds.LiftLockdown:
                    return CollectSections(options, locked: true);

                case PrisonYardIds.Reinforce:
                    return CollectUnderstaffed(options);

                default:
                    // Announce takes its values from the action config's Allowed Arguments, which
                    // is the right place for a fixed vocabulary that is not derived from the scene.
                    return false;
            }
        }

        private bool CollectSections(List<string> options, bool locked)
        {
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                if (board.IsLocked(s) != locked) continue;
                options.Add(s.Id());
            }
            return options.Count > 0;
        }

        private bool CollectUnderstaffed(List<string> options)
        {
            int target = availability != null ? availability.guardsPerSectionTarget : 2;
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                if (!board.HasIncidentIn(s)) continue;
                if (board.GuardsIn(s) >= target) continue;
                options.Add(s.Id());
            }
            return options.Count > 0;
        }
    }
}
