using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine;

namespace BehaviorLLM.Core.Telemetry
{
    /// <summary>
    /// Opt-in run recorder: subscribes to every <see cref="DecisionMaker"/> it is told about (or every
    /// decision maker in the scene), writes one JSON line per decision, and appends a summary row per run
    /// to a CSV that accumulates across runs so configurations can be compared directly.
    ///
    /// Output goes to <c>Application.persistentDataPath/&lt;outputFolder&gt;/</c>:
    /// <list type="bullet">
    ///   <item><description><c>&lt;runId&gt;_decisions.jsonl</c> - one record per decision.</description></item>
    ///   <item><description><c>&lt;runId&gt;_sources.csv</c> - per-source aggregates.</description></item>
    ///   <item><description><c>runs_summary.csv</c> - one row per run, appended.</description></item>
    /// </list>
    /// Add scenario-specific columns through <see cref="SetExtraSummaryField"/> before the run ends.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Telemetry/Decision Telemetry Recorder")]
    [DisallowMultipleComponent]
    public class DecisionTelemetryRecorder : MonoBehaviour
    {
        [Header("Capture")]
        [Tooltip("Record every decision. Turn off to keep the component in the scene " +
                 "without it doing anything. The project's BehaviorLLM Settings asset can also " +
                 "switch telemetry off everywhere at once, and off there wins over on here.")]
        [SerializeField] private bool enableTelemetry = true;
        [Tooltip("Record every decision maker in the scene. Turn off to record only the " +
                 "ones listed below.")]
        [FormerlySerializedAs("trackAllAgentsInScene")]
        [SerializeField] private bool trackAllInScene = true;
        [Tooltip("The decision makers to record when the option above is off. Drag them " +
                 "here.")]
        [FormerlySerializedAs("trackedAgents")]
        [SerializeField] private List<DecisionMaker> trackedSources = new List<DecisionMaker>();
        [Tooltip("Optional. A short name for this run, written into the CSV so you can " +
                 "tell runs apart later, for example 'qwen-2b-reactive'.")]
        [SerializeField] private string runLabel = "";

        [Header("Run")]
        [Tooltip("Stop recording and write the report after this many seconds. 0 records " +
                 "until the scene ends.")]
        [SerializeField] private float runDurationSec = 0f;
        [Tooltip("Write the report when the scene ends or the game quits. Leave on.")]
        [SerializeField] private bool finalizeOnDestroy = true;

        [Header("Output")]
        [Tooltip("Where to write the files, as a folder inside Unity's persistent data " +
                 "path (on Windows, under %AppData%/../LocalLow/<Company>/<Product>/).")]
        [SerializeField] private string outputFolder = "BehaviorLLM/Telemetry";
        [Tooltip("Write one line per decision to a .jsonl file, with everything known " +
                 "about it including why it failed. Read this to understand a single bad " +
                 "decision.")]
        [SerializeField] private bool writeDecisionJsonl = true;

        [Tooltip("Write a table with one row per decision maker for this run, so a " +
                 "misbehaving one stands out.")]
        [FormerlySerializedAs("writeAgentCsv")]
        [SerializeField] private bool writeSourceCsv = true;

        [Tooltip("Add one row for this run to a shared runs_summary.csv that grows " +
                 "across runs. Use it to compare models and settings side by side.")]
        [SerializeField] private bool appendSummaryCsv = true;

        [Tooltip("Print the headline numbers in the Unity console when the run ends.")]
        [SerializeField] private bool logSummaryToConsole = true;

        private readonly DecisionTelemetryAggregator runTotals = new DecisionTelemetryAggregator("ALL");
        private readonly Dictionary<string, DecisionTelemetryAggregator> perSource = new Dictionary<string, DecisionTelemetryAggregator>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DecisionMaker> subscribed = new List<DecisionMaker>();
        private readonly Dictionary<string, string> extraSummaryFields = new Dictionary<string, string>(StringComparer.Ordinal);

        private StreamWriter jsonlWriter;
        private string runId;
        private DateTime runStartUtc;
        private float runStartTime;
        private bool finalized;

        /// <summary>Identifier of the current run; also the prefix of its output files.</summary>
        public string RunId => runId;

