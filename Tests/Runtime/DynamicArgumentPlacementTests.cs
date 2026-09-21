using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// Where an action's argument values are printed, and why it matters.
    ///
    /// The action menu lives in the system prompt, which an inference server keeps in its KV cache
    /// between decisions. Any byte that changes there throws the cache away. Values that come from
    /// an <c>IArgumentOptionsProvider</c> are computed from the scene, and for a character whose
    /// arguments are *other characters* they change almost every decision, so they belong in the
    /// per-decision state block instead. Measured on the PrisonYard sample: with them in the menu,
    /// the four prisoners produced a different system prompt on essentially every decision and the
    /// prompt cache hit rate fell to 0.547; moved to the state block, all eight decision makers hold
    /// a single stable prefix and the rate rose to 0.710.
    ///
    /// The invariant these tests defend is <see cref="Menu_IsByteIdentical_WhenProviderValuesChange"/>.
    /// The rest explain how it is achieved.
    /// </summary>
    public class DynamicArgumentPlacementTests
    {
        private ActionConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<ActionConfig>();
            config.validActions = new List<ActionDefinition>
            {
                new ActionDefinition { actionName = "Wait", description = "Stand still." },
                // Values come from the scene: who is standing nearby.
                new ActionDefinition
                {
                    actionName = "Talk", description = "Talk to someone.",
                    parameterType = ActionParameterType.String
                },
                // Values authored in the asset: they cannot change while the game runs.
                new ActionDefinition
                {
                    actionName = "Announce", description = "Say something over the tannoy.",
                    parameterType = ActionParameterType.String,
                    allowedArguments = new List<string> { "Curfew", "Headcount" }
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (config != null) Object.DestroyImmediate(config);
        }

        /// <summary>A provider that answers for Talk and knows nothing about anything else.</summary>
        private static System.Func<ActionDefinition, ActionParameter, IList<string>> Provider(params string[] talkOptions)
        {
            return (def, parameter) => def.actionName == "Talk" ? new List<string>(talkOptions) : null;
        }

        [Test]
        public void Menu_IsByteIdentical_WhenProviderValuesChange()
        {
            string first = config.GetPromptDescription(Provider("Prisoner_02"), false, null);
            string second = config.GetPromptDescription(Provider("Prisoner_03", "Guard_01"), false, null);

            Assert.AreEqual(first, second,
                "The cached prefix must not move when the scene does; this is the whole point.");
        }

        [Test]
        public void Menu_IsByteIdentical_WhenTheProviderRunsOutOfTargets()
        {
            // The regression that survived the first fix: a provider reports "nothing right now"
            // and "I do not handle this action" the same way, so a marker printed only when there
            // were values appeared and disappeared, moving the prefix just as the values had.
            string withTargets = config.GetPromptDescription(Provider("Prisoner_02"), false, null);
            string withNone = config.GetPromptDescription(Provider(), false, null);

            Assert.AreEqual(withTargets, withNone);
        }

        [Test]
        public void DeferredAction_IsNamedSoTheCallerCanPrintItLater()
        {
            List<string> deferred = new List<string>();
            config.GetPromptDescription(Provider("Prisoner_02"), false, deferred);

            CollectionAssert.AreEqual(new[] { "Talk", "Announce" }, deferred);
        }

        [Test]
        public void AuthoredArguments_AreDeferredWhenAProviderCanOverrideThem()
        {
            string menu = config.GetPromptDescription(Provider("Prisoner_02"), false, null);

            StringAssert.DoesNotContain("Curfew", menu);
            StringAssert.Contains("listed under ARGUMENTS below", menu);
            StringAssert.DoesNotContain("Prisoner_02", menu);
        }

        [Test]
        public void InlineMode_KeepsTheOldLayout()
        {
            string menu = config.GetPromptDescription(Provider("Prisoner_02"), true, null);

            StringAssert.Contains("[arg: Prisoner_02]", menu,
                "Turning the setting off has to restore the pre-0.4.0 prompt exactly, or the "
                + "before/after measurement is not comparing what it claims to.");
        }

        [Test]
        public void FullPrompt_WithExamples_IsStableAcrossChangingAndEmptyProviders()
        {
            var options = new PromptBuilder.SystemPromptOptions { InlineProviderOptions = false, IncludeExamples = true };
            options.ArgumentOptionsFor = Provider("Prisoner_02");
            string first = PromptBuilder.BuildSystemPrompt(config, options);
            options.ArgumentOptionsFor = Provider("Prisoner_03");
            Assert.AreEqual(first, PromptBuilder.BuildSystemPrompt(config, options));
            options.ArgumentOptionsFor = Provider();
            Assert.AreEqual(first, PromptBuilder.BuildSystemPrompt(config, options));
            StringAssert.DoesNotContain("Prisoner_02", first);
        }

        [Test]
        public void FullPrompt_DoesNotInlineProviderOverridesOfAuthoredValues()
        {
            var options = new PromptBuilder.SystemPromptOptions { InlineProviderOptions = false };
            options.ArgumentOptionsFor = (action, parameter) => new[] { "FirstTarget" };
            string first = PromptBuilder.BuildSystemPrompt(config, options);
            options.ArgumentOptionsFor = (action, parameter) => new[] { "SecondTarget" };
            Assert.AreEqual(first, PromptBuilder.BuildSystemPrompt(config, options));
            StringAssert.DoesNotContain("FirstTarget", first);
        }

        [Test]
        public void ActionWithoutAProvider_IsNotDeferred()
        {
            List<string> deferred = new List<string>();
            string menu = config.GetPromptDescription(null, false, deferred);

            CollectionAssert.IsEmpty(deferred);
            StringAssert.Contains("[arg: Curfew | Headcount]", menu);
        }

        [Test]
        public void StatePrompt_CarriesTheDeferredValues()
        {
            var options = new Dictionary<string, IList<string>>
            {
                { "Talk", new List<string> { "Prisoner_02", "Guard_01" } }
            };

            string state = PromptBuilder.BuildStatePrompt("Nothing visible.", null, options);

            StringAssert.Contains("ARGUMENTS:", state);
            StringAssert.Contains("- Talk: Prisoner_02 | Guard_01", state);
            StringAssert.Contains("Nothing visible.", state);
        }

        [Test]
        public void StatePrompt_OmitsTheBlockWhenThereIsNothingToDefer()
        {
            string state = PromptBuilder.BuildStatePrompt("Nothing visible.", null, null);

            StringAssert.DoesNotContain("ARGUMENTS:", state);
        }

        [Test]
        public void StatePrompt_KeepsAvailableActionsAndArgumentsTogether()
        {
            var options = new Dictionary<string, IList<string>> { { "Talk", new List<string> { "Guard_01" } } };

            string state = PromptBuilder.BuildStatePrompt("Nothing visible.", new[] { "Wait", "Talk" }, options);

            Assert.Less(state.IndexOf("AVAILABLE THIS TURN:", System.StringComparison.Ordinal),
                        state.IndexOf("ARGUMENTS:", System.StringComparison.Ordinal));
            Assert.Less(state.IndexOf("ARGUMENTS:", System.StringComparison.Ordinal),
                        state.IndexOf("STATE:", System.StringComparison.Ordinal));
        }
    }
}
