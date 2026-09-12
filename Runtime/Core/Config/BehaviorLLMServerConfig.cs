using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>How the client talks to the server.</summary>
    public enum BackendTransport
    {
        /// <summary><c>/v1/chat/completions</c>: the server applies the model's chat template,
        /// honours the thinking switch and returns reasoning separately. The default, and the
        /// right choice for every instruct model.</summary>
        ChatCompletions = 0,

        /// <summary>llama-server's native <c>/completion</c> with a raw prompt and no chat
        /// template. For base models, or for measuring what the template is worth.</summary>
        RawCompletion = 1
    }

    /// <summary>
    /// Everything about <i>where the model runs and how we talk to it</i>, in one reusable asset:
    /// the address, the transport, the process launch flags, the retry policy and how much the
    /// server is allowed to say in the console.
    ///
    /// Both the client component and the optional server-process component read this, so a
    /// project points every scene at one asset and changes a port, a slot count or a timeout in
    /// one place. What the model <i>is</i> lives in a separate Model Config, so you can swap
    /// models without touching this and change servers without touching that.
    ///
    /// Create with <c>Create &gt; BehaviorLLM &gt; Server Config</c>, or start from the preset the
    /// package ships at <c>Runtime/Defaults/Server_LocalLlama.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewServerConfig", menuName = "BehaviorLLM/Server Config", order = 2)]
    public class BehaviorLLMServerConfig : ScriptableObject
    {
        [Header("Connection")]
        [Tooltip("The address of the AI server, without any path after the port. The " +
                 "default is what llama-server uses when you start it yourself. Change " +
                 "it to point at a server on another machine.")]
        public string baseUrl = "http://localhost:8080";

        [Tooltip("How requests are sent. Chat Completions is right for every normal " +
                 "(instruct) model and is what you want. Raw Completion sends the text " +
                 "with no formatting, for base models or experiments.")]
        public BackendTransport transport = BackendTransport.ChatCompletions;

        [Tooltip("When a Behavior LLM Server component is in the scene, use the address " +
                 "of the server it started instead of Base Url above. Leave on; then " +
                 "changing Port below is enough.")]
        public bool followManagedServerEndpoint = true;

        [Tooltip("Start the server the first time a decision is needed, if it is not " +
                 "running yet. Play mode starts fast, and the first decision waits for " +
                 "the model to load. Turn off if you start the server yourself.")]
        public bool autoStartServerOnFirstRequest = true;

        [Header("Managed Process")]
        [Tooltip("Start the server as soon as the scene loads. Leave off if you run " +
                 "llama-server yourself, or if you prefer the on-demand start above.")]
        public bool autoStartOnAwake = false;

        [Tooltip("The llama-server program to run. It is looked for in StreamingAssets, " +
                 "then on your PATH, then in the usual install folders. Type a full path " +
                 "to use a specific build.")]
        public string executableName = "llama-server";

        [Tooltip("The network port the server listens on. Change it if something else " +
                 "already uses 8080, and give each server its own port if you run more " +
                 "than one.")]
        public int port = 8080;

        [Tooltip("How many decision makers the server can serve at once, each in its own " +
                 "lane. Each lane remembers its own text, so set this to at least the " +
                 "number of decision makers, or they overwrite each other and every " +
                 "decision is slow. Note that the model's Context Size is split across " +
                 "these lanes.")]
        public int parallelSlots = 4;

        [Tooltip("Advanced. Lets the server reuse remembered text even when the start of " +
                 "it has changed, in chunks of this many tokens. 0 turns it off. Some " +
                 "builds do not support it; it is then dropped automatically and nothing " +
                 "breaks.")]
        public int cacheReuse = 256;

        [Tooltip("Let the server format the text the way the model expects. Required for " +
                 "Chat Completions. Leave on.")]
        public bool useJinja = true;

        [Tooltip("Advanced. Extra command-line options passed to llama-server exactly as " +
                 "typed, for anything not covered above, for example --flash-attn on.")]
        public string extraArguments = "";

        [Header("Model File Source")]
        [Tooltip("Also read the model chosen in Tools > BehaviorLLM > Model Catalog, " +
                 "which saves a small file into StreamingAssets. Turn off to use only " +
                 "the Model Config asset.")]
        public bool useStreamingConfig = true;

        [Tooltip("The name of that file inside StreamingAssets. Only change it if you " +
                 "keep several.")]
        public string configFileName = "behaviorllm_backend_config.json";

        [Tooltip("When both exist, let the Model Catalog's choice win over the Model " +
                 "Config asset. Handy for testers who swap models without opening the " +
                 "project.")]
        public bool preferConfigModel = true;

        [Header("Requests")]
        [Tooltip("Give up on a decision after this many seconds. Make it longer than " +
                 "your slowest decision; a large model on the CPU can take several " +
                 "seconds.")]
        public int requestTimeoutSeconds = 30;

        [Tooltip("Milliseconds to wait between retries while the server is still " +
                 "starting up.")]
        public int startupRetryDelayMs = 350;

        [Tooltip("How many times to retry while the server is loading the model. " +
                 "Multiply by the delay above: the defaults allow about seven seconds.")]
        public int modelLoadingRetryCount = 20;

        [Tooltip("Advanced. Extra text sequences that make the AI stop writing when it " +
                 "produces them. Usually empty.")]
        public string[] extraStop = new string[0];

        [Header("Logging")]
        [Tooltip("Open the server in its own console window instead of sending its " +
                 "output to the Unity console. Useful when it fails to start and you " +
                 "want to see its raw log.")]
        public bool showTerminal = false;

        [Tooltip("Send everything the server prints to the Unity console. When off, only " +
                 "warnings, errors and the 'ready' line get through.")]
        public bool verboseServerLogs = false;

        [Tooltip("Print one line per request the server handles. Confirms that decisions " +
                 "are reaching it; noisy with several decision makers.")]
        public bool logServerRequestTraffic = false;

        [Tooltip("Minimum seconds between repeats of the same error in the console, so a " +
                 "server that is down does not flood it.")]
        public float errorLogCooldownSeconds = 5f;
    }
}
