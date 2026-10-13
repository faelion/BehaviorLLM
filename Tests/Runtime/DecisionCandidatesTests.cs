using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The options a decision model is offered must be exactly the decisions the generative
    /// paradigm's grammar would allow: no fewer, or a legal action can never be chosen, and no
    /// more, or the model can pick something the game then refuses.
    /// </summary>
    public class DecisionCandidatesTests
    {
        private ActionConfig config;

        [TearDown]
        public void TearDown()
        {
            if (config != null) Object.DestroyImmediate(config);
        }

        [Test]
        public void ActionsWithAndWithoutValues_BecomeOneOptionPerValue()
        {
            config = Config(
                Action("Patrol", "Walk a route.", Param("route", "Route_North", "Route_South")),
                Action("HoldPosition", "Stand still."));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config);

            CollectionAssert.AreEqual(new[] { "Patrol Route_North", "Patrol Route_South", "HoldPosition" }, Keys(result));
            Assert.AreEqual("Walk a route.", result.Candidates[0].Description);
            Assert.AreEqual("Route_South", result.Candidates[1].Arguments["route"]);
            Assert.AreEqual(0, result.Candidates[2].Arguments.Count);
        }

        [Test]
        public void ProviderValues_ReplaceTheAuthoredOnes()
        {
            config = Config(Action("Chase", "Chase someone.", Param("target", "Nobody")));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config, new DecisionCandidates.Options
            {
                ArgumentOptionsFor = (a, p) => new List<string> { "Intruder_01", "Intruder_02" }
            });

            CollectionAssert.AreEqual(new[] { "Chase Intruder_01", "Chase Intruder_02" }, Keys(result));
        }

        [Test]
        public void UnavailableActions_AreLeftOut()
        {
            config = Config(Action("Chase", "c"), Action("HoldPosition", "h"));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config, new DecisionCandidates.Options
            {
                IsActionAvailable = def => def.actionName != "Chase"
            });

            CollectionAssert.AreEqual(new[] { "HoldPosition" }, Keys(result));
        }

        [Test]
        public void AParameterWithoutAList_MakesItsActionUnenumerable()
        {
            config = Config(Action("Say", "Say a word.", Param("word")), Action("HoldPosition", "h"));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config);

            CollectionAssert.AreEqual(new[] { "HoldPosition" }, Keys(result));
            CollectionAssert.AreEqual(new[] { "Say" }, result.UnenumerableActions);
        }

        [Test]
        public void SeveralParameters_FormEveryCombinationNamedByParameter()
        {
            config = Config(Action("MoveTo", "Go somewhere.", Param("destination", "Gate", "Yard"), Param("speed", "Walk", "Run")));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config);

            CollectionAssert.AreEqual(new[]
            {
                "MoveTo destination=Gate speed=Walk", "MoveTo destination=Gate speed=Run",
                "MoveTo destination=Yard speed=Walk", "MoveTo destination=Yard speed=Run"
            }, Keys(result));
            Assert.AreEqual("Yard", result.Candidates[3].Arguments["destination"]);
            Assert.AreEqual("Run", result.Candidates[3].Arguments["speed"]);
        }

        [Test]
        public void TheModelsLimit_TruncatesAndSaysSo()
        {
            config = Config(Action("Patrol", "p", Param("route", "A", "B", "C")), Action("HoldPosition", "h"));

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config, new DecisionCandidates.Options { MaxCandidates = 2 });

            CollectionAssert.AreEqual(new[] { "Patrol A", "Patrol B" }, Keys(result));
            Assert.IsTrue(result.Truncated);
        }

        [Test]
        public void LegacySingleArgument_IsEnumeratedFromItsAllowedArguments()
        {
            config = Config(new ActionDefinition
            {
                actionName = "Retreat",
                description = "Fall back.",
                parameterType = ActionParameterType.String,
                allowedArguments = new List<string> { "SafeZone_A", "SafeZone_B" }
            });

            DecisionCandidates.Result result = DecisionCandidates.Enumerate(config);

            CollectionAssert.AreEqual(new[] { "Retreat SafeZone_A", "Retreat SafeZone_B" }, Keys(result));
            Assert.AreEqual("SafeZone_B", result.Candidates[1].Arguments.First);
        }

        // ------------------------------------------------------------------ helpers

        private static string[] Keys(DecisionCandidates.Result result)
        {
            string[] keys = new string[result.Candidates.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = result.Candidates[i].Key;
            return keys;
        }

        private static ActionConfig Config(params ActionDefinition[] actions)
        {
            ActionConfig c = ScriptableObject.CreateInstance<ActionConfig>();
            c.validActions = new List<ActionDefinition>(actions);
            return c;
        }

        private static ActionDefinition Action(string name, string description, params ActionParameter[] parameters)
        {
            return new ActionDefinition { actionName = name, description = description, parameters = new List<ActionParameter>(parameters) };
        }

        private static ActionParameter Param(string name, params string[] values)
        {
            return new ActionParameter { name = name, allowedValues = new List<string>(values) };
        }
    }
}
