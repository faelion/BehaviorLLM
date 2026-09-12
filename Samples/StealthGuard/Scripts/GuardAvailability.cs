using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Decides which actions the guard may take this turn, from state the game already knows.
    ///
    /// This is the sample's main teaching point. The obvious way to express "do not chase while
    /// badly hurt" is a sentence in the ActionConfig's Model Instructions, and it does not work:
    /// measured across three models, that two-condition rule was ignored in 15 of 18 cases, and
    /// rewording it made the larger model worse. Removing Chase from the menu instead took the
    /// 2B model from 0 to 5 correct out of 6, because the JSON Schema is enforced while the model
    /// samples and an action that is not in it cannot be produced.
    ///
    /// The rule of thumb: if the game can already answer the question, do not ask the model.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Availability")]
    [RequireComponent(typeof(GuardHealth))]
    public class GuardAvailability : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("Let the guard chase only while it is healthy. Turn this off to watch the failure " +
                 "this sample exists to demonstrate: the guard keeps chasing at 20 health because the " +
                 "written rule alone does not stop it.")]
        public bool gateChaseOnHealth = true;

        [Tooltip("Offer Retreat only while the guard is hurt, so a healthy guard is never tempted to " +
                 "run away. Gating in both directions keeps the menu to the actions that make sense now.")]
        public bool gateRetreatOnHealth = true;

        private GuardHealth health;

        private void Awake()
        {
            health = GetComponent<GuardHealth>();
        }

        public bool IsActionAvailable(string actionName)
        {
            if (health == null) return true;

            switch (actionName)
            {
                case StealthGuardIds.Chase:
                    return !gateChaseOnHealth || health.IsHealthy;
                case StealthGuardIds.Retreat:
                    return !gateRetreatOnHealth || !health.IsHealthy;
                default:
                    // Unknown names stay available, so adding an action to the config later is not
                    // silently disabled by this provider.
                    return true;
            }
        }
    }
}
