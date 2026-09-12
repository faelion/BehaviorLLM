using UnityEngine;
using System.Collections.Generic;
using System.Text;

namespace BehaviorLLM.Core.Perception
{
    /// <summary>
    /// Passive metadata for any GameObject the decision maker should be able to perceive (player,
    /// enemy, item, the decision maker's own root). Vision strategies discover this component via
    /// collider lookup and call <see cref="GetContextInfo"/> to format it for the prompt.
    ///
    /// Self-perception (reporting this object's own context as a prompt observation) lives
    /// on the separate <c>SelfObservationModule</c> component. Add that alongside this
    /// component when you want the decision maker to see itself.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Perception/LLM Context Object")]
    public class LLMContextObject : MonoBehaviour
    {
        [Header("Semantic Info")]
        [Tooltip("The name the AI sees for this object, for example 'Orc Warrior' or " +
                 "'Red Key'. Defaults to the GameObject's name.")]
        public string objectName;

        [Tooltip("A short category the AI sees, for example 'Enemy', 'Item', 'Location' " +
                 "or 'Self'. It helps the AI tell things apart.")]
        public string objectType = "Generic";

        [TextArea(2, 5)]
        [Tooltip("Optional. A sentence about this object that never changes, for example " +
                 "'A locked gate to the north yard'.")]
        public string staticDescription;

        [Header("Dynamic Data")]
        [Tooltip("Optional. Live values from other scripts to include, such as health or " +
                 "current state. Each entry names a component and a field, property or " +
                 "method on it; the current value is read every decision.")]
        public List<ContextDataBinding> dataBindings = new List<ContextDataBinding>();

        private void Reset()
        {
            objectName = gameObject.name;
        }

        public string GetContextInfo()
        {
            StringBuilder sb = new StringBuilder();
            // EXPLICIT FORMAT: ID: Name | Type: Category | Info: Description
            sb.Append($"ID: {objectName} | Type: {objectType}");

            if (!string.IsNullOrEmpty(staticDescription))
                sb.Append($" | Info: {staticDescription}");

            // Append dynamic data
            foreach (var binding in dataBindings)
            {
                if (binding.sourceComponent != null)
                {
                    sb.Append($" | {binding.memberName}: {binding.GetValue()}");
                }
            }

            return sb.ToString();
        }
    }
}
