using System;
using System.Linq;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The Editor test assembly references both the Runtime and Editor BehaviorLLM assemblies,
    /// so a green run here proves the whole reference graph compiles. The assertions are
    /// deliberately light; behaviour is covered by the Runtime tests.
    /// </summary>
    public class EditorAssemblyGraphTests
    {
        [Test]
        public void EditorAssembly_IsLoaded_AndExposesModelManagerWindow()
        {
            var editorAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BehaviorLLM.Editor");
            Assert.IsNotNull(editorAssembly, "BehaviorLLM.Editor assembly not loaded");
            Assert.IsTrue(editorAssembly.GetTypes().Any(t => t.Name == "BehaviorLLMModelManagerWindow"));
        }

        [Test]
        public void RuntimeSchemaBuilder_IsReachableFromEditorCode()
        {
            ActionConfig config = ScriptableObject.CreateInstance<ActionConfig>();
            try
            {
                config.validActions.Add(new ActionDefinition { actionName = "Stop", parameterType = ActionParameterType.None });
                StringAssert.Contains("\"const\":\"Stop\"", ActionSchemaBuilder.Build(config));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
