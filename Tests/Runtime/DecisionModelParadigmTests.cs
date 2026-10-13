using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// A decision maker in the Decision Model paradigm: what it offers the model, what it does
    /// with the distribution that comes back, and that the choice then goes through the same
    /// checks and fallback a generated decision does. A stub stands in for the server, so no
    /// model is needed.
    /// </summary>
    public class DecisionModelParadigmTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private BehaviorLLMSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<BehaviorLLMSettings>();
            settings.paradigm = DecisionParadigm.DecisionModel;
            settings.dumpAppliedSchema = false;
            BehaviorLLMSettings.Use(settings);
            cleanup.Add(settings);
        }

        [TearDown]
        public void TearDown()
        {
            BehaviorLLMSettings.Use(null);
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
            cleanup.Clear();
        }

        [Test]
        public void EveryLegalDecision_IsOfferedAsOneOption()
        {
            Fixture f = CreateFixture();
            f.Backend.Answer = Distribution(("HoldPosition", 1f));

            Think(f.Maker);

            DecisionQuestion question = f.Backend.Requests[0].Questions[0];
            Assert.AreEqual(DecisionMaker.ActionQuestionId, question.Id);
            Assert.AreEqual(DecisionQuestionType.Choice, question.Type);
            CollectionAssert.AreEqual(new[] { "Patrol Route_North", "Patrol Route_South", "Retreat SafeZone_A", "HoldPosition" },
                                      question.Options.ConvertAll(o => o.Key));
            Assert.AreEqual("Walk a route.", question.Options[0].Description);
            StringAssert.Contains("You are a guard.", question.Instructions);
            StringAssert.Contains(PromptBuilder.DecisionQuestionText, question.Instructions);
        }

        [Test]
        public void MostLikelyOption_IsDispatchedWithItsValue()
        {
            Fixture f = CreateFixture();
            f.Backend.Answer = Distribution(("Patrol Route_North", 0.2f), ("Patrol Route_South", 0.7f), ("HoldPosition", 0.1f));
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            Think(f.Maker);

            Assert.AreEqual("Patrol", f.Capture.LastActionName);
            Assert.AreEqual("Route_South", f.Capture.LastArgument);
            Assert.AreEqual(DecisionResultType.ModelAction, captured.resultType);
            Assert.AreEqual(DecisionParadigm.DecisionModel, captured.paradigm);
            Assert.AreEqual(4, captured.candidateCount);
            Assert.AreEqual(0.7f, captured.choiceProbability, 1e-5f);
            Assert.AreEqual(0, captured.completionTokens);
        }

        [Test]
        public void GenerativeBackend_IsNeverAsked()
        {
            Fixture f = CreateFixture();
            f.Backend.Answer = Distribution(("HoldPosition", 1f));

            Think(f.Maker);

            Assert.AreEqual(0, f.Generative.Requests.Count);
            Assert.AreEqual(1, f.Backend.Requests.Count);
        }

        [Test]
        public void AChoiceThatWasNotOffered_UsesTheFallback()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition");
            f.Backend.Answer = Distribution(("Fly Moon", 1f));
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            Think(f.Maker);

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
            Assert.AreEqual(DecisionResultType.FallbackAction, captured.resultType);
            StringAssert.Contains("not one of the options", captured.parseFailureReason);
        }

        [Test]
        public void AnUncertainChoice_BelowTheMinimum_UsesTheFallbackWithoutCountingAsAParseFailure()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition");
            f.Maker.Config.minChoiceProbability = 0.5f;
            f.Backend.Answer = Distribution(("Retreat SafeZone_A", 0.4f), ("Patrol Route_North", 0.35f), ("HoldPosition", 0.25f));
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            Think(f.Maker);

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
            Assert.AreEqual(DecisionResultType.FallbackAction, captured.resultType);
            Assert.IsTrue(string.IsNullOrEmpty(captured.parseFailureReason));
            StringAssert.Contains("below Min Choice Probability", captured.fallbackReason);
        }

        [Test]
        public void Sampling_NeverDrawsAnOptionWithZeroProbability()
        {
            Fixture f = CreateFixture();
            f.Maker.Config.decisionModelSelection = DecisionModelSelection.Sample;
            f.Backend.Answer = Distribution(("Patrol Route_North", 0f), ("Retreat SafeZone_A", 1f), ("HoldPosition", 0f));
            f.Backend.ChoiceOverride = "Patrol Route_North";

            for (int i = 0; i < 20; i++)
            {
                Think(f.Maker);
                Assert.AreEqual("Retreat", f.Capture.LastActionName);
            }
        }

        [Test]
        public void BackendFailure_IsReportedAsABackendError()
        {
            Fixture f = CreateFixture();
            f.Backend.Failure = "The server's model is not a decision model";
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            Think(f.Maker);

            Assert.AreEqual(DecisionResultType.BackendError, captured.resultType);
            StringAssert.Contains("not a decision model", captured.backendError);
            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [Test]
        public void ActionsTheProviderTurnsOff_AreNotOffered()
        {
            Fixture f = CreateFixture();
            SetPrivate(f.Maker, "actionAvailabilityProvider", new OnlyHold());
            f.Backend.Answer = Distribution(("HoldPosition", 1f));

            Think(f.Maker);

            CollectionAssert.AreEqual(new[] { "HoldPosition" }, f.Backend.Requests[0].Questions[0].Options.ConvertAll(o => o.Key));
        }

        [Test]
        public void TheQuestion_StaysTheSameFromOneDecisionToTheNext()
        {
            Fixture f = CreateFixture();
            f.Backend.Answer = Distribution(("HoldPosition", 1f));

            Think(f.Maker);
            Think(f.Maker);

            Assert.AreEqual(f.Backend.Requests[0].Questions[0].Instructions, f.Backend.Requests[1].Questions[0].Instructions);
            Assert.IsNull(f.Maker.JsonSchema, "a decision model is sent no JSON Schema");
        }

        // ------------------------------------------------------------------ fixture

        private sealed class OnlyHold : IActionAvailabilityProvider
        {
            public bool IsActionAvailable(string action) => action == "HoldPosition";
        }

        private static DecisionAnswer Distribution(params (string key, float p)[] entries)
        {
            DecisionAnswer answer = new DecisionAnswer { Type = DecisionQuestionType.Choice, Confidence = 0.5f };
            float best = -1f;
            foreach ((string key, float p) in entries)
            {
                answer.Probabilities.Add(new KeyValuePair<string, float>(key, p));
                if (p > best) { best = p; answer.Choice = key; }
            }
            return answer;
        }

        private Fixture CreateFixture()
        {
            GameObject go = new GameObject("DecisionModelFixture");
            cleanup.Add(go);
            ActionCaptureReceiver capture = go.AddComponent<ActionCaptureReceiver>();
            StubBackend generative = go.AddComponent<StubBackend>();
            StubDecisionBackend backend = go.AddComponent<StubDecisionBackend>();
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.systemPersona = "You are a guard.";
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
            maker.actionConfig = CreateActionConfig();
            cleanup.Add(maker.actionConfig);
            AddBinding(maker, "Patrol", capture.OnPatrol);
            AddBinding(maker, "Retreat", capture.OnRetreat);
            AddBinding(maker, "HoldPosition", capture.OnHoldPosition);

            DecisionMakerConfig cfg = ScriptableObject.CreateInstance<DecisionMakerConfig>();
            cleanup.Add(cfg);
            cfg.logDecisions = false;
            cfg.logFallbackUsage = false;
            cfg.decisionInterval = 1000f;
            maker.ApplyConfig(cfg);
            maker.RebuildBindingLookup();
            return new Fixture { Maker = maker, Capture = capture, Backend = backend, Generative = generative };
        }

        private static ActionConfig CreateActionConfig()
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            config.validActions = new List<ActionDefinition>
            {
                new ActionDefinition { actionName = "Patrol", description = "Walk a route.", parameterType = ActionParameterType.String,
                                       allowedArguments = new List<string> { "Route_North", "Route_South" } },
                new ActionDefinition { actionName = "Retreat", description = "Fall back to a safe zone.", parameterType = ActionParameterType.String,
                                       allowedArguments = new List<string> { "SafeZone_A" } },
                new ActionDefinition { actionName = "HoldPosition", description = "Stand still.", parameterType = ActionParameterType.None }
            };
            return config;
        }

        private static void AddBinding(DecisionMaker maker, string actionName, UnityAction<ActionArguments> callback)
        {
            ActionEvent evt = new ActionEvent();
            evt.AddListener(callback);
            maker.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = actionName, onExecute = evt });
        }

        private static void ConfigureFallback(DecisionMaker maker, string actionName)
        {
            object fallback = typeof(DecisionMaker).GetField("fallbackAction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(maker);
            fallback.GetType().GetField("fallbackActionName").SetValue(fallback, actionName);
            fallback.GetType().GetField("fallbackArgument").SetValue(fallback, "");
        }

        private static void SetPrivate(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }

        // The stub answers synchronously, so the whole decision has run by the time this returns.
        private static void Think(DecisionMaker maker)
        {
            typeof(DecisionMaker).GetMethod("Think", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(maker, null);
        }

        private sealed class Fixture
        {
            public DecisionMaker Maker;
            public ActionCaptureReceiver Capture;
            public StubDecisionBackend Backend;
            public StubBackend Generative;
        }
    }

    /// <summary>A decision-model backend that answers with whatever distribution the test sets.</summary>
    public class StubDecisionBackend : MonoBehaviour, IDecisionModelBackend
    {
        public DecisionAnswer Answer;
        public string Failure;

        /// <summary>When set, reported as the server's choice whatever the probabilities say.</summary>
        public string ChoiceOverride;

        public readonly List<DecisionRequest> Requests = new List<DecisionRequest>();

        public Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure != null) return Task.FromResult(DecisionResponse.Failed(Failure, 12f));

            DecisionResponse response = new DecisionResponse { LatencyMs = 12f, InputTokens = 100 };
            if (Answer != null)
            {
                DecisionAnswer copy = new DecisionAnswer
                {
                    Id = request.Questions[0].Id,
                    Type = Answer.Type,
                    Choice = ChoiceOverride ?? Answer.Choice,
                    Confidence = Answer.Confidence,
                    Probabilities = new List<KeyValuePair<string, float>>(Answer.Probabilities)
                };
                response.Answers.Add(copy);
            }
            return Task.FromResult(response);
        }
    }
}
