using System.Threading;
using System.Threading.Tasks;

namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>
    /// One completion request. Everything the backend needs travels with the request
    /// (prompt sections, structured-output schema, slot, thinking policy) so that a single
    /// backend component can serve any number of decision makers with different constraints.
    /// </summary>
    public sealed class LLMRequest
    {
        /// <summary>Static part of the prompt: persona, action menu, examples, guide. Sent as the
        /// system message in chat mode. Keep it stable between calls so the server can reuse
        /// its KV-cache prefix.</summary>
        public string SystemPrompt;

        /// <summary>Dynamic part of the prompt: the current observations. Sent as the user
        /// message in chat mode.</summary>
        public string UserPrompt;

        /// <summary>Text appended after the user prompt when the backend talks to a raw
        /// (non-chat) completion endpoint, e.g. <c>OUTPUT: </c>. Ignored in chat mode.</summary>
        public string CompletionLead;

        /// <summary>JSON Schema (as a JSON object literal) constraining the completion, or null
        /// for unconstrained generation.</summary>
        public string JsonSchema;

        /// <summary>Name reported with the schema in <c>response_format</c>.</summary>
        public string SchemaName = "decision";

        /// <summary>Maximum tokens to generate. 0 lets the backend apply its own default.</summary>
        public int MaxTokens;

        /// <summary>Extra stop sequences. May be null.</summary>
        public string[] Stop;

        /// <summary>llama-server slot to pin this request to, or -1 for automatic. Pinning one
        /// slot per decision maker keeps each cached prefix alive across decisions.</summary>
        public int SlotId = -1;

        /// <summary>Whether the model may run its native "thinking" phase before answering.
        /// Off in the Reactive profile: it adds latency proportional to the thinking length.</summary>
        public bool EnableThinking;

        /// <summary>Token budget for native thinking when <see cref="EnableThinking"/> is on.
        /// -1 leaves it unbounded.</summary>
        public int ThinkingBudgetTokens = -1;
    }

    /// <summary>
    /// Result of a completion request. <see cref="Text"/> is null when the call failed;
    /// <see cref="Error"/> then carries a human-readable reason.
    /// </summary>
    public sealed class LLMResponse
    {
        /// <summary>The generated text (the model's answer, with any native reasoning removed
        /// when the server separates it).</summary>
        public string Text;

        /// <summary>Native reasoning text returned separately by the server, if any.</summary>
        public string ReasoningText;

        public int PromptTokens;
        public int CompletionTokens;

        /// <summary>Prompt tokens served from the server's KV cache, when reported.</summary>
        public int CachedTokens;

        /// <summary>Wall-clock time of the HTTP round trip in milliseconds.</summary>
        public float LatencyMs;

        /// <summary>True when the backend successfully completed this request with its supplied
        /// JSON Schema. Backends must leave this false when no constraint was applied.</summary>
        public bool StructuredOutput;

        /// <summary>Failure reason, or null on success.</summary>
        public string Error;

        public bool Succeeded => Error == null && Text != null;

        public static LLMResponse Failed(string error, float latencyMs = 0f)
        {
            return new LLMResponse { Error = error ?? "unknown backend error", LatencyMs = latencyMs };
        }
    }

    /// <summary>
    /// Abstract interface for any local LLM backend. Decouples the decision maker from the transport
    /// (HTTP to llama-server, an in-process runtime, a test double).
    /// </summary>
    public interface ILLMBackend
    {
        /// <summary>
        /// Sends a request and returns the completion. Implementations must observe
        /// <paramref name="cancellationToken"/> and raise <see cref="System.OperationCanceledException"/>
        /// when it is signalled (the decision maker cancels when disabled or destroyed mid-inference).
        /// Non-fatal failures return <see cref="LLMResponse.Failed"/> rather than throwing.
        /// </summary>
        Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional runtime diagnostics exposed by a backend implementation.
    /// </summary>
    public interface IBackendDiagnostics
    {
        bool IsBackendReady { get; }
        string LastBackendError { get; }
    }
}
