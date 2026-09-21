using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Interfaces;
using BehaviorLLM.Core.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using BehaviorLLM.Core.Telemetry;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// Covers everything between "the model answered" and "the game did something": parsing the
    /// answer, validating it against the bindings and the action definition, running the fallback
    /// when it cannot be used, and recording what happened.
    ///
    /// Synthetic output is fed into <c>DecisionMaker.ExecuteAction(string)</c> or returned by a
    /// controllable backend. Lifecycle tests also exercise cancellation and delayed responses.
    /// No model or network is needed. Keep the ExecuteAction test entry point's signature.
    ///
    /// These tests were originally the UrbanIncidentResponse sample's reliability suite. They moved
    /// into the package when that sample was retired on 2026-09-07, because what they actually
    /// verified was package behaviour rather than anything about that scenario.
    /// </summary>
    public class DecisionMakerDispatchTests
    {
        private readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
            cleanup.Clear();
        }

        // ------------------------------------------------------------------ dispatch

        [Test]
        public void ValidJson_ExecutesAction()
        {
            Fixture f = CreateFixture();

            InvokeExecuteAction(f.Maker, "{\"action\":\"Patrol\",\"arg\":\"Route_North\"}");

            Assert.AreEqual("Patrol", f.Capture.LastActionName);
            Assert.AreEqual("Route_North", f.Capture.LastArgument);
        }

        [Test]
        public void ThinkBlockThenJson_ExecutesAction()
        {
            Fixture f = CreateFixture();

            InvokeExecuteAction(f.Maker, "<think>reasoning details\n</think>\n{\"action\":\"Patrol\",\"arg\":\"Route_North\"}");

            Assert.AreEqual("Patrol", f.Capture.LastActionName);
            Assert.AreEqual("Route_North", f.Capture.LastArgument);
        }

        [Test]
        public void ActionWithNoParameter_DispatchesWithAnEmptyArgument()
        {
            Fixture f = CreateFixture();

            // The model supplied an argument for an action that takes none; it is dropped rather
            // than passed through, so handlers never have to guard against it.
            InvokeExecuteAction(f.Maker, "{\"action\":\"HoldPosition\",\"arg\":\"somewhere\"}");

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
            Assert.AreEqual(string.Empty, f.Capture.LastArgument);
        }

        // ------------------------------------------------------------------ fallback

        [Test]
        public void FreeFormText_UsesTheFallback()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "Retreat", "SafeZone_A");

            // The pre-0.2.0 output format. It is no longer accepted, and must not be dispatched.
            InvokeExecuteAction(f.Maker, "Patrol(Route_North)");

            Assert.AreEqual(1, f.Capture.InvocationCount);
            Assert.AreEqual("Retreat", f.Capture.LastActionName);
            Assert.AreEqual("SafeZone_A", f.Capture.LastArgument);
        }

        [Test]
        public void JsonWithoutADecision_UsesTheFallbackAndReportsWhy()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "Retreat", "SafeZone_A");
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            // Well-formed JSON that is the server's own envelope rather than a decision.
            InvokeExecuteAction(f.Maker, "{\n\"index\":0,\n\"content\":\"\",\n\"tokens\":[]\n}");

            Assert.AreEqual("Retreat", f.Capture.LastActionName);
            Assert.IsNotNull(captured);
            StringAssert.Contains("action", captured.parseFailureReason);
        }

        [Test]
        public void UnboundAction_UsesTheFallback()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition", "");

            InvokeExecuteAction(f.Maker, "{\"action\":\"Teleport\",\"arg\":\"Anywhere\"}");

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
        }

        [Test]
        public void MissingArgument_UsesTheFallback()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition", "");
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            InvokeExecuteAction(f.Maker, "{\"action\":\"Patrol\",\"arg\":\"\"}");

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
            Assert.AreEqual(DecisionResultType.FallbackAction, captured.resultType);
            StringAssert.Contains("missing 'arg'", captured.fallbackReason);
        }

        [Test]
        public void WithNoFallbackNamed_UnusableOutputDispatchesNothing()
        {
            Fixture f = CreateFixture();

            InvokeExecuteAction(f.Maker, "This is not JSON.");

            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [Test]
        public void AnEmptyFallbackNameMeansNoFallback()
        {
            // Naming a fallback is what enables it: there is no separate toggle to get wrong.
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "", "");

            InvokeExecuteAction(f.Maker, "This is not JSON.");

            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [Test]
        public void AnUnboundFallbackNameDispatchesNothing()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "NotBoundToAnything", "");

            InvokeExecuteAction(f.Maker, "This is not JSON.");

            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        // ------------------------------------------------------------------ telemetry

        [Test]
        public void Telemetry_ModelAction_IsRecorded()
        {
            Fixture f = CreateFixture();
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            InvokeExecuteAction(f.Maker, "{\"reason\":\"routine\",\"action\":\"Patrol\",\"arg\":\"Route_North\"}");

            Assert.IsNotNull(captured);
            Assert.AreEqual(DecisionResultType.ModelAction, captured.resultType);
            Assert.AreEqual("Patrol", captured.actionName);
            Assert.AreEqual("Route_North", captured.argument);
            Assert.AreEqual("routine", captured.reason);
            Assert.IsFalse(captured.usedFallback);
        }

        [Test]
        public void Telemetry_FallbackAction_IsRecorded()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "Retreat", "SafeZone_A");
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            InvokeExecuteAction(f.Maker, "This is not a decision.");

            Assert.IsNotNull(captured);
            Assert.AreEqual(DecisionResultType.FallbackAction, captured.resultType);
            Assert.IsTrue(captured.usedFallback);
            Assert.AreEqual("Retreat", captured.actionName);
            Assert.AreEqual("SafeZone_A", captured.argument);
            StringAssert.Contains("parse failed", captured.fallbackReason);
        }

        [Test]
        public void Telemetry_CarriesTheProfileFromTheConfig()
        {
            Fixture f = CreateFixture();
            DecisionMakerConfig cfg = ScriptableObject.CreateInstance<DecisionMakerConfig>();
            cfg.profile = DecisionProfile.Deliberative;
            cfg.logDecisions = false;
            f.Maker.ApplyConfig(cfg);
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;

            InvokeExecuteAction(f.Maker, "{\"action\":\"HoldPosition\",\"arg\":\"\"}");

            Assert.AreEqual(DecisionProfile.Deliberative, captured.profile);
        }

        // ------------------------------------------------------------------ composition

        [Test]
        public void ObservationComposer_CompactLimitsVisionAndMemory()
        {
            GameObject go = new GameObject("ObsRoot");
            cleanup.Add(go);
            FakeObservationModule self = go.AddComponent<FakeObservationModule>();
            self.topicName = "Self-Status";
            self.observation = "ID: Guard_01 | Type: Self";
            FakeObservationModule vision = go.AddComponent<FakeObservationModule>();
            vision.topicName = "Vision";
            vision.observation = "- A\n- B\n- C\n- D";
            FakeObservationModule memory = go.AddComponent<FakeObservationModule>();
            memory.topicName = "Short-Term Memory";
            memory.observation = "- m1\n- m2\n- m3";

            List<IObservationModule> modules = new List<IObservationModule> { self, vision, memory };
            string compact = ObservationComposer.ComposeCompact(modules, 2, 1);

            Assert.IsTrue(compact.Contains("--- Self-Status ---"));
            Assert.IsTrue(compact.Contains("ID: Guard_01"));
            Assert.IsTrue(compact.Contains("- A"));
            Assert.IsTrue(compact.Contains("- B"));
            Assert.IsFalse(compact.Contains("- C"));
            Assert.IsTrue(compact.Contains("- m1"));
            Assert.IsFalse(compact.Contains("- m2"));
        }

        [Test]
        public void UnknownArgument_IsRejectedEvenWithoutStructuredOutput()
        {
            Fixture f = CreateFixture();
            f.Maker.Config.useStructuredOutput = false;
            f.Maker.actionConfig.validActions[0].allowedArguments.Add("Route_North");
            ConfigureFallback(f.Maker, "HoldPosition", "");

            InvokeExecuteAction(f.Maker, "{\"action\":\"Patrol\",\"arg\":\"Unknown\"}");

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
        }

        [Test]
        public void OrphanBinding_CannotBeExecutedByTheModel()
        {
            Fixture f = CreateFixture();
            f.Maker.actionConfig.validActions.RemoveAt(0);
            InvokeExecuteAction(f.Maker, "{\"action\":\"Patrol\",\"arg\":\"Route_North\"}");
            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [Test]
        public void ArgumentOptions_RefreshWithoutAnAvailabilityChangeOrManualRebuild()
        {
            Fixture f = CreateFixture();
            MutableOptions options = new MutableOptions();
            SetPrivate(f.Maker, "argumentOptionsProvider", options);
            StubBackend backend = f.Maker.GetComponent<StubBackend>();
            backend.Handler = (request, token) => Task.FromResult(new LLMResponse { Text = "{\"action\":\"HoldPosition\",\"arg\":\"\"}" });
            InvokePrivate(f.Maker, "Think");
            StringAssert.Contains("Route_North", backend.Requests[0].JsonSchema);

            options.Value = "Route_South";
            InvokePrivate(f.Maker, "Think");

            StringAssert.Contains("Route_South", backend.Requests[1].JsonSchema);
            Assert.IsFalse(backend.Requests[1].JsonSchema.Contains("Route_North"));
            StringAssert.Contains("Route_South", backend.Requests[1].UserPrompt);
            Assert.AreEqual(backend.Requests[0].SystemPrompt, backend.Requests[1].SystemPrompt);
        }

        [Test]
        public void ImpossibleStateBudget_UsesFallbackWithoutSendingRequest()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition", "");
            InvokePrivate(f.Maker, "Think");
            StubBackend backend = f.Maker.GetComponent<StubBackend>();
            f.Maker.Config.maxPromptChars = backend.Requests[0].SystemPrompt.Length + 1;
            backend.Requests.Clear();
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;
            InvokePrivate(f.Maker, "Think");
            Assert.AreEqual(0, backend.Requests.Count);
            Assert.IsTrue(captured.usedFallback);
            StringAssert.Contains("Prompt budget", captured.backendError);
        }

