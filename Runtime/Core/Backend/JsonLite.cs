using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// A small JSON reader for the one response shape <c>JsonUtility</c> cannot map: objects whose
    /// keys are data. A decision model answers with <c>{"answers":{"&lt;question id&gt;":{
    /// "probabilities":{"&lt;option&gt;":0.7, ...}}}}</c>, and both levels of keys are chosen by the
    /// caller at runtime, which a <c>[Serializable]</c> class cannot declare.
    ///
    /// Objects are read into ordered key-value lists rather than dictionaries because the order is
    /// meaningful here: the server lists probabilities in the order the options were asked.
    /// Values come back as <c>string</c>, <c>double</c>, <c>bool</c>, <c>null</c>,
    /// <c>List&lt;object&gt;</c> or <c>List&lt;KeyValuePair&lt;string, object&gt;&gt;</c>.
    /// </summary>
    internal static class JsonLite
    {
        /// <summary>Parses a whole document. False, with a reason, on anything malformed.</summary>
        public static bool TryParse(string json, out object value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty document"; return false; }

            int i = 0;
            try
            {
                value = ReadValue(json, ref i);
                SkipWhitespace(json, ref i);
                if (i != json.Length) { error = $"unexpected text at {i}"; value = null; return false; }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                value = null;
                return false;
            }
        }

        /// <summary>The value under <paramref name="key"/> in an object, or null when absent or not an object.</summary>
        public static object Get(object node, string key)
        {
            if (node is List<KeyValuePair<string, object>> obj)
            {
                for (int i = 0; i < obj.Count; i++)
                {
                    if (string.Equals(obj[i].Key, key, StringComparison.Ordinal)) return obj[i].Value;
                }
            }
            return null;
        }

        /// <summary>The object's members in document order, or null when the node is not an object.</summary>
        public static List<KeyValuePair<string, object>> AsObject(object node) =>
            node as List<KeyValuePair<string, object>>;

        public static string AsString(object node) => node as string;

        public static float AsFloat(object node, float fallback = 0f) =>
            node is double d ? (float)d : fallback;

        public static int AsInt(object node, int fallback = 0) =>
            node is double d ? (int)Math.Round(d) : fallback;

        // ------------------------------------------------------------------ reading

        private static object ReadValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of document");

            char c = s[i];
            switch (c)
            {
                case '{': return ReadObject(s, ref i);
                case '[': return ReadArray(s, ref i);
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i);
                    throw new FormatException($"unexpected '{c}' at {i}");
            }
        }

        private static List<KeyValuePair<string, object>> ReadObject(string s, ref int i)
        {
            List<KeyValuePair<string, object>> members = new List<KeyValuePair<string, object>>();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return members; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException($"expected a key at {i}");
                string key = ReadString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
                i++;
                members.Add(new KeyValuePair<string, object>(key, ReadValue(s, ref i)));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return members; }
                throw new FormatException($"expected ',' or '}}' at {i}");
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            List<object> items = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return items; }

            while (true)
            {
                items.Add(ReadValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return items; }
                throw new FormatException($"expected ',' or ']' at {i}");
            }
        }

        private static string ReadString(string s, ref int i)
        {
            i++; // opening quote
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;

                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("truncated \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"bad escape '\\{e}'");
                }
            }
            throw new FormatException("unterminated string");
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"bad number at {start}");
            return value;
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException($"expected '{literal}' at {i}");
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
