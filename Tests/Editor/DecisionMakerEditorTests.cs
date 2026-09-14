using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Editor.Inspectors;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The inspector keeps <c>DecisionMaker.actionBindings</c> as a mirror of the Action Config.
    /// These cover the reconciliation itself, which is the part that could lose a user's wiring:
    /// order follows the config, wiring survives by name, unwired leftovers vanish, wired
    /// leftovers are kept where the inspector can flag them.
    /// </summary>
    public class DecisionMakerEditorTests
    {
        private GameObject host;
        private DecisionMaker maker;
        private Receiver receiver;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("EditorTestHost");
            receiver = host.AddComponent<Receiver>();
            maker = host.AddComponent<DecisionMaker>();
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        [Test]
        public void EmptyList_GetsOneEntryPerConfigActionInOrder()
        {
            ActionConfig config = Config("Patrol", "Chase", "HoldPosition");

            int orphanStart = Run(config);

            CollectionAssert.AreEqual(new[] { "Patrol", "Chase", "HoldPosition" }, Names());
            Assert.AreEqual(3, orphanStart);
            Assert.AreEqual(0, maker.actionBindings[0].onExecute.GetPersistentEventCount(), "new entries start empty");
        }

        [Test]
        public void ExistingWiring_SurvivesReordering()
        {
            AddBinding("HoldPosition", wired: true);
            AddBinding("Patrol", wired: true);
            ActionConfig config = Config("Patrol", "Chase", "HoldPosition");

            Run(config);

            CollectionAssert.AreEqual(new[] { "Patrol", "Chase", "HoldPosition" }, Names());
            Assert.AreEqual(1, maker.actionBindings[0].onExecute.GetPersistentEventCount(), "Patrol keeps its wiring");
            Assert.AreEqual(0, maker.actionBindings[1].onExecute.GetPersistentEventCount(), "Chase is new and empty");
            Assert.AreEqual(1, maker.actionBindings[2].onExecute.GetPersistentEventCount(), "HoldPosition keeps its wiring");
        }

        [Test]
        public void UnwiredEntryForARemovedAction_IsDropped()
        {
            AddBinding("Patrol", wired: false);
            AddBinding("Teleport", wired: false);

            int orphanStart = Run(Config("Patrol"));

            CollectionAssert.AreEqual(new[] { "Patrol" }, Names());
            Assert.AreEqual(1, orphanStart);
        }

        [Test]
        public void WiredEntryForARemovedAction_IsKeptAfterTheConfigEntries()
        {
            AddBinding("Teleport", wired: true);
            AddBinding("Patrol", wired: true);

            int orphanStart = Run(Config("Patrol", "Chase"));

            CollectionAssert.AreEqual(new[] { "Patrol", "Chase", "Teleport" }, Names());
            Assert.AreEqual(2, orphanStart, "orphans start right after the config's actions");
            Assert.AreEqual(1, maker.actionBindings[2].onExecute.GetPersistentEventCount(), "the orphan's wiring is intact");
        }

        [Test]
        public void NameCase_IsNormalisedToTheConfig()
        {
            AddBinding("patrol", wired: true);

            Run(Config("Patrol"));

            Assert.AreEqual("Patrol", maker.actionBindings[0].actionName);
            Assert.AreEqual(1, maker.actionBindings[0].onExecute.GetPersistentEventCount());
        }

        [Test]
        public void ConfigWithBlankAndDuplicateNames_IsCollapsed()
        {
            ActionConfig config = Config("Patrol", "", "patrol", "Chase");

            List<ActionDefinition> actions = DecisionMakerEditor.CollectActions(config);

            CollectionAssert.AreEqual(new[] { "Patrol", "Chase" }, actions.ConvertAll(a => a.actionName));
        }

        [Test]
        public void RunningTwice_ChangesNothing()
        {
            AddBinding("Chase", wired: true);
            ActionConfig config = Config("Patrol", "Chase");

            Run(config);
            string[] first = Names();
            Run(config);

            CollectionAssert.AreEqual(first, Names());
            Assert.AreEqual(1, maker.actionBindings[1].onExecute.GetPersistentEventCount());
        }

        // ------------------------------------------------------------------ helpers

        private int Run(ActionConfig config)
        {
            SerializedObject so = new SerializedObject(maker);
            so.Update();
            int orphanStart = DecisionMakerEditor.Reconcile(so.FindProperty("actionBindings"), DecisionMakerEditor.CollectActions(config));
            so.ApplyModifiedPropertiesWithoutUndo();
            return orphanStart;
        }

        private string[] Names()
        {
            return maker.actionBindings.ConvertAll(b => b.actionName).ToArray();
        }

        private void AddBinding(string name, bool wired)
        {
            ActionEvent evt = new ActionEvent();
            if (wired) UnityEventTools.AddPersistentListener(evt, receiver.Handle);
            maker.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = name, onExecute = evt });
        }

        private static ActionConfig Config(params string[] names)
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            config.validActions = new List<ActionDefinition>();
            foreach (string n in names)
            {
                config.validActions.Add(new ActionDefinition { actionName = n, description = n, parameterType = ActionParameterType.None });
            }
            return config;
        }

        public class Receiver : MonoBehaviour
        {
            public void Handle(ActionArguments arg) { }
        }
    }
}
