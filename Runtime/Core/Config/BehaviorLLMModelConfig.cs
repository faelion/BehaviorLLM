using BehaviorLLM.Core.Decisions;
using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Everything that depends on *which model* is running, in one reusable asset: the file to
    /// load, how to launch it, how to sample from it, and what it was measured to be good at.
    ///
    /// Keeping this out of the components means a project can swap models by swapping one
    /// reference, and can keep as many tuned variants as it likes without a prefab per variant.
    /// Create with <c>Create &gt; BehaviorLLM &gt; Model Config</c>, or start from one of the
    /// presets the package ships under <c>Runtime/Defaults/Models</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewModelConfig", menuName = "BehaviorLLM/Model Config", order = 1)]
    public class BehaviorLLMModelConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("The name shown for this model in menus and logs.")]
        public string displayName = "Unnamed model";
        [Tooltip("The model file to load, ending in .gguf. Just the file name if it is " +
                 "in StreamingAssets/models, or a full path to a file anywhere else. " +
                 "Download one with Tools > BehaviorLLM > Model Catalog.")]
        public string modelFileName = "";
        [Tooltip("Where the file was downloaded from, so a teammate can get the same " +
                 "one.")]
        public string sourceUrl = "";
        [Tooltip("The licence the model is released under. Check it before shipping a " +
                 "game with the model included.")]
        public string license = "";
        [Tooltip("The model's size in billions of parameters, for reference. Bigger is " +
                 "smarter and slower.")]
        public float parametersB = 0f;
        [Tooltip("The compression level of the file, for example Q4_K_M, for reference.")]
        public string quantisation = "Q4_K_M";

        [Header("Server")]
        [Tooltip("How much text the server can hold at once, in tokens. It is split " +
                 "between the lanes set by Parallel Slots in the Server Config, so 8192 " +
                 "with 4 lanes gives 2048 each. A typical decision uses 300 to 600 " +
                 "tokens.")]
        public int contextSize = 8192;

        [Tooltip("How much of the model runs on the graphics card. 99 means as much as " +
                 "fits, which is what you want when the model fits in video memory. 0 " +
                 "runs it on the CPU: several times slower, but it works anywhere.")]
        public int gpuLayers = 99;

        [Header("Sampling")]
        [Tooltip("The most the AI may write per decision, in tokens. A plain decision is " +
                 "about 20; a reason adds about one per three characters. Too low cuts " +
                 "the answer off and loses the decision; too high only wastes time if " +
                 "the model rambles.")]
        public int maxTokens = 256;

        [Range(0f, 2f)]
        [Tooltip("How random the AI's choices are. Low (0.1 to 0.3) makes a character " +
                 "consistent and predictable. Raise it to make characters that share a " +
                 "persona behave differently from each other, and expect more odd " +
                 "choices.")]
        public float temperature = 0.2f;

        [Range(0f, 1f)]
        [Tooltip("Advanced sampling setting. 0.95 is a safe default. Lowering it narrows " +
                 "the AI's word choices further.")]
        public float topP = 0.95f;

        [Tooltip("Advanced sampling setting. 40 is a safe default; 0 disables it.")]
        public int topK = 40;

        [Tooltip("Discourages the AI from repeating itself. 1.0 means no penalty and is " +
                 "right for short decisions. Raise it only if the model repeats words.")]
        public float repeatPenalty = 1.0f;

        [Header("Measured behaviour")]
        [Tooltip("Which profile this model was measured to do best with. A decision " +
                 "maker set to the other profile gets a warning when the scene starts; " +
                 "your choice is never changed.")]
        public DecisionProfile recommendedProfile = DecisionProfile.Reactive;
        [Tooltip("Whether this model still produces usable decisions when Thinking " +
                 "Budget Tokens is above 0. Small models usually do not: they repeat the " +
                 "question instead. When this is off, setting a thinking budget gives a " +
                 "warning.")]
        public bool nativeThinkingUsable = false;
        [Tooltip("From the measurement run: the share of answers that were correctly " +
                 "formatted, from 0 to 1.")]
        [Range(0f, 1f)] public float measuredValidActionRate = 0f;
        [Tooltip("From the measurement run: the share of answers that chose the action a " +
                 "person would have, from 0 to 1.")]
        [Range(0f, 1f)] public float measuredExpectedActionRate = 0f;
        [Tooltip("From the measurement run: the typical (median) time from request to " +
                 "answer, in milliseconds.")]
        public float measuredP50LatencyMs = 0f;
        [TextArea(2, 6)]
        [Tooltip("How the numbers above were measured, and anything else worth knowing " +
                 "about this model.")]
        public string measurementNotes = "";

        /// <summary>True when the asset names a model file to load.</summary>
        public bool HasModelFile => !string.IsNullOrWhiteSpace(modelFileName);

        /// <summary>
        /// Path passed to the server: absolute paths are used as-is, bare file names resolve under
        /// <c>StreamingAssets/models</c>.
        /// </summary>
        public string ResolveModelRelativePath()
        {
            if (!HasModelFile) return string.Empty;
            string file = modelFileName.Trim().Replace('\\', '/');
            if (System.IO.Path.IsPathRooted(file) || file.Contains("/")) return file;
            return "models/" + file;
        }
    }
}
