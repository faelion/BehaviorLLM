using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Decides which actions a guard may take this turn, from things the game already knows.
    ///
    /// Every rule here could have been written as a sentence in the action config instead, and
    /// every one of them would then have been unreliable: the project measured two-condition
    /// prose rules being ignored in 15 of 18 cases. An action removed from the menu is removed
    /// from the JSON Schema, and the model physically cannot emit it.
    /// </summary>
    [AddComponentMenu("PrisonYard/Guard Availability")]
    [RequireComponent(typeof(GuardState))]
    public class GuardAvailability : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("How close a prisoner must be before the guard is offered Search and Escort, in " +
                 "metres. The guard also has to be able to see them.")]
        public float reachDistance = 3f;

        [Tooltip("Seconds a radio call keeps Respond on the menu. After this the guard goes back to " +
                 "its routine unless the incident is still open.")]
        public float radioMemorySeconds = 20f;

        [Tooltip("Offer Retreat only below half health, and withhold the hands-on actions there. " +
                 "Turn this off to watch a hurt guard keep wading into fights, which is the failure " +
                 "this kind of gating exists to prevent.")]
        public bool gateOnHealth = true;

        // Resolved lazily rather than in Awake. A DecisionMaker builds its schema in its own
        // Awake, which calls into this provider, and Unity does not promise which component on a
        // GameObject wakes first: relying on Awake here threw a null reference on the very first
        // decision of the run.
        private GuardState guardCache;
        private PrisonStatusBoard boardCache;
        private PrisonRadio radioCache;
        private readonly List<PrisonerState> scratch = new List<PrisonerState>();

        private GuardState guard => guardCache != null ? guardCache : (guardCache = GetComponent<GuardState>());
        private PrisonStatusBoard board => boardCache != null ? boardCache : (boardCache = FindFirstObjectByType<PrisonStatusBoard>());
        private PrisonRadio radio => radioCache != null ? radioCache : (radioCache = FindFirstObjectByType<PrisonRadio>());

        public bool IsActionAvailable(string actionName)
        {
            switch (actionName)
            {
                case PrisonYardIds.HoldPosition:
                case PrisonYardIds.Patrol:
                    return true;

                case PrisonYardIds.Retreat:
                    return !gateOnHealth || !guard.IsHealthy;

                case PrisonYardIds.Escort:
                    return IsFitForHandsOn() && HasReachablePrisoner(requireUnescorted: true);

                case PrisonYardIds.Search:
                    return IsFitForHandsOn() && HasReachablePrisoner(requireUnescorted: false);

                case PrisonYardIds.Report:
                    return CanSeeTrouble();

                case PrisonYardIds.Respond:
                    return HasSomewhereToRespondTo();

                default:
                    // Unknown names stay available, so adding an action to the config later is not
                    // silently disabled by this provider.
                    return true;
            }
        }

        /// <summary>A badly hurt guard is not offered the actions that put it next to a prisoner.</summary>
        private bool IsFitForHandsOn()
        {
            return !gateOnHealth || guard.IsHealthy;
        }

        private bool HasReachablePrisoner(bool requireUnescorted)
        {
            guard.CollectVisiblePrisoners(scratch, reachDistance);
            for (int i = 0; i < scratch.Count; i++)
            {
                if (requireUnescorted && scratch[i].EscortedBy != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>True when the guard can actually see something worth calling in.</summary>
        private bool CanSeeTrouble()
        {
            guard.CollectVisiblePrisoners(scratch, float.MaxValue);
            for (int i = 0; i < scratch.Count; i++)
            {
                PrisonerState p = scratch[i];
                if (p.activity == PrisonActivities.Fighting) return true;

                // A prisoner somewhere they should not be, while that section is shut.
                if (board == null) continue;
                PrisonSection? where = board.SectionAt(p.transform.position);
                if (where != null && board.IsLocked(where.Value) && p.EscortedBy == null) return true;
            }
            return false;
        }

        /// <summary>True when a section has open trouble, or the radio mentioned one recently.</summary>
        private bool HasSomewhereToRespondTo()
        {
            if (board != null && board.HasAnyIncident()) return true;
            return MostRecentRadioSection() != null;
        }

        /// <summary>The section most recently named on the radio, within the memory window.</summary>
        public PrisonSection? MostRecentRadioSection()
        {
            if (radio == null) return null;
            var messages = radio.Messages;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (Time.time - messages[i].Time > radioMemorySeconds) break;
                if (messages[i].Section != null) return messages[i].Section;
            }
            return null;
        }
    }

    /// <summary>
    /// The activity strings the prison uses. They reach the model through context bindings, so a
    /// guard's vision literally reads "activity: Fighting" on a prisoner it can see.
    /// </summary>
    public static class PrisonActivities
    {
        public const string Idle = "Idle";
        public const string Fighting = "Fighting";
        public const string Talking = "Talking";
        public const string Hiding = "Hiding something";
        public const string Sneaking = "Sneaking";
        public const string BeingEscorted = "Being escorted";
        public const string Complying = "Complying";
        public const string Walking = "Walking";
        public const string Recovering = "Recovering";
    }
}
