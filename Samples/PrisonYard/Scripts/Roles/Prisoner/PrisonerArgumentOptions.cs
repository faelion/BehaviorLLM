using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Supplies the argument values a prisoner may use. A locked section is not in the list, so a
    /// lockdown is not something the model is asked to respect: it is something it cannot express.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prisoner Argument Options")]
    [RequireComponent(typeof(PrisonerState))]
    [RequireComponent(typeof(PrisonerAvailability))]
    public class PrisonerArgumentOptions : MonoBehaviour, IArgumentOptionsProvider
    {
        [Tooltip("How close another prisoner must be to be offered as someone to talk to, in metres.")]
        public float talkDistance = 4f;

        [Tooltip("How close another prisoner must be to be offered as someone to fight, in metres.")]
        public float fightDistance = 3f;

        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private PrisonerAvailability availabilityCache;
        private PrisonStatusBoard boardCache;
        private PrisonClock clockCache;
        private readonly List<PrisonerState> scratch = new List<PrisonerState>();

        private PrisonerAvailability availability => availabilityCache != null ? availabilityCache : (availabilityCache = GetComponent<PrisonerAvailability>());
        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());
        private PrisonClock clock => clockCache != null ? clockCache : (clockCache = FindFirstObjectByType<PrisonClock>());

        public bool TryGetArgumentOptions(string actionName, string parameterName, List<string> options)
        {
            switch (actionName)
            {
                case PrisonYardIds.Wander:
                    return CollectWanderSections(options);

                case PrisonYardIds.Sneak:
                    return CollectSneakSections(options);

                case PrisonYardIds.Talk:
                    return CollectNeighbours(options, talkDistance);

                case PrisonYardIds.Fight:
                    return CollectNeighbours(options, fightDistance);

                case PrisonYardIds.Hide:
                    PrisonMarker spot = availability.FindHidingSpot();
                    if (spot == null) return false;
                    options.Add(spot.id);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Unlocked sections, always including the one the schedule says to be in.</summary>
        private bool CollectWanderSections(List<string> options)
        {
            PrisonSection scheduled = clock != null ? clock.ScheduledSection : PrisonSection.CellBlock;

            for (int i = 0; i < PrisonSections.PrisonerAccessible.Length; i++)
            {
                PrisonSection s = PrisonSections.PrisonerAccessible[i];
                bool locked = board != null && board.IsLocked(s);
                if (locked && s != scheduled) continue;
                if (!options.Contains(s.Id())) options.Add(s.Id());
            }

            // The scheduled section is always offered, so a prisoner is never left with nowhere
            // legitimate to go.
            if (!options.Contains(scheduled.Id())) options.Add(scheduled.Id());
            return options.Count > 0;
        }

        /// <summary>Unlocked sections other than the one the schedule says to be in.</summary>
        private bool CollectSneakSections(List<string> options)
        {
            PrisonSection scheduled = clock != null ? clock.ScheduledSection : PrisonSection.CellBlock;
            PrisonSection? here = board != null ? board.SectionAt(transform.position) : null;

            for (int i = 0; i < PrisonSections.PrisonerAccessible.Length; i++)
            {
                PrisonSection s = PrisonSections.PrisonerAccessible[i];
                if (s == scheduled) continue;
                if (here != null && s == here.Value) continue;
                if (board != null && board.IsLocked(s)) continue;
                options.Add(s.Id());
            }
            return options.Count > 0;
        }

        private bool CollectNeighbours(List<string> options, float distance)
        {
            availability.CollectNeighbours(scratch, distance);
            for (int i = 0; i < scratch.Count; i++)
            {
                if (!options.Contains(scratch[i].name)) options.Add(scratch[i].name);
            }
            return options.Count > 0;
        }
    }
}
