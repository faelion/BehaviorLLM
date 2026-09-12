using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Decides what the warden may do this turn. The warden is a game system rather than a
    /// character, but nothing about the gating is different: the same interface, reading the same
    /// status board it sees in its prompt.
    /// </summary>
    [AddComponentMenu("PrisonYard/Warden Availability")]
    public class WardenAvailability : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("How many guards a section needs before reinforcing it is pointless. Reinforce is " +
                 "withheld once a section already has this many.")]
        public int guardsPerSectionTarget = 2;

        [Tooltip("Offer Lockdown only while something is actually going wrong. Turn this off to " +
                 "watch the warden lock down a quiet prison for no reason.")]
        public bool gateLockdownOnIncidents = true;

        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private PrisonStatusBoard boardCache;

        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());

        public bool IsActionAvailable(string actionName)
        {
            switch (actionName)
            {
                case PrisonYardIds.Observe:
                case PrisonYardIds.Announce:
                    return true;

                case PrisonYardIds.Lockdown:
                    if (board == null) return false;
                    if (gateLockdownOnIncidents && !board.HasAnyIncident()) return false;
                    return HasUnlockedSection();

                case PrisonYardIds.LiftLockdown:
                    return board != null && board.AnyLocked();

                case PrisonYardIds.Reinforce:
                    return board != null && HasUnderstaffedIncident();

                default:
                    // Unknown names stay available, so adding an action to the config later is not
                    // silently disabled by this provider.
                    return true;
            }
        }

        private bool HasUnlockedSection()
        {
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                if (!board.IsLocked(PrisonSections.All[i])) return true;
            }
            return false;
        }

        /// <summary>True when somewhere has trouble and not enough guards to deal with it.</summary>
        private bool HasUnderstaffedIncident()
        {
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                if (!board.HasIncidentIn(s)) continue;
                if (board.GuardsIn(s) < guardsPerSectionTarget) return true;
            }
            return false;
        }
    }
}
