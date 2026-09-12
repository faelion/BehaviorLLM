using System.Reflection;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// Regression cover for config precedence on <see cref="BehaviorLLMServer"/>.
    ///
    /// The bug this exists for: the StreamingAssets file was read first and returned early when it
    /// was disabled or absent, so an assigned model config was never applied and the server tried
    /// to launch the placeholder `models/my-model.gguf`. It only showed up when the sample was
    /// actually played on a machine where the Model Catalog had never written that file.
    /// </summary>
    public class ServerConfigResolutionTests
    {
        private GameObject host;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("ServerUnderTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void RejectedSchema_FailsBeforeSendingAnUnconstrainedRequest()
        {
            BehaviorLLMServer server = host.AddComponent<BehaviorLLMServer>();
            typeof(BehaviorLLMServer).GetProperty("GrammarLoadFailed").GetSetMethod(true).Invoke(server, new object[] { true });
            BehaviorLLMServerConfig config = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            config.autoStartServerOnFirstRequest = false;
            config.followManagedServerEndpoint = false;
            BehaviorLLMClient client = host.AddComponent<BehaviorLLMClient>();
            typeof(BehaviorLLMClient).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(client, server);
            client.ApplyConfig(config, null);
            try
            {
                var pending = client.CompleteAsync(new LLMRequest { JsonSchema = "{\"type\":\"object\"}" });
                Assert.IsTrue(pending.IsCompleted, "Schema rejection must fail before any HTTP request.");
                LLMResponse response = pending.GetAwaiter().GetResult();
                Assert.IsFalse(response.Succeeded);
                Assert.IsFalse(response.StructuredOutput);
                StringAssert.Contains("rejected", response.Error);
            }
            finally { Object.DestroyImmediate(config); }
        }

        private static string ResolvedModelPath(BehaviorLLMServer server)
        {
            MethodInfo resolve = typeof(BehaviorLLMServer).GetMethod("ResolveRuntimeConfig", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(resolve, "ResolveRuntimeConfig is the method under test");
            object cfg = resolve.Invoke(server, null);
            FieldInfo path = cfg.GetType().GetField("modelRelativePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(path, "RuntimeServerConfig.modelRelativePath");
            return (string)path.GetValue(cfg);
        }

        private BehaviorLLMServer ServerWithModelConfig(bool useStreamingConfig, string modelFile = "granite-4.1-3b-Q4_K_M.gguf")
        {
            BehaviorLLMModelConfig model = ScriptableObject.CreateInstance<BehaviorLLMModelConfig>();
            model.displayName = "Test model";
            model.modelFileName = modelFile;
            model.contextSize = 4096;
            model.gpuLayers = 42;

            BehaviorLLMServerConfig serverCfg = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            serverCfg.useStreamingConfig = useStreamingConfig;
            // A file name that cannot exist, so the StreamingAssets branch always yields null.
            serverCfg.configFileName = "definitely_missing_backend_config.json";

            BehaviorLLMServer server = host.AddComponent<BehaviorLLMServer>();
            SerializedObjectSet(server, "modelConfig", model);
            SerializedObjectSet(server, "serverConfig", serverCfg);
            return server;
        }

        private static void SerializedObjectSet(Object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"field '{field}'");
            f.SetValue(target, value);
        }

        [Test]
        public void ModelConfigApplies_WhenTheStreamingConfigIsMissing()
        {
            BehaviorLLMServer server = ServerWithModelConfig(useStreamingConfig: true);
            Assert.AreEqual("models/granite-4.1-3b-Q4_K_M.gguf", ResolvedModelPath(server));
        }

        [Test]
        public void ModelConfigApplies_WhenTheStreamingConfigIsDisabled()
        {
            BehaviorLLMServer server = ServerWithModelConfig(useStreamingConfig: false);
            Assert.AreEqual("models/granite-4.1-3b-Q4_K_M.gguf", ResolvedModelPath(server));
        }

        [Test]
        public void AFreshComponentArrivesWiredToTheShippedPresets()
        {
            // Adding the component in the Editor runs Reset(), which points it at the config
            // assets the package ships. This is what lets someone drop a server into a scene and
            // press play without authoring anything, so it is worth guarding.
            BehaviorLLMServer server = host.AddComponent<BehaviorLLMServer>();

            Assert.IsNotNull(PrivateField<BehaviorLLMServerConfig>(server, "serverConfig"), "server config preset");
            Assert.IsNotNull(PrivateField<BehaviorLLMModelConfig>(server, "modelConfig"), "model config preset");
        }

        [Test]
        public void WithNoConfigAtAllAndNoStreamingFile_NoModelIsResolved()
        {
            // There is nowhere left to hide a stale default: with every config cleared and no
            // StreamingAssets file, the server resolves nothing and reports the actionable
            // "download a model" error instead of launching against a placeholder path.
            BehaviorLLMServerConfig serverCfg = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            serverCfg.useStreamingConfig = false;

            BehaviorLLMServer server = host.AddComponent<BehaviorLLMServer>();
            SerializedObjectSet(server, "serverConfig", serverCfg);
            SerializedObjectSet(server, "modelConfig", null);

            Assert.IsEmpty(ResolvedModelPath(server));
        }

        private static T PrivateField<T>(Object target, string field) where T : class
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"field '{field}'");
            return f.GetValue(target) as T;
        }

        [Test]
        public void TheStreamingFileNameComesFromTheServerConfig()
        {
            BehaviorLLMServerConfig serverCfg = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            serverCfg.useStreamingConfig = true;
            serverCfg.configFileName = "definitely_missing_backend_config.json";

            BehaviorLLMModelConfig model = ScriptableObject.CreateInstance<BehaviorLLMModelConfig>();
            model.modelFileName = "from-the-asset.gguf";

            BehaviorLLMServer server = host.AddComponent<BehaviorLLMServer>();
            SerializedObjectSet(server, "serverConfig", serverCfg);
            SerializedObjectSet(server, "modelConfig", model);

            // The named file does not exist, so the model config is the only source left.
            Assert.AreEqual("models/from-the-asset.gguf", ResolvedModelPath(server));
        }

        [Test]
        public void ModelConfigAlsoCarriesContextAndGpuLayers()
        {
            BehaviorLLMServer server = ServerWithModelConfig(useStreamingConfig: false);
            MethodInfo resolve = typeof(BehaviorLLMServer).GetMethod("ResolveRuntimeConfig", BindingFlags.Instance | BindingFlags.NonPublic);
            object cfg = resolve.Invoke(server, null);

            FieldInfo ctx = cfg.GetType().GetField("contextSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo ngl = cfg.GetType().GetField("gpuLayers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.AreEqual(4096, (int)ctx.GetValue(cfg));
            Assert.AreEqual(42, (int)ngl.GetValue(cfg));
        }
    }
}
