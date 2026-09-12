using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    public class LlamaRequestWriterTests
    {
        private static LlamaRequestWriter.GenerationSettings Settings()
        {
            return new LlamaRequestWriter.GenerationSettings { MaxTokens = 48, Temperature = 0.2f, TopP = 0.9f, TopK = 20, RepeatPenalty = 1.05f };
        }

        private static LLMRequest Request()
        {
            return new LLMRequest
            {
                SystemPrompt = "You are a \"guard\".\nLine two",
                UserPrompt = "STATE:\n- A",
                CompletionLead = "OUTPUT: ",
                JsonSchema = "{\"oneOf\":[{\"type\":\"object\"}]}",
                MaxTokens = 48
            };
        }

        [Test]
        public void Chat_EmbedsMessagesSchemaAndThinkingOff()
        {
            string body = LlamaRequestWriter.WriteChatCompletion(Request(), Settings(), Request().JsonSchema);

            StringAssert.Contains("{\"messages\":[{\"role\":\"system\",\"content\":\"You are a \\\"guard\\\".\\nLine two\"},{\"role\":\"user\",\"content\":\"STATE:\\n- A\"}]", body);
            StringAssert.Contains("\"cache_prompt\":true", body);
            StringAssert.Contains("\"max_tokens\":48", body);
            StringAssert.Contains("\"temperature\":0.2", body);
            StringAssert.Contains("\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"decision\",\"schema\":{\"oneOf\":[{\"type\":\"object\"}]}}}", body);
            StringAssert.Contains("\"chat_template_kwargs\":{\"enable_thinking\":false}", body);
            Assert.IsFalse(body.Contains("id_slot"), "slot must be omitted when unset");
            Assert.IsFalse(body.Contains("thinking_budget_tokens"));
            Assert.IsFalse(body.Contains("\"stop\""), "chat mode adds no stop sequences by default");
        }

        [Test]
        public void Chat_SlotAndThinkingBudget_WhenRequested()
        {
            LLMRequest r = Request();
            r.SlotId = 3;
            r.EnableThinking = true;
            r.ThinkingBudgetTokens = 64;

            string body = LlamaRequestWriter.WriteChatCompletion(r, Settings(), null);

            StringAssert.Contains("\"id_slot\":3", body);
            StringAssert.Contains("\"enable_thinking\":true", body);
            StringAssert.Contains("\"thinking_budget_tokens\":64", body);
            Assert.IsFalse(body.Contains("response_format"), "no schema when null");
        }

        [Test]
        public void Raw_ConcatenatesPromptWithLeadAndInlinesSchema()
        {
            string body = LlamaRequestWriter.WriteRawCompletion(Request(), Settings(), Request().JsonSchema);

            StringAssert.Contains("{\"prompt\":\"You are a \\\"guard\\\".\\nLine two\\n\\nSTATE:\\n- A\\nOUTPUT: \"", body);
            StringAssert.Contains("\"n_predict\":48", body);
            StringAssert.Contains("\"json_schema\":{\"oneOf\":[{\"type\":\"object\"}]}", body);
            StringAssert.Contains("\"stop\":[\"\\nSTATE:\",\"\\n###\"]", body);
        }

        [Test]
        public void ExtraStops_AreMerged()
        {
            var s = Settings();
            s.ExtraStop = new[] { "User:", "" };
            LLMRequest r = Request();
            r.Stop = new[] { "\nObservation:" };

            string body = LlamaRequestWriter.WriteChatCompletion(r, s, null);

            StringAssert.Contains("\"stop\":[\"\\nObservation:\",\"User:\"]", body);
        }
    }
}
