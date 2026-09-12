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
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Behavior LLM Client")]
    public class BehaviorLLMClient : MonoBehaviour, ILLMBackend, IBackendDiagnostics
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

        /// <summary>Transport in effect.</summary>
        public BackendTransport ActiveTransport => Cfg.transport;

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
        public string Endpoint => (activeBaseUrl ?? string.Empty).TrimEnd('/') +
            (Cfg.transport == BackendTransport.ChatCompletions ? "/v1/chat/completions" : "/completion");

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

            string body = Cfg.transport == BackendTransport.ChatCompletions
                ? LlamaRequestWriter.WriteChatCompletion(request, BuildSettings(request), schema)
                : LlamaRequestWriter.WriteRawCompletion(request, BuildSettings(request), schema);
            string endpoint = Endpoint;

            int maxAttempts = server != null ? Mathf.Max(2, Cfg.modelLoadingRetryCount + 1) : 1;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
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

                    float latencyMs = Mathf.Max(0f, (Time.realtimeSinceStartup - started) * 1000f);
                    string raw = web.downloadHandler != null ? web.downloadHandler.text : string.Empty;

                    if (web.result == UnityWebRequest.Result.Success)
                    {
                        ClearBackendFailure();
                        LLMResponse response = ParseResponseBody(raw, latencyMs);
                        response.StructuredOutput = response.Succeeded && !string.IsNullOrEmpty(schema);
                        return response;
                    }

                    bool loadingModel = IsModelLoadingResponse(web.responseCode, raw);
                    bool startupIssue = IsLikelyStartupConnectionError(web.error);
                    if (attempt < maxAttempts && (startupIssue || loadingModel))
                    {
                        await Task.Delay(Mathf.Max(50, Cfg.startupRetryDelayMs), cancellationToken);
                        continue;
                    }

                    string message = loadingModel
                        ? "Server is still loading the model. Wait a few seconds and retry."
                        : $"Request failed ({web.result}) at '{endpoint}': {web.error}" + (string.IsNullOrWhiteSpace(raw) ? "" : $" | Body: {raw}");
                    SetBackendFailure(message);
                    return LLMResponse.Failed(message, latencyMs);
                }
            }

            SetBackendFailure("Request failed: exhausted retries.");
            return LLMResponse.Failed(LastBackendError);
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
