using UnityEngine;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using System.Linq;

namespace BehaviorLLM.Core.Modules
{
    /// <summary>
    /// Remembers the last few things that happened, so the decision maker knows what it was just
    /// doing and can notice that it is repeating itself. How many are kept, and the heading they
    /// appear under in the prompt, come from a shared <see cref="PerceptionConfig"/> asset.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Perception/Basic Memory")]
    public class BasicMemory : MonoBehaviour, IObservationModule, IBudgetedObservation
    {
        [Header("Configuration")]
        [Tooltip("Memory settings: how many recent events to remember. Use the same " +
                 "Perception Config asset as the vision module on this object, so one " +
                 "asset describes everything it notices. Leave empty for sensible " +
                 "defaults.")]
        [SerializeField] private PerceptionConfig config;

        private PerceptionConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(config));
        private PerceptionConfig cfg;

        private Queue<string> shortTermHistory = new Queue<string>();

        // Runs when the component is first added in the Editor.
        private void Reset()
        {
            if (config == null) config = BehaviorLLMDefaults.FindShipped<PerceptionConfig>(BehaviorLLMDefaults.PerceptionConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
        }

        public string TopicName => Cfg.memoryTopicName;
        public ObservationKind Kind => ObservationKind.Memory;
        public bool HasInterrupt() => false; // Memory usually doesn't trigger reflexes

        public void RecordEvent(string description)
        {
            // Timestamp could be added here if needed
            string entries = $"[{Time.time:F1}s] {description}";
            
            shortTermHistory.Enqueue(entries);
            
            // Prune old memories
            while (shortTermHistory.Count > Cfg.memoryCapacity)
            {
                shortTermHistory.Dequeue();
            }
        }

        /// <summary>Swaps the perception configuration at runtime. Null restores the defaults.</summary>
        public void ApplyConfig(PerceptionConfig newConfig)
        {
            config = newConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newConfig);
        }

        public string GetObservation()
        {
            return GetObservation(0);
        }

        /// <summary>Most recent <paramref name="maxEntries"/> events in chronological order (0 = all).</summary>
        public string GetObservation(int maxEntries)
        {
            if (shortTermHistory.Count == 0) return "No recent history.";

            int skip = maxEntries > 0 ? Mathf.Max(0, shortTermHistory.Count - maxEntries) : 0;
            StringBuilder sb = new StringBuilder();
            int index = 0;
            foreach (var mem in shortTermHistory)
            {
                if (index++ < skip) continue;
                sb.AppendLine($"- {mem}");
            }
            return sb.ToString();
        }

        // Handy for debugging
        public void Clear() => shortTermHistory.Clear();
    }
}
