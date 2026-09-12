using BehaviorLLM.Core.Decisions;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    public class DecisionParserTests
    {
        [Test]
        public void Parse_PlainJson_ReturnsActionAndArgument()
        {
            var r = DecisionParser.Parse("{\"action\":\"Attack\",\"arg\":\"Orc\"}");
            Assert.IsTrue(r.Succeeded, r.Error);
            Assert.AreEqual("Attack", r.Action);
            Assert.AreEqual("Orc", r.Argument);
            Assert.IsNull(r.Reason);
        }

        [Test]
        public void Parse_WithReasonField_ReturnsReason()
        {
            var r = DecisionParser.Parse("{\"reason\":\"enemy close\",\"action\":\"Attack\",\"arg\":\"Orc\"}");
            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual("enemy close", r.Reason);
        }

        [Test]
        public void Parse_ThinkBlockAndFences_AreStripped()
        {
            var r = DecisionParser.Parse("<think>\nlots of {braces} here\n</think>\n```json\n{\"action\":\"Stop\",\"arg\":\"\"}\n```");
            Assert.IsTrue(r.Succeeded, r.Error);
            Assert.AreEqual("Stop", r.Action);
            Assert.AreEqual(string.Empty, r.Argument);
        }

        [Test]
        public void Parse_UnterminatedThinkBlock_IsError()
        {
            var r = DecisionParser.Parse("<think>still thinking about {\"action\":\"Attack\"}");
            Assert.IsFalse(r.Succeeded);
        }

        [Test]
        public void Parse_GemmaChannelBlock_IsStripped()
        {
            var r = DecisionParser.Parse("<|channel>thought\nsome reasoning\n<channel|>{\"action\":\"Flee\",\"arg\":\"SafeZone_A\"}");
            Assert.IsTrue(r.Succeeded, r.Error);
            Assert.AreEqual("Flee", r.Action);
        }

        [Test]
        public void Parse_ProsePreamble_IsTolerated()
        {
            var r = DecisionParser.Parse("Sure, here is my decision: {\"action\":\"Patrol\",\"arg\":\"Route_A\"} Hope it helps.");
            Assert.IsTrue(r.Succeeded);
            Assert.AreEqual("Patrol", r.Action);
            Assert.AreEqual("Route_A", r.Argument);
        }

        [Test]
        public void Parse_BracesInsideStrings_DoNotConfuseExtraction()
        {
            var r = DecisionParser.Parse("{\"reason\":\"see {this}\",\"action\":\"Stop\",\"arg\":\"\"}");
            Assert.IsTrue(r.Succeeded, r.Error);
            Assert.AreEqual("see {this}", r.Reason);
        }

        [Test]
        public void Parse_QuotedArgument_IsNormalized()
        {
            var r = DecisionParser.Parse("{\"action\":\"Attack\",\"arg\":\"'Orc'\"}");
            Assert.AreEqual("Orc", r.Argument);
        }

        [Test]
        public void Parse_MissingAction_IsError()
        {
            var r = DecisionParser.Parse("{\"index\":0,\"content\":\"\"}");
            Assert.IsFalse(r.Succeeded);
            StringAssert.Contains("action", r.Error);
        }

        [Test]
        public void Parse_EmptyOrNoJson_IsError()
        {
            Assert.IsFalse(DecisionParser.Parse("").Succeeded);
            Assert.IsFalse(DecisionParser.Parse(null).Succeeded);
            Assert.IsFalse(DecisionParser.Parse("FollowRoutine(Route_A)").Succeeded);
        }
    }
}
