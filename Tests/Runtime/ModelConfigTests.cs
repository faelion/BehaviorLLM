using BehaviorLLM.Core.Config;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    public class ModelConfigTests
    {
        private BehaviorLLMModelConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<BehaviorLLMModelConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void BareFileName_ResolvesUnderTheModelsFolder()
        {
            config.modelFileName = "Qwen3.5-2B-Q4_K_M.gguf";
            Assert.AreEqual("models/Qwen3.5-2B-Q4_K_M.gguf", config.ResolveModelRelativePath());
            Assert.IsTrue(config.HasModelFile);
        }

        [Test]
        public void RelativePath_IsKeptAsAuthored()
        {
            config.modelFileName = "custom/place/model.gguf";
            Assert.AreEqual("custom/place/model.gguf", config.ResolveModelRelativePath());
        }

        [Test]
        public void BackslashesAreNormalised()
        {
            config.modelFileName = @"custom\place\model.gguf";
            Assert.AreEqual("custom/place/model.gguf", config.ResolveModelRelativePath());
        }

        [Test]
        public void AbsolutePath_IsKept()
        {
            config.modelFileName = @"D:\models\model.gguf";
            Assert.AreEqual("D:/models/model.gguf", config.ResolveModelRelativePath());
        }

        [Test]
        public void NoFileName_IsEmptyAndFlagged()
        {
            Assert.IsFalse(config.HasModelFile);
            Assert.AreEqual(string.Empty, config.ResolveModelRelativePath());

            config.modelFileName = "   ";
            Assert.IsFalse(config.HasModelFile);
            Assert.AreEqual(string.Empty, config.ResolveModelRelativePath());
        }
    }
}
