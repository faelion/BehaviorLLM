using System;
using System.Collections.Generic;
using System.Threading;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Interfaces;
using BehaviorLLM.Core.Modules;
using BehaviorLLM.Core.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// Gives one game object the ability to decide for itself. It runs a single loop, over and
    /// over: look at the scene through whatever observation modules are attached, describe what it
    /// found to a local language model, read back the one action the model chose, and raise the
    /// event the designer wired to that action.
    ///
    /// It owns no game logic of its own. Moving, shooting and everything else lives in the
    /// handlers, which is what lets the same component drive an enemy, a companion or a game
    /// system such as a director adjusting difficulty.
    ///
    /// How it decides is configured through a <see cref="DecisionMakerConfig"/> asset rather than
    /// through fields here, so a project can retune every decision maker at once and keep as many
    /// variations as it likes without a prefab for each.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Decision Maker")]
    public class DecisionMaker : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Who this character or system is, written to the AI as 'You are...'. " +
                 "Two or three specific sentences work best: what it is, where it is, " +
                 "and what it cares about. This text is the main thing that makes two " +
                 "decision makers with the same actions behave differently.")]
        [TextArea(3, 10)] public string systemPersona = "You are a helpful game character.";

        [Header("Configuration")]
        [Tooltip("The list of actions the AI is allowed to choose from. It can never " +
                 "pick anything that is not in this list. Create one with Create > " +
                 "BehaviorLLM > Action Config, add your actions there, then hook each " +
                 "one up to a function under Actions below. Several decision makers can " +
                 "share one asset.")]
        public ActionConfig actionConfig;

        [Tooltip("Tuning settings: how often it decides, whether it thinks before " +
                 "acting, and how much it logs. Create one with Create > BehaviorLLM > " +
                 "Decision Maker Config, or duplicate the Config_Reactive preset from " +
                 "Runtime/Defaults. Leave empty to use sensible defaults.")]
        [SerializeField] private DecisionMakerConfig config;

        [Header("Scene Wiring")]
        [Tooltip("The component that sends requests to the AI server, normally a " +
                 "Behavior LLM Client. Leave empty and it is found automatically: first " +
                 "on this object, then anywhere in the scene. Set it only when the scene " +
                 "has several clients and you need a specific one.")]
        [FormerlySerializedAs("defaultBackend")]
        [SerializeField] private MonoBehaviour defaultBackendComponent;

        [Tooltip("Which of the server's parallel lanes this decision maker uses. The " +
                 "server remembers the unchanged part of each lane's text, so a decision " +
                 "maker with its own lane answers much faster on every decision after " +
                 "the first. -1 lets the server pick. Give each decision maker a " +
                 "different number, from 0 up to Parallel Slots minus one in the Server " +
                 "Config.")]
        [SerializeField] private int requestSlot = -1;

        [Tooltip("Optional. A script on this object that lists the valid values for each " +
                 "action's argument, such as the waypoint names or enemy IDs present in " +
                 "the scene right now. The AI can then only choose from that list. To " +
                 "make one, write a MonoBehaviour that implements " +
                 "IArgumentOptionsProvider and drag it here. Leave empty to use the " +
                 "Allowed Arguments typed into the Action Config, if any.")]
        [SerializeField] private MonoBehaviour argumentOptionsProviderComponent;

        [Tooltip("Optional. A script on this object that says which actions are allowed " +
                 "on each decision, for example no Chase while health is low. An action " +
                 "it turns off cannot be chosen at all, which is far more reliable than " +
                 "asking the AI to remember a rule in text. To make one, write a " +
                 "MonoBehaviour that implements IActionAvailabilityProvider and drag it " +
                 "here.")]
        [SerializeField] private MonoBehaviour actionAvailabilityProviderComponent;

        [Tooltip("Optional. A script that checks or corrects the argument after the AI " +
                 "has chosen it, for example turning 'the gate' into 'Gate_01' or " +
                 "rejecting an unknown name. To make one, write a MonoBehaviour that " +
                 "implements IActionArgumentPolicy and drag it here. Prefer Argument " +
                 "Options Provider when you already know the valid values, because that " +
                 "stops bad values before they are written.")]
        [SerializeField] private MonoBehaviour actionArgumentPolicyComponent;

        [Header("Actions")]
        // Action source-of-truth split (kept intentionally):
        //   1. ActionConfig.validActions = the SCHEMA: which actions exist, their parameter
        //      types, descriptions, example and allowed arguments. Drives the prompt and the
        //      JSON Schema constraint.
        //   2. actionBindings (this field)  = the WIRING: schema action name -> UnityEvent
        //      handler in the scene. Per decision maker; cannot be derived from the schema.
        //   3. bindingsByName               = the CACHE: O(1) dispatch lookup built at Awake.
        // Divergence between (1) and (2) is reported as a warning at Awake.
        [Tooltip("What actually happens in the game for each action. Add one entry per " +
                 "action in the Action Config, type the same name, and hook up the " +
                 "function to call, the same way you would for a UI button. The function " +
                 "receives the argument the AI chose as a string, or an empty string for " +
                 "actions that take none.")]
        public List<ActionBinding> actionBindings;

        [SerializeField] private FallbackActionConfig fallbackAction = new FallbackActionConfig();

        [Serializable]
        public struct ActionBinding
        {
            [Tooltip("The action this entry handles. Must be spelled exactly as in the " +
                     "Action Config. A name that does not match is reported in the " +
                     "console when the scene starts.")]
            public string actionName;

            [Tooltip("The function to call when the AI picks this action, like a UI " +
                     "button's On Click. It receives one string: the argument the AI " +
                     "chose, or an empty string for actions that take none.")]
            public ActionEvent onExecute;
        }

        [Serializable]
        public class FallbackActionConfig
        {
            [Tooltip("What to do when the AI's answer cannot be used, for example it " +
                     "named an action that does not exist or left out a required " +
                     "argument. Type the name of a safe action from the Action Config, " +
                     "such as HoldPosition. Leave empty to do nothing on a bad answer. " +
                     "Recommended for anything you ship.")]
            public string fallbackActionName;

            [Tooltip("The argument to pass to the fallback action, if that action needs " +
                     "one. Otherwise leave empty.")]
            public string fallbackArgument;
        }

        /// <summary>The configuration in effect, including the fallback used when none is assigned.</summary>
        public DecisionMakerConfig Config => Cfg;

        /// <summary>Raised after every decision attempt, including backend failures.</summary>
        public event Action<DecisionTelemetry> DecisionTelemetryRecorded;

        #if UNITY_EDITOR
        /// <summary>
        /// Editor only. Raised after every decision's telemetry, for the Prompt Inspector window.
        ///
        /// It is separate from <see cref="DecisionTelemetryRecorded"/> on purpose. Telemetry is
        /// written to disk for every run and is deliberately all numbers and short strings; putting
        /// whole prompts and response bodies in it would multiply the size of a JSONL file that
        /// exists to be loaded into a spreadsheet. The text belongs to a debugging window that is
        /// open for a minute, not to a run report, so it travels on its own channel and only while
        /// something is listening.
        /// </summary>
        public static event Action<DecisionMaker, DecisionTelemetry> DecisionInspected;

        /// <summary>
        /// Editor only. While false, the exchange text is not kept, so the cost of the inspector is
        /// paid only when its window is open. The Prompt Inspector sets it.
        /// </summary>
        public static bool CaptureExchanges;

        /// <summary>Editor only. The system prompt as it was sent for the last decision.</summary>
        public string LastSystemPrompt { get; private set; }

        /// <summary>Editor only. The per-decision STATE block as it was sent for the last decision.</summary>
        public string LastStatePrompt { get; private set; }

        /// <summary>Editor only. The JSON Schema applied to the last decision, or empty when unconstrained.</summary>
        public string LastSchema { get; private set; }

        /// <summary>Editor only. Exactly what the model replied for the last decision, before parsing.</summary>
        public string LastResponseText { get; private set; }

        /// <summary>Editor only. The model's separate reasoning channel for the last decision, if any.</summary>
        public string LastReasoningText { get; private set; }
        #endif

        /// <summary>The static prompt prefix currently in use (built at Awake / RebuildSchema).</summary>
        public string SystemPrompt => cachedSystemPrompt;

        /// <summary>The JSON Schema currently sent with each request, or null when structured output is off.</summary>
        public string JsonSchema => cachedSchema;

        // Never null: an unassigned reference falls back to a throwaway instance carrying the
        // documented defaults, so a component someone dropped into a scene without reading
        // anything still runs. Resolved lazily as well as at Awake, because editor tooling and
        // tests reach into a component that has never woken up.
        private DecisionMakerConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(config));
        private DecisionMakerConfig cfg;

        private ILLMBackend backend;
        private List<IObservationModule> observations;
        private BasicMemory memoryModule;
        private IActionArgumentPolicy actionArgumentPolicy;
        private IArgumentOptionsProvider argumentOptionsProvider;
        private IActionAvailabilityProvider actionAvailabilityProvider;
        private Dictionary<string, ActionBinding> bindingsByName;
        private string cachedSystemPrompt = string.Empty;

        // Actions whose argument list was left out of the menu because it comes from a provider and
        // therefore changes. Their current values go in the state block instead; see
        // DecisionMakerConfig.dynamicArgumentsInState.
        private readonly List<string> deferredArgumentActions = new List<string>();
        private readonly Dictionary<string, IList<string>> deferredArgumentValues =
            new Dictionary<string, IList<string>>();
        private string cachedSchema;

        // Resolve the schema each turn: argument values can change without the action names
        // changing. The schema is outside the prompt prefix cached by the backend.
        private readonly List<string> availableActions = new List<string>();

        private float timer;
        private bool isThinking;
        private int decisionCounter;
        private float lastBackendWarningTime = -999f;
        private const float BackendWarningCooldown = 5f;
        private const int ReactiveTokenBudget = 48;

        // Linked to component lifetime: cancelled when the component is disabled or destroyed so any
        // in-flight backend call aborts cleanly instead of resuming on a dead GameObject.
        private CancellationTokenSource thinkCts;
        private CancellationTokenSource requestCts;

        // ------------------------------------------------------------------ lifecycle

        // Runs when the component is first added in the Editor: point the new component at the
        // presets the package ships so it works before the user has authored a single asset.
        private void Reset()
        {
            if (config == null) config = BehaviorLLMDefaults.FindShipped<DecisionMakerConfig>(BehaviorLLMDefaults.DecisionMakerConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
            observations = new List<IObservationModule>(GetComponentsInChildren<IObservationModule>());
            memoryModule = GetComponentInChildren<BasicMemory>();
            ResolveOptionalComponents();
            BuildBindingLookup();
            ResolveBackend();
            RebuildSchema();
            ValidatePromptBudget();
        }

        /// <summary>
        /// A prompt budget smaller than the cached system prompt leaves nothing for the
        /// observations, so the character is asked what to do and told almost nothing about the
        /// situation. It still answers with a structurally valid action and nothing in the
        /// telemetry marks the decision as wrong, which is what makes the mistake expensive to
        /// find. Startup is the last point at which it is cheap, so the configuration is refused
        /// here rather than quietly honoured for the rest of the run.
        /// </summary>
        private void ValidatePromptBudget()
        {
            if (Cfg.maxPromptChars <= 0) return;

            int room = Cfg.maxPromptChars - cachedSystemPrompt.Length;
            int floor = Mathf.Max(0, Cfg.minStatePromptChars);
            if (room >= floor) return;

            int systemLength = cachedSystemPrompt.Length;
            int budget = Cfg.maxPromptChars;
            BehaviorLLMLog.Error(() =>
                $"[DecisionMaker] '{name}' disabled: Max Prompt Chars is {budget}, but this " +
                $"character's system prompt alone is {systemLength} characters, leaving {room} " +
                $"for observations against a minimum of {floor}. It would decide without being " +
                $"told what it can see. Raise Max Prompt Chars above {systemLength + floor}, " +
                $"lower Min State Prompt Chars, or set Max Prompt Chars to 0 for no limit.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            thinkCts = new CancellationTokenSource();
            // Characters enabled on the same frame would otherwise count the same interval from
            // the same instant and stay in step forever, turning a steady load into bursts of
            // contention. Starting each one part-way through its first interval breaks that up.
            timer = Cfg.decisionIntervalJitter > 0f
                ? UnityEngine.Random.Range(0f, Cfg.decisionInterval * Cfg.decisionIntervalJitter)
                : 0f;
        }

        /// <summary>
        /// Resets the decision timer, spread around zero by the configured jitter so the next
        /// decision lands slightly early or slightly late. Centred on zero on purpose: the mean
        /// interval is exactly what the config asks for, so jitter costs no decisions per minute.
        /// </summary>
        private void ResetTimer()
        {
            timer = Cfg.decisionIntervalJitter > 0f
                ? UnityEngine.Random.Range(-1f, 1f) * Cfg.decisionInterval * Cfg.decisionIntervalJitter
                : 0f;
        }

        private void OnDisable()
        {
            CancelAndDisposeThinkCts();
            requestCts = null;
            // A Think() awaiting the backend would otherwise leave isThinking stuck on re-enable.
            isThinking = false;
            timer = 0f;
        }

        private void OnDestroy()
        {
            CancelAndDisposeThinkCts();
        }

        private void Update()
        {
            bool interrupt = false;
            for (int i = 0; i < observations.Count; i++)
            {
                if (observations[i].HasInterrupt())
                {
                    interrupt = true;
                    if (Cfg.logDecisions) BehaviorLLMLog.Requested(() => $"[DecisionMaker] Interrupt raised by {observations[i].TopicName}.");
                    break;
                }
            }

            if (isThinking)
            {
                if (interrupt)
                {
                    // Wait for cancellation to finish before reusing this object's server slot.
                    timer = Cfg.decisionInterval;
                    requestCts?.Cancel();
                }
                return;
            }

            timer += Time.deltaTime;
            if (timer >= Cfg.decisionInterval || interrupt)
            {
                ResetTimer();
                Think();
            }
        }

        // ------------------------------------------------------------------ setup

        private void ResolveOptionalComponents()
        {
            actionArgumentPolicy = actionArgumentPolicyComponent as IActionArgumentPolicy;
            if (actionArgumentPolicyComponent != null && actionArgumentPolicy == null)
                BehaviorLLMLog.Error(() => "[DecisionMaker] actionArgumentPolicyComponent does not implement IActionArgumentPolicy.");

            argumentOptionsProvider = argumentOptionsProviderComponent as IArgumentOptionsProvider;
            if (argumentOptionsProviderComponent != null && argumentOptionsProvider == null)
                BehaviorLLMLog.Error(() => "[DecisionMaker] argumentOptionsProviderComponent does not implement IArgumentOptionsProvider.");

            actionAvailabilityProvider = actionAvailabilityProviderComponent as IActionAvailabilityProvider;
            if (actionAvailabilityProviderComponent != null && actionAvailabilityProvider == null)
                BehaviorLLMLog.Error(() => "[DecisionMaker] actionAvailabilityProviderComponent does not implement IActionAvailabilityProvider.");
        }

        private void ResolveBackend()
        {
            backend = GetComponent<ILLMBackend>();
            if (backend == null && defaultBackendComponent != null)
            {
                backend = defaultBackendComponent as ILLMBackend;
                if (backend == null) BehaviorLLMLog.Error(() => "[DecisionMaker] Default Backend Component does not implement ILLMBackend.");
            }
            if (backend == null)
            {
                MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is ILLMBackend found) { backend = found; break; }
                }
            }
            if (backend == null) BehaviorLLMLog.Error(() => "[DecisionMaker] No Backend (ILLMBackend) found!");
            WarnOnModelConfigMismatch();
        }

        // Advisory only: a model config records what that model was measured to do well, and this
        // says so when a config is set up against it. It never overrides the author's choice,
        // because a project may have measured something different on its own scenarios.
        private void WarnOnModelConfigMismatch()
        {
            if (!BehaviorLLMSettings.Current.warnOnModelProfileMismatch) return;

            BehaviorLLMClient client = backend as BehaviorLLMClient;
            BehaviorLLMModelConfig model = client != null ? client.ModelConfig : null;
            if (model == null) return;

            // A model that always reasons and cannot be told not to needs the Deliberative
            // profile and a thinking budget, or it spends the Reactive budget thinking and
            // never answers; the client then falls back to raw completion, where such a model
            // gives the cheapest legal action every time. Seen on 2026-09-14 with LFM2.5-2.6B:
            // twelve decisions, all HoldPosition, all "valid".
            if (model.reasoningModel && (Cfg.profile != DecisionProfile.Deliberative || Cfg.thinkingBudgetTokens <= 0))
            {
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': '{model.displayName}' is a reasoning model whose " +
                                 "chat template has no thinking switch, but this decision maker runs " +
                                 $"{Cfg.profile} with a thinking budget of {Cfg.thinkingBudgetTokens}. It will spend " +
                                 "the token budget reasoning and return no answer, and the client's raw-completion " +
                                 "fallback then produces the cheapest action every time. Set Profile to Deliberative " +
                                 "and Thinking Budget Tokens to a few hundred on the decision maker config, or pick a " +
                                 "model that does not reason first (all three shipped presets).");
            }

            if (Cfg.UsesNativeThinking && !model.nativeThinkingUsable && !model.reasoningModel)
            {
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': native thinking is enabled (budget {Cfg.thinkingBudgetTokens}) " +
                                 $"but '{model.displayName}' is marked as not producing usable decisions while thinking. " +
                                 "Expect the model to restate the prompt instead of deciding. Use the reason field instead, " +
                                 "or clear the flag on the model config if your own measurements disagree.");
            }

            if (Cfg.profile != model.recommendedProfile)
            {
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': profile is {Cfg.profile} but '{model.displayName}' was " +
                                 $"measured to do best with {model.recommendedProfile}" +
                                 (model.measuredExpectedActionRate > 0f ? $" ({model.measuredExpectedActionRate:P0} expected-action rate)" : "") +
                                 ". This is advisory; measure your own scenarios before trusting either.");
            }
        }

        /// <summary>
        /// Swaps the configuration at runtime and rebuilds everything that depends on it. Use this
        /// to give one decision maker a different cadence or profile from the others sharing its
        /// asset: clone the asset with <c>Instantiate</c>, change what you need and pass it here.
        /// Passing null restores the documented defaults.
        /// </summary>
        public void ApplyConfig(DecisionMakerConfig newConfig)
        {
            config = newConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newConfig);
            RebuildSchema();
            // Swapping in a config whose budget cannot carry the system prompt is the same
            // silent failure as starting with one, so it is refused on the same terms.
            ValidatePromptBudget();
        }

        /// <summary>
        /// Rebuilds the system prompt and the JSON Schema after editing the ActionConfig or
        /// profile. Dynamic argument providers are refreshed automatically each decision.
        /// </summary>
        public void RebuildSchema()
        {
            int reasonChars = Cfg.EffectiveReasonChars;
            Func<ActionDefinition, ActionParameter, IList<string>> optionsFor = argumentOptionsProvider != null ? ResolveArgumentOptions : (Func<ActionDefinition, ActionParameter, IList<string>>)null;

            // The full action menu stays in the system prompt even when a provider gates it, so
            // the prefix the server caches never changes. Which of those actions may be chosen
            // is announced per decision and enforced by the per-request schema.
            deferredArgumentActions.Clear();
            cachedSystemPrompt = PromptBuilder.BuildSystemPrompt(actionConfig, new PromptBuilder.SystemPromptOptions
            {
                Persona = systemPersona,
                IncludeExamples = Cfg.includeExamplesInPrompt,
                ReasonMaxChars = reasonChars,
                ArgumentOptionsFor = optionsFor,
                DynamicMenu = actionAvailabilityProvider != null,
                InlineProviderOptions = !Cfg.dynamicArgumentsInState,
                DeferredArgumentActions = deferredArgumentActions
            });

            string previousSchema = cachedSchema;
            cachedSchema = null;
            if (Cfg.useStructuredOutput && actionConfig != null)
            {
                string schema = ActionSchemaBuilder.Build(actionConfig, new ActionSchemaBuilder.Options
                {
                    ReasonMaxChars = reasonChars,
                    ArgumentOptionsFor = optionsFor
                });
                if (!string.IsNullOrEmpty(schema))
                {
                    cachedSchema = schema;
                    #if UNITY_EDITOR
                    if (schema != previousSchema) DumpToStreamingAssets("_last_applied_schema.json", schema);
                    #endif
                }
            }
        }

        /// <summary>
        /// Fills <see cref="availableActions"/> with the actions the provider allows this turn.
        /// Returns false when a provider is present and allows nothing, which is a scene bug
        /// worth surfacing rather than sending the model an empty menu.
        /// </summary>
        private bool ResolveAvailableActions()
        {
            availableActions.Clear();
            if (actionAvailabilityProvider == null || actionConfig == null || actionConfig.validActions == null) return true;

            for (int i = 0; i < actionConfig.validActions.Count; i++)
            {
                ActionDefinition def = actionConfig.validActions[i];
                if (def == null || string.IsNullOrWhiteSpace(def.actionName)) continue;
                if (actionAvailabilityProvider.IsActionAvailable(def.actionName)) availableActions.Add(def.actionName);
            }
            return availableActions.Count > 0;
        }

        /// <summary>Schema using this turn's availability and argument values.</summary>
        private string ResolveSchemaForAvailability()
        {
            if (!Cfg.useStructuredOutput || actionConfig == null) return null;

            int reasonChars = Cfg.EffectiveReasonChars;
            string schema = ActionSchemaBuilder.Build(actionConfig, new ActionSchemaBuilder.Options
            {
                ReasonMaxChars = reasonChars,
                ArgumentOptionsFor = argumentOptionsProvider != null ? ResolveArgumentOptions : (Func<ActionDefinition, ActionParameter, IList<string>>)null,
                IsActionAvailable = def => actionAvailabilityProvider == null || IsAvailableNow(def.actionName)
            });
            if (string.IsNullOrEmpty(schema)) schema = null;
            return schema;
        }

        /// <summary>
        /// Current values for the actions whose argument list was left out of the menu, or null
        /// when there are none. Resolved fresh each decision: that is the whole point of moving
        /// them out of the cached prefix.
        /// </summary>
        private IDictionary<string, IList<string>> CurrentDeferredArguments()
        {
            if (deferredArgumentActions.Count == 0 || argumentOptionsProvider == null) return null;

            deferredArgumentValues.Clear();
            for (int i = 0; i < deferredArgumentActions.Count; i++)
            {
                string name = deferredArgumentActions[i];
                if (actionConfig == null || !actionConfig.TryGetAction(name, out ActionDefinition def)) continue;

                // Only parameters whose values come from the provider are deferred; an authored
                // list is stable and stays in the cached action menu where it costs nothing.
                List<ActionParameter> parameters = def.Parameters;
                for (int p = 0; p < parameters.Count; p++)
                {
                    ActionParameter parameter = parameters[p];
                    if (parameter == null) continue;
                    if (parameter.allowedValues != null && parameter.allowedValues.Count > 0) continue;

                    IList<string> options = ResolveArgumentOptions(def, parameter);
                    if (options == null || options.Count == 0) continue;

                    // One entry per action for a single parameter, so the state block reads as it
                    // always has; qualified by parameter name only when an action has several.
                    string key = parameters.Count == 1 ? name : name + "." + parameter.name;
                    deferredArgumentValues[key] = options;
                }
            }
            return deferredArgumentValues.Count > 0 ? deferredArgumentValues : null;
        }

        private IList<string> ResolveArgumentOptions(ActionDefinition def, ActionParameter parameter)
        {
            if (argumentOptionsProvider == null || def == null) return null;
            List<string> options = new List<string>();
            return argumentOptionsProvider.TryGetArgumentOptions(def.actionName, parameter != null ? parameter.name : "arg", options) ? options : null;
        }

        /// <summary>
        /// Call after mutating <see cref="actionBindings"/> at runtime (editor tools, test
        /// fixtures) so the dispatch dictionary picks up the change.
        /// </summary>
        public void RebuildBindingLookup()
        {
            BuildBindingLookup();
        }

        private void BuildBindingLookup()
        {
            bindingsByName = new Dictionary<string, ActionBinding>(StringComparer.OrdinalIgnoreCase);
            if (actionBindings != null)
            {
                for (int i = 0; i < actionBindings.Count; i++)
                {
                    ActionBinding binding = actionBindings[i];
                    if (string.IsNullOrWhiteSpace(binding.actionName)) continue;
                    if (bindingsByName.ContainsKey(binding.actionName))
                    {
                        BehaviorLLMLog.Warn(() => $"[DecisionMaker] Duplicate action binding '{binding.actionName}' on '{gameObject.name}'. First binding wins.");
                        continue;
                    }
                    bindingsByName.Add(binding.actionName, binding);
                }
            }
            WarnOnBindingSchemaMismatch();
        }

        // Pure diagnostic: catches a binding the schema never advertises (it can only fire via
        // fallback) and a schema action with no binding (dispatch falls through every time).
        private void WarnOnBindingSchemaMismatch()
        {
            if (!BehaviorLLMSettings.Current.warnOnBindingMismatch) return;

            if (actionConfig == null || actionConfig.validActions == null) return;

            HashSet<string> schemaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actionConfig.validActions.Count; i++)
            {
                ActionDefinition def = actionConfig.validActions[i];
                if (def != null && !string.IsNullOrWhiteSpace(def.actionName)) schemaNames.Add(def.actionName);
            }

            foreach (string bound in bindingsByName.Keys)
            {
                if (!schemaNames.Contains(bound))
                    BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': action binding '{bound}' has no matching entry in ActionConfig.validActions. The model will never be asked to emit it.");
            }
            foreach (string schemaName in schemaNames)
            {
                if (!bindingsByName.ContainsKey(schemaName))
                    BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': ActionConfig action '{schemaName}' has no binding. The model can emit it but dispatch will fall through.");
            }
        }

        // ------------------------------------------------------------------ think

        private async void Think()
        {
            if (backend == null || actionConfig == null) return;
            if (thinkCts == null) return; // disabled between Update() and Think()
            isThinking = true;
            CancellationTokenSource currentRequest = CancellationTokenSource.CreateLinkedTokenSource(thinkCts.Token);
            requestCts = currentRequest;
            CancellationToken token = currentRequest.Token;

            try
            {
                if (!ResolveAvailableActions())
                {
                    BehaviorLLMLog.Warn(() => $"[DecisionMaker] '{gameObject.name}': the action availability provider allows no action this turn; skipping the decision.");
                    return;
                }

                // Keep dynamic argument descriptions consistent with the fresh schema. The
                // prefix changes only when the provider's values change.
                if (argumentOptionsProvider != null) RebuildSchema();
                string schema = ResolveSchemaForAvailability();
                if (Cfg.useStructuredOutput && string.IsNullOrEmpty(schema))
                {
                    EmitBackendUnavailableTelemetry(0, LLMResponse.Failed("No usable action schema for this decision."));
                    return;
                }
                string observationText = ObservationComposer.Compose(observations, Cfg.maxVisionEntries, Cfg.maxMemoryEntries);
                string statePrompt = PromptBuilder.BuildStatePrompt(
                    observationText,
                    actionAvailabilityProvider != null ? availableActions : null,
                    CurrentDeferredArguments());
                if (Cfg.maxPromptChars > 0)
                {
                    // Validated at Awake, so by here the budget is known to leave usable room.
                    int stateBudget = Cfg.maxPromptChars - cachedSystemPrompt.Length;
                    statePrompt = PromptBuilder.TrimStatePrompt(statePrompt, stateBudget);
                }

                LLMRequest request = BuildRequest(statePrompt, schema);
                int promptLength = cachedSystemPrompt.Length + statePrompt.Length;

                #if UNITY_EDITOR
                if (CaptureExchanges)
                {
                    LastSystemPrompt = cachedSystemPrompt;
                    LastStatePrompt = statePrompt;
                    LastSchema = schema;
                    LastResponseText = null;
                    LastReasoningText = null;
                }
                #endif

                #if UNITY_EDITOR
                if (Cfg.logPrompts) BehaviorLLMLog.Requested(() => $"[DecisionMaker] Prompt ({promptLength} chars):\n{cachedSystemPrompt}\n{statePrompt}");
                #endif

                LLMResponse response = await backend.CompleteAsync(request, token);
                token.ThrowIfCancellationRequested();

                if (response == null || !response.Succeeded || string.IsNullOrWhiteSpace(response.Text))
                {
                    LogBackendUnavailable(response);
                    EmitBackendUnavailableTelemetry(promptLength, response);
                    return;
                }

                if (Cfg.logDecisions) BehaviorLLMLog.Requested(() => $"[DecisionMaker] Answer: {response.Text}");

                #if UNITY_EDITOR
                if (CaptureExchanges)
                {
                    LastResponseText = response.Text;
                    LastReasoningText = response.ReasoningText;
                }
                #endif

                HandleResponse(response, promptLength);
            }
            catch (OperationCanceledException)
            {
                // Expected when the component is disabled mid-inference.
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Error(() => $"[DecisionMaker] Think loop failed: {e.Message}");
            }
            finally
            {
                if (requestCts == currentRequest)
                {
                    requestCts = null;
                    isThinking = false;
                }
                currentRequest.Dispose();
            }
        }

        private LLMRequest BuildRequest(string statePrompt, string schema)
        {
            bool nativeThinking = Cfg.UsesNativeThinking;
            int reasonChars = Cfg.EffectiveReasonChars;

            int tokens = Cfg.maxTokens;
            if (tokens <= 0)
            {
                // A bare {"action":..,"arg":..} is ~20 tokens; reasons run ~1 token per 3 chars.
                tokens = ReactiveTokenBudget + reasonChars / 3 + (nativeThinking ? Cfg.thinkingBudgetTokens : 0);
            }

            return new LLMRequest
            {
                SystemPrompt = cachedSystemPrompt,
                UserPrompt = statePrompt,
                CompletionLead = PromptBuilder.CompletionLead,
                JsonSchema = schema,
                SchemaName = "decision",
                MaxTokens = tokens,
                SlotId = requestSlot,
                EnableThinking = nativeThinking,
                ThinkingBudgetTokens = nativeThinking ? Cfg.thinkingBudgetTokens : 0
            };
        }

        // ------------------------------------------------------------------ act

        // Kept as a single-string entry point so tests can feed synthetic model output.
        private void ExecuteAction(string llmResponse)
        {
            HandleResponse(new LLMResponse { Text = llmResponse }, -1);
        }

        private void HandleResponse(LLMResponse response, int promptLengthChars)
        {
            DecisionTelemetry telemetry = CreateTelemetryRecord(promptLengthChars, response);

            DecisionParser.Result parsed = DecisionParser.Parse(response.Text);
            if (!parsed.Succeeded)
            {
                telemetry.parseFailureReason = parsed.Error;
                FailOrFallback($"parse failed ({parsed.Error})", telemetry, $"[DecisionMaker] Could not parse decision: {response.Text}");
                EmitTelemetry(telemetry);
                return;
            }

            telemetry.reason = parsed.Reason;
            string action = parsed.Action;
            ActionArguments arguments = parsed.Arguments ?? ActionArguments.Empty;

            if (!IsActionValid(action))
            {
                telemetry.parseFailureReason = $"Unbound action '{action}'.";
                FailOrFallback($"unbound action '{action}'", telemetry, $"[DecisionMaker] Model chose an unbound action: {action}");
                EmitTelemetry(telemetry);
                return;
            }

            actionConfig.TryGetAction(action, out ActionDefinition definition);
            action = definition.actionName;

            // The world can change while inference runs, even with a valid request schema.
            if (actionAvailabilityProvider != null && !actionAvailabilityProvider.IsActionAvailable(action))
            {
                telemetry.parseFailureReason = $"Action '{action}' is not available this turn.";
                FailOrFallback($"unavailable action '{action}'", telemetry, $"[DecisionMaker] Model chose an action that is unavailable this turn: {action}");
                EmitTelemetry(telemetry);
                return;
            }

            // Every parameter the action declares is validated in turn: present, accepted by the
            // optional policy, and still one of the values currently allowed. A decision that is
            // well formed but names a value the world no longer offers is the failure mode the
            // samples hit most, so it is checked here rather than trusted from the schema.
            List<string> names = new List<string>();
            List<string> resolved = new List<string>();
            List<ActionParameter> parameters = definition.Parameters;
            bool policyNormalized = false;

            for (int p = 0; p < parameters.Count; p++)
            {
                ActionParameter parameter = parameters[p];
                if (parameter == null) continue;

                string value = arguments[parameter.name];
                if (string.IsNullOrWhiteSpace(value) && parameters.Count == 1) value = arguments.First;

                if (string.IsNullOrWhiteSpace(value))
                {
                    telemetry.parseFailureReason = $"Missing '{parameter.name}' for action '{action}'.";
                    FailOrFallback($"missing '{parameter.name}' for '{action}'", telemetry,
                        $"[DecisionMaker] Empty '{parameter.name}' for action: {action}");
                    EmitTelemetry(telemetry);
                    return;
                }

                if (actionArgumentPolicy != null)
                {
                    string normalized;
                    string reason;
                    if (!actionArgumentPolicy.TryNormalizeArgument(action, parameter.name, value, out normalized, out reason))
                    {
                        telemetry.argumentPolicyDecision = ArgumentPolicyDecision.Rejected;
                        telemetry.argumentPolicyReason = reason;
                        FailOrFallback($"argument policy rejected '{action}({value})': {reason}", telemetry,
                            $"[DecisionMaker] Argument policy rejected '{action}({value})': {reason}");
                        EmitTelemetry(telemetry);
                        return;
                    }
                    string prior = value;
                    value = DecisionParser.NormalizeArgument(normalized);
                    if (!string.Equals(prior, value, StringComparison.Ordinal)) policyNormalized = true;
                }

                if (!IsParameterValueValid(definition, parameter, value))
                {
                    telemetry.parseFailureReason = $"Value '{value}' is not currently allowed for '{action}.{parameter.name}'.";
                    FailOrFallback(telemetry.parseFailureReason, telemetry, $"[DecisionMaker] {telemetry.parseFailureReason}");
                    EmitTelemetry(telemetry);
                    return;
                }

                names.Add(parameter.name);
                resolved.Add(value);
            }

            if (actionArgumentPolicy != null && telemetry.argumentPolicyDecision != ArgumentPolicyDecision.Rejected)
            {
                telemetry.argumentPolicyDecision = policyNormalized
                    ? ArgumentPolicyDecision.Normalized
                    : ArgumentPolicyDecision.Unchanged;
            }

            ActionArguments finalArguments = new ActionArguments(action, names, resolved);
            telemetry.resultType = DecisionResultType.ModelAction;
            telemetry.actionName = action;
            telemetry.argument = finalArguments.ToCompactString();
            DispatchAction(action, finalArguments);
            EmitTelemetry(telemetry);
        }

        private void FailOrFallback(string reason, DecisionTelemetry telemetry, string warningIfNoFallback)
        {
            if (TryExecuteFallback(reason, telemetry)) return;
            telemetry.resultType = DecisionResultType.NoAction;
            BehaviorLLMLog.Warn(() => warningIfNoFallback);
        }

        private bool TryExecuteFallback(string reason, DecisionTelemetry telemetry)
        {
            if (fallbackAction == null || string.IsNullOrWhiteSpace(fallbackAction.fallbackActionName)) return false;

            string actionName = fallbackAction.fallbackActionName != null ? fallbackAction.fallbackActionName.Trim() : string.Empty;
            string argument = DecisionParser.NormalizeArgument(fallbackAction.fallbackArgument);

            if (!IsActionValid(actionName))
            {
                telemetry.parseFailureReason = $"Fallback failed: unbound fallback action '{actionName}'. Source: {reason}";
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] Fallback action '{actionName}' is not bound. Reason: {reason}");
                return false;
            }

            actionConfig.TryGetAction(actionName, out ActionDefinition definition);
            actionName = definition.actionName;

            // The configured fallback carries at most one value, so it can only stand in for an
            // action of one parameter or none. An action needing more is rejected here rather
            // than dispatched half-filled.
            List<ActionParameter> fallbackParameters = definition.Parameters;
            ActionArguments fallbackArguments;
            if (fallbackParameters.Count == 0)
            {
                fallbackArguments = ActionArguments.Empty;
            }
            else if (fallbackParameters.Count == 1 && fallbackParameters[0] != null)
            {
                fallbackArguments = new ActionArguments(actionName,
                    new List<string> { fallbackParameters[0].name },
                    new List<string> { argument ?? string.Empty });
            }
            else
            {
                telemetry.fallbackReason = $"Fallback '{actionName}' takes {fallbackParameters.Count} values, " +
                                           $"which a fallback cannot supply. Source: {reason}";
                return false;
            }

            string renderedFallback = fallbackArguments.ToCompactString();
            if ((actionAvailabilityProvider != null && !actionAvailabilityProvider.IsActionAvailable(actionName)) ||
                !AreArgumentsValid(actionName, fallbackArguments))
            {
                telemetry.fallbackReason = $"Fallback '{actionName}({renderedFallback})' is not currently allowed. Source: {reason}";
                return false;
            }

            if (Cfg.logFallbackUsage)
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] Executing fallback '{actionName}({renderedFallback})' because {reason}.");

            telemetry.resultType = DecisionResultType.FallbackAction;
            telemetry.usedFallback = true;
            telemetry.fallbackReason = reason;
            telemetry.actionName = actionName;
            telemetry.argument = renderedFallback;
            DispatchAction(actionName, fallbackArguments);
            return true;
        }

        private bool IsActionValid(string actionName)
        {
            return bindingsByName != null && !string.IsNullOrEmpty(actionName) && bindingsByName.ContainsKey(actionName)
                && actionConfig != null && actionConfig.TryGetAction(actionName, out _);
        }

        /// <summary>
        /// True when a value is one the action currently accepts for that parameter. Re-checked
        /// at dispatch rather than trusted from the schema, because the schema was built before
        /// the request and the world can move while inference runs.
        /// </summary>
        private bool IsParameterValueValid(ActionDefinition def, ActionParameter parameter, string value)
        {
            if (def == null || parameter == null) return false;
            bool numeric = parameter.type == ActionParameterType.Int;
            if (numeric ? !int.TryParse(value, out _) : !ActionSchemaBuilder.IsIdentifier(value)) return false;

            List<string> options = ActionSchemaBuilder.CollectOptions(def, parameter, new ActionSchemaBuilder.Options
            {
                ArgumentOptionsFor = argumentOptionsProvider != null ? ResolveArgumentOptions : (Func<ActionDefinition, ActionParameter, IList<string>>)null
            });
            return options.Count == 0 || options.Contains(value);
        }

        /// <summary>Whole-decision check used by the fallback path, which builds its own values.</summary>
        private bool AreArgumentsValid(string actionName, ActionArguments arguments)
        {
            if (actionConfig == null || !actionConfig.TryGetAction(actionName, out ActionDefinition def)) return false;

            List<ActionParameter> parameters = def.Parameters;
            if (parameters.Count == 0) return arguments == null || arguments.Count == 0;

            for (int i = 0; i < parameters.Count; i++)
            {
                ActionParameter parameter = parameters[i];
                if (parameter == null) continue;
                string value = arguments != null ? arguments[parameter.name] : string.Empty;
                if (string.IsNullOrEmpty(value) && arguments != null && parameters.Count == 1) value = arguments.First;
                if (!IsParameterValueValid(def, parameter, value)) return false;
            }
            return true;
        }

        // Case-insensitive to match binding lookup: a model that answers "chase" instead of
        // "Chase" should fail on availability for the same reason it would on binding, or not
        // at all, but never inconsistently between the two.
        private bool IsAvailableNow(string actionName)
        {
            for (int i = 0; i < availableActions.Count; i++)
            {
                if (string.Equals(availableActions[i], actionName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void DispatchAction(string actionName, ActionArguments arguments)
        {
            string rendered = arguments != null ? arguments.ToCompactString() : string.Empty;
            if (memoryModule != null) memoryModule.RecordEvent($"Executed Action: {actionName}({rendered})");

            ActionBinding binding;
            if (bindingsByName != null && bindingsByName.TryGetValue(actionName, out binding))
            {
                if (Cfg.logDecisions) BehaviorLLMLog.Requested(() => $"[DecisionMaker] Executing {actionName}('{rendered}').");
                binding.onExecute.Invoke(arguments ?? ActionArguments.Empty);
                return;
            }
            BehaviorLLMLog.Warn(() => $"[DecisionMaker] No binding found for action: {actionName}");
        }

        // ------------------------------------------------------------------ telemetry

        private DecisionTelemetry CreateTelemetryRecord(int promptLengthChars, LLMResponse response)
        {
            return new DecisionTelemetry
            {
                sourceId = gameObject != null ? gameObject.name : string.Empty,
                decisionIndex = ++decisionCounter,
                timestampSec = Time.time,
                latencyMs = response != null ? response.LatencyMs : 0f,
                promptLengthChars = promptLengthChars,
                responseLengthChars = response != null && response.Text != null ? response.Text.Length : 0,
                promptTokens = response != null ? response.PromptTokens : 0,
                completionTokens = response != null ? response.CompletionTokens : 0,
                cachedTokens = response != null ? response.CachedTokens : 0,
                profile = Cfg.profile,
                structuredOutput = response != null && response.StructuredOutput,
                resultType = DecisionResultType.NoAction,
                actionName = string.Empty,
                argument = string.Empty,
                reason = string.Empty,
                fallbackReason = string.Empty,
                parseFailureReason = string.Empty,
                backendError = string.Empty,
                argumentPolicyReason = string.Empty
            };
        }

        private void EmitBackendUnavailableTelemetry(int promptLengthChars, LLMResponse response)
        {
            DecisionTelemetry telemetry = CreateTelemetryRecord(promptLengthChars, response);
            telemetry.resultType = DecisionResultType.BackendError;
            telemetry.backendError = DescribeBackendFailure(response);
            TryExecuteFallback($"backend unavailable ({telemetry.backendError})", telemetry);
            EmitTelemetry(telemetry);
        }

        private string DescribeBackendFailure(LLMResponse response)
        {
            if (response != null && !string.IsNullOrWhiteSpace(response.Error)) return response.Error;
            if (backend is IBackendDiagnostics diag && !string.IsNullOrWhiteSpace(diag.LastBackendError)) return diag.LastBackendError;
            return "empty backend response.";
        }

        private void LogBackendUnavailable(LLMResponse response)
        {
            if (Time.time - lastBackendWarningTime < BackendWarningCooldown) return;
            lastBackendWarningTime = Time.time;
            BehaviorLLMLog.Warn(() => $"[DecisionMaker] Backend unavailable ({DescribeBackendFailure(response)})");
        }

        private void EmitTelemetry(DecisionTelemetry telemetry)
        {
            if (telemetry != null) DecisionTelemetryRecorded?.Invoke(telemetry);

            #if UNITY_EDITOR
            // After the recorder, so a decision reaches the window and the CSV in the same order.
            if (telemetry != null && CaptureExchanges) DecisionInspected?.Invoke(this, telemetry);
            #endif
        }

        // ------------------------------------------------------------------ misc

        private void CancelAndDisposeThinkCts()
        {
            if (thinkCts == null) return;
            try { thinkCts.Cancel(); } catch (ObjectDisposedException) { }
            thinkCts.Dispose();
            thinkCts = null;
        }

        #if UNITY_EDITOR
        // Written so the schema can be POSTed by hand to llama-server for out-of-band debugging.
        // Editor only, and switchable: the write is small but it happens on every schema rebuild,
        // which is once per decision for a decision maker with dynamic argument options.
        private static void DumpToStreamingAssets(string fileName, string content)
        {
            if (!BehaviorLLMSettings.Current.dumpAppliedSchema) return;

            try
            {
                string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, content);
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Warn(() => $"[DecisionMaker] Failed to write {fileName}: {e.Message}");
            }
        }
        #endif
    }
}
