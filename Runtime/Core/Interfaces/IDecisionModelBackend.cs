using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>The three kinds of question a decision model answers.</summary>
    public enum DecisionQuestionType
    {
        /// <summary>Pick one of a set of named options. Each comes back with a probability.</summary>
        Choice = 0,

        /// <summary>Place the situation on an ordered scale of 2 to 10 levels, lowest first.</summary>
        Score = 1,

        /// <summary>A yes-or-no question, answered with the probability of yes. Sent as
        /// <c>noul</c>, the name TypeSafe's API and llama-server give it.</summary>
        YesNo = 2
    }

    /// <summary>One option of a <see cref="DecisionQuestionType.Choice"/> question, or one level of a
    /// <see cref="DecisionQuestionType.Score"/> question.</summary>
    public sealed class DecisionOption
    {
        /// <summary>What the model reads as the option, and the key its probability comes back
        /// under. Must be unique within the question.</summary>
        public string Key;

        /// <summary>Optional explanation the model reads beside the key. Null sends none.</summary>
        public string Description;

        public DecisionOption() { }

        public DecisionOption(string key, string description = null)
        {
            Key = key;
            Description = description;
        }
    }

    /// <summary>One question asked about the request's state.</summary>
    public sealed class DecisionQuestion
    {
        /// <summary>Name the answer comes back under. Unique within the request.</summary>
        public string Id;

        public DecisionQuestionType Type;

        /// <summary>What is being asked, in plain words: who is deciding and what about.</summary>
        public string Instructions;

        /// <summary>The options of a Choice question, or the levels of a Score question from lowest
        /// to highest. A YesNo question needs none.</summary>
        public List<DecisionOption> Options = new List<DecisionOption>();
    }

    /// <summary>
    /// Everything a decision model is asked in one round trip: the situation, once, and any number
    /// of questions about it. The server answers every question in the same call and, for the
    /// causal models, shares the work of reading the state between them.
    /// </summary>
    public sealed class DecisionRequest
    {
        /// <summary>The situation the questions are about, as text.</summary>
        public string State;

        /// <summary>
        /// Optional. The leading part of <see cref="State"/> that is identical on every request from
        /// the same asker, such as a character's persona and guide. A backend may use it to have the
        /// server keep that part between requests; one that ignores it answers exactly the same.
        /// Null or empty when nothing is constant. When set, <see cref="State"/> starts with it.
        /// </summary>
        public string ConstantPrefix;

        /// <summary>Model name for servers that require one (TypeSafe's hosted API). llama-server
        /// ignores it. Null omits the field.</summary>
        public string Model;

        public List<DecisionQuestion> Questions = new List<DecisionQuestion>();
    }

    /// <summary>The answer to one <see cref="DecisionQuestion"/>.</summary>
    public sealed class DecisionAnswer
    {
        public string Id;
        public DecisionQuestionType Type;

        /// <summary>Choice: the most probable option's key, as the server reported it.</summary>
        public string Choice;

        /// <summary>Score: the probability-weighted level, 0-based, which can fall between levels.</summary>
        public float Score;

        /// <summary>YesNo: the probability that the answer is yes.</summary>
        public float Probability;

        /// <summary>
        /// Choice and Score: one entry per option or level in the order the server listed them,
        /// which is the order they were asked in. Keys are option keys for a Choice and level
        /// indices ("0", "1"...) for a Score. The values sum to 1.
        /// </summary>
        public List<KeyValuePair<string, float>> Probabilities = new List<KeyValuePair<string, float>>();

        /// <summary>Choice and Score: 0 when every option was equally likely, 1 when one took everything.</summary>
        public float Confidence;

        /// <summary>The probability given to <paramref name="key"/>, or 0 when it was not listed.</summary>
        public float ProbabilityOf(string key)
        {
            for (int i = 0; i < Probabilities.Count; i++)
            {
                if (string.Equals(Probabilities[i].Key, key, StringComparison.Ordinal)) return Probabilities[i].Value;
            }
            return 0f;
        }
    }

    /// <summary>
    /// Result of a <see cref="DecisionRequest"/>. <see cref="Error"/> is null on success; on failure
    /// it says why and <see cref="Answers"/> is empty.
    /// </summary>
    public sealed class DecisionResponse
    {
        public List<DecisionAnswer> Answers = new List<DecisionAnswer>();

        /// <summary>Tokens the server read, across every question. Decision models write none.</summary>
        public int InputTokens;

        /// <summary>Wall-clock time of the HTTP round trip in milliseconds.</summary>
        public float LatencyMs;

        /// <summary>The model the server says answered, when it reports one.</summary>
        public string ModelName;

        /// <summary>Failure reason, or null on success.</summary>
        public string Error;

        public bool Succeeded => Error == null;

        /// <summary>The answer to the question with this id, or null.</summary>
        public DecisionAnswer Find(string id)
        {
            for (int i = 0; i < Answers.Count; i++)
            {
                if (string.Equals(Answers[i].Id, id, StringComparison.Ordinal)) return Answers[i];
            }
            return null;
        }

        public static DecisionResponse Failed(string error, float latencyMs = 0f)
        {
            return new DecisionResponse { Error = error ?? "unknown backend error", LatencyMs = latencyMs };
        }
    }

    /// <summary>
    /// A backend that serves a decision model: one that scores options instead of writing text, in
    /// the shape of TypeSafe's System One API (<c>POST /v1/systemone</c>), which llama-server
    /// implements for the open models (Kev, Laya, Julia-1 and others).
    ///
    /// Kept separate from <see cref="ILLMBackend"/> rather than folded into it because the two take
    /// different requests and return different things; a component may implement both, as
    /// <c>BehaviorLLMClient</c> does, and serve whichever paradigm the project uses.
    /// </summary>
    public interface IDecisionModelBackend
    {
        /// <summary>
        /// Sends one request and returns every answer. Implementations must observe
        /// <paramref name="cancellationToken"/> and raise <see cref="OperationCanceledException"/> when
        /// it is signalled. Non-fatal failures return <see cref="DecisionResponse.Failed"/> rather
        /// than throwing.
        /// </summary>
        Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);
    }
}
