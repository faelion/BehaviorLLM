using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// Some models ignore the request to stop thinking, because that request is a chat-template
    /// variable and their template is out of date. They then spend the whole token budget
    /// reasoning and return nothing to act on. The client answers by moving to raw completion,
    /// where no template runs and the schema constrains the reply from its first token.
    ///
    /// The detection has to be narrow. Switching transport on the wrong failure would hide a
    /// server problem behind a silent retry, so these tests pin what does and does not count.
    ///
    /// Reproduced on Gemma 4 E4B, whose server logs "detected an outdated gemma4 chat template":
    /// 333 tokens and 76 s to reach an answer over chat completions, against 7-16 tokens and
    /// under 8 s for the same prompt and schema over raw completion.
    /// </summary>
    public class ThinkingTransportSwitchTests
    {
        private static LLMResponse Reply(string text, int completionTokens, string reasoning = null)
        {
            return new LLMResponse
            {
                Text = text,
                ReasoningText = reasoning,
                CompletionTokens = completionTokens
            };
        }

        [Test]
        public void TokensSpentAndNothingSaid_IsTheSignature()
        {
            Assert.IsTrue(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions,
                Reply(string.Empty, 160, "The user has provided the current state...")));
        }

        [Test]
        public void WhitespaceCountsAsNothingSaid()
        {
            Assert.IsTrue(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions, Reply("   \n", 50)));
        }

        [Test]
        public void NoTokensAtAll_IsAServerProblem_NotAThinkingOne()
        {
            // Switching here would retry - and probably hide - a real backend failure.
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions, Reply(string.Empty, 0)));
        }

        [Test]
        public void AnAnswerThatArrived_IsNeverASwitch()
        {
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions,
                Reply("{\"action\":\"Patrol\",\"arg\":\"Route_North\"}", 16)));
        }

        [Test]
        public void ABadAnswerIsStillAnAnswer()
        {
            // The model replying with prose is a prompting problem. Raw completion does not fix
            // it, so this must go through the parser and the fallback like any other bad decision.
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions, Reply("I think I should patrol.", 9)));
        }

        [Test]
        public void AlreadyOnRawCompletion_HasNowhereToSwitchTo()
        {
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.RawCompletion, Reply(string.Empty, 160, "thinking...")));
        }

        [Test]
        public void AFailedResponseIsNotASwitch()
        {
            LLMResponse failed = LLMResponse.Failed("connection refused");
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions, failed));
        }

        [Test]
        public void NullIsHandled()
        {
            Assert.IsFalse(BehaviorLLMClient.IsThinkingWithoutAnswering(
                BackendTransport.ChatCompletions, null));
        }
    }
}
