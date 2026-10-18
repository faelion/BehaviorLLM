using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// HTTP backend for a local llama.cpp <c>llama-server</c> (or any server exposing the same
    /// OpenAI-compatible <c>/v1/chat/completions</c> endpoint). Schema, slot and thinking policy
    /// arrive with each <see cref="LLMRequest"/>, so one client can serve any number of decision makers.
    ///
    /// It serves both paradigms. A generative model is asked through <see cref="CompleteAsync"/>;
    /// a decision model through <see cref="DecideAsync"/>, which posts to the server's
    /// <c>/v1/systemone</c> endpoint. Which one is used is the project's paradigm, not the client's.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Behavior LLM Client")]
    public class BehaviorLLMClient : MonoBehaviour, ILLMBackend, IDecisionModelBackend, IBackendDiagnostics
    {
        [Header("Configuration")]
        [Tooltip("Where the AI server is and how to talk to it: address, timeouts, " +
                 "retries. Create one with Create > BehaviorLLM > Server Config, or " +
                 "duplicate the Server_LocalLlama preset from Runtime/Defaults. Leave " +
                 "empty for sensible defaults, which expect a local server on port 8080.")]
        [SerializeField] private BehaviorLLMServerConfig serverConfig;

        [Tooltip("Which model is running and how it should generate text. Pick one of " +
                 "the presets in Runtime/Defaults/Models, or create one with Create > " +
                 "BehaviorLLM > Model Config. Use the same asset on the Behavior LLM " +
                 "Server so both agree.")]
        [SerializeField] private BehaviorLLMModelConfig modelConfig;

        [Header("Scene Wiring")]
        [Tooltip("Optional. The Behavior LLM Server component in this scene, if you let " +
                 "BehaviorLLM run the server for you. Leave empty to find it " +
                 "automatically. Leave the scene without one if you start llama-server " +
                 "yourself.")]
        [SerializeField] private BehaviorLLMServer server;

        public bool IsBackendReady { get; private set; } = true;
        public string LastBackendError { get; private set; }

        // Resolved at Awake; never null, so no field read needs a null check.
        private BehaviorLLMServerConfig cfg;
        private BehaviorLLMModelConfig model;

        // The address actually in use. Starts from the config and is overwritten by the managed
        // server's real address when one is running, which is why it is not the serialized field.
        private string activeBaseUrl;

        private float lastErrorLogTime = -999f;
        private string lastLoggedError;

        /// <summary>Sampling configuration, exposed so telemetry can record what was measured.</summary>
        public struct GenerationSummary
        {
            public int MaxTokens;
            public float Temperature;
            public float TopP;
            public int TopK;
            public float RepeatPenalty;
        }

        /// <summary>Address requests are currently sent to.</summary>
        public string BaseUrl => activeBaseUrl;

        /// <summary>Transport in effect, after any automatic switch.</summary>
        public BackendTransport ActiveTransport => transportOverride ?? Cfg.transport;

        /// <summary>
        /// Set once a model has been caught thinking instead of answering, after which this client
        /// uses raw completion for the rest of the session. Null while the configured transport is
        /// in use. Cleared by <see cref="ApplyConfig"/>, since a different model may behave.
        /// </summary>
        private BackendTransport? transportOverride;

        /// <summary>True when the client changed transport by itself, for tooling to report.</summary>
        public bool SwitchedTransportAutomatically => transportOverride.HasValue;

        /// <summary>Server configuration in effect, including the fallback when none is assigned.</summary>
        public BehaviorLLMServerConfig ServerConfig => Cfg;

        /// <summary>Model configuration in effect, including the fallback when none is assigned.</summary>
        public BehaviorLLMModelConfig ModelConfig => Model;

        /// <summary>Sampling actually in effect, exposed so telemetry can record what was measured.</summary>
        public GenerationSummary Settings => new GenerationSummary
        {
            MaxTokens = Model.maxTokens,
            Temperature = Model.temperature,
            TopP = Model.topP,
            TopK = Model.topK,
            RepeatPenalty = Model.repeatPenalty
        };

        /// <summary>Full endpoint the current transport posts to.</summary>
        public string Endpoint => EndpointFor(ActiveTransport);

        /// <summary>Full endpoint decision-model requests post to.</summary>
        public string DecisionEndpoint => (activeBaseUrl ?? string.Empty).TrimEnd('/') + SystemOneProtocol.EndpointPath;

        private string EndpointFor(BackendTransport transport) =>
            (activeBaseUrl ?? string.Empty).TrimEnd('/') +
            (transport == BackendTransport.ChatCompletions ? "/v1/chat/completions" : "/completion");

        // Resolved lazily as well as at Awake, because editor tooling and tests read these
        // properties on a component that has never woken up.
        private BehaviorLLMServerConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(serverConfig));
        private BehaviorLLMModelConfig Model => model != null ? model : (model = BehaviorLLMDefaults.OrTransientDefault(modelConfig));

        // ------------------------------------------------------------------ response shapes

        [Serializable] private class ResponseBody
        {
            public Choice[] choices;   // chat completions
            public Usage usage;
            public Timings timings;    // llama-server extension on both endpoints
            public string content;     // raw /completion
            public int tokens_cached;  // raw /completion
        }
        [Serializable] private class Choice { public Message message; public string text; }
        [Serializable] private class Message { public string content; public string reasoning_content; }
        [Serializable] private class Usage { public int prompt_tokens; public int completion_tokens; }
        [Serializable] private class Timings { public int prompt_n; public int predicted_n; public int cache_n; }

        // ------------------------------------------------------------------ lifecycle

        // Runs when the component is first added in the Editor: point it at the presets the
        // package ships so it works before the user has authored a single asset.
        private void Reset()
        {
            if (serverConfig == null) serverConfig = BehaviorLLMDefaults.FindShipped<BehaviorLLMServerConfig>(BehaviorLLMDefaults.ServerConfigAsset);
            if (modelConfig == null) modelConfig = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(BehaviorLLMDefaults.ModelConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(serverConfig);
            model = BehaviorLLMDefaults.OrTransientDefault(modelConfig);
            activeBaseUrl = NormalizeBaseUrl(cfg.baseUrl);
            ResolveServer();
            RefreshEndpointFromServer();
            if (server != null) IsBackendReady = server.IsReady;
        }

        /// <summary>
        /// Swaps the server and model configuration at runtime. Either argument may be null to
        /// fall back to the documented defaults. The address is re-resolved immediately, so this
        /// is also how you point a running scene at a different server.
        /// </summary>
        public void ApplyConfig(BehaviorLLMServerConfig newServerConfig, BehaviorLLMModelConfig newModelConfig)
        {
            serverConfig = newServerConfig;
            modelConfig = newModelConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newServerConfig);
            model = BehaviorLLMDefaults.OrTransientDefault(newModelConfig);
            transportOverride = null;   // a different model may honour the chat template
            priming = new DecisionPrefixPriming();   // nor does a different server or model keep any prefix
            activeBaseUrl = NormalizeBaseUrl(cfg.baseUrl);
            RefreshEndpointFromServer();
        }

        /// <summary>Strips endpoint paths left over from configurations that stored a full completion URL.</summary>
        public static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "http://localhost:8080";
            string u = url.Trim().TrimEnd('/');
            foreach (string suffix in new[] { "/v1/chat/completions", "/v1/completions", "/completion" })
            {
                if (u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return u.Substring(0, u.Length - suffix.Length);
            }
            return u;
        }

        // Explicit inspector field wins; otherwise one scene scan. No static singleton.
        private void ResolveServer()
        {
            if (server != null) return;
            server = FindFirstObjectByType<BehaviorLLMServer>(FindObjectsInactive.Include);
        }

        private void RefreshEndpointFromServer()
        {
            if (!Cfg.followManagedServerEndpoint || server == null) return;
            if (!string.IsNullOrWhiteSpace(server.BaseUrl)) activeBaseUrl = server.BaseUrl;
        }

        private bool PrepareEndpointAndServer()
        {
            ResolveServer();
            RefreshEndpointFromServer();
            if (string.IsNullOrWhiteSpace(activeBaseUrl))
            {
                SetBackendFailure("Base URL is empty.");
                return false;
            }
            if (server == null) return true;

            // A blocked server has already explained itself once, with instructions. Retrying and
            // re-reporting it per request is what turned one missing file into a wall of identical
            // console errors, so record the reason for the caller and stay quiet.
            if (server.StartupBlocked)
            {
                IsBackendReady = false;
                LastBackendError = server.LastError;
                return false;
            }

            if (Cfg.autoStartServerOnFirstRequest && !server.IsRunning) server.StartServer();
            if (!string.IsNullOrWhiteSpace(server.LastError))
            {
                if (server.StartupBlocked)
                {
                    IsBackendReady = false;
                    LastBackendError = server.LastError;
                }
                else
                {
                    SetBackendFailure($"Server startup failed: {server.LastError}");
                }
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ request

        public async Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) return LLMResponse.Failed("request is null");
            if (!PrepareEndpointAndServer()) return LLMResponse.Failed(LastBackendError);

            // Never silently weaken a constrained request after a schema rejection.
            string schema = request.JsonSchema;
            if (!string.IsNullOrEmpty(schema) && server != null && server.GrammarLoadFailed)
                return LLMResponse.Failed("The server rejected a JSON Schema. Correct the schema and restart the server before retrying constrained requests.");

            BackendTransport transport = ActiveTransport;
            string body = BuildBody(request, schema, transport);
            string endpoint = EndpointFor(transport);

            int maxAttempts = MaxAttempts;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                HttpResult http = await PostAsync(endpoint, body, cancellationToken);

                if (http.Success)
                {
                    ClearBackendFailure();
                    LLMResponse response = ParseResponseBody(http.Raw, http.LatencyMs);
                    response.StructuredOutput = response.Succeeded && !string.IsNullOrEmpty(schema);

                    // A model whose chat template ignores the thinking switch spends the whole
                    // budget reasoning and returns nothing to act on. Retry once without the
                    // template: on /completion the schema constrains generation from the first
                    // token, so a preamble is not merely discouraged but impossible.
                    if (ShouldRetryWithoutChatTemplate(transport, response))
                    {
                        transportOverride = BackendTransport.RawCompletion;
                        BehaviorLLMLog.Warn(() =>
                            "[BehaviorLLMClient] The model spent its whole budget of " + response.CompletionTokens +
                            " tokens reasoning and gave no answer, so its chat template ignores the " +
                            "request not to think: either it is out of date, or this is a reasoning " +
                            "model that always thinks first. Switching this client to raw completion, " +
                            "which sends the prompt without the chat template and forbids thinking from " +
                            "the first token. That fixes an out-of-date template. It does NOT fix a " +
                            "reasoning model: forbidden to think and stripped of its template, a small " +
                            "one gives the cheapest legal answer every time (the first action, with no " +
                            "argument). For a reasoning model set Profile to Deliberative with a Thinking " +
                            "Budget on the decision maker config, or use a model that does not reason " +
                            "first. To make raw completion permanent for a template that only needed " +
                            "the switch, set Transport to RawCompletion on the server config.", this);

                        transport = BackendTransport.RawCompletion;
                        body = BuildBody(request, schema, transport);
                        endpoint = EndpointFor(transport);
                        continue;
                    }

                    return response;
                }

                if (attempt < maxAttempts && http.Retryable)
                {
                    await Task.Delay(Mathf.Max(50, Cfg.startupRetryDelayMs), cancellationToken);
                    continue;
                }

                string message = http.LoadingModel
                    ? "Server is still loading the model. Wait a few seconds and retry."
                    : IsDecisionModelRefusingToGenerate(http.Code, http.Raw)
                        ? "The server is running a decision model, which scores options but cannot write an answer, " +
                          "while the project's paradigm is Generative LLM. Choose a chat model in BehaviorLLM > Model " +
                          "Manager, or set Paradigm to Decision Model in the BehaviorLLM Settings asset."
                        : $"Request failed ({http.Result}) at '{endpoint}': {http.Error}" + (string.IsNullOrWhiteSpace(http.Raw) ? "" : $" | Body: {http.Raw}");
                SetBackendFailure(message);
                return LLMResponse.Failed(message, http.LatencyMs);
            }

            SetBackendFailure("Request failed: exhausted retries.");
            return LLMResponse.Failed(LastBackendError);
        }

        /// <summary>
        /// Asks a decision model, through the server's <c>/v1/systemone</c> endpoint. Shares the
        /// server management and the startup retries with <see cref="CompleteAsync"/>; what it adds
        /// is reading the two failures specific to this paradigm, an old server and a chat model,
        /// into messages that say what to change.
        /// </summary>
        public async Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) return DecisionResponse.Failed("request is null");
            if (!PrepareEndpointAndServer()) return DecisionResponse.Failed(LastBackendError);

            string body = SystemOneProtocol.WriteRequest(request);
            string endpoint = DecisionEndpoint;

            int maxAttempts = MaxAttempts;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                HttpResult http = await PostAsync(endpoint, body, cancellationToken);

                if (http.Success)
                {
                    ClearBackendFailure();
                    DecisionResponse decided = SystemOneProtocol.ParseResponse(http.Raw, http.LatencyMs);
                    if (decided.Succeeded) PrimeAfterDecision(request.ConstantPrefix);
                    return decided;
                }

                if (attempt < maxAttempts && http.Retryable)
                {
                    await Task.Delay(Mathf.Max(50, Cfg.startupRetryDelayMs), cancellationToken);
                    continue;
                }

                // A server that failed may have restarted and lost every saved prefix.
                priming.Forget();
                string message = http.LoadingModel
                    ? "Server is still loading the model. Wait a few seconds and retry."
                    : DescribeDecisionFailure(http, endpoint);
                SetBackendFailure(message);
                return DecisionResponse.Failed(message, http.LatencyMs);
            }

            priming.Forget();
            SetBackendFailure("Request failed: exhausted retries.");
            return DecisionResponse.Failed(LastBackendError);
        }

        // ------------------------------------------------------------------ prefix priming

        private DecisionPrefixPriming priming = new DecisionPrefixPriming();

        /// <summary>
        /// Asks the server to keep this character's constant prefix, when the model config names its
        /// template head and the bookkeeping says it is due. Not awaited: the decision has already
        /// been answered, and the priming only has to arrive before the character's next one.
        /// </summary>
        private void PrimeAfterDecision(string constantPrefix)
        {
            string head = Model.decisionPrefixHead;
            if (string.IsNullOrEmpty(head) || !priming.AfterDecision(constantPrefix)) return;
            _ = PrimeAsync(head, constantPrefix);
        }

        private async Task PrimeAsync(string head, string constantPrefix)
        {
            DecisionPrefixPriming current = priming;
            try
            {
                string endpoint = (activeBaseUrl ?? string.Empty).TrimEnd('/') + SystemOneProtocol.PrimingEndpointPath;
                HttpResult http = await PostAsync(endpoint, SystemOneProtocol.WritePrimingRequest(head, constantPrefix), destroyCancellationToken);
                if (http.Success || current.Disabled) return;

                // Only this saving is lost, and decisions carry on as before, so it is said once and
                // never retried.
                current.Disable();
                string reason = http.Error + (string.IsNullOrWhiteSpace(http.Raw) ? "" : $" | Body: {http.Raw}");
                BehaviorLLMLog.Warn(() => "[BehaviorLLMClient] The server refused to keep the constant part of decision " +
                                          $"requests ({reason}). Decisions still work, but each one rereads it. Clear " +
                                          "Decision Prefix Head on the model config if this model does not support it.", this);
            }
            catch (OperationCanceledException)
            {
                // The client was destroyed while the request was in flight.
            }
            catch (Exception e)
            {
                current.Forget();
                BehaviorLLMLog.Info(() => $"[BehaviorLLMClient] Priming a decision prefix failed: {e.Message}");
            }
        }

        /// <summary>
        /// The two failures that mean the setup is wrong rather than the request, said in terms of
        /// what to change. llama-server answers 404 for a path it does not have, which is what a
        /// build from before decision models looks like, and 501 when the endpoint exists but the
        /// loaded model is not a decision model.
        /// </summary>
        internal static string DescribeDecisionFailure(long code, string raw, string endpoint, string transportError)
        {
            string serverMessage = SystemOneProtocol.ReadErrorMessage(raw);
            // llama-server reports an oversized prompt as 400 for the question and options, and as 500
            // for the types that read the whole prompt in one batch; either way the fix is the batch.
            if (serverMessage != null && serverMessage.IndexOf("batch size", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The decision request is too large for the server to read in one batch (" + serverMessage + "). " +
                       "Raise Decision Batch Tokens on the model config and restart the server, or offer fewer options " +
                       "with an Action Availability Provider.";
            switch (code)
            {
                case 404:
                    return $"The server at '{endpoint}' has no decision-model endpoint. llama-server builds before b11361 " +
                           "cannot serve decision models; install a newer one from BehaviorLLM > Model Manager (Server tab).";
                case 501:
                    return "The server's model is not a decision model" + (serverMessage != null ? $" ({serverMessage})" : "") +
                           ", while the project's paradigm is Decision Model. Choose a decision model (Kev, Laya, Julia-1...) " +
                           "in BehaviorLLM > Model Manager, or set Paradigm back to Generative LLM in the BehaviorLLM Settings asset.";
                case 400:
                case 422:
                    return "The server rejected the decision request: " + (serverMessage ?? raw);
                default:
                    return $"Decision request failed at '{endpoint}': {transportError}" +
                           (string.IsNullOrWhiteSpace(raw) ? "" : $" | Body: {raw}");
            }
        }

        private static string DescribeDecisionFailure(HttpResult http, string endpoint) =>
            DescribeDecisionFailure(http.Code, http.Raw, endpoint, http.Error);

        /// <summary>
        /// A decision model loaded in embedding mode answers a completion request with a 500 saying
        /// the context does not compute logits. That is the paradigm being wrong, not the server
        /// being broken, so it gets its own message.
        /// </summary>
        internal static bool IsDecisionModelRefusingToGenerate(long code, string raw)
        {
            return code == 500 && !string.IsNullOrEmpty(raw) &&
                   raw.IndexOf("does not support logits", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ transport

        private int MaxAttempts => server != null ? Mathf.Max(2, Cfg.modelLoadingRetryCount + 1) : 1;

        private struct HttpResult
        {
            public bool Success;
            public long Code;
            public string Raw;
            public string Error;
            public UnityWebRequest.Result Result;
            public float LatencyMs;
            public bool LoadingModel;
            public bool Retryable;
        }

        /// <summary>One POST, with cancellation and the timeout from the server config. Never throws
        /// for an HTTP failure; only cancellation escapes, as <see cref="OperationCanceledException"/>.</summary>
        private async Task<HttpResult> PostAsync(string endpoint, string body, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float started = Time.realtimeSinceStartup;

            using (UnityWebRequest web = new UnityWebRequest(endpoint, "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.timeout = Mathf.Max(1, Cfg.requestTimeoutSeconds);

                UnityWebRequestAsyncOperation op = web.SendWebRequest();
                using (cancellationToken.Register(() => { try { web.Abort(); } catch { } }))
                {
                    while (!op.isDone) await Task.Yield();
                }
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);

                string raw = web.downloadHandler != null ? web.downloadHandler.text : string.Empty;
                bool loadingModel = IsModelLoadingResponse(web.responseCode, raw);
                return new HttpResult
                {
                    Success = web.result == UnityWebRequest.Result.Success,
                    Code = web.responseCode,
                    Raw = raw,
                    Error = web.error,
                    Result = web.result,
                    LatencyMs = Mathf.Max(0f, (Time.realtimeSinceStartup - started) * 1000f),
                    LoadingModel = loadingModel,
                    Retryable = loadingModel || IsLikelyStartupConnectionError(web.error)
                };
            }
        }

        private LlamaRequestWriter.GenerationSettings BuildSettings(LLMRequest request)
        {
            GenerationSummary s = Settings;
            int tokens = request.MaxTokens > 0 ? request.MaxTokens : s.MaxTokens;
            if (s.MaxTokens > 0) tokens = Mathf.Min(tokens, s.MaxTokens);
            return new LlamaRequestWriter.GenerationSettings
            {
                MaxTokens = tokens,
                Temperature = s.Temperature,
                TopP = s.TopP,
                TopK = s.TopK,
                RepeatPenalty = s.RepeatPenalty,
                ExtraStop = Cfg.extraStop
            };
        }

        /// <summary>Parses either a chat-completions or a raw completion body into an <see cref="LLMResponse"/>.</summary>
        private string BuildBody(LLMRequest request, string schema, BackendTransport transport)
        {
            return transport == BackendTransport.ChatCompletions
                ? LlamaRequestWriter.WriteChatCompletion(request, BuildSettings(request), schema)
                : LlamaRequestWriter.WriteRawCompletion(request, BuildSettings(request), schema);
        }

        /// <summary>
        /// True for the one failure that a different transport actually fixes: a chat-completions
        /// reply that generated tokens but carries no answer.
        ///
        /// Both halves matter. No text with no tokens is a server or cancellation problem and
        /// switching would hide it; text that fails to parse is the model answering badly, which
        /// raw completion does not help with. Tokens spent and nothing said is specifically a
        /// model reasoning past its budget behind a template that ignored the request not to.
        /// Checked once per client, because the second attempt latches the transport.
        /// </summary>
        internal static bool IsThinkingWithoutAnswering(BackendTransport transport, LLMResponse response)
        {
            return transport == BackendTransport.ChatCompletions
                   && response != null
                   && response.Succeeded
                   && string.IsNullOrWhiteSpace(response.Text)
                   && response.CompletionTokens > 0;
        }

        private bool ShouldRetryWithoutChatTemplate(BackendTransport transport, LLMResponse response)
        {
            return !transportOverride.HasValue
                   && Cfg.autoSwitchTransportOnThinking
                   && IsThinkingWithoutAnswering(transport, response);
        }

        public static LLMResponse ParseResponseBody(string raw, float latencyMs)
        {
            LLMResponse r = new LLMResponse { LatencyMs = latencyMs };
            if (string.IsNullOrWhiteSpace(raw)) { r.Error = "empty response body"; return r; }

            ResponseBody body = null;
            try { body = JsonUtility.FromJson<ResponseBody>(raw); }
            catch { /* not JSON: treat the whole body as text below */ }

            if (body == null) { r.Text = raw; return r; }

            if (body.choices != null && body.choices.Length > 0 && body.choices[0] != null)
            {
                Choice c = body.choices[0];
                if (c.message != null)
                {
                    r.Text = c.message.content ?? string.Empty;
                    r.ReasoningText = c.message.reasoning_content;
                }
                else r.Text = c.text ?? string.Empty;
            }
            else if (body.content != null) r.Text = body.content;
            else r.Text = raw;

            if (body.usage != null) { r.PromptTokens = body.usage.prompt_tokens; r.CompletionTokens = body.usage.completion_tokens; }
            if (body.timings != null)
            {
                if (r.PromptTokens == 0) r.PromptTokens = body.timings.prompt_n;
                if (r.CompletionTokens == 0) r.CompletionTokens = body.timings.predicted_n;
                r.CachedTokens = body.timings.cache_n;
            }
            if (r.CachedTokens == 0) r.CachedTokens = body.tokens_cached;
            return r;
        }

        private static bool IsLikelyStartupConnectionError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            return error.IndexOf("Cannot connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("Connection refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsModelLoadingResponse(long code, string raw)
        {
            if (code != 503 || string.IsNullOrWhiteSpace(raw)) return false;
            return raw.IndexOf("Loading model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("unavailable_error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ diagnostics

        private void SetBackendFailure(string message)
        {
            IsBackendReady = false;
            LastBackendError = message;
            bool cooldownElapsed = Time.time - lastErrorLogTime >= Cfg.errorLogCooldownSeconds;
            bool changed = !string.Equals(lastLoggedError, message, StringComparison.Ordinal);
            if (cooldownElapsed || changed)
            {
                lastErrorLogTime = Time.time;
                lastLoggedError = message;
                BehaviorLLMLog.Error(() => $"[BehaviorLLMClient] {message}");
            }
        }

        private void ClearBackendFailure()
        {
            IsBackendReady = true;
            LastBackendError = null;
        }
    }
}
