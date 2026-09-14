using UnityEngine;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BehaviorLLM.Core.Config;
using System;
using Debug = UnityEngine.Debug;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// [OPTIONAL] Manages the lifecycle of the local llama-server process.
    /// Use this if you want BehaviorLLM to manage the server process for you.
    /// If you run llama-server (or another compatible server) yourself, you DO NOT need this component.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Behavior LLM Server")]
    public class BehaviorLLMServer : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("How to launch the server: program, port, lanes, options and logging. " +
                 "Create one with Create > BehaviorLLM > Server Config, or duplicate the " +
                 "Server_LocalLlama preset from Runtime/Defaults. Use the same asset on " +
                 "the Behavior LLM Client.")]
        [SerializeField] private BehaviorLLMServerConfig serverConfig;

        [Tooltip("Which model file to load and how much of it to put on the graphics " +
                 "card. Pick a preset from Runtime/Defaults/Models, or create one with " +
                 "Create > BehaviorLLM > Model Config. Leave empty to use the model " +
                 "chosen in BehaviorLLM > Model Manager.")]
        [SerializeField] private BehaviorLLMModelConfig modelConfig;

        // Resolved at Awake; never null, so no field read needs a null check.
        private BehaviorLLMServerConfig settings;

        private Process serverProcess;
        private int activePort;
        private int activeContextSize;
        private int activeGpuLayers;
        public bool IsRunning => serverProcess != null && !serverProcess.HasExited;
        public bool IsReady { get; private set; }
        public string LastError { get; private set; }
        public string LastResolvedModelPath { get; private set; }
        public int ActivePort => activePort > 0 ? activePort : Settings.port;
        /// <summary>Context window the server was launched with (resolved from the config file).</summary>
        public int ContextSize => activeContextSize;
        /// <summary>Layers offloaded to the GPU (-ngl).</summary>
        public int GpuLayers => activeGpuLayers;
        /// <summary>Concurrent slots the server was launched with (-np).</summary>
        public int ParallelSlots => Settings.parallelSlots;
        public string BaseUrl => $"http://localhost:{ActivePort}";
        public string CompletionEndpoint => BaseUrl + "/v1/chat/completions";

        // Latches `true` the first time llama-server logs `failed to parse grammar` (it reports
        // a rejected JSON Schema the same way). Clients reject subsequent constrained requests
        // until the process is restarted, rather than silently generating without constraints.
        public bool GrammarLoadFailed { get; private set; }

        // Lifecycle is per-scene by design. If you need the server process to outlive a
        // scene reload, place this component on a root GameObject and add Unity's
        // DontDestroyOnLoad (or your own persistence wrapper) yourself. The old
        // implementation called DontDestroyOnLoad unconditionally, which warned at runtime
        // whenever the component was nested under a non-root GameObject and silently
        // ignored the call. Making persistence explicit removes that footgun.
        // Runs when the component is first added in the Editor: point it at the presets the
        // package ships so it works before the user has authored a single asset.
        private void Reset()
        {
            if (serverConfig == null) serverConfig = BehaviorLLMDefaults.FindShipped<BehaviorLLMServerConfig>(BehaviorLLMDefaults.ServerConfigAsset);
            if (modelConfig == null) modelConfig = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(BehaviorLLMDefaults.ModelConfigAsset);
        }

        private void Awake()
        {
            settings = BehaviorLLMDefaults.OrTransientDefault(serverConfig);
            activePort = settings.port;
            activeContextSize = ModelSettings.contextSize;
            activeGpuLayers = ModelSettings.gpuLayers;
            if (settings.autoStartOnAwake) StartServer();
        }

        // Resolved lazily as well as at Awake, because editor tooling and tests read these
        // before the component has ever woken up.
        private BehaviorLLMServerConfig Settings => settings != null ? settings : (settings = BehaviorLLMDefaults.OrTransientDefault(serverConfig));
        private BehaviorLLMModelConfig ModelSettings => modelConfig != null ? modelConfig : BehaviorLLMDefaults.OrTransientDefault<BehaviorLLMModelConfig>(null);

        public void StartServer()
        {
            // Refuse quietly once we know startup cannot succeed, so a client that retries per
            // request does not turn one missing file into hundreds of identical console errors.
            if (StartupBlocked) return;

            if (IsRunning)
            {
                BehaviorLLMLog.Warn(() => "[BehaviorLLMServer] Server is already running.");
                return;
            }

            LastError = null;
            LastResolvedModelPath = null;
            IsReady = false;
            GrammarLoadFailed = false;

            RuntimeServerConfig cfg = ResolveRuntimeConfig();
            activePort = cfg.port;
            activeContextSize = cfg.contextSize;
            activeGpuLayers = cfg.gpuLayers;
            string binPath = ResolveExecutablePath(cfg.executableRelativePath);

            if (!File.Exists(binPath))
            {
                SetError($"llama-server not found. Looked for '{cfg.executableRelativePath}' under StreamingAssets, " +
                         $"on PATH, and in the usual install locations. Install it with " +
                         $"BehaviorLLM > Model Manager (Server tab), or with 'winget install llama.cpp' / 'brew install llama.cpp', " +
                         $"or set Executable Name on the server config to the full path of the binary. " +
                         $"(If you installed it while the Editor was open, restart Unity so it picks up your PATH.)", permanent: true);
                return;
            }

            string fullModelPath = ResolveModelPath(cfg.modelRelativePath);
            if (!File.Exists(fullModelPath))
            {
                SetError($"Model file not found at: {fullModelPath}. Download one with " +
                         $"BehaviorLLM > Model Manager, or point this component's Model Config at a " +
                         $"GGUF you already have.", permanent: true);
                return;
            }
            LastResolvedModelPath = fullModelPath;

            // Construct arguments for llama-server
            string args = BuildArgs(fullModelPath, cfg);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(binPath),
                UseShellExecute = Settings.showTerminal,
                CreateNoWindow = !Settings.showTerminal,
                RedirectStandardOutput = !Settings.showTerminal,
                RedirectStandardError = !Settings.showTerminal
            };

            try
            {
                serverProcess = new Process { StartInfo = startInfo };
                
                if (!Settings.showTerminal)
                {
                    serverProcess.OutputDataReceived += (sender, e) => ForwardServerLogLine(e.Data, false);
                    serverProcess.ErrorDataReceived += (sender, e) => ForwardServerLogLine(e.Data, true);
                }

                serverProcess.Start();
                
                if (!Settings.showTerminal)
                {
                    serverProcess.BeginOutputReadLine();
                    serverProcess.BeginErrorReadLine();
                }

                BehaviorLLMLog.Info(() => $"[BehaviorLLMServer] Started server on port {cfg.port} | Model: {fullModelPath}");
            }
            catch (System.Exception e)
            {
                SetError($"Failed to start server: {e.Message}");
            }
        }

        /// <summary>
        /// Swaps the server and model configuration. Either argument may be null to fall back to
        /// the documented defaults. A running process keeps its old settings until it is restarted,
        /// so call <see cref="StopServer"/> and <see cref="StartServer"/> to apply them.
        /// </summary>
        public void ApplyConfig(BehaviorLLMServerConfig newServerConfig, BehaviorLLMModelConfig newModelConfig)
        {
            serverConfig = newServerConfig;
            modelConfig = newModelConfig;
            settings = BehaviorLLMDefaults.OrTransientDefault(newServerConfig);
            ClearStartupBlock();
        }

        public void StopServer()
        {
            IsReady = false;
            if (serverProcess != null && !serverProcess.HasExited)
            {
                BehaviorLLMLog.Info(() => "[BehaviorLLMServer] Stopping server...");
                try
                {
                    serverProcess.Kill();
                    serverProcess.WaitForExit();
                }
                catch (System.Exception e)
                {
                    BehaviorLLMLog.Warn(() => $"[BehaviorLLMServer] Error stopping server: {e.Message}");
                }
                finally
                {
                    serverProcess.Dispose();
                    serverProcess = null;
                }
            }
        }

        private void OnApplicationQuit()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
        }

        // Two kinds of failure need different handling. A missing executable or model will not fix
        // itself, and clients retry on every request, so logging it each time buries the console
        // (one run produced 74 copies of the same line). Those set StartupBlocked, are logged once,
        // and make StartServer a no-op until something changes. Transient failures still log.
        private void SetError(string message, bool permanent = false)
        {
            bool isRepeat = string.Equals(LastError, message, StringComparison.Ordinal);
            LastError = message;
            IsReady = false;
            if (permanent) StartupBlocked = true;
            if (!isRepeat) BehaviorLLMLog.Error(() => $"[BehaviorLLMServer] {message}");
        }

        /// <summary>
        /// True when startup failed for a reason that retrying cannot fix, such as a missing
        /// executable or model file. Clients check this to stop asking. Cleared by
        /// <see cref="ClearStartupBlock"/>, or by changing the model config or paths.
        /// </summary>
        public bool StartupBlocked { get; private set; }

        /// <summary>Allows startup to be attempted again after fixing paths at runtime.</summary>
        public void ClearStartupBlock()
        {
            StartupBlocked = false;
            LastError = null;
        }

        private void ForwardServerLogLine(string line, bool fromErrorStream)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            if (IsServerReadyLine(line))
            {
                IsReady = true;
                BehaviorLLMLog.Info(() => $"[BehaviorLLMServer] {line}");
                return;
            }

            // Latch the grammar-rejection signal once. llama.cpp emits this for every request
            // that ships a malformed grammar, so clients need a fast way to disable grammar
            // for subsequent calls instead of paying the parse-failure cost every time.
            if (!GrammarLoadFailed && line.IndexOf("failed to parse grammar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                GrammarLoadFailed = true;
                BehaviorLLMLog.Warn(() => "[BehaviorLLMServer] llama-server rejected the supplied JSON Schema. " +
                                 "Subsequent requests will be sent WITHOUT structured output. " +
                                 "Inspect StreamingAssets/_last_applied_schema.json and restart the server to re-enable.");
            }

            if (IsServerRequestTrafficLine(line) && !Settings.logServerRequestTraffic) return;

            // The one startup line that answers "is this running on the GPU?": "offloaded 41/41
            // layers to GPU" or "offloaded 0/41". Without it a session on the CPU looks identical
            // to a healthy one until the telemetry is read afterwards. Full offload is the
            // expected case and stays at Info; anything short of it is a warning, because the
            // project's default log level is Warnings and an Info line nobody sees answers
            // nothing. The buffer-size line beside it says where the weights went.
            if (IsOffloadSummaryLine(line))
            {
                if (TryReadOffload(line, out int offloaded, out int total) && offloaded < total)
                    BehaviorLLMLog.Warn(() => $"[BehaviorLLMServer] Only {offloaded} of {total} layers are on the GPU " +
                                              $"(GPU Layers is {activeGpuLayers}). The rest run on the CPU, several times slower. " +
                                              "Raise GPU Layers on the model config, or pick a smaller model if the card is out of memory.");
                else
                    BehaviorLLMLog.Info(() => $"[LLM] {line}");
                return;
            }

            if (IsServerErrorLine(line))
            {
                BehaviorLLMLog.Error(() => $"[LLM] {line}");
                return;
            }

            if (IsServerWarningLine(line))
            {
                BehaviorLLMLog.Warn(() => $"[LLM] {line}");
                return;
            }

            if (!Settings.verboseServerLogs)
            {
                // llama.cpp often writes normal startup/info to stderr; avoid noisy false "errors".
                return;
            }

            // Verbose mode: log both streams identically. The `fromErrorStream` flag is
            // kept on the signature for future severity routing (e.g. a "verbose-stderr-as-
            // warning" toggle) but currently the user opted into noise, so we treat both
            // streams the same.
            BehaviorLLMLog.Info(() => $"[LLM] {line}");
        }

        /// <summary>Reads "offloaded 12/41 layers to GPU". False for the buffer-size line and anything else.</summary>
        internal static bool TryReadOffload(string line, out int offloaded, out int total)
        {
            offloaded = total = 0;
            if (string.IsNullOrEmpty(line)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(line, @"offloaded\s+(\d+)\s*/\s*(\d+)\s+layers", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            return int.TryParse(m.Groups[1].Value, out offloaded) && int.TryParse(m.Groups[2].Value, out total);
        }

        private static bool IsOffloadSummaryLine(string line)
        {
            return (line.IndexOf("offloaded", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    line.IndexOf("layers to GPU", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   line.IndexOf("model buffer size", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsServerReadyLine(string line)
        {
            return line.IndexOf("server is listening on", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("HTTP server is listening", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("starting the main loop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsServerRequestTrafficLine(string line)
        {
            return line.IndexOf("log_server_r:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("request:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("done request:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsServerWarningLine(string line)
        {
            return line.IndexOf("warn:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("n_ctx_seq (", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsServerErrorLine(string line)
        {
            // Many llama.cpp "error stream" lines are informational; only mark clear failure signatures.
            return line.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("unable to", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private RuntimeServerConfig ResolveRuntimeConfig()
        {
            var cfg = new RuntimeServerConfig
            {
                executableRelativePath = Settings.executableName,
                modelRelativePath = string.Empty,
                port = Settings.port,
                contextSize = ModelSettings.contextSize,
                gpuLayers = ModelSettings.gpuLayers
            };

            // The StreamingAssets file is optional: it may be disabled, or simply absent on a
            // machine where the Model Catalog has never run. Those cases must still fall through
            // to the model config below, which is why this is not an early return.
            BehaviorLLMBackendConfigData fileCfg = Settings.useStreamingConfig ? BehaviorLLMBackendConfig.Load(Settings.configFileName) : null;
            if (fileCfg != null)
            {
                if (!string.IsNullOrWhiteSpace(fileCfg.executableRelativePath))
                {
                    cfg.executableRelativePath = fileCfg.executableRelativePath;
                }

                if (Settings.preferConfigModel && !string.IsNullOrWhiteSpace(fileCfg.activeModelRelativePath))
                {
                    cfg.modelRelativePath = fileCfg.activeModelRelativePath;
                }

                cfg.port = fileCfg.port > 0 ? fileCfg.port : cfg.port;
                cfg.contextSize = fileCfg.contextSize > 0 ? fileCfg.contextSize : cfg.contextSize;
                cfg.gpuLayers = fileCfg.gpuLayers >= 0 ? fileCfg.gpuLayers : cfg.gpuLayers;
            }

            return ApplyModelConfig(cfg);
        }

        // Applied last so an explicitly assigned model config wins over the StreamingAssets file,
        // which is machine-local and often left over from whatever the Model Catalog last installed.
        private RuntimeServerConfig ApplyModelConfig(RuntimeServerConfig cfg)
        {
            if (modelConfig == null) return cfg;

            if (modelConfig.HasModelFile) cfg.modelRelativePath = modelConfig.ResolveModelRelativePath();
            if (modelConfig.contextSize > 0) cfg.contextSize = modelConfig.contextSize;
            if (modelConfig.gpuLayers >= 0) cfg.gpuLayers = modelConfig.gpuLayers;
            return cfg;
        }

        /// <summary>
        /// Where a llama-server would actually be launched from right now, and how it was found.
        /// Public so the Editor tooling resolves it the same way the runtime does: the window used
        /// to check only StreamingAssets and report "not installed" for a machine where the
        /// runtime would have launched a system-wide install without complaint.
        /// </summary>
        /// <param name="streamingRelativePath">The configured path, usually from the backend config.</param>
        /// <param name="absolutePath">The resolved executable, when one exists.</param>
        /// <param name="source">"StreamingAssets", "PATH" or "system install", for the UI to show.</param>
        public static bool TryLocateExecutable(string streamingRelativePath, out string absolutePath, out string source)
        {
            absolutePath = null;
            source = null;

            string basePath = ResolveStreamingRelativePath(streamingRelativePath);
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!basePath.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)) basePath += ".exe";
            #endif

            if (File.Exists(basePath))
            {
                absolutePath = basePath;
                source = "StreamingAssets";
                return true;
            }

            string fileName = Path.GetFileName(basePath);

            string onPath = FindOnPath(fileName);
            if (onPath != null) { absolutePath = onPath; source = "PATH"; return true; }

            string wellKnown = FindInWellKnownLocations(fileName);
            if (wellKnown != null) { absolutePath = wellKnown; source = "system install"; return true; }

            return false;
        }

        private string ResolveExecutablePath(string relativeOrAbsolute)
        {
            string basePath = ResolveStreamingRelativePath(relativeOrAbsolute);

            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!basePath.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase))
                basePath += ".exe";
            #endif

            if (File.Exists(basePath)) return basePath;

            // Fall back to a llama-server installed system-wide, so a developer machine does not
            // need a second copy under StreamingAssets. PATH is checked first, then the usual
            // install directories: the Editor captures PATH at launch, so a package manager run
            // while Unity was open is invisible to it until a restart, and searching the known
            // locations avoids that restart.
            string fileName = Path.GetFileName(basePath);
            string onPath = FindOnPath(fileName);
            if (onPath != null) return onPath;

            string wellKnown = FindInWellKnownLocations(fileName);
            if (wellKnown != null) return wellKnown;

            return basePath;
        }

        /// <summary>Directories package managers install llama.cpp into, checked when PATH is stale.</summary>
        private static string FindInWellKnownLocations(string fileName)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<string> roots = new List<string>();

            if (!string.IsNullOrEmpty(localAppData))
            {
                roots.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));
                // winget keeps the real binaries under Packages/<id>/, so scan one level down.
                string packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
                TryAddSubdirectories(packages, "*llamacpp*", roots);
                TryAddSubdirectories(packages, "*llama.cpp*", roots);
            }
            if (!string.IsNullOrEmpty(home))
            {
                roots.Add(Path.Combine(home, ".local", "bin"));
                roots.Add(Path.Combine(home, "bin"));
            }
            roots.Add("/usr/local/bin");
            roots.Add("/opt/homebrew/bin");
            roots.Add("/usr/bin");

            for (int i = 0; i < roots.Count; i++)
            {
                try
                {
                    string candidate = Path.Combine(roots[i], fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* malformed or inaccessible path */ }
            }
            return null;
        }

        private static void TryAddSubdirectories(string root, string pattern, List<string> into)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                string[] found = Directory.GetDirectories(root, pattern);
                for (int i = 0; i < found.Length; i++) into.Add(found[i]);
            }
            catch { /* permissions */ }
        }

        private static string FindOnPath(string fileName)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar) || string.IsNullOrEmpty(fileName)) return null;
            foreach (string dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string candidate = Path.Combine(dir.Trim(), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* malformed PATH entry */ }
            }
            return null;
        }

        private string ResolveModelPath(string relativeOrAbsolute)
        {
            return ResolveStreamingRelativePath(relativeOrAbsolute);
        }

        private static string ResolveStreamingRelativePath(string relativeOrAbsolute)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolute)) return "";
            if (Path.IsPathRooted(relativeOrAbsolute)) return relativeOrAbsolute;
            return Path.Combine(Application.streamingAssetsPath, relativeOrAbsolute);
        }

        // No --reasoning-budget here on purpose: a server-wide budget disables the per-request
        // thinking control that lets reactive and deliberative decision makers share one server.
        private string BuildArgs(string fullModelPath, RuntimeServerConfig cfg)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"-m \"{fullModelPath}\"");
            sb.Append($" -c {cfg.contextSize}");
            sb.Append($" --port {cfg.port}");
            sb.Append(" --host 127.0.0.1");
            sb.Append($" -ngl {cfg.gpuLayers}");
            if (Settings.parallelSlots > 0) sb.Append($" -np {Settings.parallelSlots}");
            if (Settings.cacheReuse > 0) sb.Append($" --cache-reuse {Settings.cacheReuse}");
            if (Settings.useJinja) sb.Append(" --jinja");
            if (!string.IsNullOrWhiteSpace(Settings.extraArguments)) sb.Append(' ').Append(Settings.extraArguments.Trim());
            return sb.ToString();
        }

        private struct RuntimeServerConfig
        {
            public string executableRelativePath;
            public string modelRelativePath;
            public int port;
            public int contextSize;
            public int gpuLayers;
        }
    }
}
