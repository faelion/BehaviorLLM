using System.Collections.Generic;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Telemetry;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    public class DecisionTelemetryAggregatorTests
    {
        private static DecisionTelemetry Record(DecisionResultType type, float latency = 100f, int promptTokens = 0, int cachedTokens = 0)
        {
            return new DecisionTelemetry
            {
                sourceId = "Agent",
                resultType = type,
                latencyMs = latency,
                promptLengthChars = 500,
                promptTokens = promptTokens,
                completionTokens = 10,
                cachedTokens = cachedTokens,
                structuredOutput = true
            };
        }

        [Test]
        public void Rates_CountEveryOutcomeAgainstTheTotal()
        {
            var agg = new DecisionTelemetryAggregator();
            agg.Add(Record(DecisionResultType.ModelAction));
            agg.Add(Record(DecisionResultType.ModelAction));
            agg.Add(Record(DecisionResultType.FallbackAction));
            agg.Add(Record(DecisionResultType.NoAction));

            Assert.AreEqual(4, agg.TotalDecisions);
            Assert.AreEqual(0.5f, agg.ValidActionRate, 0.0001f);
            Assert.AreEqual(0.25f, agg.FallbackRate, 0.0001f);
            Assert.AreEqual(0.25f, agg.NoActionRate, 0.0001f);
            Assert.AreEqual(0f, agg.BackendErrorRate, 0.0001f);
        }

        [Test]
        public void BackendErrors_DoNotPolluteTheLatencyDistribution()
        {
            var agg = new DecisionTelemetryAggregator();
            agg.Add(Record(DecisionResultType.ModelAction, 100f));
            agg.Add(Record(DecisionResultType.ModelAction, 200f));
            agg.Add(Record(DecisionResultType.BackendError, 30000f));

            Assert.AreEqual(150f, agg.MeanLatencyMs, 0.01f);
            Assert.AreEqual(200f, agg.P95LatencyMs, 0.01f);
            Assert.AreEqual(1, agg.BackendErrors);
        }

        [Test]
        public void CacheHitRate_IsCachedOverPromptTokens()
        {
            var agg = new DecisionTelemetryAggregator();
            agg.Add(Record(DecisionResultType.ModelAction, 100f, promptTokens: 1000, cachedTokens: 900));
            agg.Add(Record(DecisionResultType.ModelAction, 100f, promptTokens: 1000, cachedTokens: 700));

            Assert.AreEqual(0.8f, agg.CacheHitRate, 0.0001f);
            Assert.AreEqual(1000f, agg.MeanPromptTokens, 0.01f);
            Assert.AreEqual(10f, agg.MeanCompletionTokens, 0.01f);
        }

        [Test]
        public void ParseFailuresAndPolicyDecisions_AreCountedIndependentlyOfResult()
        {
            var agg = new DecisionTelemetryAggregator();
            var failed = Record(DecisionResultType.FallbackAction);
            failed.parseFailureReason = "no JSON object found";
            agg.Add(failed);

            var normalized = Record(DecisionResultType.ModelAction);
            normalized.argumentPolicyDecision = ArgumentPolicyDecision.Normalized;
            agg.Add(normalized);

            var rejected = Record(DecisionResultType.FallbackAction);
            rejected.argumentPolicyDecision = ArgumentPolicyDecision.Rejected;
            agg.Add(rejected);

            Assert.AreEqual(1, agg.ParseFailures);
            Assert.AreEqual(1, agg.PolicyNormalized);
            Assert.AreEqual(1, agg.PolicyRejected);
        }

        [Test]
        public void ProfileAndStructuredFlags_AreCounted()
        {
            var agg = new DecisionTelemetryAggregator();
            var reactive = Record(DecisionResultType.ModelAction);
            reactive.profile = DecisionProfile.Reactive;
            agg.Add(reactive);

            var deliberative = Record(DecisionResultType.ModelAction);
            deliberative.profile = DecisionProfile.Deliberative;
            deliberative.structuredOutput = false;
            agg.Add(deliberative);

            Assert.AreEqual(1, agg.DeliberativeDecisions);
            Assert.AreEqual(1, agg.StructuredDecisions);
        }

        [Test]
        public void Percentile_UsesNearestRank_AndHandlesEdgeCases()
        {
            var values = new List<float> { 50f, 10f, 40f, 20f, 30f };

            Assert.AreEqual(30f, DecisionTelemetryAggregator.Percentile(values, 50f), 0.01f);
            Assert.AreEqual(50f, DecisionTelemetryAggregator.Percentile(values, 95f), 0.01f);
            Assert.AreEqual(10f, DecisionTelemetryAggregator.Percentile(values, 0f), 0.01f);
            Assert.AreEqual(0f, DecisionTelemetryAggregator.Percentile(new List<float>(), 95f), 0.01f);
            Assert.AreEqual(0f, DecisionTelemetryAggregator.Percentile(null, 95f), 0.01f);
            // The caller's list must not be reordered.
            Assert.AreEqual(50f, values[0], 0.01f);
        }

        [Test]
        public void EmptyAggregator_ReportsZeroesNotDivisionByZero()
        {
            var agg = new DecisionTelemetryAggregator();
            Assert.AreEqual(0f, agg.ValidActionRate);
            Assert.AreEqual(0f, agg.CacheHitRate);
            Assert.AreEqual(0f, agg.MeanLatencyMs);
            Assert.AreEqual(0f, agg.MeanPromptTokens);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var agg = new DecisionTelemetryAggregator();
            agg.Add(Record(DecisionResultType.ModelAction, 100f, 100, 50));
            agg.Reset();

            Assert.AreEqual(0, agg.TotalDecisions);
            Assert.AreEqual(0f, agg.MeanLatencyMs);
            Assert.AreEqual(0f, agg.CacheHitRate);
            Assert.IsNull(agg.LastAction);
        }

        [Test]
        public void CsvQuoting_OnlyWhenNeeded()
        {
            Assert.AreEqual("plain", DecisionTelemetryRecorder.Csv("plain"));
            Assert.AreEqual("\"a,b\"", DecisionTelemetryRecorder.Csv("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", DecisionTelemetryRecorder.Csv("say \"hi\""));
            Assert.AreEqual(string.Empty, DecisionTelemetryRecorder.Csv(null));
        }
    }
}
