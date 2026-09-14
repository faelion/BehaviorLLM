using System.Collections.Generic;
using System.Reflection;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// Two guards on the decision loop itself: refusing a prompt budget that cannot work, and
    /// spreading decisions out in time so characters sharing a config do not ask together.
    ///
    /// Both come from the future lines in the thesis. The budget one closes a silent failure: an
    /// impossible budget used to be clamped to 64 characters of state, so the character decided
    /// without being told what it could see and nothing in the telemetry looked wrong.
    /// </summary>
    public class DecisionLoopGuardTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in cleanup)
                if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
        }

        private static ActionConfig MakeConfig()
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            config.validActions.Add(new ActionDefinition
            {
                actionName = "Patrol",
                description = "Walk the route",
                parameterType = ActionParameterType.None
            });
            return config;
        }

        /// <summary>
        /// Silences the package while a deliberately bad configuration is applied. Uses the
        /// project's own log ceiling rather than the test framework's, which also demonstrates
        /// that the ceiling really does gate an error.
        /// </summary>
        private DecisionMaker MakeMakerExpectingRefusal(DecisionMakerConfig cfg)
        {
            BehaviorLLMSettings quiet = ScriptableObject.CreateInstance<BehaviorLLMSettings>();
            quiet.editorLogLevel = BehaviorLLMLogLevel.Off;
            quiet.playerLogLevel = BehaviorLLMLogLevel.Off;
            cleanup.Add(quiet);
            BehaviorLLMSettings.Use(quiet);
            try { return MakeMaker(cfg); }
            finally { BehaviorLLMSettings.Use(null); }
        }

        private DecisionMaker MakeMaker(DecisionMakerConfig cfg)
        {
            GameObject go = new GameObject("LoopGuardFixture");
            cleanup.Add(go);
            // Added before the DecisionMaker so its Awake resolves a backend on the same object.
            go.AddComponent<StubBackend>();
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
            ActionConfig actions = MakeConfig();
            cleanup.Add(actions);
            maker.actionConfig = actions;
            maker.ApplyConfig(cfg);
            return maker;
        }

        private static DecisionMakerConfig MakeCfg(int maxPromptChars, float jitter, float interval = 2f)
        {
            DecisionMakerConfig cfg = ScriptableObject.CreateInstance<DecisionMakerConfig>();
            cfg.maxPromptChars = maxPromptChars;
            cfg.decisionIntervalJitter = jitter;
            cfg.decisionInterval = interval;
            return cfg;
        }

        private static float TimerOf(DecisionMaker maker)
        {
            FieldInfo f = typeof(DecisionMaker).GetField("timer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "DecisionMaker.timer field not found");
            return (float)f.GetValue(maker);
        }

        private static int SystemPromptLength(DecisionMaker maker)
        {
            FieldInfo f = typeof(DecisionMaker).GetField("cachedSystemPrompt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "DecisionMaker.cachedSystemPrompt field not found");
            return ((string)f.GetValue(maker)).Length;
        }

        // ------------------------------------------------------------------ prompt budget

        [Test]
        public void BudgetLeavingNoRoomForObservations_DisablesTheComponent()
        {
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 10, jitter: 0f);
            cleanup.Add(cfg);
            DecisionMaker maker = MakeMakerExpectingRefusal(cfg);

            Assert.IsFalse(maker.enabled,
                "A budget of 10 characters cannot carry the system prompt, so the component " +
                "must refuse rather than decide without observations.");
        }

        [Test]
        public void BudgetWithAmpleRoom_LeavesTheComponentRunning()
        {
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 0, jitter: 0f);
            cleanup.Add(cfg);
            DecisionMaker maker = MakeMaker(cfg);
            int needed = SystemPromptLength(maker) + cfg.minStatePromptChars + 500;

            DecisionMakerConfig generous = MakeCfg(maxPromptChars: needed, jitter: 0f);
            cleanup.Add(generous);
            DecisionMaker ok = MakeMaker(generous);

            Assert.IsTrue(ok.enabled, "A budget with room to spare must not be refused.");
        }

        [Test]
        public void NoBudget_IsNeverRefused()
        {
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 0, jitter: 0f);
            cleanup.Add(cfg);
            Assert.IsTrue(MakeMaker(cfg).enabled, "0 means no limit and must always be allowed.");
        }

        // ------------------------------------------------------------------ jitter

        [Test]
        public void WithoutJitter_EveryCharacterStartsInLockstep()
        {
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 0, jitter: 0f);
            cleanup.Add(cfg);
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(0f, TimerOf(MakeMaker(cfg)), 1e-6f,
                    "Jitter 0 must reproduce the original lockstep behaviour exactly.");
        }

        [Test]
        public void WithJitter_CharactersStartAtDifferentPointsInTheInterval()
        {
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 0, jitter: 0.5f, interval: 2f);
            cleanup.Add(cfg);

            HashSet<float> starts = new HashSet<float>();
            for (int i = 0; i < 12; i++)
            {
                float t = TimerOf(MakeMaker(cfg));
                Assert.GreaterOrEqual(t, 0f, "A first-tick offset must never be negative.");
                Assert.LessOrEqual(t, cfg.decisionInterval * cfg.decisionIntervalJitter + 1e-4f,
                    "The offset must stay inside the configured fraction of the interval.");
                starts.Add(t);
            }

            // Twelve draws from a continuous range landing on one value would mean no spread.
            Assert.Greater(starts.Count, 1,
                "Characters sharing a config must not all start at the same point.");
        }

        [Test]
        public void JitterIsCentredSoTheAverageRateIsUnchanged()
        {
            // The reset after each decision is centred on zero rather than delaying, so jitter
            // buys a steady load without costing decisions per minute.
            DecisionMakerConfig cfg = MakeCfg(maxPromptChars: 0, jitter: 0.4f, interval: 2f);
            cleanup.Add(cfg);
            DecisionMaker maker = MakeMaker(cfg);

            MethodInfo reset = typeof(DecisionMaker).GetMethod("ResetTimer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(reset, "DecisionMaker.ResetTimer not found");

            float sum = 0f;
            bool sawNegative = false, sawPositive = false;
            const int draws = 400;
            for (int i = 0; i < draws; i++)
            {
                reset.Invoke(maker, null);
                float t = TimerOf(maker);
                sum += t;
                if (t < 0f) sawNegative = true;
                if (t > 0f) sawPositive = true;
            }

            Assert.IsTrue(sawNegative && sawPositive,
                "A centred reset must land on both sides of the configured interval.");
            float bound = cfg.decisionInterval * cfg.decisionIntervalJitter;
            Assert.That(sum / draws, Is.EqualTo(0f).Within(bound * 0.25f),
                "The mean offset must sit near zero, or jitter would change the decision rate.");
        }
    }
}
