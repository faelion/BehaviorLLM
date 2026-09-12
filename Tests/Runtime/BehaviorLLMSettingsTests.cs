using BehaviorLLM.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The project-wide settings asset and the log gate every console line goes through.
    ///
    /// The point of these is the two invariants that are easy to break later: the package must work
    /// with no settings asset at all, and the log gate must be a ceiling rather than a switch that
    /// can turn something on that its own config turned off.
    /// </summary>
    public class BehaviorLLMSettingsTests
    {
        private BehaviorLLMSettings scratch;

        [SetUp]
        public void SetUp()
        {
            scratch = ScriptableObject.CreateInstance<BehaviorLLMSettings>();
            BehaviorLLMSettings.Use(scratch);
        }

        [TearDown]
        public void TearDown()
        {
            BehaviorLLMSettings.Use(null);
            if (scratch != null) Object.DestroyImmediate(scratch);
        }

        [Test]
        public void NoAssetInProject_StillReturnsUsableDefaults()
        {
            BehaviorLLMSettings.Use(null);
            Object.DestroyImmediate(scratch);
            scratch = null;

            BehaviorLLMSettings settings = BehaviorLLMSettings.Current;

            Assert.IsNotNull(settings, "Current must never be null; callers read it without a null check.");
            Assert.AreEqual(BehaviorLLMLogLevel.Warnings, settings.editorLogLevel);
            Assert.AreEqual(BehaviorLLMLogLevel.ErrorsOnly, settings.playerLogLevel);
        }

        [Test]
        public void Defaults_AllowWarningsAndErrorsButNotPerDecisionChatter()
        {
            Assert.IsTrue(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.ErrorsOnly));
            Assert.IsTrue(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Warnings));
            Assert.IsFalse(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Verbose),
                "Verbose is opt-in; a default project should not print a line per decision.");
        }

        [Test]
        public void ConfigSwitches_WorkAtTheDefaultLevel()
        {
            // The regression this guards: routing every line through the gate once made ticking
            // `logPrompts` on a config do nothing until the project level was also raised, which is
            // a trap. A line the author explicitly asked for prints at the normal level.
            int printed = 0;
            BehaviorLLMLog.Requested(() => { printed++; return "asked for"; });

            Assert.AreEqual(1, printed);
        }

        [Test]
        public void ErrorsOnly_SilencesTheConfigSwitchesToo()
        {
            scratch.editorLogLevel = BehaviorLLMLogLevel.ErrorsOnly;
            int printed = 0;

            BehaviorLLMLog.Requested(() => { printed++; return "asked for"; });

            Assert.AreEqual(0, printed,
                "Dropping the project level is how a whole project is quietened without visiting every config.");
        }

        [Test]
        public void Off_SilencesEvenErrors()
        {
            scratch.editorLogLevel = BehaviorLLMLogLevel.Off;

            Assert.IsFalse(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.ErrorsOnly));
            Assert.IsFalse(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Warnings));
            Assert.IsFalse(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Verbose));
        }

        [Test]
        public void Verbose_AllowsEveryLevel()
        {
            scratch.editorLogLevel = BehaviorLLMLogLevel.Verbose;

            Assert.IsTrue(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.ErrorsOnly));
            Assert.IsTrue(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Warnings));
            Assert.IsTrue(BehaviorLLMLog.Allows(BehaviorLLMLogLevel.Verbose));
        }

        [Test]
        public void SuppressedMessage_IsNeverBuilt()
        {
            scratch.editorLogLevel = BehaviorLLMLogLevel.ErrorsOnly;
            int built = 0;

            BehaviorLLMLog.Info(() => { built++; return "expensive"; });
            BehaviorLLMLog.Warn(() => { built++; return "expensive"; });

            Assert.AreEqual(0, built,
                "The whole point of passing a closure is that a suppressed line costs no string building.");
        }

        [Test]
        public void TelemetryMasterSwitch_VetoesEvenInTheEditor()
        {
            scratch.telemetryEnabled = false;
            scratch.telemetryInBuilds = true;

            Assert.IsFalse(scratch.TelemetryAllowed);
        }

        [Test]
        public void TelemetryDefaults_RecordInTheEditorOnly()
        {
            // Tests run in the Editor, so this is the "on in the Editor" half of the default.
            Assert.IsTrue(scratch.telemetryEnabled);
            Assert.IsFalse(scratch.telemetryInBuilds);
            Assert.IsTrue(scratch.TelemetryAllowed);
        }

        [Test]
        public void ActiveLogLevel_UsesTheEditorLevelWhileInTheEditor()
        {
            scratch.editorLogLevel = BehaviorLLMLogLevel.Verbose;
            scratch.playerLogLevel = BehaviorLLMLogLevel.Off;

            Assert.AreEqual(BehaviorLLMLogLevel.Verbose, scratch.ActiveLogLevel);
        }
    }
}
