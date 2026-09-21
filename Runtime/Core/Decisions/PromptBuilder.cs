using System;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// Assembles the two halves of a decision prompt.
    ///
    /// The <b>system prompt</b> (persona, output contract, action menu, examples, guide) is
    /// static for the life of a decision maker and is built once; llama-server reuses its KV cache for
    /// it on every call. The <b>state prompt</b> (observations) changes every decision and is
    /// kept last. This ordering is the single most effective latency optimisation available
    /// to something that decides on a repeating tick (prefix caching; see Gim et al., "Prompt Cache", MLSys 2024).
    ///
    /// Line endings are always <c>\n</c> regardless of platform so prompts (and their cache
    /// keys) are identical everywhere.
    /// </summary>
    public static class PromptBuilder
    {
        public const string StateHeader = "STATE:";
        public const string CompletionLead = "OUTPUT: ";
        private const char NL = '\n';

        public const string AvailableHeader = "AVAILABLE THIS TURN:";

        public const string ArgumentsHeader = "ARGUMENTS:";

        public sealed class SystemPromptOptions
        {
            public string Persona = "You are a game NPC.";
            public bool IncludeExamples = true;
            /// <summary>When > 0 the contract asks for a leading `reason` field of at most this many characters.</summary>
            public int ReasonMaxChars;
            /// <summary>
            /// When false, argument lists that come from an <c>IArgumentOptionsProvider</c> are
            /// left out of the action menu and belong in the state block instead. Keeps the cached
            /// prefix stable for characters whose arguments are other characters.
            /// </summary>
            public bool InlineProviderOptions = true;

            /// <summary>Filled in by the builder with the actions whose options were deferred.</summary>
            public IList<string> DeferredArgumentActions;

            /// <summary>Optional runtime argument options, mirrored in the action menu.</summary>
            public Func<ActionDefinition, ActionParameter, IList<string>> ArgumentOptionsFor;
            /// <summary>Set when an availability provider gates the menu, so the contract explains
            /// the per-turn list that appears in the state block.</summary>
            public bool DynamicMenu;
        }

        public static string BuildSystemPrompt(ActionConfig config, SystemPromptOptions options)
        {
            options = options ?? new SystemPromptOptions();
            StringBuilder sb = new StringBuilder(1024);

            sb.Append(options.Persona ?? string.Empty).Append(NL);
            sb.Append("ROLE: Function caller. Reply with ONE JSON object ");
            if (options.ReasonMaxChars > 0)
            {
                sb.Append("{\"reason\":\"<why, under ").Append(options.ReasonMaxChars)
                  .Append(" characters>\",\"action\":\"<name>\"}");
            }
            else
            {
                sb.Append("{\"action\":\"<name>\"}");
            }
            sb.Append(" and nothing else: no markdown, no commentary.").Append(NL);
            sb.Append("Add one JSON string field for each parameter in the chosen action signature, using its name. " +
                      "A single type-only signature (e.g. `Attack(String)`) uses `arg`. " +
                      "Actions without parameters need no argument fields. Int values are integer strings.").Append(NL);
            if (options.DynamicMenu)
            {
                sb.Append("Some actions are unavailable on some turns. Choose only from the list after ")
                  .Append(AvailableHeader).Append(" in the state block.").Append(NL);
            }
            sb.Append(NL);

            if (config != null)
            {
                sb.Append(config.GetPromptDescription(options.ArgumentOptionsFor,
                                                      options.InlineProviderOptions,
                                                      options.DeferredArgumentActions));
                sb.Append(NL);

                if (options.IncludeExamples) AppendExamples(sb, config, options);

                if (!string.IsNullOrWhiteSpace(config.modelInstructions))
                {
                    sb.Append("GUIDE:").Append(NL);
                    sb.Append(config.modelInstructions.Trim()).Append(NL);
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Dynamic half of the prompt.</summary>
        public static string BuildStatePrompt(string observations)
        {
            return BuildStatePrompt(observations, null);
        }

        /// <summary>
        /// Dynamic half of the prompt, optionally led by the actions available this turn.
        ///
        /// The available list belongs here, in the per-decision message, and not in the action
        /// menu of the system prompt: the system prompt is what the server keeps in its KV cache
        /// between decisions, so changing it every tick would throw that cache away. The schema
        /// travels with each request instead, and is what actually prevents an unavailable action
        /// from being emitted; this line only tells the model why the menu shrank.
        /// </summary>
        public static string BuildStatePrompt(string observations, IList<string> availableActions)
        {
            return BuildStatePrompt(observations, availableActions, null);
        }

        /// <summary>
        /// The per-decision half of the prompt: which actions may be chosen, the current values for
        /// any action whose arguments were left out of the menu, and the observations.
        ///
        /// <paramref name="argumentOptions"/> is what makes deferring worthwhile. An action whose
        /// arguments are other entities cannot have its list printed in the cached prefix without
        /// rewriting that prefix every decision; printed here instead, the prefix stays stable and
        /// the model still sees exactly which values are legal this turn.
        /// </summary>
        public static string BuildStatePrompt(string observations, IList<string> availableActions,
                                              IDictionary<string, IList<string>> argumentOptions)
        {
            string body = string.IsNullOrWhiteSpace(observations) ? "[No Observations]" : observations.TrimEnd();
            bool hasAvailable = availableActions != null && availableActions.Count > 0;
            bool hasArguments = argumentOptions != null && argumentOptions.Count > 0;
            if (!hasAvailable && !hasArguments) return StateHeader + NL + body;

            StringBuilder sb = new StringBuilder(body.Length + 128);

            if (hasAvailable)
            {
                sb.Append(AvailableHeader).Append(' ');
                for (int i = 0; i < availableActions.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(availableActions[i]);
                }
                sb.Append(NL);
            }

            if (hasArguments)
            {
                sb.Append(ArgumentsHeader).Append(NL);
                foreach (KeyValuePair<string, IList<string>> entry in argumentOptions)
                {
                    if (entry.Value == null || entry.Value.Count == 0) continue;
                    sb.Append("- ").Append(entry.Key).Append(": ")
                      .Append(string.Join(" | ", entry.Value)).Append(NL);
                }
            }

            sb.Append(StateHeader).Append(NL).Append(body);
            return sb.ToString();
        }

        /// <summary>
        /// Trims a state prompt to <paramref name="maxChars"/> by dropping whole lines from the
        /// end. The header and the first observation line are always kept, and the cut never
        /// lands mid-word: a prompt ending in a half token makes small models complete the
        /// template instead of answering (this caused a large fallback-rate regression once).
        /// </summary>
        public static string TrimStatePrompt(string statePrompt, int maxChars)
        {
            if (string.IsNullOrEmpty(statePrompt) || maxChars <= 0 || statePrompt.Length <= maxChars) return statePrompt;

            string[] lines = statePrompt.Split(NL);
            int stateIndex = Array.FindIndex(lines, line => line.TrimEnd('\r') == StateHeader);
            // Metadata precedes STATE. Keep it intact along with the first observation rather
            // than mistaking AVAILABLE/ARGUMENTS for the observation itself. The caller rejects
            // an impossible budget instead of sending a decision without any world state.
            int minimumLines = stateIndex >= 0 ? Math.Min(lines.Length, stateIndex + 2) : Math.Min(lines.Length, 2);
            if (stateIndex >= 0)
            {
                // Topic headings are not observations. Keep the first payload line too.
                int firstObservation = stateIndex + 1;
                while (firstObservation < lines.Length &&
                       (string.IsNullOrWhiteSpace(lines[firstObservation]) ||
                        lines[firstObservation].TrimStart().StartsWith("---", StringComparison.Ordinal)))
                    firstObservation++;
                minimumLines = Math.Min(lines.Length, firstObservation + 1);
            }
            StringBuilder sb = new StringBuilder(maxChars);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                int projected = sb.Length + line.Length + (sb.Length > 0 ? 1 : 0);
                if (projected > maxChars && i >= minimumLines) break;
                if (sb.Length > 0) sb.Append(NL);
                sb.Append(line);
            }
            return sb.ToString();
        }

        private static void AppendExamples(StringBuilder sb, ActionConfig config, SystemPromptOptions options)
        {
            ActionDefinition noArg = null;
            ActionDefinition withArg = null;
            for (int i = 0; i < config.validActions.Count; i++)
            {
                ActionDefinition a = config.validActions[i];
                if (a == null || !ActionSchemaBuilder.IsIdentifier(a.actionName)) continue;
                if (!a.TakesArgument && noArg == null) noArg = a;
                if (a.TakesArgument && withArg == null) withArg = a;
            }
            if (noArg == null && withArg == null) return;

            sb.Append("### EXAMPLES").Append(NL);
            if (noArg != null)
            {
                sb.Append(StateHeader).Append(NL);
                sb.Append("Nothing visible.").Append(NL);
                sb.Append(CompletionLead).Append(ExampleJson(noArg, "Nothing relevant in view.", options)).Append(NL);
                sb.Append(NL);
            }
            if (withArg != null)
            {
                string arg = ResolveExampleArgument(withArg, withArg.FirstParameter, options);
                sb.Append(StateHeader).Append(NL);
                sb.Append("- ID: ").Append(arg).Append(" | Type: DynamicObject").Append(NL);
                sb.Append(CompletionLead).Append(ExampleJson(withArg, "A valid target is visible.", options)).Append(NL);
                sb.Append(NL);
            }
        }

        private static string ExampleJson(ActionDefinition action, string reason, SystemPromptOptions options)
        {
            var json = new StringBuilder("{");
            if (options.ReasonMaxChars > 0)
            {
                if (reason.Length > options.ReasonMaxChars) reason = reason.Substring(0, options.ReasonMaxChars);
                json.Append("\"reason\":\"").Append(reason).Append("\",");
            }
            json.Append("\"action\":\"").Append(action.actionName).Append('"');
            foreach (ActionParameter parameter in action.Parameters)
            {
                if (parameter == null || !ActionSchemaBuilder.IsIdentifier(parameter.name)) continue;
                json.Append(",\"").Append(parameter.name).Append("\":\"")
                    .Append(ResolveExampleArgument(action, parameter, options)).Append('"');
            }
            return json.Append('}').ToString();
        }

        // Priority: first argument option (schema and example then agree), then the authored
        // exampleArgument, then a neutral placeholder.
        private static string ResolveExampleArgument(ActionDefinition action, ActionParameter parameter, SystemPromptOptions options)
        {
            List<string> opts = ActionSchemaBuilder.CollectOptions(action, parameter,
                new ActionSchemaBuilder.Options { ArgumentOptionsFor = options.InlineProviderOptions ? options.ArgumentOptionsFor : null });
            if (opts.Count > 0) return opts[0];
            string example = parameter != null ? parameter.exampleValue : null;
            if (parameter != null && parameter.type == ActionParameterType.Int)
                return int.TryParse(example, out int value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "1";
            if (ActionSchemaBuilder.IsIdentifier(example)) return example;
            return "Target";
        }
    }
}
