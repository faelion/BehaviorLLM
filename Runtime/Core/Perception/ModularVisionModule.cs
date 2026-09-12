using UnityEngine;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using System.Collections.Generic;
using System.Text;

namespace BehaviorLLM.Core.Perception
{
    public enum VisionMode { Sphere, Cone, Global }

    [AddComponentMenu("BehaviorLLM/Perception/Modular Vision Module")]
    /// <summary>
    /// Notices the objects around this one and describes them to the decision maker. What it can
    /// see, how far, what blocks the view and how often it looks all come from a shared
    /// <see cref="PerceptionConfig"/> asset rather than from fields here.
    /// </summary>
    public class ModularVisionModule : MonoBehaviour, IObservationModule, IBudgetedObservation
    {
        [Header("Configuration")]
        [Tooltip("Vision settings: shape, range, which layers count and which block " +
                 "sight, and how often to look. Create one with Create > BehaviorLLM > " +
                 "Perception Config, or duplicate the Perception_Default preset from " +
                 "Runtime/Defaults. Leave empty for sensible defaults. Give the Basic " +
                 "Memory on this object the same asset.")]
        [SerializeField] private PerceptionConfig config;

        // Runtime state
        private IVisionStrategy strategy;
        private List<LLMContextObject> visibleObjects = new List<LLMContextObject>();
        private readonly HashSet<int> lastVisibleIds = new HashSet<int>();
        private readonly HashSet<int> scratchVisibleIds = new HashSet<int>();
        private float scanAccumulator = 0f;
        private bool firstScanPerformed = false;
        private bool interruptPending = false;

        // Resolved lazily as well as at Awake, because gizmo drawing and editor tooling read
        // these on a component that has never woken up.
        private PerceptionConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(config));
        private PerceptionConfig cfg;

        /// <summary>The perception settings in effect, including the fallback when none is assigned.</summary>
        public PerceptionConfig Config => Cfg;

        public string TopicName => Cfg.visionTopicName;
        public ObservationKind Kind => ObservationKind.Vision;

        // Runs when the component is first added in the Editor: point it at the preset the
        // package ships so it works before the user has authored a single asset.
        private void Reset()
        {
            if (config == null) config = BehaviorLLMDefaults.FindShipped<PerceptionConfig>(BehaviorLLMDefaults.PerceptionConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
            UpdateStrategy();
        }

        private void OnValidate()
        {
            // Update strategy when inspector changes (if playing)
            if (Application.isPlaying) UpdateStrategy();
        }

        // Range used for the actual physics scan. In Global mode this is overridden to
        // an effectively-infinite sweep without mutating the user-configured `range`.
        private const float GlobalModeScanRange = 9999f;

        private float EffectiveScanRange => Cfg.mode == VisionMode.Global ? GlobalModeScanRange : Cfg.range;

        private void UpdateStrategy()
        {
            switch (Cfg.mode)
            {
                case VisionMode.Sphere: strategy = new SphereVisionStrategy(); break;
                case VisionMode.Cone: strategy = new ConeVisionStrategy(Cfg.fovAngle); break;
                // Global: not implemented as its own strategy yet; fall back to sphere.
                // The effective scan range is handled by EffectiveScanRange so that the
                // serialized `range` is not silently overwritten when switching modes.
                case VisionMode.Global: strategy = new SphereVisionStrategy(); break;
            }
        }

        public void Update()
        {
            if (strategy == null) return;

            scanAccumulator += Time.deltaTime;
            if (firstScanPerformed && scanAccumulator < Cfg.scanInterval) return;
            scanAccumulator = 0f;
            firstScanPerformed = true;

            visibleObjects = strategy.Scan(transform, EffectiveScanRange, Cfg.perceptionLayers, Cfg.occluderLayers);

            if (Cfg.triggerInterruptOnNewObject)
            {
                UpdateInterruptByIdentity();
            }
        }

        private void UpdateInterruptByIdentity()
        {
            scratchVisibleIds.Clear();
            for (int i = 0; i < visibleObjects.Count; i++)
            {
                LLMContextObject obj = visibleObjects[i];
                if (obj == null) continue;
                scratchVisibleIds.Add(obj.GetInstanceID());
            }

            // Trigger if any previously-unseen object has appeared. A pure size drop with
            // no new entries (e.g. an object left the cone) is intentionally NOT a reflex.
            foreach (int id in scratchVisibleIds)
            {
                if (!lastVisibleIds.Contains(id))
                {
                    interruptPending = true;
                    break;
                }
            }

            lastVisibleIds.Clear();
            foreach (int id in scratchVisibleIds) lastVisibleIds.Add(id);
        }

        /// <summary>
        /// Swaps the perception configuration at runtime. Use it to widen one character's senses
        /// without giving every character that shares the asset the same change: clone the asset
        /// with <c>Instantiate</c>, edit it and pass it here. Null restores the defaults.
        /// </summary>
        public void ApplyConfig(PerceptionConfig newConfig)
        {
            config = newConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newConfig);
            UpdateStrategy();
        }

        /// <summary>
        /// What was visible at the last scan. Read-only: the list is reused between scans, so copy
        /// it rather than holding on to it.
        /// </summary>
        public IReadOnlyList<LLMContextObject> VisibleObjects => visibleObjects;

        /// <summary>
        /// True when the given object was visible at the last scan. Lets game code answer "is
        /// anyone watching?" without asking the model to reason about it.
        /// </summary>
        public bool Sees(Transform target)
        {
            if (target == null) return false;
            for (int i = 0; i < visibleObjects.Count; i++)
            {
                LLMContextObject obj = visibleObjects[i];
                if (obj != null && obj.transform == target) return true;
            }
            return false;
        }

        public bool HasInterrupt()
        {
            if (interruptPending)
            {
                interruptPending = false;
                return true;
            }
            return false;
        }

        public string GetObservation()
        {
            return GetObservation(0);
        }

        /// <summary>Nearest <paramref name="maxEntries"/> visible objects (0 = all), nearest first.</summary>
        public string GetObservation(int maxEntries)
        {
            if (visibleObjects.Count == 0) return "Nothing visible.";

            visibleObjects.Sort((a, b) =>
                Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));

            int count = maxEntries > 0 ? Mathf.Min(maxEntries, visibleObjects.Count) : visibleObjects.Count;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                LLMContextObject obj = visibleObjects[i];
                if (obj == null) continue;
                string dist = Vector3.Distance(transform.position, obj.transform.position).ToString("F1");
                sb.AppendLine($"- [{dist}m] {obj.GetContextInfo()}");
            }
            return sb.ToString();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            if (Cfg.mode == VisionMode.Sphere) Gizmos.DrawWireSphere(transform.position, Cfg.range);
            if (Cfg.mode == VisionMode.Cone)
            {
                Gizmos.DrawWireSphere(transform.position, 0.5f);
                Vector3 left = Quaternion.Euler(0, -Cfg.fovAngle / 2, 0) * transform.forward * Cfg.range;
                Vector3 right = Quaternion.Euler(0, Cfg.fovAngle / 2, 0) * transform.forward * Cfg.range;
                Gizmos.DrawLine(transform.position, transform.position + left);
                Gizmos.DrawLine(transform.position, transform.position + right);
            }
        }
    }
}