        /// <summary>Aggregate over every tracked decision maker so far.</summary>
        public DecisionTelemetryAggregator RunTotals => runTotals;

        /// <summary>Directory the report is written to.</summary>
        public string OutputDirectory => Path.GetFullPath(Path.Combine(Application.persistentDataPath,
            string.IsNullOrWhiteSpace(outputFolder) ? "BehaviorLLM/Telemetry" : outputFolder.Replace('\\', '/')));

        /// <summary>
        /// Adds or replaces a scenario-specific column in the summary CSV (e.g. task success).
        /// Column order follows first insertion, so keep the set stable across runs that share a file.
        /// </summary>
        public void SetExtraSummaryField(string column, string value)
        {
            if (string.IsNullOrWhiteSpace(column)) return;
            extraSummaryFields[column] = value ?? string.Empty;
        }

        public void SetExtraSummaryField(string column, float value)
        {
            SetExtraSummaryField(column, value.ToString("0.####", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ lifecycle

        private void Start()
        {
            if (!Recording) return;

            runStartUtc = DateTime.UtcNow;
            runStartTime = Time.time;
            runId = $"{runStartUtc:yyyyMMdd_HHmmss}" + (string.IsNullOrWhiteSpace(runLabel) ? "" : "_" + Sanitize(runLabel));

            if (trackAllInScene)
            {
                DecisionMaker[] all = FindObjectsByType<DecisionMaker>(FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++) Track(all[i]);
            }
            else
            {
                for (int i = 0; i < trackedSources.Count; i++) Track(trackedSources[i]);
            }

            if (subscribed.Count == 0)
                BehaviorLLMLog.Warn(() => "[DecisionTelemetryRecorder] No DecisionMaker found to track; the run will be empty.");
        }

        private void Update()
        {
            if (!finalized && Recording && runDurationSec > 0f && Time.time - runStartTime >= runDurationSec)
            {
                FinalizeRun("duration elapsed");
            }
        }

        private void OnApplicationQuit()
        {
            if (finalizeOnDestroy) FinalizeRun("application quit");
        }

        private void OnDestroy()
        {
            if (finalizeOnDestroy) FinalizeRun("recorder destroyed");
            Unsubscribe();
            CloseJsonl();
        }

        /// <summary>
        /// Whether this recorder may write anything right now: its own switch, and the project's.
        ///
        /// The project-wide switch is a veto rather than an override, so a scene that has
        /// deliberately turned its recorder off stays off. It exists so that shipping a scene with
        /// a recorder still in it costs nothing: turn telemetry off in the settings asset and no
        /// build writes CSVs into the player's data folder, with no scene to edit and no component
        /// to remember to remove.
        /// </summary>
        public bool Recording => enableTelemetry && BehaviorLLMSettings.Current.TelemetryAllowed;

        /// <summary>Subscribes to one decision maker (safe to call at runtime; duplicates are ignored).</summary>
        public void Track(DecisionMaker source)
        {
            if (source == null || subscribed.Contains(source)) return;
            source.DecisionTelemetryRecorded += OnDecision;
            subscribed.Add(source);
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < subscribed.Count; i++)
            {
                if (subscribed[i] != null) subscribed[i].DecisionTelemetryRecorded -= OnDecision;
            }
            subscribed.Clear();
        }

        // ------------------------------------------------------------------ capture

        private void OnDecision(DecisionTelemetry telemetry)
        {
            if (!Recording || finalized || telemetry == null) return;

            runTotals.Add(telemetry);

            string sourceId = string.IsNullOrWhiteSpace(telemetry.sourceId) ? "(unnamed)" : telemetry.sourceId;
            DecisionTelemetryAggregator agg;
            if (!perSource.TryGetValue(sourceId, out agg))
            {
                agg = new DecisionTelemetryAggregator(sourceId);
                perSource.Add(sourceId, agg);
            }
            agg.Add(telemetry);

            if (writeDecisionJsonl) WriteDecisionLine(telemetry);
        }

        private void WriteDecisionLine(DecisionTelemetry telemetry)
        {
            try
            {
                if (jsonlWriter == null)
                {
                    Directory.CreateDirectory(OutputDirectory);
                    jsonlWriter = new StreamWriter(Path.Combine(OutputDirectory, runId + "_decisions.jsonl"), false, Encoding.UTF8);
                }
                jsonlWriter.WriteLine(JsonUtility.ToJson(telemetry));
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Warn(() => $"[DecisionTelemetryRecorder] Could not write decision log: {e.Message}");
                writeDecisionJsonl = false;
            }
        }

        // ------------------------------------------------------------------ report

        /// <summary>Writes the report. Safe to call more than once; only the first call writes.</summary>
        public void FinalizeRun(string reason = "manual")
        {
            if (finalized || !Recording || runId == null) return;
            finalized = true;
            CloseJsonl();

            try
            {
                Directory.CreateDirectory(OutputDirectory);
                if (writeSourceCsv) WriteSourceCsv();
                if (appendSummaryCsv) AppendSummaryCsv(reason);
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Error(() => $"[DecisionTelemetryRecorder] Failed to write report: {e.Message}");
                return;
            }

            if (logSummaryToConsole)
            {
                BehaviorLLMLog.Requested(() => $"[DecisionTelemetryRecorder] Run '{runId}' ({reason}): {runTotals.TotalDecisions} decisions, " +
                          $"valid {runTotals.ValidActionRate:P1}, fallback {runTotals.FallbackRate:P1}, " +
                          $"latency mean {runTotals.MeanLatencyMs:F0} ms / p95 {runTotals.P95LatencyMs:F0} ms, " +
                          $"cache hit {runTotals.CacheHitRate:P1}. Report: {OutputDirectory}");
            }
        }

        private void CloseJsonl()
        {
            if (jsonlWriter == null) return;
            try { jsonlWriter.Flush(); jsonlWriter.Dispose(); } catch { }
            jsonlWriter = null;
        }

        private void WriteSourceCsv()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("source_id,total_decisions,model_actions,fallback_actions,no_actions,backend_errors,parse_failures,")
              .Append("policy_rejected,policy_normalized,valid_action_rate,fallback_rate,mean_latency_ms,p50_latency_ms,")
              .Append("p95_latency_ms,mean_prompt_chars,mean_prompt_tokens,mean_completion_tokens,cache_hit_rate,last_action,last_argument\n");

            foreach (DecisionTelemetryAggregator a in perSource.Values)
            {
                sb.Append(string.Join(",",
                    Csv(a.SourceId), N(a.TotalDecisions), N(a.ModelActions), N(a.FallbackActions), N(a.NoActions),
                    N(a.BackendErrors), N(a.ParseFailures), N(a.PolicyRejected), N(a.PolicyNormalized),
                    F(a.ValidActionRate), F(a.FallbackRate), F(a.MeanLatencyMs), F(a.MedianLatencyMs), F(a.P95LatencyMs),
                    F(a.MeanPromptChars), F(a.MeanPromptTokens), F(a.MeanCompletionTokens), F(a.CacheHitRate),
                    Csv(a.LastAction), Csv(a.LastArgument))).Append('\n');
            }

            File.WriteAllText(Path.Combine(OutputDirectory, runId + "_sources.csv"), sb.ToString(), Encoding.UTF8);
        }

        private void AppendSummaryCsv(string reason)
        {
            string path = Path.Combine(OutputDirectory, "runs_summary.csv");
            BackendDescription backend = DescribeBackend();
            float elapsed = Mathf.Max(0.001f, Time.time - runStartTime);

            List<string> headers = new List<string>
            {
                "run_id","run_label","run_start_utc","scene","elapsed_sec","sources","model","transport","endpoint",
                "context_size","gpu_layers","parallel_slots","max_tokens","temperature","top_p","top_k","repeat_penalty",
                "total_decisions","decisions_per_minute","model_actions","fallback_actions","no_actions","backend_errors",
                "parse_failures","policy_rejected","policy_normalized","deliberative_decisions","structured_decisions",
                "valid_action_rate","fallback_rate","no_action_rate","backend_error_rate",
                "mean_latency_ms","p50_latency_ms","p95_latency_ms","mean_prompt_chars","mean_prompt_tokens",
                "mean_completion_tokens","cache_hit_rate","finalization_reason"
            };
            List<string> values = new List<string>
            {
                Csv(runId), Csv(runLabel), Csv(runStartUtc.ToString("o", CultureInfo.InvariantCulture)),
                Csv(SceneManager.GetActiveScene().name), F(elapsed), N(subscribed.Count),
                Csv(backend.Model), Csv(backend.Transport), Csv(backend.Endpoint),
                N(backend.ContextSize), N(backend.GpuLayers), N(backend.ParallelSlots),
                N(backend.MaxTokens), F(backend.Temperature), F(backend.TopP), N(backend.TopK), F(backend.RepeatPenalty),
                N(runTotals.TotalDecisions), F(runTotals.TotalDecisions / elapsed * 60f),
                N(runTotals.ModelActions), N(runTotals.FallbackActions), N(runTotals.NoActions), N(runTotals.BackendErrors),
                N(runTotals.ParseFailures), N(runTotals.PolicyRejected), N(runTotals.PolicyNormalized),
                N(runTotals.DeliberativeDecisions), N(runTotals.StructuredDecisions),
                F(runTotals.ValidActionRate), F(runTotals.FallbackRate), F(runTotals.NoActionRate), F(runTotals.BackendErrorRate),
                F(runTotals.MeanLatencyMs), F(runTotals.MedianLatencyMs), F(runTotals.P95LatencyMs),
                F(runTotals.MeanPromptChars), F(runTotals.MeanPromptTokens), F(runTotals.MeanCompletionTokens),
                F(runTotals.CacheHitRate), Csv(reason)
            };

            foreach (KeyValuePair<string, string> extra in extraSummaryFields)
            {
                headers.Add(Sanitize(extra.Key));
                values.Add(Csv(extra.Value));
            }

            bool writeHeader = !File.Exists(path);
            using (StreamWriter writer = new StreamWriter(path, true, Encoding.UTF8))
            {
                if (writeHeader) writer.WriteLine(string.Join(",", headers));
                writer.WriteLine(string.Join(",", values));
            }
        }

        private struct BackendDescription
        {
            public string Model, Transport, Endpoint;
            public int ContextSize, GpuLayers, ParallelSlots, MaxTokens, TopK;
            public float Temperature, TopP, RepeatPenalty;
        }

        // Reads what the scene's client and server can tell us about the configuration under
        // test, so a summary row is self-describing when several models are compared.
        private BackendDescription DescribeBackend()
        {
            BackendDescription d = new BackendDescription { Model = "", Transport = "", Endpoint = "" };

            BehaviorLLMClient client = FindFirstClient();
            if (client != null)
            {
                d.Transport = client.ActiveTransport.ToString();
                d.Endpoint = client.Endpoint;
                BehaviorLLMClient.GenerationSummary g = client.Settings;
                d.MaxTokens = g.MaxTokens;
                d.Temperature = g.Temperature;
                d.TopP = g.TopP;
                d.TopK = g.TopK;
                d.RepeatPenalty = g.RepeatPenalty;
            }

            BehaviorLLMServer server = FindFirstObjectByType<BehaviorLLMServer>(FindObjectsInactive.Include);
            if (server != null)
            {
                d.Model = string.IsNullOrEmpty(server.LastResolvedModelPath) ? "" : Path.GetFileName(server.LastResolvedModelPath);
                d.ContextSize = server.ContextSize;
                d.GpuLayers = server.GpuLayers;
                d.ParallelSlots = server.ParallelSlots;
                if (string.IsNullOrEmpty(d.Endpoint)) d.Endpoint = server.CompletionEndpoint;
            }

            return d;
        }

        private BehaviorLLMClient FindFirstClient()
        {
            for (int i = 0; i < subscribed.Count; i++)
            {
                if (subscribed[i] == null) continue;
                BehaviorLLMClient c = subscribed[i].GetComponent<BehaviorLLMClient>();
                if (c != null) return c;
            }
            return FindFirstObjectByType<BehaviorLLMClient>(FindObjectsInactive.Include);
        }

        // ------------------------------------------------------------------ formatting

        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>Quotes a CSV field only when it needs it.</summary>
        public static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            }
            return sb.ToString();
        }
    }
}
