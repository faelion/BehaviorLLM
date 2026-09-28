using UnityEngine;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Interfaces;

namespace BehaviorLLM.Core.Modules
{
    /// <summary>
    /// Remembers the last few things that happened, so the decision maker knows what it was just
    /// doing and can notice that it is repeating itself. How many are kept, and the heading they
    /// appear under in the prompt, come from a shared <see cref="PerceptionConfig"/> asset.
    ///
    /// It records executed actions by listening to the <see cref="DecisionMaker"/> on this object
    /// or a parent, through <see cref="DecisionMaker.DecisionTelemetryRecorded"/>.
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

        private DecisionMaker owner;

        // Runs when the component is first added in the Editor.
        private void Reset()
        {
            if (config == null) config = BehaviorLLMDefaults.FindShipped<PerceptionConfig>(BehaviorLLMDefaults.PerceptionConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
        }

        private void OnEnable() => SubscribeToOwner();

        // Tried again at Start for a memory added in code before its decision maker: OnEnable
        // runs inside AddComponent, when there is nothing above it to find yet. In a loaded
        // scene every component already exists, so OnEnable is enough and this does nothing.
        private void Start() => SubscribeToOwner();

        private void OnDisable()
        {
            if (owner != null) owner.DecisionTelemetryRecorded -= OnDecision;
            owner = null;
        }

        private void SubscribeToOwner()
        {
            if (owner != null) return;
            owner = GetComponentInParent<DecisionMaker>();
            if (owner != null) owner.DecisionTelemetryRecorded += OnDecision;
        }

        // Only decisions that made the character do something are memories. A parse failure or a
        // backend error with no fallback executed nothing, and remembering it as an action would
        // tell the model it did something it did not.
        private void OnDecision(DecisionTelemetry telemetry)
        {
            if (telemetry == null) return;
            if (telemetry.resultType != DecisionResultType.ModelAction &&
                telemetry.resultType != DecisionResultType.FallbackAction) return;
            RecordEvent($"Executed Action: {telemetry.actionName}({telemetry.argument})");
        }

        public string TopicName => Cfg.memoryTopicName;
        public ObservationKind Kind => ObservationKind.Memory;
        public bool HasInterrupt() => false; // Memory usually doesn't trigger reflexes

        /// <summary>
        /// Adds one timestamped entry, dropping the oldest beyond the configured capacity. Every
        /// executed action arrives here on its own; call it from game code to remember anything
        /// else the character should know it lived through.
        /// </summary>
        public void RecordEvent(string description)
        {
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
