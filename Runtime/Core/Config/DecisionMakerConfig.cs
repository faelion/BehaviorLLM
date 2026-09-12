using BehaviorLLM.Core.Decisions;
using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Everything that tunes *how* a <see cref="DecisionMaker"/> decides, in one reusable asset:
    /// how often it decides, how much it is allowed to deliberate, how big its prompt may grow
    /// and how loud it is in the console.
    ///
    /// None of this lives on the component, so a project can retune every decision maker that
    /// shares a config by editing one asset, and can keep as many variations as it likes
    /// (a jumpy guard, a patient director) without a prefab for each. What stays on the
    /// component is only what cannot be shared: its persona, its action bindings and the scene
    /// objects it talks to.
    ///
    /// Create with <c>Create &gt; BehaviorLLM &gt; Decision Maker Config</c>, or start from the
    /// preset the package ships at <c>Runtime/Defaults/Config_Reactive.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewDecisionMakerConfig", menuName = "BehaviorLLM/Decision Maker Config", order = 0)]
    public class DecisionMakerConfig : ScriptableObject
    {
        [Header("Decision Loop")]
        [Tooltip("Seconds between decisions. Each decision is one request to the AI, so " +
                 "this is the main cost control: 0.5 is four times the work of 2. Sight " +
                 "interrupts still cause an immediate decision regardless. Try 1 to 3 " +
                 "seconds for characters that must react, 10 or more for a manager or " +
                 "director.")]
        public float decisionInterval = 2.0f;

        [Header("Decision Profile")]
        [Tooltip("Reactive: answer as fast as possible, with no thinking. Use it for " +
                 "enemies, allies and anything that must react within a beat. " +
                 "Deliberative: the AI writes a short reason before choosing, which can " +
                 "improve the choice but makes each decision 50 to 90 percent slower. In " +
                 "our tests it only helped the largest model (4B).")]
        public DecisionProfile profile = DecisionProfile.Reactive;

        [Tooltip("Deliberative only. The most characters the AI may write as its reason " +
                 "before choosing. 0 removes the reason. Keep it short: about 120 worked " +
                 "best, and long reasons made small models worse.")]
        public int reasonMaxChars = 120;

        [Tooltip("Deliberative only. Lets the AI think privately before answering, up to " +
                 "this many tokens. Leave at 0. On a 2B model this made the AI repeat " +
                 "the question instead of answering. Only worth trying with a 4B model " +
                 "or larger, and only after measuring.")]
        public int thinkingBudgetTokens = 0;

        [Tooltip("The most text the AI may produce per decision, in tokens (roughly one " +
                 "token per short word). 0 sets it automatically from the profile, which " +
                 "is what you want. Set it only to stop a runaway model.")]
        public int maxTokens = 0;

        [Header("Structured Output")]
        [Tooltip("Force the AI to answer in the exact format expected, choosing only " +
                 "from the allowed actions and arguments. It costs nothing measurable " +
                 "and removes almost all bad answers. Leave on. Turn off only to see how " +
                 "the AI behaves without it.")]
        public bool useStructuredOutput = true;

        [Tooltip("Keep argument lists that come from an Argument Options Provider out of the " +
                 "action menu, and send the current values in the per-decision state block instead. " +
                 "Leave this on. An action whose arguments are other characters - who is nearby, " +
                 "which hiding place is free - changes its list almost every decision, and printing " +
                 "it in the menu rewrites the cached prompt prefix every time. Measured on the " +
                 "PrisonYard sample: off, the four prisoners got a different prefix on essentially " +
                 "every decision. Turning it off restores the pre-0.4.0 layout for comparison.")]
        public bool dynamicArgumentsInState = true;

        [Tooltip("Show the AI two example decisions so it learns the expected shape. " +
                 "Cheap, because the server remembers them between decisions, and it " +
                 "noticeably helps small models. Leave on.")]
        public bool includeExamplesInPrompt = true;

        [Header("Prompt Budget")]
        [Tooltip("The most seen objects to tell the AI about per decision, nearest " +
                 "first. 0 means all of them. Lower it in crowded scenes so the " +
                 "important thing is not lost in a long list.")]
        public int maxVisionEntries = 0;

        [Tooltip("The most remembered events to tell the AI about per decision, newest " +
                 "first. 0 means all of them.")]
        public int maxMemoryEntries = 0;

        [Tooltip("A hard limit on the total text sent to the AI per decision, in " +
                 "characters. 0 means no limit. When exceeded, observations are dropped " +
                 "from the end of the list; the persona and the action list are never " +
                 "cut. Set it if the model's context is small and the scene can produce " +
                 "many observations.")]
        public int maxPromptChars = 0;

        [Header("Diagnostics")]
        [Tooltip("Print the full text sent to the AI in the console before each " +
                 "decision. Very useful while setting up a scene, far too noisy " +
                 "afterwards. Editor only.")]
        public bool logPrompts = false;

        [Tooltip("Print the AI's answer and the action that ran, one line each. Keep on " +
                 "while building, off in busy scenes.")]
        public bool logDecisions = true;

        [Tooltip("Print a warning each time the fallback action runs, saying why. Keep " +
                 "on: fallbacks happening often is the first sign that something is " +
                 "wrong with the prompt or the model.")]
        public bool logFallbackUsage = true;

        /// <summary>
        /// Characters of `reason` actually requested: the configured cap under
        /// <see cref="DecisionProfile.Deliberative"/>, and zero under Reactive.
        /// </summary>
        public int EffectiveReasonChars => profile == DecisionProfile.Deliberative ? Mathf.Max(0, reasonMaxChars) : 0;

        /// <summary>Whether the model's native thinking phase is requested for this profile.</summary>
        public bool UsesNativeThinking => profile == DecisionProfile.Deliberative && thinkingBudgetTokens > 0;
    }
}
