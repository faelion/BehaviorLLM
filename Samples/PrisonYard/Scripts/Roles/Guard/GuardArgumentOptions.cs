using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Supplies the argument values a guard may use, read from the scene each decision. The model
    /// can only name a patrol point that exists, a prisoner it can actually see, or a section that
    /// actually has trouble in it, because anything else is absent from the JSON Schema.
    /// </summary>
    [AddComponentMenu("PrisonYard/Guard Argument Options")]
    [RequireComponent(typeof(GuardState))]
    public class GuardArgumentOptions : MonoBehaviour, IArgumentOptionsProvider
    {
        [Tooltip("How close a prisoner must be to be offered as a target for Search and Escort, in " +
                 "metres. Keep it the same as the guard availability's reach.")]
        public float reachDistance = 3f;

        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private GuardState guardCache;
        private GuardAvailability availabilityCache;
        private PrisonStatusBoard boardCache;
        private PrisonMarker[] markers;
        private readonly List<PrisonerState> scratch = new List<PrisonerState>();
        private readonly List<PrisonSection> sections = new List<PrisonSection>();

        private GuardState guard => guardCache != null ? guardCache : (guardCache = GetComponent<GuardState>());
        private GuardAvailability availability => availabilityCache != null ? availabilityCache : (availabilityCache = GetComponent<GuardAvailability>());
        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());

        /// <summary>Re-reads the scene's markers. Call after adding one at runtime.</summary>
        public void RefreshMarkers()
        {
            markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
        }

        public bool TryGetArgumentOptions(string actionName, List<string> options)
        {
            switch (actionName)
            {
                case PrisonYardIds.Patrol:
                    return CollectPatrolPoints(options);

                case PrisonYardIds.Respond:
                    return CollectRespondSections(options);

                case PrisonYardIds.Escort:
                    return CollectPrisoners(options, requireUnescorted: true);

                case PrisonYardIds.Search:
                    return CollectPrisoners(options, requireUnescorted: false);

                case PrisonYardIds.Report:
                    return CollectReportSections(options);

                case PrisonYardIds.Retreat:
                    options.Add(PrisonSection.Infirmary.Id());
                    return true;

                default:
                    // Returning false leaves the action unconstrained, which is the right default
                    // for an action this provider knows nothing about.
                    return false;
            }
        }

        /// <summary>The patrol points of the guard's own section, or all of them if it has none.</summary>
        private bool CollectPatrolPoints(List<string> options)
        {
            if (markers == null) RefreshMarkers();

            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m == null || m.kind != PrisonMarkerKind.PatrolPoint) continue;
                if (m.section != guard.assignedSection) continue;
                if (!options.Contains(m.id)) options.Add(m.id);
            }

            if (options.Count > 0) return true;

            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m == null || m.kind != PrisonMarkerKind.PatrolPoint) continue;
                if (!options.Contains(m.id)) options.Add(m.id);
            }
            return options.Count > 0;
        }

        /// <summary>Sections with open trouble, plus whatever the radio just mentioned.</summary>
        private bool CollectRespondSections(List<string> options)
        {
            if (board != null)
            {
                board.SectionsWithIncidents(sections);
                for (int i = 0; i < sections.Count; i++) options.Add(sections[i].Id());
            }

            PrisonSection? heard = availability != null ? availability.MostRecentRadioSection() : null;
            if (heard != null && !options.Contains(heard.Value.Id())) options.Add(heard.Value.Id());

            return options.Count > 0;
        }

        /// <summary>The section the guard is standing in, which is the one it can report on.</summary>
        private bool CollectReportSections(List<string> options)
        {
            PrisonSection? here = board != null ? board.SectionAt(transform.position) : null;
            options.Add((here ?? guard.assignedSection).Id());
            return true;
        }

        private bool CollectPrisoners(List<string> options, bool requireUnescorted)
        {
            guard.CollectVisiblePrisoners(scratch, reachDistance);
            for (int i = 0; i < scratch.Count; i++)
            {
                PrisonerState p = scratch[i];
                if (requireUnescorted && p.EscortedBy != null) continue;
                if (!options.Contains(p.name)) options.Add(p.name);
            }
            return options.Count > 0;
        }
    }
}
