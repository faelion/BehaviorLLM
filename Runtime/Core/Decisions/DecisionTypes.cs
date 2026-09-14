using System;
using UnityEngine.Events;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// How much latency a decision maker is willing to spend per decision.
    /// </summary>
    public enum DecisionProfile
    {
        /// <summary>Answer immediately: no native thinking, no reason field, small token budget.
        /// For enemies, allies and anything that must react within a beat.</summary>
        Reactive = 0,

        /// <summary>Reason before acting: a capped <c>reason</c> field and/or a native thinking
        /// budget. For managers, directors and other low-frequency decision makers.</summary>
        Deliberative = 1
    }

    public enum DecisionResultType
    {
        ModelAction = 0,
        FallbackAction = 1,
        NoAction = 2,
        BackendError = 3
    }

    public enum ArgumentPolicyDecision
    {
        NotApplicable = 0,
        Unchanged = 1,
        Normalized = 2,
        Rejected = 3
    }

    /// <summary>
    /// One record per decision attempt, published through <c>DecisionMaker.DecisionTelemetryRecorded</c>.
    /// Failure modes set <see cref="parseFailureReason"/> / <see cref="resultType"/> rather than
    /// only logging, so harnesses can aggregate them.
    /// </summary>
    [Serializable]
    public class DecisionTelemetry
    {
        public string sourceId;
        public int decisionIndex;
        public float timestampSec;
        public float latencyMs;
        public int promptLengthChars;
        public int responseLengthChars;
        public int promptTokens;
        public int completionTokens;
        public int cachedTokens;
        public DecisionProfile profile;
        public bool structuredOutput;
        public DecisionResultType resultType;
        public bool usedFallback;
        public string actionName;
        public string argument;
        public string reason;
        public string fallbackReason;
        public string parseFailureReason;
        public string backendError;
        public ArgumentPolicyDecision argumentPolicyDecision = ArgumentPolicyDecision.NotApplicable;
        public string argumentPolicyReason;
    }

    [Serializable]
    /// <summary>
    /// What a bound handler receives. Carries every value the model chose for the action, so a
    /// one-value action reads <c>args.First</c> and a multi-value one reads <c>args["speed"]</c>.
    /// </summary>
    public class ActionEvent : UnityEvent<ActionArguments> { }
}
