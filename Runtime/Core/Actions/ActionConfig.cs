using UnityEngine;
using System.Collections.Generic;
using System;
using System.Text;

namespace BehaviorLLM.Core.Actions
{
    public enum ActionParameterType { None, String, Int }

    /// <summary>
    /// One value an action takes. An action with no parameters is just a verb ("Hold"); one with
    /// several is a verb with named operands ("MoveTo(destination, speed)"). The name matters:
    /// it becomes the JSON property the model fills in, and it is what the action menu shows the
    /// model, so <c>destination</c> reads better than <c>arg</c>.
    /// </summary>
    [Serializable]
    public class ActionParameter
    {
        [Tooltip("Name of this value, for example destination or target. Letters, digits and " +
                 "underscores only. It becomes the field the AI fills in, and the AI reads it as " +
                 "a description of what the value means, so name it for what it is.")]
        public string name = "arg";

        [Tooltip("String or Int. The value always arrives in your handler as a string; Int tells " +
                 "the AI to answer with a number and is available as GetInt on the arguments.")]
        public ActionParameterType type = ActionParameterType.String;

        [Tooltip("Optional. A sample value such as 'Route_North', shown to the AI as part of an " +
                 "example of a correct answer.")]
        public string exampleValue;

        [Tooltip("Optional. The complete list of values the AI may use here. When filled in, the " +
                 "AI can only answer with one of these. Leave empty to accept any single word " +
                 "(letters, digits and underscores). If the valid values change while the game " +
                 "runs, use an Argument Options Provider on the decision maker instead.")]
        public List<string> allowedValues = new List<string>();
    }

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

        [Tooltip("The values this action takes, in order. Leave empty for an action that is just " +
                 "a verb, like HoldPosition. Each one becomes a field the AI must fill in, and " +
                 "each can have its own list of allowed values.")]
        public List<ActionParameter> parameters = new List<ActionParameter>();

        // ---------------------------------------------------------------- legacy, migrated on load
        // Actions used to carry exactly one unnamed argument. These three fields are what those
        // assets serialised, and they are kept so an asset authored before parameters existed
        // still describes the same action. MigrateLegacy folds them into a single parameter named
        // "arg", which is also the JSON property name they produced, so the schema is unchanged.

        [HideInInspector] public ActionParameterType parameterType;
        [HideInInspector] public string exampleArgument;
        [HideInInspector] public List<string> allowedArguments = new List<string>();

        [NonSerialized] private bool migrated;

        /// <summary>
        /// Folds a pre-parameters action into the parameter list. Idempotent, and safe to call on
        /// an action built in code, which is why every accessor goes through it rather than
        /// relying on deserialisation alone.
        /// </summary>
        public void MigrateLegacy()
        {
            if (migrated) return;
            migrated = true;

            if (parameters == null) parameters = new List<ActionParameter>();
            if (parameters.Count > 0) return;
            if (parameterType == ActionParameterType.None) return;

            parameters.Add(new ActionParameter
            {
                name = "arg",
                type = parameterType,
                exampleValue = exampleArgument,
                // Shared, not copied. A copy is taken at first access, so anything added to the
                // legacy list afterwards - by editor tooling, or by a test building a config in
                // code - would be invisible, and an action would silently accept any value.
                allowedValues = allowedArguments ?? (allowedArguments = new List<string>())
            });
        }

        /// <summary>This action's parameters, with any legacy single argument folded in.</summary>
        public List<ActionParameter> Parameters
        {
            get { MigrateLegacy(); return parameters; }
        }

        /// <summary>True when the action takes at least one value.</summary>
        public bool TakesArgument => Parameters.Count > 0;

        /// <summary>The first parameter, or null for an action that is just a verb.</summary>
        public ActionParameter FirstParameter => Parameters.Count > 0 ? Parameters[0] : null;

