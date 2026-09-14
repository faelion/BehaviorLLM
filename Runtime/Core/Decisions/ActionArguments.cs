using System;
using System.Collections.Generic;
using System.Text;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// The values the model chose for one action, delivered to the bound handler.
    ///
    /// An action that takes nothing arrives with <see cref="Count"/> zero; one that takes a single
    /// value is read with <see cref="First"/>; one that takes several is read by name. Values are
    /// always strings because that is what the model produced and what the schema constrained;
    /// <see cref="GetInt"/> is there for parameters declared as Int.
    ///
    /// Immutable on purpose. A handler that could rewrite its own arguments would make the
    /// telemetry record a decision that did not happen.
    /// </summary>
    public sealed class ActionArguments
    {
        private static readonly string[] NoNames = new string[0];

        /// <summary>An action invoked with no values. Shared, because it carries no state.</summary>
        public static readonly ActionArguments Empty = new ActionArguments(string.Empty, null, null);

        private readonly string[] names;
        private readonly string[] values;

        /// <summary>The action these values belong to, as the model named it.</summary>
        public string Action { get; }

        public ActionArguments(string action, IList<string> parameterNames, IList<string> parameterValues)
        {
            Action = action ?? string.Empty;
            int count = parameterValues != null ? parameterValues.Count : 0;
            if (count == 0)
            {
                names = NoNames;
                values = NoNames;
                return;
            }

            names = new string[count];
            values = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = parameterNames != null && i < parameterNames.Count ? parameterNames[i] : "arg";
                values[i] = parameterValues[i] ?? string.Empty;
            }
        }

        /// <summary>Convenience for the common single-value action.</summary>
        public static ActionArguments Single(string action, string value)
        {
            return new ActionArguments(action, new[] { "arg" }, new[] { value ?? string.Empty });
        }

        /// <summary>How many values the model supplied.</summary>
        public int Count => values.Length;

        /// <summary>
        /// The first value, or an empty string when the action takes none. This is what a handler
        /// for a one-value action reads, and it never returns null, so it is safe to compare and
        /// to pass straight into game code.
        /// </summary>
        public string First => values.Length > 0 ? values[0] : string.Empty;

        /// <summary>Value by position. Out-of-range returns an empty string rather than throwing:
        /// a handler reading an argument the action does not declare is a wiring mistake, not a
        /// reason to take down the frame.</summary>
        public string this[int index] =>
            index >= 0 && index < values.Length ? values[index] : string.Empty;

        /// <summary>Value by parameter name, case-insensitive. Empty string when absent.</summary>
        public string this[string parameterName]
        {
            get
            {
                TryGet(parameterName, out string value);
                return value;
            }
        }

        /// <summary>The parameter names, in the order the action declares them.</summary>
        public IReadOnlyList<string> Names => names;

        /// <summary>True when the named parameter was supplied, with its value.</summary>
        public bool TryGet(string parameterName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(parameterName)) return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    value = values[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The named value as a whole number, or <paramref name="fallback"/> when it is missing or
        /// not a number. A schema that declares the parameter Int makes the second case unlikely,
        /// but a custom backend or a policy rewrite can still produce it.
        /// </summary>
        public int GetInt(string parameterName, int fallback = 0)
        {
            return TryGet(parameterName, out string raw) && int.TryParse(raw, out int parsed)
                ? parsed
                : fallback;
        }

        /// <summary>The first value as a whole number.</summary>
        public int GetInt(int fallback = 0) => int.TryParse(First, out int parsed) ? parsed : fallback;

        /// <summary>
        /// Compact rendering for logs and telemetry: the bare value for a single-parameter action
        /// so existing run reports read exactly as before, and <c>name=value</c> pairs beyond that.
        /// </summary>
        public string ToCompactString()
        {
            if (values.Length == 0) return string.Empty;
            if (values.Length == 1) return values[0];

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(names[i]).Append('=').Append(values[i]);
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return values.Length == 0 ? Action + "()" : Action + "(" + ToCompactString() + ")";
        }
    }
}