#if UNITY_EDITOR
        [Test]
        public void SchemaDump_MatchesFilteredRequestAndClearsWhenUnconstrained()
        {
            Fixture f = CreateFixture();
            SetPrivate(f.Maker, "actionAvailabilityProvider", new MutableAvailability { AllowPatrol = false });
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "_last_applied_schema.json");
            string previous = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            try
            {
                InvokePrivate(f.Maker, "Think");
                StubBackend backend = f.Maker.GetComponent<StubBackend>();
                Assert.AreEqual(backend.Requests[0].JsonSchema, System.IO.File.ReadAllText(path));
                StringAssert.DoesNotContain("Patrol", System.IO.File.ReadAllText(path));
                f.Maker.Config.useStructuredOutput = false;
                InvokePrivate(f.Maker, "Think");
                Assert.AreEqual(string.Empty, System.IO.File.ReadAllText(path));
            }
            finally
            {
                if (previous == null) System.IO.File.Delete(path);
                else System.IO.File.WriteAllText(path, previous);
            }
        }
#endif

        [Test]
        public void Response_RechecksArgumentsAndAvailabilityAfterInference()
        {
            Fixture f = CreateFixture();
            MutableOptions options = new MutableOptions();
            MutableAvailability availability = new MutableAvailability();
            SetPrivate(f.Maker, "argumentOptionsProvider", options);
            SetPrivate(f.Maker, "actionAvailabilityProvider", availability);
            ConfigureFallback(f.Maker, "HoldPosition", "");
            StubBackend backend = f.Maker.GetComponent<StubBackend>();
            backend.Handler = (request, token) =>
            {
                options.Value = "Route_South";
                return Task.FromResult(new LLMResponse { Text = "{\"action\":\"Patrol\",\"arg\":\"Route_North\"}", StructuredOutput = true });
            };
            InvokePrivate(f.Maker, "Think");
            Assert.AreEqual("HoldPosition", f.Capture.LastActionName, "old argument must be rejected");

            backend.Handler = (request, token) =>
            {
                availability.AllowPatrol = false;
                return Task.FromResult(new LLMResponse { Text = "{\"action\":\"Patrol\",\"arg\":\"Route_South\"}", StructuredOutput = true });
            };
            InvokePrivate(f.Maker, "Think");
            Assert.AreEqual("HoldPosition", f.Capture.LastActionName, "newly unavailable action must be rejected");
            Assert.AreEqual(2, f.Capture.InvocationCount);
        }

        [Test]
        public void LowercaseAction_UsesCanonicalNameForAvailability()
        {
            Fixture f = CreateFixture();
            SetPrivate(f.Maker, "actionAvailabilityProvider", new MutableAvailability());
            InvokeExecuteAction(f.Maker, "{\"action\":\"patrol\",\"arg\":\"Route_North\"}");
            Assert.AreEqual("Patrol", f.Capture.LastActionName);
        }

        [Test]
        public void BackendFailure_ExecutesFallbackAndCountsBothOutcomes()
        {
            Fixture f = CreateFixture();
            ConfigureFallback(f.Maker, "HoldPosition", "");
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;
            InvokePrivate(f.Maker, "Think");

            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
            Assert.AreEqual(DecisionResultType.FallbackAction, captured.resultType);
            Assert.IsNotEmpty(captured.backendError);
            Assert.IsFalse(captured.structuredOutput);
            DecisionTelemetryAggregator aggregate = new DecisionTelemetryAggregator();
            aggregate.Add(captured);
            Assert.AreEqual(1, aggregate.BackendErrors);
            Assert.AreEqual(1, aggregate.FallbackActions);
        }

        [Test]
        public void BackendFailure_WithoutFallbackRemainsABackendError()
        {
            Fixture f = CreateFixture();
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;
            InvokePrivate(f.Maker, "Think");
            Assert.AreEqual(DecisionResultType.BackendError, captured.resultType);
            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [Test]
        public void Fallback_MustHaveACurrentlyAllowedArgumentAndAction()
        {
            Fixture f = CreateFixture();
            f.Maker.actionConfig.validActions[0].allowedArguments.Add("Route_North");
            ConfigureFallback(f.Maker, "Patrol", "Unknown");
            InvokeExecuteAction(f.Maker, "bad output");
            Assert.AreEqual(0, f.Capture.InvocationCount);

            ConfigureFallback(f.Maker, "Patrol", "Route_North");
            SetPrivate(f.Maker, "actionAvailabilityProvider", new MutableAvailability { AllowPatrol = false });
            InvokeExecuteAction(f.Maker, "bad output");
            Assert.AreEqual(0, f.Capture.InvocationCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Telemetry_UsesBackendConstraintStatus(bool applied)
        {
            Fixture f = CreateFixture();
            f.Maker.GetComponent<StubBackend>().Handler = (request, token) => Task.FromResult(new LLMResponse
            {
                Text = "{\"action\":\"HoldPosition\",\"arg\":\"\"}", StructuredOutput = applied
            });
            DecisionTelemetry captured = null;
            f.Maker.DecisionTelemetryRecorded += t => captured = t;
            InvokePrivate(f.Maker, "Think");
            Assert.AreEqual(applied, captured.structuredOutput);
        }

        [Test]
        public void PromptLogging_RespectsItsSwitch()
        {
            Fixture f = CreateFixture();
            int prompts = 0;
            Application.LogCallback listener = (message, stack, type) => { if (message.StartsWith("[DecisionMaker] Prompt (")) prompts++; };
            Application.logMessageReceived += listener;
            try
            {
                InvokePrivate(f.Maker, "Think");
                Assert.AreEqual(0, prompts);
                f.Maker.Config.logPrompts = true;
                InvokePrivate(f.Maker, "Think");
                Assert.AreEqual(1, prompts);
            }
            finally { Application.logMessageReceived -= listener; }
        }

        [UnityTest]
        public IEnumerator Interrupt_CancelsAndDiscardsOldAnswerThenRequestsFreshState()
        {
            Fixture f = CreateFixture();
            FakeObservationModule observation = f.Maker.gameObject.AddComponent<FakeObservationModule>();
            observation.topicName = "World";
            observation.observation = "old state";
            SetPrivate(f.Maker, "observations", new List<IObservationModule> { observation });
            StubBackend backend = f.Maker.GetComponent<StubBackend>();
            TaskCompletionSource<LLMResponse> pending = new TaskCompletionSource<LLMResponse>();
            CancellationToken capturedToken = default;
            backend.Handler = (request, token) =>
            {
                if (backend.Requests.Count == 1) { capturedToken = token; return pending.Task; }
                return Task.FromResult(new LLMResponse { Text = "{\"action\":\"HoldPosition\",\"arg\":\"\"}" });
            };
            InvokePrivate(f.Maker, "Think");
            observation.observation = "new state";
            observation.interrupt = true;
            InvokePrivate(f.Maker, "Update");
            Assert.IsTrue(capturedToken.IsCancellationRequested);
            pending.SetResult(new LLMResponse { Text = "{\"action\":\"Patrol\",\"arg\":\"Route_North\"}" });
            for (int frame = 0; frame < 30 && backend.Requests.Count < 2; frame++) yield return null;
            Assert.AreEqual(2, backend.Requests.Count);
            StringAssert.Contains("new state", backend.Requests[1].UserPrompt);
            Assert.AreEqual(1, f.Capture.InvocationCount);
            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
        }

        [UnityTest]
        public IEnumerator Reenable_OldRequestCannotDispatchOrClearNewRequestState()
        {
            Fixture f = CreateFixture();
            StubBackend backend = f.Maker.GetComponent<StubBackend>();
            TaskCompletionSource<LLMResponse> oldRequest = new TaskCompletionSource<LLMResponse>();
            TaskCompletionSource<LLMResponse> newRequest = new TaskCompletionSource<LLMResponse>();
            backend.Handler = (request, token) => backend.Requests.Count == 1 ? oldRequest.Task : newRequest.Task;
            InvokePrivate(f.Maker, "Think");
            f.Maker.enabled = false;
            f.Maker.enabled = true;
            InvokePrivate(f.Maker, "Think");

            oldRequest.SetResult(new LLMResponse { Text = "{\"action\":\"Patrol\",\"arg\":\"Route_North\"}" });
            yield return null;
            yield return null;
            Assert.AreEqual(0, f.Capture.InvocationCount);
            Assert.IsTrue((bool)typeof(DecisionMaker).GetField("isThinking", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f.Maker));

            newRequest.SetResult(new LLMResponse { Text = "{\"action\":\"HoldPosition\",\"arg\":\"\"}" });
            for (int frame = 0; frame < 30 && f.Capture.InvocationCount == 0; frame++) yield return null;
            Assert.AreEqual(1, f.Capture.InvocationCount);
            Assert.AreEqual("HoldPosition", f.Capture.LastActionName);
        }

        private sealed class MutableOptions : IArgumentOptionsProvider
        {
            public string Value = "Route_North";
            public bool TryGetArgumentOptions(string action, string parameterName, List<string> options)
            {
                if (action != "Patrol") return false;
                options.Add(Value);
                return true;
            }
        }

        private sealed class MutableAvailability : IActionAvailabilityProvider
        {
            public bool AllowPatrol = true;
            public bool IsActionAvailable(string action) => action == "HoldPosition" || (action == "Patrol" && AllowPatrol);
        }

        private static void SetPrivate(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }

        private static void InvokePrivate(object instance, string name)
        {
            instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        }

        // ------------------------------------------------------------------ fixture

        private Fixture CreateFixture()
        {
            GameObject go = new GameObject("DecisionMakerFixture");
            cleanup.Add(go);
            ActionCaptureReceiver capture = go.AddComponent<ActionCaptureReceiver>();
            // Added before the DecisionMaker so its Awake finds a backend on the same object.
            // The stub handles requests locally; a missing backend would log an error at Awake.
            go.AddComponent<StubBackend>();
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
            maker.actionConfig = CreateActionConfig();
            AddBinding(maker, "Patrol", capture.OnPatrol);
            AddBinding(maker, "Retreat", capture.OnRetreat);
            AddBinding(maker, "HoldPosition", capture.OnHoldPosition);

            // Keep the console quiet: these tests deliberately feed unusable output.
            DecisionMakerConfig cfg = ScriptableObject.CreateInstance<DecisionMakerConfig>();
            cfg.logDecisions = false;
            cfg.logFallbackUsage = false;
            cfg.decisionInterval = 1000f;
            maker.ApplyConfig(cfg);

            // The dispatch table is built once in Awake from the serialized list; the fixture
            // mutates it afterwards, so rebuild before the first ExecuteAction.
            maker.RebuildBindingLookup();
            return new Fixture { Maker = maker, Capture = capture };
        }

        private static ActionConfig CreateActionConfig()
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            config.validActions = new List<ActionDefinition>
            {
                new ActionDefinition { actionName = "Patrol", description = "Walk a route.", parameterType = ActionParameterType.String },
                new ActionDefinition { actionName = "Retreat", description = "Fall back to a safe zone.", parameterType = ActionParameterType.String },
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

        private static void ConfigureFallback(DecisionMaker maker, string actionName, string argument)
        {
            FieldInfo field = typeof(DecisionMaker).GetField("fallbackAction", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "DecisionMaker.fallbackAction");
            object fallback = field.GetValue(maker);
            fallback.GetType().GetField("fallbackActionName").SetValue(fallback, actionName);
            fallback.GetType().GetField("fallbackArgument").SetValue(fallback, argument);
        }

        private static void InvokeExecuteAction(DecisionMaker maker, string response)
        {
            MethodInfo method = typeof(DecisionMaker).GetMethod("ExecuteAction", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            Assert.IsNotNull(method, "DecisionMaker.ExecuteAction(string) is the test entry point");
            method.Invoke(maker, new object[] { response });
        }

        private sealed class Fixture
        {
            public DecisionMaker Maker;
            public ActionCaptureReceiver Capture;
        }
    }

    /// <summary>Records what the bound UnityEvents were asked to do.</summary>
    public class ActionCaptureReceiver : MonoBehaviour
    {
        public string LastActionName { get; private set; }
        public string LastArgument { get; private set; }
        public int InvocationCount { get; private set; }

        public void OnPatrol(ActionArguments args) { InvocationCount++; LastActionName = "Patrol"; LastArgument = args.First; }
        public void OnRetreat(ActionArguments args) { InvocationCount++; LastActionName = "Retreat"; LastArgument = args.First; }
        public void OnHoldPosition(ActionArguments args) { InvocationCount++; LastActionName = "HoldPosition"; LastArgument = args.First; }
    }

    /// <summary>
    /// A controllable backend that records requests. Without a handler it returns a failure,
    /// allowing tests to cover fallback behavior without starting a server.
    /// </summary>
    public class StubBackend : MonoBehaviour, ILLMBackend
    {
        public System.Func<LLMRequest, CancellationToken, Task<LLMResponse>> Handler;
        public readonly List<LLMRequest> Requests = new List<LLMRequest>();
        public Task<LLMResponse> CompleteAsync(LLMRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Handler != null) return Handler(request, cancellationToken);
            return Task.FromResult(LLMResponse.Failed("stub backend: no request should reach here"));
        }
    }

    /// <summary>A module that reports whatever it is told to, for composition tests.</summary>
    public class FakeObservationModule : MonoBehaviour, IObservationModule
    {
        public string topicName;
        public string observation;
        public bool interrupt;
        public string TopicName => topicName;
        public bool HasInterrupt() { bool pending = interrupt; interrupt = false; return pending; }
        public string GetObservation() => observation;
    }
}
