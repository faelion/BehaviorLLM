using UnityEngine;
using System.Collections.Generic;
using System;
using System.Text;

namespace BehaviorLLM.Core.Actions
{
    public enum ActionParameterType { None, String, Int }

    [Serializable]
    public class ActionDefinition
    {
        [Tooltip("The name of the action, for example Patrol or Attack. Letters, digits " +
                 "and underscores only, no spaces. This exact text is what the AI " +
                 "answers with, and what you match in Action Bindings on the decision " +
                 "maker.")]
        public string actionName;

        [TextArea]
        [Tooltip("What the action does, written for the AI to read. Keep it short and " +
                 "say when it applies, for example 'Chase a visible intruder to catch " +
                 "them'. This is the AI's only explanation of the action.")]
        public string description;

        [Tooltip("Whether the action needs an extra value. None: just the action, like " +
                 "HoldPosition. String or Int: the AI must also give a value, like the " +
                 "name of a waypoint for Patrol. The value always arrives in your " +
                 "function as a string.")]
        public ActionParameterType parameterType;

        [Tooltip("Optional. A sample value for this action's argument, like " +
                 "'Route_North', shown to the AI as an example of a correct answer. Only " +
                 "used when Parameter Type is not None.")]
        public string exampleArgument;

        [Tooltip("Optional. The complete list of values the AI may use as this action's " +
                 "argument. When filled in, the AI can only answer with one of these. " +
                 "Leave empty to accept any single word (letters, digits and " +
                 "underscores). If the valid values change while the game runs, use an " +
                 "Argument Options Provider on the decision maker instead.")]
        public List<string> allowedArguments = new List<string>();

        public bool TakesArgument => parameterType != ActionParameterType.None;
    }

    [CreateAssetMenu(fileName = "NewActionConfig", menuName = "BehaviorLLM/Action Config")]
    public class ActionConfig : ScriptableObject
    {
        [Header("Capabilities")]
        [Tooltip("Every action the AI can choose from. Add one entry per action. This " +
                 "only defines what exists; which function runs for each is set " +
                 "separately in Action Bindings on each decision maker, so several can " +
                 "share this asset.")]
        public List<ActionDefinition> validActions = new List<ActionDefinition>();

        [Header("Prompt Engineering")]
        [TextArea(5, 10)]
        [Tooltip("Extra guidance for the AI about preferences and style, such as 'Prefer " +
                 "patrolling over standing still'. Keep it short. Do not put if-then " +
                 "rules here: in our tests a rule with two conditions was ignored 15 " +
                 "times out of 18. When an action should not be allowed in some " +
                 "situation, remove it with an Action Availability Provider on the " +
                 "decision maker instead, which the AI cannot ignore.")]
        public string modelInstructions = "Choose exactly one action per turn. If nothing relevant is visible, choose the safest idle action.";

        /// <summary>
        /// Renders the action menu block of the prompt. <paramref name="optionsFor"/> may supply
        /// runtime argument options per action; static <c>allowedArguments</c> are used otherwise.
        /// </summary>
        public string GetPromptDescription(Func<ActionDefinition, IList<string>> optionsFor = null)
        {
            return GetPromptDescription(optionsFor, true, null);
        }

        /// <summary>
        /// The action menu, optionally leaving provider-supplied argument lists out of it.
        ///
        /// This exists because of where the two kinds of argument list come from.
        /// <see cref="ActionDefinition.allowedArguments"/> is authored in this asset and cannot
        /// change while the game runs, so printing it here costs nothing: it is part of the stable
        /// prefix an inference server keeps in its KV cache. A list from an
        /// <c>IArgumentOptionsProvider</c> is computed from the scene, and for anything whose
        /// arguments are *other entities* - who is standing nearby, which hiding place is free - it
        /// changes almost every decision. Printing that here rewrites the prefix each turn and
        /// throws the cache away.
        ///
        /// With <paramref name="inlineProviderOptions"/> false, those actions are listed without
        /// their values and the names are reported through <paramref name="deferred"/>, so the
        /// caller can put the current values in the per-decision state block instead. The model
        /// still sees them; the schema enforces them either way.
        /// </summary>
        public string GetPromptDescription(Func<ActionDefinition, IList<string>> optionsFor,
                                           bool inlineProviderOptions,
                                           IList<string> deferred)
        {
            StringBuilder sb = new StringBuilder("AVAILABLE ACTIONS:\n");
            for (int i = 0; i < validActions.Count; i++)
            {
                ActionDefinition act = validActions[i];
                if (act == null || string.IsNullOrWhiteSpace(act.actionName)) continue;

                sb.Append("- ").Append(act.actionName);
                if (act.TakesArgument) sb.Append('(').Append(act.parameterType).Append(')');
                sb.Append(": ").Append(act.description);

                if (act.TakesArgument)
                {
                    // Whether an action's values are authored here is a property of this asset and
                    // never changes while the game runs, which is exactly what the cached prefix
                    // needs. Asking the provider instead does not work: it reports "nothing right
                    // now" and "I do not handle this action" the same way, so the marker would
                    // appear and disappear as targets came and went, rewriting the prefix just as
                    // the values themselves used to.
                    bool authored = act.allowedArguments != null && act.allowedArguments.Count > 0;

                    if (!inlineProviderOptions && !authored && optionsFor != null)
                    {
                        sb.Append(" [arg: listed under ARGUMENTS below]");
                        deferred?.Add(act.actionName);
                    }
                    else
                    {
                        IList<string> fromProvider = optionsFor != null ? optionsFor(act) : null;
                        IList<string> options = fromProvider != null && fromProvider.Count > 0
                            ? fromProvider
                            : act.allowedArguments;
                        if (options != null && options.Count > 0)
                        {
                            sb.Append(" [arg: ").Append(string.Join(" | ", options)).Append(']');
                        }
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public string GetPromptDescription()
        {
            return GetPromptDescription(null);
        }

        /// <summary>Case-insensitive lookup of an action definition by name.</summary>
        public bool TryGetAction(string actionName, out ActionDefinition definition)
        {
            definition = null;
            if (validActions == null || string.IsNullOrWhiteSpace(actionName)) return false;
            for (int i = 0; i < validActions.Count; i++)
            {
                ActionDefinition action = validActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.actionName)) continue;
                if (action.actionName.Equals(actionName, StringComparison.OrdinalIgnoreCase))
                {
                    definition = action;
                    return true;
                }
            }
            return false;
        }
    }
}
