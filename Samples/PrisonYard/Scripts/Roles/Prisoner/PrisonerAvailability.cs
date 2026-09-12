using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Decides which actions a prisoner may take this turn. This is the sample's clearest example
    /// of the package's central lesson.
    ///
    /// "Only start a fight if you are a hothead and no guard can see you" is a two-condition rule.
    /// Written into the prompt it would be ignored most of the time, as the project measured. Here
    /// both conditions are things the game already knows exactly: the temperament is a field, and
    /// whether a guard can see this prisoner is recomputed every frame from the guards' own vision.
    /// So Fight simply is not in the schema, and the model cannot choose it.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prisoner Availability")]
    [RequireComponent(typeof(PrisonerState))]
    public class PrisonerAvailability : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("How close another prisoner must be to be worth talking to, in metres.")]
        public float talkDistance = 4f;

        [Tooltip("How close another prisoner must be to start a fight with, in metres.")]
        public float fightDistance = 3f;

        [Tooltip("Withhold the misbehaving actions while a guard can see this prisoner. Turn this " +
                 "off to watch prisoners start fights in front of the guards, which is what asking " +
                 "the model to remember the rule would get you.")]
        public bool gateOnBeingWatched = true;

        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private PrisonerState stateCache;
        private PrisonStatusBoard boardCache;
        private readonly List<PrisonerState> scratch = new List<PrisonerState>();

        private PrisonerState state => stateCache != null ? stateCache : (stateCache = GetComponent<PrisonerState>());
        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());

        public bool IsActionAvailable(string actionName)
        {
            // Nothing else is on the menu while a guard is walking them somewhere: the only honest
            // answer is to comply.
            bool underControl = state.EscortedBy != null;

            switch (actionName)
            {
                case PrisonYardIds.Comply:
                    return underControl;

                case PrisonYardIds.FollowSchedule:
                    return !underControl;

                case PrisonYardIds.Wander:
                    return !underControl && !IsHereLocked();

                case PrisonYardIds.Talk:
                    return !underControl && HasNeighbour(talkDistance);

                case PrisonYardIds.Fight:
                    return !underControl
                        && state.temperament == PrisonerTemperament.Hothead
                        && Unwatched()
                        && !IsHereLocked()
                        && HasNeighbour(fightDistance);

                case PrisonYardIds.Hide:
                    return !underControl && state.hasContraband && Unwatched() && HasHidingSpotHere();

                case PrisonYardIds.Sneak:
                    return !underControl
                        && state.temperament == PrisonerTemperament.Escapee
                        && Unwatched()
                        && HasSomewhereToSneak();

                default:
                    // Unknown names stay available, so adding an action to the config later is not
                    // silently disabled by this provider.
                    return true;
            }
        }

        private bool Unwatched()
        {
            return !gateOnBeingWatched || !state.ObservedByGuard;
        }

        private bool IsHereLocked()
        {
            if (board == null) return false;
            PrisonSection? here = board.SectionAt(transform.position);
            return here != null && board.IsLocked(here.Value);
        }

        private bool HasNeighbour(float distance)
        {
            CollectNeighbours(scratch, distance);
            return scratch.Count > 0;
        }

        private bool HasHidingSpotHere()
        {
            return FindHidingSpot() != null;
        }

        private bool HasSomewhereToSneak()
        {
            if (board == null) return false;
            PrisonSection? here = board.SectionAt(transform.position);
            for (int i = 0; i < PrisonSections.PrisonerAccessible.Length; i++)
            {
                PrisonSection s = PrisonSections.PrisonerAccessible[i];
                if (here != null && s == here.Value) continue;
                if (board.IsLocked(s)) continue;
                return true;
            }
            return false;
        }

        /// <summary>The nearest hiding spot in the section this prisoner is standing in, or null.</summary>
        public PrisonMarker FindHidingSpot()
        {
            if (board == null) return null;
            PrisonSection? here = board.SectionAt(transform.position);
            if (here == null) return null;

            PrisonMarker[] markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m != null && m.kind == PrisonMarkerKind.HidingSpot && m.section == here.Value) return m;
            }
            return null;
        }

        /// <summary>Other prisoners within reach, nearest first. Shared with the options provider.</summary>
        public void CollectNeighbours(List<PrisonerState> into, float distance)
        {
            into.Clear();
            PrisonerState[] all = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                PrisonerState other = all[i];
                if (other == null || other == state) continue;
                if (Vector3.Distance(transform.position, other.transform.position) > distance) continue;
                into.Add(other);
            }
            into.Sort((a, b) => Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));
        }
    }
}
