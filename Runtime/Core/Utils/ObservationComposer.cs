using System;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Interfaces;

namespace BehaviorLLM.Core.Utils
{
    /// <summary>
    /// Aggregates observations from multiple modules into the prompt's STATE block, one
    /// <c>--- Topic ---</c> section per module with something to say. Always uses <c>\n</c>.
    /// </summary>
    public static class ObservationComposer
    {
        public const string NoObservations = "[No Observations]";
        private const char NL = '\n';

        /// <summary>Composes every module's full observation.</summary>
        public static string Compose(List<IObservationModule> modules)
        {
            return Compose(modules, 0, 0);
        }

        /// <summary>
        /// Composes observations under per-kind entry budgets (0 = unlimited). Modules that
        /// implement <see cref="IBudgetedObservation"/> are asked for at most that many entries;
        /// other modules are included verbatim.
        /// </summary>
        public static string Compose(List<IObservationModule> modules, int maxVisionEntries, int maxMemoryEntries)
        {
            if (modules == null || modules.Count == 0) return NoObservations;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < modules.Count; i++)
            {
                IObservationModule module = modules[i];
                if (module == null) continue;

                string obs;
                IBudgetedObservation budgeted = module as IBudgetedObservation;
                int limit = budgeted != null ? LimitFor(budgeted.Kind, maxVisionEntries, maxMemoryEntries) : 0;
                if (budgeted != null && limit > 0) obs = budgeted.GetObservation(limit);
                else obs = module.GetObservation();

                AppendSection(sb, module.TopicName, obs);
            }

            return sb.Length == 0 ? NoObservations : sb.ToString();
        }

        /// <summary>
        /// Legacy entry point kept for callers written against topic names: modules that do not
        /// implement <see cref="IBudgetedObservation"/> are budgeted by matching their
        /// <c>TopicName</c> against "Vision" / "Short-Term Memory". Prefer implementing the
        /// interface.
        /// </summary>
        public static string ComposeCompact(List<IObservationModule> modules, int maxVisionEntries, int maxMemoryEntries)
        {
            if (modules == null || modules.Count == 0) return NoObservations;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < modules.Count; i++)
            {
                IObservationModule module = modules[i];
                if (module == null) continue;

                string obs;
                IBudgetedObservation budgeted = module as IBudgetedObservation;
                if (budgeted != null)
                {
                    int limit = LimitFor(budgeted.Kind, maxVisionEntries, maxMemoryEntries);
                    obs = limit > 0 ? budgeted.GetObservation(limit) : module.GetObservation();
                }
                else
                {
                    obs = module.GetObservation();
                    string topic = module.TopicName ?? string.Empty;
                    if (topic.Equals("Vision", StringComparison.OrdinalIgnoreCase)) obs = LimitLines(obs, maxVisionEntries, "Nothing visible.");
                    else if (topic.Equals("Short-Term Memory", StringComparison.OrdinalIgnoreCase)) obs = LimitLines(obs, maxMemoryEntries, "No recent history.");
                }

                AppendSection(sb, module.TopicName, obs);
            }

            return sb.Length == 0 ? NoObservations : sb.ToString();
        }

        private static void AppendSection(StringBuilder sb, string topic, string observation)
        {
            if (string.IsNullOrWhiteSpace(observation)) return;
            sb.Append("--- ").Append(topic).Append(" ---").Append(NL);
            sb.Append(observation.TrimEnd()).Append(NL);
        }

        private static int LimitFor(ObservationKind kind, int maxVision, int maxMemory)
        {
            switch (kind)
            {
                case ObservationKind.Vision: return Math.Max(0, maxVision);
                case ObservationKind.Memory: return Math.Max(0, maxMemory);
                default: return 0;
            }
        }

        /// <summary>Keeps the first <paramref name="maxEntries"/> non-empty lines (0 = keep all).</summary>
        public static string LimitLines(string observation, int maxEntries, string emptyFallback)
        {
            if (maxEntries <= 0 || string.IsNullOrEmpty(observation)) return observation;

            string[] raw = observation.Split(NL);
            List<string> lines = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
            if (lines.Count == 0) return emptyFallback;
            if (lines.Count > maxEntries) lines.RemoveRange(maxEntries, lines.Count - maxEntries);
            return string.Join("\n", lines);
        }
    }
}
