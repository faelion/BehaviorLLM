using UnityEngine;
using BehaviorLLM.Core.Interfaces;

namespace BehaviorLLM.Core.Perception
{
    /// <summary>
    /// Wraps an <see cref="LLMContextObject"/> as an <see cref="IObservationModule"/> so the
    /// decision maker reports the host GameObject's own context as a self-status observation.
    ///
    /// Replaces the legacy <c>isObservationSource</c> boolean that used to live on
    /// LLMContextObject. Making self-perception an explicit component separates two roles
    /// that were previously toggled by a single checkbox:
    /// <list type="bullet">
    ///   <item><description>Passive metadata that Vision strategies discover by collider lookup
    ///   (LLMContextObject's only remaining role).</description></item>
    ///   <item><description>Active observation produced every decision tick (this component's
    ///   role).</description></item>
    /// </list>
    /// Add this component alongside an LLMContextObject on the decision maker root to surface
    /// "Self-Status" in the prompt.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Perception/Self Observation Module")]
    [RequireComponent(typeof(LLMContextObject))]
    public class SelfObservationModule : MonoBehaviour, IObservationModule
    {
        [Tooltip("The heading this object's own status appears under in the text sent to " +
                 "the AI.")]
        public string topicName = "Self-Status";

        private LLMContextObject contextObject;

        public string TopicName => topicName;

        private void Awake()
        {
            contextObject = GetComponent<LLMContextObject>();
        }

        public string GetObservation()
        {
            if (contextObject == null) contextObject = GetComponent<LLMContextObject>();
            return contextObject != null ? contextObject.GetContextInfo() : string.Empty;
        }

        // Self-status changes (health drops, state flips) are surfaced via dynamic data
        // bindings in the context object's prompt block; they do not currently trigger
        // an explicit interrupt. Override via subclass if your project needs that.
        public bool HasInterrupt() => false;
    }
}
