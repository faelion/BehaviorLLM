using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    public class ClientResponseParsingTests
    {
        [Test]
        public void ChatCompletion_ContentReasoningAndUsage()
        {
            string body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"action\\\":\\\"Stop\\\",\\\"arg\\\":\\\"\\\"}\",\"reasoning_content\":\"why\"},\"finish_reason\":\"stop\"}]," +
                          "\"usage\":{\"prompt_tokens\":120,\"completion_tokens\":12},\"timings\":{\"prompt_n\":120,\"predicted_n\":12,\"cache_n\":100}}";

            LLMResponse r = BehaviorLLMClient.ParseResponseBody(body, 42f);

            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual("{\"action\":\"Stop\",\"arg\":\"\"}", r.Text);
            Assert.AreEqual("why", r.ReasoningText);
            Assert.AreEqual(120, r.PromptTokens);
            Assert.AreEqual(12, r.CompletionTokens);
            Assert.AreEqual(100, r.CachedTokens);
            Assert.AreEqual(42f, r.LatencyMs);
        }

        [Test]
        public void RawCompletion_ContentAndCachedTokens()
        {
            string body = "{\"content\":\"{\\\"action\\\":\\\"Attack\\\",\\\"arg\\\":\\\"Orc\\\"}\",\"tokens_cached\":80,\"timings\":{\"prompt_n\":90,\"predicted_n\":10}}";

            LLMResponse r = BehaviorLLMClient.ParseResponseBody(body, 1f);

            Assert.AreEqual("{\"action\":\"Attack\",\"arg\":\"Orc\"}", r.Text);
            Assert.AreEqual(90, r.PromptTokens);
            Assert.AreEqual(10, r.CompletionTokens);
            Assert.AreEqual(80, r.CachedTokens);
        }

        [Test]
        public void NonJsonBody_IsReturnedAsText()
        {
            LLMResponse r = BehaviorLLMClient.ParseResponseBody("plain text", 1f);
            Assert.AreEqual("plain text", r.Text);
        }

        [Test]
        public void EmptyBody_IsFailure()
        {
            Assert.IsFalse(BehaviorLLMClient.ParseResponseBody("", 1f).Succeeded);
        }

        [Test]
        public void NormalizeBaseUrl_StripsLegacyEndpointPaths()
        {
            Assert.AreEqual("http://localhost:8080", BehaviorLLMClient.NormalizeBaseUrl("http://localhost:8080/completion"));
            Assert.AreEqual("http://localhost:8080", BehaviorLLMClient.NormalizeBaseUrl("http://localhost:8080/v1/chat/completions/"));
            Assert.AreEqual("http://host:1234", BehaviorLLMClient.NormalizeBaseUrl("http://host:1234"));
        }
    }
}