        /// <summary>Case-insensitive lookup of a parameter by name.</summary>
        public bool TryGetParameter(string parameterName, out ActionParameter parameter)
        {
            parameter = null;
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            List<ActionParameter> all = Parameters;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    parameter = all[i];
                    return true;
                }
            }
            return false;
        }
    }

    [CreateAssetMenu(fileName = "NewActionConfig", menuName = "BehaviorLLM/Action Config")]
    public class ActionConfig : ScriptableObject, ISerializationCallbackReceiver
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

        public void OnBeforeSerialize() { }

        /// <summary>Folds any pre-parameters actions into the parameter list as the asset loads.</summary>
        public void OnAfterDeserialize()
        {
            if (validActions == null) return;
            for (int i = 0; i < validActions.Count; i++) validActions[i]?.MigrateLegacy();
        }

        /// <summary>
        /// Renders the action menu block of the prompt. <paramref name="optionsFor"/> may supply
        /// runtime options per parameter; authored <c>allowedValues</c> are used otherwise.
        /// </summary>
        public string GetPromptDescription(Func<ActionDefinition, ActionParameter, IList<string>> optionsFor = null)
        {
            return GetPromptDescription(optionsFor, true, null);
        }

        /// <summary>
        /// The action menu, optionally leaving provider-supplied argument lists out of it.
        ///
        /// This exists because of where the two kinds of argument list come from.
        /// <see cref="ActionParameter.allowedValues"/> is authored in this asset and normally
        /// stays fixed. With no provider, it belongs in the stable prefix an inference server
        /// keeps in its KV cache. A list from an
        /// <c>IArgumentOptionsProvider</c> is computed from the scene, and for anything whose
        /// arguments are *other entities* - who is standing nearby, which hiding place is free - it
        /// changes almost every decision. Printing that here rewrites the prefix each turn and
        /// throws the cache away.
        ///
        /// Providers may override authored lists too. With <paramref name="inlineProviderOptions"/>
        /// false and a provider attached, all parameters are listed without
        /// their values and the action names are reported through <paramref name="deferred"/>, so
        /// the caller can put the current values in the per-decision state block instead. The model
        /// still sees them; the schema enforces them either way.
        /// </summary>
        public string GetPromptDescription(Func<ActionDefinition, ActionParameter, IList<string>> optionsFor,
                                           bool inlineProviderOptions,
                                           IList<string> deferred)
        {
            StringBuilder sb = new StringBuilder("AVAILABLE ACTIONS:\n");
            for (int i = 0; i < validActions.Count; i++)
            {
                ActionDefinition act = validActions[i];
                if (act == null || string.IsNullOrWhiteSpace(act.actionName)) continue;

                List<ActionParameter> parameters = act.Parameters;
                sb.Append("- ").Append(act.actionName);
                if (parameters.Count == 1 && parameters[0] != null && parameters[0].name == "arg")
                {
                    // The legacy shape: one unnamed value. Its name says nothing the model can
                    // use, so print the type as before and leave the cached prefix untouched for
                    // every config authored before parameters existed.
                    sb.Append('(').Append(parameters[0].type).Append(')');
                }
                else if (parameters.Count > 0)
                {
                    sb.Append('(');
                    for (int p = 0; p < parameters.Count; p++)
                    {
                        if (p > 0) sb.Append(", ");
                        sb.Append(parameters[p] != null ? parameters[p].name : "arg");
                    }
                    sb.Append(')');
                }
                sb.Append(": ").Append(act.description);

                bool anyDeferred = false;
                for (int p = 0; p < parameters.Count; p++)
                {
                    ActionParameter parameter = parameters[p];
                    if (parameter == null) continue;

                    // A provider can override authored values too. Defer consistently while a
                    // provider is attached, even when it has no options on this particular turn.
                    if (!inlineProviderOptions && optionsFor != null)
                    {
                        sb.Append(" [").Append(parameter.name).Append(": listed under ARGUMENTS below]");
                        anyDeferred = true;
                        continue;
                    }

                    IList<string> fromProvider = optionsFor != null ? optionsFor(act, parameter) : null;
                    IList<string> options = fromProvider != null && fromProvider.Count > 0
                        ? fromProvider
                        : parameter.allowedValues;
                    if (options != null && options.Count > 0)
                    {
                        sb.Append(" [").Append(parameter.name).Append(": ")
                          .Append(string.Join(" | ", options)).Append(']');
                    }
                }
                if (anyDeferred) deferred?.Add(act.actionName);

                sb.Append('\n');
            }
            return sb.ToString();
        }

        public string GetPromptDescription()
        {
            return GetPromptDescription((Func<ActionDefinition, ActionParameter, IList<string>>)null);
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
