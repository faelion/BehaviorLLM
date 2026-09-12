using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// <see cref="ActionSchemaBuilder"/> is a pure function over an <see cref="ActionConfig"/>,
    /// so each test builds an in-memory config and asserts on the schema text. Runs in EditMode
    /// and PlayMode; no scene or backend involved.
    /// </summary>
    public class ActionSchemaBuilderTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in cleanup) if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
        }

        private ActionConfig Config(params ActionDefinition[] actions)
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            cleanup.Add(config);
            config.validActions.AddRange(actions);
            return config;
        }

        private static ActionDefinition Action(string name, ActionParameterType type = ActionParameterType.String, params string[] allowed)
        {
            ActionDefinition def = new ActionDefinition { actionName = name, parameterType = type };
            def.allowedArguments.AddRange(allowed);
            return def;
        }

        [Test]
        public void Build_NullOrEmptyConfig_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, ActionSchemaBuilder.Build(null));
            Assert.AreEqual(string.Empty, ActionSchemaBuilder.Build(Config()));
        }

        [Test]
        public void Build_OneBranchPerAction_WithConstActionAndArgumentShape()
        {
            string schema = ActionSchemaBuilder.Build(Config(Action("Stop", ActionParameterType.None), Action("Attack")));

            StringAssert.StartsWith("{\"oneOf\":[", schema);
            StringAssert.Contains("\"action\":{\"const\":\"Stop\"},\"arg\":{\"const\":\"\"}", schema);
            StringAssert.Contains("\"action\":{\"const\":\"Attack\"},\"arg\":{\"type\":\"string\",\"pattern\":\"^[A-Za-z0-9_]+$\"}", schema);
            StringAssert.Contains("\"required\":[\"action\",\"arg\"]", schema);
            StringAssert.Contains("\"additionalProperties\":false", schema);
            Assert.IsFalse(schema.Contains("reason"), "reason field must be absent unless requested");
        }

        [Test]
        public void Build_AllowedArguments_BecomeEnum()
        {
            string schema = ActionSchemaBuilder.Build(Config(Action("Move", ActionParameterType.String, "Zone_A", "Zone_B", "Zone_A", "bad name")));

            StringAssert.Contains("\"arg\":{\"enum\":[\"Zone_A\",\"Zone_B\"]}", schema);
            Assert.IsFalse(schema.Contains("bad name"), "non-identifier options must be dropped");
        }

        [Test]
        public void Build_ProviderOptions_OverrideStaticList()
        {
            ActionConfig config = Config(Action("Move", ActionParameterType.String, "Static_1"));
            string schema = ActionSchemaBuilder.Build(config, new ActionSchemaBuilder.Options
            {
                ArgumentOptionsFor = def => new List<string> { "Runtime_1", "Runtime_2" }
            });

            StringAssert.Contains("\"enum\":[\"Runtime_1\",\"Runtime_2\"]", schema);
            Assert.IsFalse(schema.Contains("Static_1"));
        }

        [Test]
        public void Build_ReasonField_IsFirstAndRequired()
        {
            string schema = ActionSchemaBuilder.Build(Config(Action("Stop", ActionParameterType.None)), new ActionSchemaBuilder.Options { ReasonMaxChars = 80 });

            StringAssert.Contains("\"properties\":{\"reason\":{\"type\":\"string\",\"maxLength\":80},\"action\"", schema);
            StringAssert.Contains("\"required\":[\"reason\",\"action\",\"arg\"]", schema);
        }

        [Test]
        public void Build_SkipsInvalidActionNames()
        {
            string schema = ActionSchemaBuilder.Build(Config(Action("Valid_1"), Action("Bad Name"), Action("Acción"), Action("  "), Action(null)));

            StringAssert.Contains("\"Valid_1\"", schema);
            Assert.IsFalse(schema.Contains("Bad Name"));
            Assert.IsFalse(schema.Contains("Acción"));
            Assert.AreEqual(1, CountOccurrences(schema, "\"type\":\"object\""));
        }


        [Test]
        public void Build_AvailabilityFilter_DropsUnavailableActions()
        {
            ActionConfig config = Config(Action("Chase"), Action("Retreat"), Action("Patrol"));

            string schema = ActionSchemaBuilder.Build(config, new ActionSchemaBuilder.Options
            {
                IsActionAvailable = def => def.actionName != "Chase"
            });

            Assert.IsFalse(schema.Contains("\"Chase\""), "an unavailable action must not appear in the schema at all");
            StringAssert.Contains("\"const\":\"Retreat\"", schema);
            StringAssert.Contains("\"const\":\"Patrol\"", schema);
            Assert.AreEqual(2, CountOccurrences(schema, "\"type\":\"object\""));
        }

        [Test]
        public void Build_AvailabilityFilter_RejectingEverything_ReturnsEmpty()
        {
            ActionConfig config = Config(Action("Chase"), Action("Retreat"));

            string schema = ActionSchemaBuilder.Build(config, new ActionSchemaBuilder.Options
            {
                IsActionAvailable = def => false
            });

            Assert.AreEqual(string.Empty, schema, "no available action means no usable constraint");
        }

        [Test]
        public void Build_AvailabilityFilter_CombinesWithArgumentEnums()
        {
            ActionConfig config = Config(
                Action("Chase", ActionParameterType.String, "Intruder_01"),
                Action("Retreat", ActionParameterType.String, "SafeZone_A", "SafeZone_B"));

            string schema = ActionSchemaBuilder.Build(config, new ActionSchemaBuilder.Options
            {
                IsActionAvailable = def => def.actionName == "Retreat"
            });

            StringAssert.Contains("\"arg\":{\"enum\":[\"SafeZone_A\",\"SafeZone_B\"]}", schema);
            Assert.IsFalse(schema.Contains("Intruder_01"));
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
            return count;
        }
    }
}
