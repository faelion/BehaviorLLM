using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The System One wire format, both directions. The response fixtures are bodies llama-server
    /// b11429 actually returned for Kev-4B on 2026-10-13, so a change in how they are read shows up
    /// here rather than as every decision falling back in a scene.
    /// </summary>
    public class SystemOneProtocolTests
    {
        private const string KevResponse =
            "{\"model\":\"D:/models/Kev-4B-Q4_K_M.gguf\",\"answers\":{" +
            "\"decision\":{\"type\":\"choice\",\"choice\":\"Alarm\",\"probabilities\":{\"Patrol Route_North\":0.03286009559813383," +
            "\"Patrol Route_South\":0.04962357767127666,\"Chase Intruder_01\":0.13992990851497583,\"Alarm\":0.5640951937557869," +
            "\"HoldPosition\":0.21349122445982674},\"confidence\":0.45511899219473356}," +
            "\"alarming\":{\"type\":\"noul\",\"noul\":0.5836988495831169}," +
            "\"threat\":{\"type\":\"score\",\"score\":1.6478637665376026,\"legend\":{\"0\":\"none\",\"1\":\"low\",\"2\":\"high\",\"3\":\"critical\"}," +
            "\"probabilities\":{\"0\":0.21023495318302507,\"1\":0.16522277877783728,\"2\":0.3909858163576478,\"3\":0.2335564516814899}," +
            "\"confidence\":0.18075086317462274}},\"usage\":{\"input_tokens\":291,\"output_tokens\":0}}";

        [Test]
        public void ChoiceQuestion_WritesCriteriaAsAnObjectKeyedByOption()
        {
            DecisionRequest request = new DecisionRequest { State = "STATE:\nAn intruder." };
            DecisionQuestion question = new DecisionQuestion { Id = "action", Type = DecisionQuestionType.Choice, Instructions = "Which action?" };
            question.Options.Add(new DecisionOption("Patrol Route_North", "Walk a route."));
            question.Options.Add(new DecisionOption("HoldPosition"));
            request.Questions.Add(question);

            string body = SystemOneProtocol.WriteRequest(request);

            Assert.AreEqual(
                "{\"state\":\"STATE:\\nAn intruder.\",\"questions\":{\"action\":{\"type\":\"choice\",\"instructions\":\"Which action?\"," +
                "\"criteria\":{\"Patrol Route_North\":\"Walk a route.\",\"HoldPosition\":null}}}}",
                body);
        }

        [Test]
        public void ScoreAndYesNoQuestions_UseTheirWireShapes()
        {
            DecisionRequest request = new DecisionRequest { State = "s", Model = "jev-latest" };
            DecisionQuestion score = new DecisionQuestion { Id = "threat", Type = DecisionQuestionType.Score, Instructions = "How bad?" };
            score.Options.Add(new DecisionOption("none"));
            score.Options.Add(new DecisionOption("0", "high"));
            request.Questions.Add(score);
            request.Questions.Add(new DecisionQuestion { Id = "angry", Type = DecisionQuestionType.YesNo, Instructions = "Angry?" });

            string body = SystemOneProtocol.WriteRequest(request);

            StringAssert.Contains("\"model\":\"jev-latest\"", body);
            StringAssert.Contains("\"threat\":{\"type\":\"score\",\"instructions\":\"How bad?\",\"criteria\":[\"none\",\"high\"]}", body);
            StringAssert.Contains("\"angry\":{\"type\":\"noul\",\"instructions\":\"Angry?\"}", body);
        }

        [Test]
        public void PrimingRequest_IsAPromptOnlyCompletionOfTheHeadAndPrefix()
        {
            string body = SystemOneProtocol.WritePrimingRequest("<|startoftext|><|im_start|>user\n", "You are \"Guard_01\".\n");

            Assert.AreEqual("{\"prompt\":\"<|startoftext|><|im_start|>user\\nYou are \\\"Guard_01\\\".\\n\",\"n_predict\":0,\"cache_prompt\":true}", body);
            Assert.AreEqual("/completion", SystemOneProtocol.PrimingEndpointPath);
        }

        [Test]
        public void ModelField_IsOmittedWhenUnset()
        {
            DecisionRequest request = new DecisionRequest { State = "s" };
            request.Questions.Add(new DecisionQuestion { Id = "q", Type = DecisionQuestionType.YesNo, Instructions = "?" });

            StringAssert.DoesNotContain("\"model\"", SystemOneProtocol.WriteRequest(request));
        }

        [Test]
        public void KevResponse_ReadsEveryQuestionType()
        {
            DecisionResponse response = SystemOneProtocol.ParseResponse(KevResponse, 203f);

            Assert.IsTrue(response.Succeeded, response.Error);
            Assert.AreEqual(291, response.InputTokens);
            Assert.AreEqual(203f, response.LatencyMs);

            DecisionAnswer choice = response.Find("decision");
            Assert.AreEqual(DecisionQuestionType.Choice, choice.Type);
            Assert.AreEqual("Alarm", choice.Choice);
            Assert.AreEqual(0.4551f, choice.Confidence, 1e-4f);
            Assert.AreEqual(0.5641f, choice.ProbabilityOf("Alarm"), 1e-4f);

            DecisionAnswer yesNo = response.Find("alarming");
            Assert.AreEqual(DecisionQuestionType.YesNo, yesNo.Type);
            Assert.AreEqual(0.5837f, yesNo.Probability, 1e-4f);

            DecisionAnswer score = response.Find("threat");
            Assert.AreEqual(DecisionQuestionType.Score, score.Type);
            Assert.AreEqual(1.6479f, score.Score, 1e-4f);
            Assert.AreEqual(4, score.Probabilities.Count);
        }

        [Test]
        public void Probabilities_KeepTheOrderTheOptionsWereAskedIn()
        {
            DecisionAnswer choice = SystemOneProtocol.ParseResponse(KevResponse, 0f).Find("decision");

            string[] expected = { "Patrol Route_North", "Patrol Route_South", "Chase Intruder_01", "Alarm", "HoldPosition" };
            Assert.AreEqual(expected.Length, choice.Probabilities.Count);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], choice.Probabilities[i].Key);
        }

        [Test]
        public void ErrorBody_BecomesAFailedResponseWithTheServerMessage()
        {
            const string body = "{\"error\":{\"code\":400,\"message\":\"\\\"questions\\\" must be a non-empty object\",\"type\":\"invalid_request_error\"}}";

            DecisionResponse response = SystemOneProtocol.ParseResponse(body, 5f);

            Assert.IsFalse(response.Succeeded);
            StringAssert.Contains("questions", response.Error);
        }

        [Test]
        public void MalformedBody_FailsWithoutThrowing()
        {
            Assert.IsFalse(SystemOneProtocol.ParseResponse("{\"answers\":{", 0f).Succeeded);
            Assert.IsFalse(SystemOneProtocol.ParseResponse("", 0f).Succeeded);
            Assert.IsFalse(SystemOneProtocol.ParseResponse("{\"model\":\"x\"}", 0f).Succeeded);
        }

        [Test]
        public void EscapedText_RoundTripsThroughTheReader()
        {
            DecisionResponse response = SystemOneProtocol.ParseResponse(
                "{\"answers\":{\"q\":{\"type\":\"choice\",\"choice\":\"Say \\\"hi\\\" \\u00e9\",\"probabilities\":{\"Say \\\"hi\\\" \\u00e9\":1.0},\"confidence\":1}}}", 0f);

            Assert.AreEqual("Say \"hi\" \u00e9", response.Find("q").Choice);
            Assert.AreEqual(1f, response.Find("q").ProbabilityOf("Say \"hi\" \u00e9"));
        }

        [Test]
        public void DecisionFailures_NameTheSetupProblem()
        {
            StringAssert.Contains("b11361", BehaviorLLMClient.DescribeDecisionFailure(404, "", "http://x/v1/systemone", "HTTP/1.1 404 Not Found"));
            StringAssert.Contains("not a decision model", BehaviorLLMClient.DescribeDecisionFailure(501,
                "{\"error\":{\"code\":501,\"message\":\"This model is not a decision model\"}}", "e", "501"));
            Assert.IsTrue(BehaviorLLMClient.IsDecisionModelRefusingToGenerate(500,
                "{\"error\":{\"code\":500,\"message\":\"the current context does not support logits computation. skipping\"}}"));
            Assert.IsFalse(BehaviorLLMClient.IsDecisionModelRefusingToGenerate(500, "{\"error\":{\"message\":\"out of memory\"}}"));
        }
    }
}
