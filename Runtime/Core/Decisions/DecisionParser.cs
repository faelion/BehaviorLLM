using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BehaviorLLM.Core.Decisions
{
    /// <summary>
    /// Turns raw model output into an action decision. The contract is a single flat JSON object
    /// carrying an optional reason, the action, and one field per parameter the action declares;
    /// everything else the model might wrap around it (native thinking tags, markdown fences,
    /// prose) is stripped first.
    ///
    /// The object is read by hand rather than through <c>JsonUtility</c> because the parameter
    /// names are not known at compile time - they come from the action config - and JsonUtility
    /// can only fill in fields it was declared with. The schema keeps the object flat and its
    /// values strings, so a small reader is enough.
    /// </summary>
    public static class DecisionParser
    {
        public struct Result
        {
            public string Action;
            public string Reason;
            /// <summary>Every value the model supplied, named by the action's parameters.</summary>
            public ActionArguments Arguments;
            /// <summary>Null when parsing succeeded.</summary>
            public string Error;
            public bool Succeeded => Error == null;

            /// <summary>The first value, for the common single-parameter action.</summary>
            public string Argument => Arguments != null ? Arguments.First : string.Empty;
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

            List<string> keys;
            List<string> values;
            string readError;
            if (!TryReadFlatObject(payload, out keys, out values, out readError))
            {
                result.Error = readError;
                return result;
            }

            string action = null;
            string reason = null;
            List<string> argNames = new List<string>();
            List<string> argValues = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], ActionKey, StringComparison.OrdinalIgnoreCase))
                {
                    action = values[i];
                }
                else if (string.Equals(keys[i], ReasonKey, StringComparison.OrdinalIgnoreCase))
                {
                    reason = values[i];
                }
                else
                {
                    // Anything that is neither the action nor its reason is one of the action's
                    // parameters, in the order the model emitted them, which the schema fixes to
                    // the order the action declares.
                    string normalized = NormalizeArgument(values[i]);
                    if (string.IsNullOrEmpty(normalized)) continue;
                    argNames.Add(keys[i]);
                    argValues.Add(normalized);
                }
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                result.Error = "JSON has no `action` field";
                return result;
            }

            result.Action = action.Trim();
            result.Reason = reason != null ? reason.Trim() : null;
            result.Arguments = new ActionArguments(result.Action, argNames, argValues);
            return result;
        }

        public const string ActionKey = "action";
        public const string ReasonKey = "reason";

        /// <summary>
        /// Reads a flat JSON object of string values into parallel key and value lists, preserving
        /// the order the model emitted them. Non-string scalars are carried through as their
        /// literal text, so a model that answers a bare number where the schema asked for a string
        /// still produces a usable argument rather than a parse failure.
        /// </summary>
        public static bool TryReadFlatObject(string json, out List<string> keys, out List<string> values, out string error)
        {
            keys = new List<string>();
            values = new List<string>();
            error = null;

            if (string.IsNullOrWhiteSpace(json)) { error = "empty JSON object"; return false; }

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != OpenBrace) { error = "response is not a JSON object"; return false; }
            i++;

            while (true)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length) { error = "JSON object is not closed"; return false; }
                if (json[i] == CloseBrace) return true;
                if (json[i] == Comma) { i++; continue; }

                if (json[i] != Quote) { error = "expected a field name at position " + i; return false; }
                string key;
                if (!TryReadString(json, ref i, out key)) { error = "unterminated field name"; return false; }

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != Colon) { error = "expected a colon after " + key; return false; }
                i++;
                SkipWhitespace(json, ref i);
                if (i >= json.Length) { error = "missing value for " + key; return false; }

                string value;
                if (json[i] == Quote)
                {
                    if (!TryReadString(json, ref i, out value)) { error = "unterminated value for " + key; return false; }
                }
                else
                {
                    int start = i;
                    while (i < json.Length && json[i] != Comma && json[i] != CloseBrace) i++;
                    value = json.Substring(start, i - start).Trim();
                }

                keys.Add(key);
                values.Add(value);
            }
        }

        private const char OpenBrace = '{';
        private const char CloseBrace = '}';
        private const char Comma = ',';
        private const char Colon = ':';
        private const char Quote = '"';
        private const char Backslash = '\\';

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        /// <summary>Reads one JSON string literal, resolving the escapes a model actually emits.</summary>
        private static bool TryReadString(string s, ref int i, out string value)
        {
            value = null;
            if (i >= s.Length || s[i] != Quote) return false;
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == Quote) { value = sb.ToString(); return true; }
                if (c != Backslash) { sb.Append(c); continue; }
                if (i >= s.Length) return false;

                char esc = s[i++];
                switch (esc)
                {
                    case 'n': sb.Append(NewLineChar); break;
                    case 't': sb.Append(TabChar); break;
                    case 'r': sb.Append(ReturnChar); break;
                    case 'u':
                        ushort code;
                        if (i + 4 <= s.Length &&
                            ushort.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber,
                                            System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(esc); break;   // covers an escaped quote, slash or backslash
                }
            }
            return false;
        }

        private const char NewLineChar = '\n';
        private const char TabChar = '\t';
        private const char ReturnChar = '\r';

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
