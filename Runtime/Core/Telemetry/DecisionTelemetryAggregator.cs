using System;
using System.Collections.Generic;
using BehaviorLLM.Core.Decisions;

namespace BehaviorLLM.Core.Telemetry
{
    /// <summary>
    /// Rolls a stream of <see cref="DecisionTelemetry"/> records into the counters and
    /// distributions a run report needs. Pure and allocation-light: no Unity types, no IO, so
    /// it is unit-testable and can be reused by editor tools.
    /// </summary>
    public sealed class DecisionTelemetryAggregator
    {
        private readonly List<float> latencies = new List<float>();
        private readonly List<int> promptChars = new List<int>();

        public string SourceId { get; private set; }

        public int TotalDecisions { get; private set; }
        public int ModelActions { get; private set; }
        public int FallbackActions { get; private set; }
        public int NoActions { get; private set; }
        public int BackendErrors { get; private set; }
        public int ParseFailures { get; private set; }
        public int PolicyRejected { get; private set; }
        public int PolicyNormalized { get; private set; }
        public int DeliberativeDecisions { get; private set; }
        public int StructuredDecisions { get; private set; }

        public long PromptTokens { get; private set; }
        public long CompletionTokens { get; private set; }
        public long CachedTokens { get; private set; }

        public string LastAction { get; private set; }
        public string LastArgument { get; private set; }

        public DecisionTelemetryAggregator(string sourceId = null)
        {
            SourceId = sourceId;
        }

        /// <summary>
        /// Share of decisions where the model itself produced a valid, dispatchable action.
        /// This is the headline reliability number: fallbacks, unparseable output and backend
        /// failures all count against it.
        /// </summary>
        public float ValidActionRate => Rate(ModelActions);
        public float FallbackRate => Rate(FallbackActions);
        public float NoActionRate => Rate(NoActions);
        public float BackendErrorRate => Rate(BackendErrors);

        /// <summary>Share of prompt tokens the server served from its KV cache (prefix reuse).</summary>
        public float CacheHitRate => PromptTokens > 0 ? (float)CachedTokens / PromptTokens : 0f;

        public float MeanLatencyMs => Mean(latencies);
        public float MedianLatencyMs => Percentile(latencies, 50f);
        public float P95LatencyMs => Percentile(latencies, 95f);
        public float MeanPromptChars => Mean(promptChars);
        public float MeanPromptTokens => TotalDecisions > 0 ? (float)PromptTokens / TotalDecisions : 0f;
        public float MeanCompletionTokens => TotalDecisions > 0 ? (float)CompletionTokens / TotalDecisions : 0f;

        public void Add(DecisionTelemetry t)
        {
            if (t == null) return;

            TotalDecisions++;
            if (SourceId == null) SourceId = t.sourceId;

            // A backend failure has no meaningful latency sample to contribute: the request
            // either never left or timed out, and including it would skew the distribution
            // the reader uses to judge in-game responsiveness.
            if (t.latencyMs > 0f && t.resultType != DecisionResultType.BackendError && string.IsNullOrEmpty(t.backendError)) latencies.Add(t.latencyMs);
            if (t.promptLengthChars > 0) promptChars.Add(t.promptLengthChars);

            PromptTokens += Math.Max(0, t.promptTokens);
            CompletionTokens += Math.Max(0, t.completionTokens);
            CachedTokens += Math.Max(0, t.cachedTokens);

            if (t.profile == DecisionProfile.Deliberative) DeliberativeDecisions++;
            if (t.structuredOutput) StructuredDecisions++;
            // A backend failure may also execute a fallback. Count both outcomes.
            if (t.resultType == DecisionResultType.BackendError || !string.IsNullOrEmpty(t.backendError)) BackendErrors++;
            if (!string.IsNullOrWhiteSpace(t.parseFailureReason)) ParseFailures++;

            if (t.argumentPolicyDecision == ArgumentPolicyDecision.Rejected) PolicyRejected++;
            else if (t.argumentPolicyDecision == ArgumentPolicyDecision.Normalized) PolicyNormalized++;

            if (!string.IsNullOrWhiteSpace(t.actionName))
            {
                LastAction = t.actionName;
                LastArgument = t.argument;
            }

            switch (t.resultType)
            {
                case DecisionResultType.ModelAction: ModelActions++; break;
                case DecisionResultType.FallbackAction: FallbackActions++; break;
                case DecisionResultType.NoAction: NoActions++; break;
                case DecisionResultType.BackendError: break;
            }
        }

        public void Reset()
        {
            latencies.Clear();
            promptChars.Clear();
            TotalDecisions = ModelActions = FallbackActions = NoActions = BackendErrors = 0;
            ParseFailures = PolicyRejected = PolicyNormalized = 0;
            DeliberativeDecisions = StructuredDecisions = 0;
            PromptTokens = CompletionTokens = CachedTokens = 0;
            LastAction = LastArgument = null;
        }

        private float Rate(int count) => TotalDecisions > 0 ? (float)count / TotalDecisions : 0f;

        private static float Mean(List<float> values)
        {
            if (values.Count == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return (float)(sum / values.Count);
        }

        private static float Mean(List<int> values)
        {
            if (values.Count == 0) return 0f;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return (float)(sum / values.Count);
        }

        /// <summary>
        /// Nearest-rank percentile over a copy of the samples (the caller's order is preserved).
        /// </summary>
        public static float Percentile(List<float> values, float percentile)
        {
            if (values == null || values.Count == 0) return 0f;
            List<float> sorted = new List<float>(values);
            sorted.Sort();
            if (sorted.Count == 1) return sorted[0];

            int rank = (int)Math.Ceiling(percentile / 100f * sorted.Count);
            rank = Math.Min(Math.Max(rank, 1), sorted.Count);
            return sorted[rank - 1];
        }
    }
}
