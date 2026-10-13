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

    /// <summary>
    /// How the package turns a situation into an action. Chosen once per project in
    /// <c>BehaviorLLMSettings</c>, because it decides which kind of model the server loads.
    /// </summary>
    public enum DecisionParadigm
    {
        /// <summary>A generative chat model writes a JSON decision, constrained by a grammar built
        /// from the action config so it can only name legal actions and values.</summary>
        GenerativeLLM = 0,

        /// <summary>A decision model in the style of TypeSafe's Jev reads the situation and returns a
        /// probability for every legal action instead of writing text, through llama-server's
        /// <c>/v1/systemone</c> endpoint. Nothing is generated, so there is no format to get wrong
        /// and no reason field.</summary>
        DecisionModel = 1
    }

    /// <summary>Which candidate a decision model's distribution is turned into.</summary>
    public enum DecisionModelSelection
    {
        /// <summary>The most probable action, every time. The same situation gives the same answer.</summary>
        MostLikely = 0,

        /// <summary>An action drawn at random in proportion to its probability, so characters that
        /// share a config and see the same thing do not all act in lockstep.</summary>
        Sample = 1
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

        /// <summary>Which paradigm produced this decision.</summary>
        public DecisionParadigm paradigm;

        /// <summary>Decision models only: how many actions, with their values, were offered as choices.</summary>
        public int candidateCount;

        /// <summary>Decision models only: the probability the model gave the action that was taken,
        /// 0 to 1. Zero for generated decisions, which come with no distribution.</summary>
        public float choiceProbability;

        /// <summary>Decision models only: the model's confidence over the whole distribution, 0 when
        /// every option was equally likely and 1 when one option took everything.</summary>
        public float choiceConfidence;
    }

    [Serializable]
    /// <summary>
    /// What a bound handler receives. Carries every value the model chose for the action, so a
    /// one-value action reads <c>args.First</c> and a multi-value one reads <c>args["speed"]</c>.
    /// </summary>
    public class ActionEvent : UnityEvent<ActionArguments> { }
}
