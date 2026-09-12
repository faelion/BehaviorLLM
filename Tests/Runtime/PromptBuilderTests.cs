using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    public class PromptBuilderTests
    {
        private ActionConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ActionConfig>();
            config.modelInstructions = "Stay calm.";
            config.validActions.Add(new ActionDefinition { actionName = "HoldPosition", description = "Wait.", parameterType = ActionParameterType.None });
            ActionDefinition move = new ActionDefinition { actionName = "MoveTo", description = "Walk to a zone.", parameterType = ActionParameterType.String, exampleArgument = "Zone_X" };
            move.allowedArguments.Add("Zone_A");
            move.allowedArguments.Add("Zone_B");
            config.validActions.Add(move);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void SystemPrompt_ContainsContractMenuExamplesAndGuide()
        {
            string prompt = PromptBuilder.BuildSystemPrompt(config, new PromptBuilder.SystemPromptOptions { Persona = "You are a guard." });

            StringAssert.StartsWith("You are a guard.", prompt);
            StringAssert.Contains("{\"action\":\"<name>\",\"arg\":\"<value>\"}", prompt);
            StringAssert.Contains("- HoldPosition: Wait.", prompt);
            StringAssert.Contains("- MoveTo(String): Walk to a zone. [arg: Zone_A | Zone_B]", prompt);
            StringAssert.Contains("### EXAMPLES", prompt);
            StringAssert.Contains("OUTPUT: {\"action\":\"HoldPosition\",\"arg\":\"\"}", prompt);
            StringAssert.Contains("OUTPUT: {\"action\":\"MoveTo\",\"arg\":\"Zone_A\"}", prompt);
            StringAssert.Contains("GUIDE:\nStay calm.", prompt);
        }

        [Test]
        public void SystemPrompt_ReasonField_ChangesContractAndExamples()
        {
            string prompt = PromptBuilder.BuildSystemPrompt(config, new PromptBuilder.SystemPromptOptions { ReasonMaxChars = 60 });

            StringAssert.Contains("{\"reason\":\"<why, under 60 characters>\",\"action\":\"<name>\",\"arg\":\"<value>\"}", prompt);
            StringAssert.Contains("{\"reason\":\"", prompt);
        }

        [Test]
        public void SystemPrompt_IsStableAcrossCalls()
        {
            var options = new PromptBuilder.SystemPromptOptions();
            Assert.AreEqual(PromptBuilder.BuildSystemPrompt(config, options), PromptBuilder.BuildSystemPrompt(config, options));
        }

        [Test]
        public void StatePrompt_HasHeaderAndFallbackForEmptyObservations()
        {
            Assert.AreEqual("STATE:\n- A", PromptBuilder.BuildStatePrompt("- A\n"));
            StringAssert.Contains("[No Observations]", PromptBuilder.BuildStatePrompt(""));
        }

        [Test]
        public void TrimStatePrompt_CutsWholeLinesFromTheEnd_AndKeepsHeaderPlusFirstLine()
        {
            string state = "STATE:\n--- Vision ---\n- [1.0m] ID: A\n- [2.0m] ID: B\n- [3.0m] ID: C";

            string trimmed = PromptBuilder.TrimStatePrompt(state, 40);

            Assert.LessOrEqual(trimmed.Length, 40);
            StringAssert.StartsWith("STATE:\n--- Vision ---\n- [1.0m] ID: A", trimmed);
            Assert.IsFalse(trimmed.Contains("ID: C"));
            Assert.IsFalse(trimmed.EndsWith("ID"), "must not cut mid-word");

            string tiny = PromptBuilder.TrimStatePrompt(state, 5);
            Assert.AreEqual("STATE:\n--- Vision ---", tiny, "header and first line survive an impossible budget");
        }

        [Test]
        public void StatePrompt_ListsAvailableActionsBeforeTheState()
        {
            string prompt = PromptBuilder.BuildStatePrompt("- A", new List<string> { "MoveTo", "HoldPosition" });

            Assert.AreEqual("AVAILABLE THIS TURN: MoveTo, HoldPosition\nSTATE:\n- A", prompt);
        }

        [Test]
        public void StatePrompt_OmitsTheLineWhenNoAvailabilityIsGiven()
        {
            Assert.IsFalse(PromptBuilder.BuildStatePrompt("- A", null).Contains(PromptBuilder.AvailableHeader));
            Assert.IsFalse(PromptBuilder.BuildStatePrompt("- A", new List<string>()).Contains(PromptBuilder.AvailableHeader));
        }

        [Test]
        public void SystemPrompt_ExplainsTheTurnListOnlyWhenTheMenuIsDynamic()
        {
            string stat = PromptBuilder.BuildSystemPrompt(config, new PromptBuilder.SystemPromptOptions { DynamicMenu = false });
            string dyn = PromptBuilder.BuildSystemPrompt(config, new PromptBuilder.SystemPromptOptions { DynamicMenu = true });

            Assert.IsFalse(stat.Contains(PromptBuilder.AvailableHeader));
            StringAssert.Contains("Choose only from the list after AVAILABLE THIS TURN:", dyn);
            // The full menu stays in the cached prefix either way; only the wording differs.
            StringAssert.Contains("- MoveTo(String)", dyn);
            StringAssert.Contains("- HoldPosition: Wait.", dyn);
        }

        [Test]
        public void TrimStatePrompt_KeepsTheAvailabilityLineUnderBudget()
        {
            string state = PromptBuilder.BuildStatePrompt("--- Vision ---\n- [1.0m] ID: A\n- [2.0m] ID: B\n- [3.0m] ID: C",
                                                          new List<string> { "MoveTo", "HoldPosition" });

            string trimmed = PromptBuilder.TrimStatePrompt(state, 60);

            StringAssert.StartsWith("AVAILABLE THIS TURN: MoveTo, HoldPosition\nSTATE:", trimmed);
            Assert.LessOrEqual(trimmed.Length, 60);
        }

        [Test]
        public void TrimStatePrompt_NoOpWhenWithinBudget()
        {
            Assert.AreEqual("STATE:\n- A", PromptBuilder.TrimStatePrompt("STATE:\n- A", 100));
            Assert.AreEqual("STATE:\n- A", PromptBuilder.TrimStatePrompt("STATE:\n- A", 0));
        }
    }
}
