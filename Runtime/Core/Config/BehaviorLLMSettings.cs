using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>How much the package is allowed to write to the console.</summary>
    public enum BehaviorLLMLogLevel
    {
        /// <summary>Nothing at all, not even errors. For a shipped build that handles its own reporting.</summary>
        Off = 0,
        /// <summary>Only things that are broken and need fixing.</summary>
        ErrorsOnly = 1,
        /// <summary>
        /// Errors, the warnings about misconfiguration and fallbacks, and anything a config asset
        /// explicitly asked for such as <c>logPrompts</c>. The default.
        /// </summary>
        Warnings = 2,
        /// <summary>Adds internal detail nobody asked for. Useful while wiring a scene up.</summary>
        Verbose = 3
    }

    /// <summary>
    /// Project-wide switches for the package: how loud it is, whether it records anything, and
    /// which diagnostics it writes.
    ///
    /// The other config assets each tune one *domain* and are meant to exist in several
    /// variants - a Reactive preset and a Deliberative one, a perception profile per character
    /// type. This one is different: there is one per project, and it answers questions that have
    /// nothing to do with how any particular decision maker behaves. "Should a shipped build print
    /// anything?" is not a property of a guard.
    ///
    /// It acts as a **ceiling**, never as an override. A decision maker that has turned its own
    /// prompt logging off stays quiet whatever this says; what this can do is silence one that has
    /// it on, which is what makes a single switch enough to quieten a whole project before a build.
    ///
    /// The ceiling sits above the per-config switches rather than in front of them: ticking
    /// <c>logPrompts</c> works at the default level, because having to raise a second, project-wide
    /// switch before the first one does anything is a trap. Dropping to <c>ErrorsOnly</c> is what
    /// silences it.
    ///
    /// Create it with <c>Assets > Create > BehaviorLLM > Settings</c> and put it in a
    /// <c>Resources</c> folder so it is found at runtime. Without a project override, the package
    /// loads its uniquely named shipped preset, then falls back to the defaults declared here.
    /// </summary>
    [CreateAssetMenu(fileName = "BehaviorLLMSettings", menuName = "BehaviorLLM/Settings", order = 0)]
    public class BehaviorLLMSettings : ScriptableObject
    {
        /// <summary>
        /// Name the asset must have, and the <c>Resources</c> path it is loaded from. Renaming the
        /// asset means the package stops finding it and silently falls back to the defaults.
        /// </summary>
        public const string ResourceName = "BehaviorLLMSettings";

        /// <summary>Unique resource path for the shipped preset; project overrides use ResourceName.</summary>
        public const string DefaultResourcePath = "BehaviorLLM/Defaults/Settings";

        [Header("Logging")]
        [Tooltip("How much the package prints to the console in the Editor and in a development " +
                 "build. Warnings is the default: misconfiguration, fallbacks, and anything a " +
                 "config asset explicitly asked for such as Log Prompts. Verbose adds internal " +
                 "detail nobody asked for. Errors Only silences the rest, including the switches " +
                 "on the config assets, which is how you quieten a whole project in one place. Off " +
                 "silences even errors, which is only sensible if something else reports failures.")]
        public BehaviorLLMLogLevel editorLogLevel = BehaviorLLMLogLevel.Warnings;

        [Tooltip("How much the package prints in a non-development player build. Errors Only by " +
                 "default: a shipped game should not be writing a console line every time a guard " +
                 "decides something, and the string formatting behind those lines is not free.")]
        public BehaviorLLMLogLevel playerLogLevel = BehaviorLLMLogLevel.ErrorsOnly;

        [Header("Telemetry")]
        [Tooltip("Master switch for the Decision Telemetry Recorder. Off, a recorder left in the " +
                 "scene records nothing and writes no files, so shipping a scene that still has one " +
                 "in it costs nothing and needs no scene edit.")]
        public bool telemetryEnabled = true;

        [Tooltip("Let telemetry run in a player build as well as in the Editor. Off by default: " +
                 "the recorder writes CSV and JSONL into the player's persistent data folder, which " +
                 "is rarely what a shipped game wants. Turn it on for a playtest build you intend " +
                 "to collect numbers from.")]
        public bool telemetryInBuilds = false;

        [Header("Diagnostics")]
        [Tooltip("Write the JSON Schema of the last request to StreamingAssets, so it can be " +
                 "replayed against llama-cli outside Unity. Editor only, and worth turning off if " +
                 "the file writes show up while profiling.")]
        public bool dumpAppliedSchema = true;

        [Tooltip("Warn at startup when a decision maker's action bindings and its Action Config " +
                 "disagree - an action with no binding, or a binding for an action that is not in " +
                 "the config. Leave this on: a silent mismatch means an action the model can choose " +
                 "and the game then ignores.")]
        public bool warnOnBindingMismatch = true;

        [Tooltip("Warn at startup when a decision maker is set up against what its model was " +
                 "measured to do, such as deliberation on a model that scored worse with it. Turn " +
                 "off if you are deliberately running a configuration the measurements advise " +
                 "against and do not want reminding.")]
        public bool warnOnModelProfileMismatch = true;

        private static BehaviorLLMSettings loaded;
        private static bool searched;

        /// <summary>
        /// The project's settings, or a throwaway instance carrying the defaults declared above.
        /// Never null, so callers can read a field without a null check.
        ///
        /// Loaded once from <c>Resources</c> and cached. This is a project-wide asset rather than a
        /// scene object on purpose: it has to answer for code that runs before any scene is loaded,
        /// and there is deliberately nothing to wire up in the scene for it.
        /// </summary>
        public static BehaviorLLMSettings Current
        {
            get
            {
                if (loaded != null) return loaded;
                if (!searched)
                {
                    searched = true;
                    loaded = Resources.Load<BehaviorLLMSettings>(ResourceName);
                    if (loaded == null) loaded = Resources.Load<BehaviorLLMSettings>(DefaultResourcePath);
                }
                return loaded != null ? loaded : (loaded = CreateInstance<BehaviorLLMSettings>());
            }
        }

        /// <summary>
        /// Points the package at a specific settings asset, or at nothing to make it search again.
        /// Meant for tests and for a project that keeps several and swaps them per build target.
        /// </summary>
        public static void Use(BehaviorLLMSettings settings)
        {
            loaded = settings;
            searched = settings != null;
        }

        /// <summary>The level in force for wherever this code is running right now.</summary>
        public BehaviorLLMLogLevel ActiveLogLevel =>
            Application.isEditor || Debug.isDebugBuild ? editorLogLevel : playerLogLevel;

        /// <summary>True when telemetry may record here.</summary>
        public bool TelemetryAllowed =>
            telemetryEnabled && (telemetryInBuilds || Application.isEditor);
    }
}
