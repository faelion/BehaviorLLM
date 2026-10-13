using System;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// Turns an action config into the closed list of complete decisions a decision model chooses
    /// between: one option per action and legal combination of its values.
    ///
    /// A generative model fills in an action and then each of its parameters, and the grammar keeps
    /// every step legal. A decision model does not write, so the whole decision has to be one of
    /// the options it is shown. <c>Patrol</c> with two routes therefore becomes two options,
    /// <c>Patrol Route_North</c> and <c>Patrol Route_South</c>, and the probability the model gives
    /// each is the probability of that whole decision.
    ///
    /// The values come from exactly where the schema gets them, <see cref="ActionSchemaBuilder.CollectOptions"/>
    /// and the same availability filter, so the options are by construction the decisions the
    /// generative paradigm's grammar would have allowed. A parameter with no list of values (any
    /// identifier, any integer) cannot be offered as a finite set; its action is left out and
    /// reported in <see cref="Result.UnenumerableActions"/>.
    /// </summary>
    public static class DecisionCandidates
    {
        /// <summary>One complete decision offered to the model.</summary>
        public sealed class Candidate
        {
            /// <summary>What the model reads as the option, and the key its probability comes back
            /// under: the action name, then its values separated by spaces, or as <c>name=value</c>
            /// pairs when the action takes more than one.</summary>
            public string Key;

            /// <summary>The action's description, read by the model beside the key.</summary>
            public string Description;

            /// <summary>The action, as named in the config.</summary>
            public string Action;

            /// <summary>The values to dispatch with, named by the action's parameters.</summary>
            public ActionArguments Arguments;
        }

        public sealed class Options
        {
            /// <summary>Live values per parameter, as the decision maker's provider supplies them.
            /// Null uses the authored lists only.</summary>
            public Func<ActionDefinition, ActionParameter, IList<string>> ArgumentOptionsFor;

            /// <summary>Drops actions the game does not allow this turn. Null allows every action.</summary>
            public Func<ActionDefinition, bool> IsActionAvailable;

            /// <summary>The most options to produce; the model's own limit. 0 or less means no limit.</summary>
            public int MaxCandidates;
        }

        public sealed class Result
        {
            public readonly List<Candidate> Candidates = new List<Candidate>();

            /// <summary>Actions left out because a parameter has no finite list of values.</summary>
            public readonly List<string> UnenumerableActions = new List<string>();

            /// <summary>True when <see cref="Options.MaxCandidates"/> cut the list short.</summary>
            public bool Truncated;

            /// <summary>The candidate with this key, or null.</summary>
            public Candidate Find(string key)
            {
                if (key == null) return null;
                for (int i = 0; i < Candidates.Count; i++)
                {
                    if (string.Equals(Candidates[i].Key, key, StringComparison.Ordinal)) return Candidates[i];
                }
                return null;
            }
        }

        /// <summary>Every decision the config allows right now, in config order.</summary>
        public static Result Enumerate(ActionConfig config, Options options = null)
        {
            options = options ?? new Options();
            Result result = new Result();
            if (config == null || config.validActions == null) return result;

            ActionSchemaBuilder.Options valueSource = new ActionSchemaBuilder.Options
            {
                ArgumentOptionsFor = options.ArgumentOptionsFor
            };
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

            for (int a = 0; a < config.validActions.Count; a++)
            {
                ActionDefinition action = config.validActions[a];
                if (action == null || !ActionSchemaBuilder.IsIdentifier(action.actionName)) continue;
                if (options.IsActionAvailable != null && !options.IsActionAvailable(action)) continue;

                List<ActionParameter> parameters = action.Parameters;
                List<List<string>> values = new List<List<string>>(parameters.Count);
                bool enumerable = true;
                for (int p = 0; p < parameters.Count; p++)
                {
                    ActionParameter parameter = parameters[p];
                    if (parameter == null || !ActionSchemaBuilder.IsIdentifier(parameter.name)) { values.Add(null); continue; }
                    List<string> legal = ActionSchemaBuilder.CollectOptions(action, parameter, valueSource);
                    if (legal.Count == 0) { enumerable = false; break; }
                    values.Add(legal);
                }

                if (!enumerable)
                {
                    result.UnenumerableActions.Add(action.actionName);
                    continue;
                }

                if (!AddCombinations(result, keys, action, parameters, values, options.MaxCandidates)) break;
            }
            return result;
        }

        // Walks the cartesian product of the parameters' values like an odometer, last parameter
        // fastest, so the options for one action stay together and in authored order.
        private static bool AddCombinations(Result result, HashSet<string> keys, ActionDefinition action,
                                            List<ActionParameter> parameters, List<List<string>> values, int max)
        {
            int[] index = new int[values.Count];
            while (true)
            {
                if (max > 0 && result.Candidates.Count >= max)
                {
                    result.Truncated = true;
                    return false;
                }

                Candidate candidate = Build(action, parameters, values, index);
                if (keys.Add(candidate.Key)) result.Candidates.Add(candidate);

                int digit = values.Count - 1;
                while (digit >= 0)
                {
                    if (values[digit] == null) { digit--; continue; }
                    index[digit]++;
                    if (index[digit] < values[digit].Count) break;
                    index[digit] = 0;
                    digit--;
                }
                if (digit < 0) return true;
            }
        }

        private static Candidate Build(ActionDefinition action, List<ActionParameter> parameters,
                                       List<List<string>> values, int[] index)
        {
            List<string> names = new List<string>();
            List<string> chosen = new List<string>();
            for (int p = 0; p < values.Count; p++)
            {
                if (values[p] == null) continue;
                names.Add(parameters[p].name);
                chosen.Add(values[p][index[p]]);
            }

            StringBuilder key = new StringBuilder(action.actionName);
            for (int i = 0; i < chosen.Count; i++)
            {
                key.Append(' ');
                if (chosen.Count > 1) key.Append(names[i]).Append('=');
                key.Append(chosen[i]);
            }

            return new Candidate
            {
                Key = key.ToString(),
                Description = string.IsNullOrWhiteSpace(action.description) ? null : action.description.Trim(),
                Action = action.actionName,
                Arguments = new ActionArguments(action.actionName, names, chosen)
            };
        }
    }
}
