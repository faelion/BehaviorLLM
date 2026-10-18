using System;
using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Interfaces;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// Writes and reads the System One wire format: TypeSafe's API for Jev, which llama-server
    /// implements as <c>POST /v1/systemone</c> for the open decision models.
    ///
    /// Request: <c>{"state": "...", "questions": {"&lt;id&gt;": {"type": "choice",
    /// "instructions": "...", "criteria": {"&lt;option&gt;": "&lt;description&gt;" | null}}}}</c>. A score
    /// question's criteria is an array of level descriptions; a yes/no question (<c>noul</c>) has none.
    ///
    /// Both directions are hand-rolled for the same reason as <see cref="LlamaRequestWriter"/>:
    /// <c>questions</c> and <c>criteria</c> are objects keyed by data, which <c>JsonUtility</c> can
    /// neither write nor read.
    /// </summary>
    public static class SystemOneProtocol
    {
        /// <summary>The endpoint path, relative to the server's base address.</summary>
        public const string EndpointPath = "/v1/systemone";

        /// <summary>The request body for <paramref name="request"/>.</summary>
        public static string WriteRequest(DecisionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            StringBuilder sb = new StringBuilder(512 + (request.State != null ? request.State.Length : 0));
            sb.Append("{\"state\":");
            LlamaRequestWriter.AppendJsonString(sb, request.State ?? string.Empty);
            if (!string.IsNullOrEmpty(request.Model))
            {
                sb.Append(",\"model\":");
                LlamaRequestWriter.AppendJsonString(sb, request.Model);
            }

            sb.Append(",\"questions\":{");
            bool first = true;
            if (request.Questions != null)
            {
                for (int q = 0; q < request.Questions.Count; q++)
                {
                    DecisionQuestion question = request.Questions[q];
                    if (question == null || string.IsNullOrEmpty(question.Id)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    AppendQuestion(sb, question);
                }
            }
            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendQuestion(StringBuilder sb, DecisionQuestion question)
        {
            LlamaRequestWriter.AppendJsonString(sb, question.Id);
            sb.Append(":{\"type\":\"").Append(WireType(question.Type)).Append('"');
            sb.Append(",\"instructions\":");
            LlamaRequestWriter.AppendJsonString(sb, question.Instructions ?? string.Empty);

            List<DecisionOption> options = question.Options;
            bool hasOptions = options != null && options.Count > 0;
            switch (question.Type)
            {
                case DecisionQuestionType.Choice:
                    sb.Append(",\"criteria\":{");
                    if (hasOptions)
                    {
                        bool first = true;
                        for (int i = 0; i < options.Count; i++)
                        {
                            DecisionOption option = options[i];
                            if (option == null || string.IsNullOrEmpty(option.Key)) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            LlamaRequestWriter.AppendJsonString(sb, option.Key);
                            sb.Append(':');
                            if (string.IsNullOrEmpty(option.Description)) sb.Append("null");
                            else LlamaRequestWriter.AppendJsonString(sb, option.Description);
                        }
                    }
                    sb.Append('}');
                    break;

                case DecisionQuestionType.Score:
                    sb.Append(",\"criteria\":[");
                    if (hasOptions)
                    {
                        for (int i = 0; i < options.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            DecisionOption level = options[i];
                            string text = level == null ? string.Empty
                                : !string.IsNullOrEmpty(level.Description) ? level.Description : level.Key;
                            LlamaRequestWriter.AppendJsonString(sb, text ?? string.Empty);
                        }
                    }
                    sb.Append(']');
                    break;

                case DecisionQuestionType.YesNo:
                    // Optional descriptions of what yes and no mean, as the two options "true"
                    // and "false"; omitted when the question needs none.
                    if (hasOptions)
                    {
                        sb.Append(",\"criteria\":{");
                        bool first = true;
                        for (int i = 0; i < options.Count; i++)
                        {
                            DecisionOption option = options[i];
                            if (option == null || (option.Key != "true" && option.Key != "false")) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            LlamaRequestWriter.AppendJsonString(sb, option.Key);
                            sb.Append(':');
                            LlamaRequestWriter.AppendJsonString(sb, option.Description ?? string.Empty);
                        }
                        sb.Append('}');
                    }
                    break;
            }
            sb.Append('}');
        }

        /// <summary>Path of the plain completion endpoint the priming request goes to.</summary>
        public const string PrimingEndpointPath = "/completion";

        /// <summary>
        /// The body that has llama-server read <paramref name="head"/> followed by
        /// <paramref name="constantPrefix"/> and keep the result.
        ///
        /// Why a completion and not a decision: llama-server lets a later request resume from a
        /// saved point only where it created one, and it creates them while reading the prompt of a
        /// completion, never of a decision. A model like LiquidAI d1 keeps a running state that
        /// cannot be rewound, so without a saved point every decision rereads its whole prompt.
        /// This request is that prompt's opening, exactly as the decision template renders it (the
        /// template's head, then the state's constant start); the server saves a point a few tokens
        /// before its end, and each later decision of the same character, whose prompt starts the
        /// same way, resumes there. Nothing is generated: the answer is not read.
        /// </summary>
        public static string WritePrimingRequest(string head, string constantPrefix)
        {
            StringBuilder sb = new StringBuilder(64 + (head?.Length ?? 0) + (constantPrefix?.Length ?? 0));
            sb.Append("{\"prompt\":");
            LlamaRequestWriter.AppendJsonString(sb, (head ?? string.Empty) + (constantPrefix ?? string.Empty));
            sb.Append(",\"n_predict\":0,\"cache_prompt\":true}");
            return sb.ToString();
        }

        /// <summary>The name a question type has on the wire.</summary>
        public static string WireType(DecisionQuestionType type)
        {
            switch (type)
            {
                case DecisionQuestionType.Score: return "score";
                case DecisionQuestionType.YesNo: return "noul";
                default: return "choice";
            }
        }

        /// <summary>
        /// Reads a success body into a response. A body that is not the expected shape comes back
        /// as a failed response saying why, never as an exception.
        /// </summary>
        public static DecisionResponse ParseResponse(string raw, float latencyMs)
        {
            if (!JsonLite.TryParse(raw, out object root, out string error))
                return DecisionResponse.Failed($"Unreadable decision-model response ({error}).", latencyMs);

            string serverError = ReadErrorMessage(root);
            if (serverError != null) return DecisionResponse.Failed(serverError, latencyMs);

            List<KeyValuePair<string, object>> answers = JsonLite.AsObject(JsonLite.Get(root, "answers"));
            if (answers == null)
                return DecisionResponse.Failed("Decision-model response has no 'answers' object.", latencyMs);

            DecisionResponse response = new DecisionResponse
            {
                LatencyMs = latencyMs,
                ModelName = JsonLite.AsString(JsonLite.Get(root, "model")),
                InputTokens = JsonLite.AsInt(JsonLite.Get(JsonLite.Get(root, "usage"), "input_tokens"))
            };

            for (int i = 0; i < answers.Count; i++)
            {
                object node = answers[i].Value;
                DecisionAnswer answer = new DecisionAnswer
                {
                    Id = answers[i].Key,
                    Type = ReadType(JsonLite.AsString(JsonLite.Get(node, "type"))),
                    Choice = JsonLite.AsString(JsonLite.Get(node, "choice")),
                    Score = JsonLite.AsFloat(JsonLite.Get(node, "score")),
                    Probability = JsonLite.AsFloat(JsonLite.Get(node, "noul")),
                    Confidence = JsonLite.AsFloat(JsonLite.Get(node, "confidence"))
                };

                List<KeyValuePair<string, object>> probabilities = JsonLite.AsObject(JsonLite.Get(node, "probabilities"));
                if (probabilities != null)
                {
                    for (int p = 0; p < probabilities.Count; p++)
                        answer.Probabilities.Add(new KeyValuePair<string, float>(probabilities[p].Key, JsonLite.AsFloat(probabilities[p].Value)));
                }
                response.Answers.Add(answer);
            }
            return response;
        }

        /// <summary>
        /// The message in an OpenAI-style error body (<c>{"error":{"message":"..."}}</c>), which both
        /// llama-server and TypeSafe send with a failure status. Null when the body is not one.
        /// </summary>
        public static string ReadErrorMessage(string raw)
        {
            return JsonLite.TryParse(raw, out object root, out _) ? ReadErrorMessage(root) : null;
        }

        private static string ReadErrorMessage(object root)
        {
            object error = JsonLite.Get(root, "error");
            if (error == null) return null;
            if (error is string text) return text;
            return JsonLite.AsString(JsonLite.Get(error, "message")) ?? "the server reported an error";
        }

        private static DecisionQuestionType ReadType(string wire)
        {
            switch (wire)
            {
                case "score": return DecisionQuestionType.Score;
                case "noul": return DecisionQuestionType.YesNo;
                default: return DecisionQuestionType.Choice;
            }
        }
    }
}
