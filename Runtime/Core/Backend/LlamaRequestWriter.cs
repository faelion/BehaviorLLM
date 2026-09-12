using System.Globalization;
using System.Text;
using BehaviorLLM.Core.Interfaces;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// Serialises an <see cref="LLMRequest"/> into the JSON body llama-server expects.
    ///
    /// The body is written by hand rather than through JsonUtility because the JSON Schema
    /// must be embedded as a nested object (JsonUtility would have to stringify it) and
    /// because empty optional fields must be omitted, not sent as <c>""</c> or <c>0</c>.
    /// </summary>
    public static class LlamaRequestWriter
    {
        public struct GenerationSettings
        {
            public int MaxTokens;
            public float Temperature;
            public float TopP;
            public int TopK;
            public float RepeatPenalty;
            public string[] ExtraStop;
        }

        // Stop sequences for the raw completion endpoint only: a new STATE/section header means
        // the model is hallucinating the next turn. Chat mode relies on the template's EOS.
        private static readonly string[] RawModeStops = { "\nSTATE:", "\n###" };

        /// <summary>Body for <c>POST /v1/chat/completions</c> (OpenAI-compatible).</summary>
        public static string WriteChatCompletion(LLMRequest request, GenerationSettings settings, string jsonSchema)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(1024 + Length(request.SystemPrompt) + Length(request.UserPrompt));
            sb.Append("{\"messages\":[");
            bool first = true;
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":");
                AppendJsonString(sb, request.SystemPrompt);
                sb.Append('}');
                first = false;
            }
            if (!first) sb.Append(',');
            sb.Append("{\"role\":\"user\",\"content\":");
            AppendJsonString(sb, request.UserPrompt ?? string.Empty);
            sb.Append("}]");

            sb.Append(",\"stream\":false,\"cache_prompt\":true");
            if (settings.MaxTokens > 0) sb.Append(",\"max_tokens\":").Append(settings.MaxTokens.ToString(inv));
            AppendSampling(sb, settings, inv);
            if (request.SlotId >= 0) sb.Append(",\"id_slot\":").Append(request.SlotId.ToString(inv));
            AppendStops(sb, request.Stop, settings.ExtraStop, null);

            if (!string.IsNullOrEmpty(jsonSchema))
            {
                sb.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":");
                AppendJsonString(sb, string.IsNullOrEmpty(request.SchemaName) ? "decision" : request.SchemaName);
                sb.Append(",\"schema\":").Append(jsonSchema).Append("}}");
            }

            // Thinking is decided per request, never server-wide: reactive decision makers must not pay
            // for a thinking phase, deliberative ones get a bounded one.
            sb.Append(",\"chat_template_kwargs\":{\"enable_thinking\":").Append(request.EnableThinking ? "true" : "false").Append('}');
            if (request.EnableThinking && request.ThinkingBudgetTokens >= 0)
                sb.Append(",\"thinking_budget_tokens\":").Append(request.ThinkingBudgetTokens.ToString(inv));

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Body for llama-server's native <c>POST /completion</c> (raw prompt, no chat template).</summary>
        public static string WriteRawCompletion(LLMRequest request, GenerationSettings settings, string jsonSchema)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string prompt = BuildRawPrompt(request);
            StringBuilder sb = new StringBuilder(512 + prompt.Length);
            sb.Append("{\"prompt\":");
            AppendJsonString(sb, prompt);
            sb.Append(",\"cache_prompt\":true");
            if (settings.MaxTokens > 0) sb.Append(",\"n_predict\":").Append(settings.MaxTokens.ToString(inv));
            AppendSampling(sb, settings, inv);
            if (request.SlotId >= 0) sb.Append(",\"id_slot\":").Append(request.SlotId.ToString(inv));
            AppendStops(sb, request.Stop, settings.ExtraStop, RawModeStops);
            if (!string.IsNullOrEmpty(jsonSchema)) sb.Append(",\"json_schema\":").Append(jsonSchema);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>System prompt, blank line, state prompt, newline, completion lead.</summary>
        public static string BuildRawPrompt(LLMRequest request)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(request.SystemPrompt)) sb.Append(request.SystemPrompt.TrimEnd()).Append("\n\n");
            sb.Append(request.UserPrompt ?? string.Empty);
            if (!string.IsNullOrEmpty(request.CompletionLead)) sb.Append('\n').Append(request.CompletionLead);
            return sb.ToString();
        }

        private static void AppendSampling(StringBuilder sb, GenerationSettings s, CultureInfo inv)
        {
            sb.Append(",\"temperature\":").Append(s.Temperature.ToString(inv));
            if (s.TopP > 0f) sb.Append(",\"top_p\":").Append(s.TopP.ToString(inv));
            if (s.TopK > 0) sb.Append(",\"top_k\":").Append(s.TopK.ToString(inv));
            if (s.RepeatPenalty > 0f) sb.Append(",\"repeat_penalty\":").Append(s.RepeatPenalty.ToString(inv));
        }

        private static void AppendStops(StringBuilder sb, string[] a, string[] b, string[] c)
        {
            int count = Count(a) + Count(b) + Count(c);
            if (count == 0) return;
            sb.Append(",\"stop\":[");
            bool first = true;
            AppendStopArray(sb, a, ref first);
            AppendStopArray(sb, b, ref first);
            AppendStopArray(sb, c, ref first);
            sb.Append(']');
        }

        private static void AppendStopArray(StringBuilder sb, string[] arr, ref bool first)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
            {
                if (string.IsNullOrEmpty(arr[i])) continue;
                if (!first) sb.Append(',');
                AppendJsonString(sb, arr[i]);
                first = false;
            }
        }

        private static int Count(string[] arr)
        {
            if (arr == null) return 0;
            int n = 0;
            for (int i = 0; i < arr.Length; i++) if (!string.IsNullOrEmpty(arr[i])) n++;
            return n;
        }

        private static int Length(string s) => s == null ? 0 : s.Length;

        public static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)c);
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
        }
    }
}
