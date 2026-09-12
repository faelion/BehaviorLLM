using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// Turns raw model output into an action decision. The contract is a single JSON object
    /// <c>{"reason"?: "...", "action": "...", "arg": "..."}</c>; everything else the model
    /// might wrap around it (native thinking tags, markdown fences, prose) is stripped first.
    /// </summary>
    public static class DecisionParser
    {
        public struct Result
        {
            public string Action;
            public string Argument;
            public string Reason;
            /// <summary>Null when parsing succeeded.</summary>
            public string Error;
            public bool Succeeded => Error == null;
        }

        [Serializable]
        private class JsonDecision
        {
            public string reason;
            public string action;
            public string arg;
        }

        // Two families of native-thinking markers are in use in 2026: the `<think>` tag
        // (Qwen, DeepSeek, Nemotron) and Gemma 4's `<|channel>thought ... <channel|>` block.
        // Both are removed whether or not they are terminated, since a truncated thinking
        // block is a common failure when the token budget is tight.
        private static readonly Regex ThinkBlock = new Regex(@"<think\b[^>]*>[\s\S]*?(</think>|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ChannelBlock = new Regex(@"<\|channel>[\s\S]*?(<channel\|>|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StrayCloseTags = new Regex(@"</think>|<channel\|>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Result Parse(string rawResponse)
        {
            Result result = new Result();
            string clean = Sanitize(rawResponse);

            if (string.IsNullOrWhiteSpace(clean))
            {
                result.Error = "empty response";
                return result;
            }

            string payload;
            string extractError;
            if (!TryExtractJsonObject(clean, out payload, out extractError))
            {
                result.Error = extractError;
                return result;
            }

            JsonDecision decision;
            try
            {
                decision = JsonUtility.FromJson<JsonDecision>(payload);
            }
            catch (Exception e)
            {
                result.Error = "invalid JSON: " + e.Message;
                return result;
            }

            if (decision == null || string.IsNullOrWhiteSpace(decision.action))
            {
                result.Error = "JSON has no `action` field";
                return result;
            }

            result.Action = decision.action.Trim();
            result.Argument = NormalizeArgument(decision.arg);
            result.Reason = decision.reason != null ? decision.reason.Trim() : null;
            return result;
        }

        /// <summary>Removes thinking blocks and markdown fences.</summary>
        public static string Sanitize(string response)
        {
            if (string.IsNullOrEmpty(response)) return string.Empty;
            string clean = ThinkBlock.Replace(response, string.Empty);
            clean = ChannelBlock.Replace(clean, string.Empty);
            clean = StrayCloseTags.Replace(clean, string.Empty);
            clean = clean.Replace("```json", string.Empty).Replace("```", string.Empty);
            return clean.Trim();
        }

        /// <summary>
        /// Returns the first balanced <c>{...}</c> substring so a prose preamble ("Sure: {...}")
        /// does not break deserialisation. Braces inside JSON strings are handled.
        /// </summary>
        public static bool TryExtractJsonObject(string input, out string json, out string error)
        {
            json = null;
            error = "no JSON object found";
            if (string.IsNullOrWhiteSpace(input)) { error = "empty response"; return false; }

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { if (depth > 0) inString = true; continue; }
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    if (depth == 0)
                    {
                        json = input.Substring(start, i - start + 1);
                        return true;
                    }
                }
            }

            if (depth > 0) error = "unterminated JSON object";
            return false;
        }

        /// <summary>Trims whitespace and one layer of surrounding quotes.</summary>
        public static string NormalizeArgument(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string clean = raw.Trim();
            if (clean.Length >= 2 &&
                ((clean[0] == '"' && clean[clean.Length - 1] == '"') ||
                 (clean[0] == '\'' && clean[clean.Length - 1] == '\'')))
            {
                clean = clean.Substring(1, clean.Length - 2);
            }
            return clean.Trim();
        }
    }
}
