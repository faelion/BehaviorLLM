using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The launch command for each paradigm, and recognising when a newer llama-server is ready.
    /// Both broke silently once already: a decision model loaded with <c>--cache-reuse</c> just
    /// logs that it ignored it, and b11429 stopped printing the "is listening" line the client
    /// waited for.
    /// </summary>
    public class ServerLaunchArgsTests
    {
        private BehaviorLLMServerConfig settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<BehaviorLLMServerConfig>();
            settings.parallelSlots = 4;
            settings.cacheReuse = 256;
            settings.useJinja = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void ChatModel_GetsCacheReuseAndTheTemplate()
        {
            string args = BehaviorLLMServer.BuildArgs("m.gguf", 8192, 8080, 99, settings, decisionModel: false);

            StringAssert.Contains("-np 4", args);
            StringAssert.Contains("--cache-reuse 256", args);
            StringAssert.Contains("--jinja", args);
        }

        [Test]
        public void DecisionModel_KeepsTheSlotsAndDropsWhatOnlyAWritingModelUses()
        {
            string args = BehaviorLLMServer.BuildArgs("kev.gguf", 8192, 8080, 99, settings, decisionModel: true);

            StringAssert.Contains("-np 4", args);
            StringAssert.DoesNotContain("--cache-reuse", args);
            StringAssert.DoesNotContain("--jinja", args);
            StringAssert.Contains("-b 2048 -ub 2048", args);
        }

        [Test]
        public void ChatModel_KeepsTheServersDefaultBatch()
        {
            StringAssert.DoesNotContain("-ub", BehaviorLLMServer.BuildArgs("m.gguf", 8192, 8080, 99, settings, decisionModel: false));
        }

        [Test]
        public void ATooLargeMenu_NamesTheSettingToRaise()
        {
            string message = BehaviorLLMClient.DescribeDecisionFailure(400,
                "{\"error\":{\"code\":400,\"message\":\"the question and its options (1115 tokens) are too large to process. increase the batch size (current batch size: 512)\"}}",
                "http://x/v1/systemone", "400");
            StringAssert.Contains("Decision Model Batch Tokens", message);
        }

        [Test]
        public void ReadyLine_IsRecognisedInOldAndNewBuilds()
        {
            Assert.IsTrue(BehaviorLLMServer.IsServerReadyLine("main: server is listening on http://127.0.0.1:8080 - starting the main loop"));
            Assert.IsTrue(BehaviorLLMServer.IsServerReadyLine("0.03.318.533 I srv  llama_server: listening on http://127.0.0.1:8091"));
            Assert.IsFalse(BehaviorLLMServer.IsServerReadyLine("0.03.314.572 I srv    load_model: initializing, n_slots = 4"));
        }
    }
}
