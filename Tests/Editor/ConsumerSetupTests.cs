using System.IO;
using System.Reflection;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>Consumer configuration and installed-package authoring regressions.</summary>
    public class ConsumerSetupTests
    {
        [TestCase("Packages/com.faelion.behaviorllm/Samples/PrisonYard", "Assets/BehaviorLLMSamples/PrisonYard")]
        [TestCase("Assets/RenamedPackage/Samples/PrisonYard", "Assets/RenamedPackage/Samples/PrisonYard")]
        public void GeneratedSampleAssetsHaveAWritableRoot(string source, string expected)
        {
            Assert.AreEqual(expected, SampleAssetPaths.OutputRoot(source, "PrisonYard"));
        }

        [Test]
        public void ProjectSettingsOverrideShippedDefaultsWithoutChangingThePreset()
        {
            BehaviorLLMSettings shipped = Resources.Load<BehaviorLLMSettings>(BehaviorLLMSettings.DefaultResourcePath);
            Assert.IsNotNull(shipped);
            if (Resources.Load<BehaviorLLMSettings>(BehaviorLLMSettings.ResourceName) != null)
                Assert.Ignore("The consuming project already has a settings override.");
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/BehaviorLLMSettingsTest");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            AssetDatabase.CreateFolder(folder, "Resources");
            try
            {
                var custom = ScriptableObject.CreateInstance<BehaviorLLMSettings>();
                custom.editorLogLevel = BehaviorLLMLogLevel.Off;
                AssetDatabase.CreateAsset(custom, folder + "/Resources/BehaviorLLMSettings.asset");
                AssetDatabase.SaveAssets();
                BehaviorLLMSettings.Use(null);
                Assert.AreSame(custom, BehaviorLLMSettings.Current);
                Assert.AreEqual(BehaviorLLMLogLevel.Warnings, shipped.editorLogLevel);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                BehaviorLLMSettings.Use(null);
            }
            Assert.AreSame(shipped, BehaviorLLMSettings.Current);
        }

        [Test]
        public void FreshServerFollowsCatalogButExplicitModelStillWins()
        {
            GameObject host = new GameObject("CatalogPrecedenceTest");
            var serverConfig = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            var customModel = ScriptableObject.CreateInstance<BehaviorLLMModelConfig>();
            serverConfig.configFileName = "behaviorllm_test_" + System.Guid.NewGuid().ToString("N") + ".json";
            string path = Path.Combine(Application.streamingAssetsPath, serverConfig.configFileName);
            Directory.CreateDirectory(Application.streamingAssetsPath);
            try
            {
                File.WriteAllText(path, "{\"activeModelRelativePath\":\"models/catalog-choice.gguf\"}");
                var server = host.AddComponent<BehaviorLLMServer>();
                var field = typeof(BehaviorLLMServer).GetField("modelConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                var preset = (BehaviorLLMModelConfig)field.GetValue(server);
                Assert.IsNotNull(preset);
                Assert.IsFalse(preset.HasModelFile, "The automatic preset must not pin a different model.");
                server.ApplyConfig(serverConfig, preset);
                Assert.AreEqual("models/catalog-choice.gguf", ResolveModel(server));
                customModel.modelFileName = "custom.gguf";
                server.ApplyConfig(serverConfig, customModel);
                Assert.AreEqual("models/custom.gguf", ResolveModel(server));
            }
            finally
            {
                File.Delete(path);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(serverConfig);
                Object.DestroyImmediate(customModel);
            }
        }

        private static string ResolveModel(BehaviorLLMServer server)
        {
            object result = typeof(BehaviorLLMServer).GetMethod("ResolveRuntimeConfig", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(server, null);
            return (string)result.GetType().GetField("modelRelativePath").GetValue(result);
        }
    }
}
