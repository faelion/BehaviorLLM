using System;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Actions;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// Builds the JSON Schema that constrains a decision to one of the configured actions.
    ///
    /// Shape (one <c>oneOf</c> branch per action, so each action can carry its own argument
    /// constraint):
    /// <code>
    /// {"oneOf":[
    ///   {"type":"object","properties":{"reason":{...},"action":{"const":"Attack"},"arg":{"enum":["Orc","Goblin"]}},
    ///    "required":["reason","action","arg"],"additionalProperties":false},
    ///   {"type":"object","properties":{"action":{"const":"Stop"},"arg":{"const":""}}, ...}
    /// ]}
    /// </code>
    /// Property order is deliberate: <c>reason</c> (optional) is generated before <c>action</c>
    /// so a deliberative decision is reasoned first, and <c>action</c> comes before <c>arg</c> so the
    /// action can be dispatched early from a stream in the future. llama-server compiles this
    /// schema to a grammar and applies it during sampling, so the model cannot emit an unknown
    /// action or, when options are given, an unknown argument.
    ///
    /// Actions whose names are not <c>[A-Za-z0-9_]+</c> are skipped (they would also break the
    /// prompt parser). Argument options are sanitised the same way.
    /// </summary>
    public static class ActionSchemaBuilder
    {
        public const string ActionKey = "action";
        public const string ArgumentKey = "arg";
        public const string ReasonKey = "reason";
        public const string IdentifierPattern = "^[A-Za-z0-9_]+$";

        public sealed class Options
        {
            /// <summary>When > 0, a leading <c>reason</c> string of at most this many characters
            /// is required before the action (Deliberative profile).</summary>
            public int ReasonMaxChars;

            /// <summary>Optional runtime source of argument options per action (scene IDs).
            /// Returns null or an empty list to fall back to the static list on the action.</summary>
            public Func<ActionDefinition, IList<string>> ArgumentOptionsFor;

            /// <summary>Optional filter that drops actions the game does not currently allow.
            /// Returning false removes the action's branch, so the model cannot emit it at all.
            /// Null means every action is available.</summary>
            public Func<ActionDefinition, bool> IsActionAvailable;
        }

        /// <summary>
        /// Returns the schema JSON, or an empty string when the config has no usable action (the
        /// caller should then skip structured output rather than send a useless constraint).
        /// </summary>
        public static string Build(ActionConfig config, Options options = null)
        {
            if (config == null || config.validActions == null) return string.Empty;
            options = options ?? new Options();

            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\"oneOf\":[");
            int emitted = 0;

            for (int i = 0; i < config.validActions.Count; i++)
            {
                ActionDefinition action = config.validActions[i];
                if (action == null || !IsIdentifier(action.actionName)) continue;
                if (options.IsActionAvailable != null && !options.IsActionAvailable(action)) continue;

                if (emitted > 0) sb.Append(',');
                AppendBranch(sb, action, options);
                emitted++;
            }

            if (emitted == 0) return string.Empty;
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendBranch(StringBuilder sb, ActionDefinition action, Options options)
        {
            bool withReason = options.ReasonMaxChars > 0;

            sb.Append("{\"type\":\"object\",\"properties\":{");
            if (withReason)
            {
                sb.Append('"').Append(ReasonKey).Append("\":{\"type\":\"string\",\"maxLength\":")
                  .Append(options.ReasonMaxChars).Append("},");
            }

            sb.Append('"').Append(ActionKey).Append("\":{\"const\":\"").Append(action.actionName).Append("\"},");
            sb.Append('"').Append(ArgumentKey).Append("\":");
            AppendArgumentSchema(sb, action, options);
            sb.Append("},\"required\":[");
            if (withReason) sb.Append('"').Append(ReasonKey).Append("\",");
            sb.Append('"').Append(ActionKey).Append("\",\"").Append(ArgumentKey).Append("\"],");
            sb.Append("\"additionalProperties\":false}");
        }

        private static void AppendArgumentSchema(StringBuilder sb, ActionDefinition action, Options options)
        {
            if (!action.TakesArgument)
            {
                sb.Append("{\"const\":\"\"}");
                return;
            }

            List<string> values = CollectOptions(action, options);
            if (values.Count > 0)
            {
                sb.Append("{\"enum\":[");
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(values[i]).Append('"');
                }
                sb.Append("]}");
                return;
            }

            sb.Append("{\"type\":\"string\",\"pattern\":\"").Append(IdentifierPattern).Append("\"}");
        }

        /// <summary>
        /// Resolves the effective argument options for an action: provider first, then the static
        /// list. Non-identifier values and duplicates are dropped.
        /// </summary>
        public static List<string> CollectOptions(ActionDefinition action, Options options)
        {
            List<string> result = new List<string>();
            if (action == null || !action.TakesArgument) return result;

            IList<string> source = null;
            if (options != null && options.ArgumentOptionsFor != null) source = options.ArgumentOptionsFor(action);
            if (source == null || source.Count == 0) source = action.allowedArguments;
            if (source == null) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                string value = source[i] != null ? source[i].Trim() : null;
                if (!IsIdentifier(value) || !seen.Add(value)) continue;
                result.Add(value);
            }
            return result;
        }

        public static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
