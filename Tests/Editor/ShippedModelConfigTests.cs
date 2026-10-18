using System.Linq;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Config;
using NUnit.Framework;
using UnityEditor;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The shipped model presets carry measurements, not opinions. These tests guard the
    /// provenance: if someone edits an asset, the numbers and the notes have to stay coherent,
    /// because <c>DecisionMaker</c> warns users based on them.
    /// </summary>
    public class ShippedModelConfigTests
    {
        private static string PresetFolder
        {
            get
            {
                var preset = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(BehaviorLLMDefaults.ModelConfigAsset);
                return System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(preset)).Replace('\\', '/') + "/Models";
            }
        }

        private static BehaviorLLMModelConfig[] LoadPresets()
        {
            return AssetDatabase.FindAssets("t:BehaviorLLMModelConfig", new[] { PresetFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BehaviorLLMModelConfig>)
                .Where(c => c != null)
                .ToArray();
        }

        [Test]
        public void FourPresetsShip()
        {
            Assert.AreEqual(4, LoadPresets().Length, $"expected the four measured presets in {PresetFolder}");
        }

        [Test]
        public void TheDecisionModelPresetIsD1WithItsMeasuredBatch()
        {
            // The package's default paradigm is Decision Model, so exactly one shipped preset must
            // serve it, and with the batch it was measured at: 2048 cost d1 0.24 GB and evened out
            // its latency under load.
            BehaviorLLMModelConfig[] decision = LoadPresets().Where(c => c.paradigm == DecisionParadigm.DecisionModel).ToArray();
            Assert.AreEqual(1, decision.Length, "one shipped decision-model preset");
            StringAssert.StartsWith("d1-3B", decision[0].modelFileName);
            Assert.AreEqual(2048, decision[0].decisionBatchTokens);
            // The head is what d1's template writes before the state: without it every decision
            // rereads the persona and the guide, which cost StealthGuard 174 ms against 110 ms.
            Assert.AreEqual("<|startoftext|><|im_start|>user\n", decision[0].decisionPrefixHead);
            Assert.AreEqual(BehaviorLLM.Editor.ModelManager.BehaviorLLMModelPresets.DecisionPrefixHeadFor("lfm2-d1"), decision[0].decisionPrefixHead);
            Assert.AreEqual(string.Empty, BehaviorLLM.Editor.ModelManager.BehaviorLLMModelPresets.DecisionPrefixHeadFor("kev"));
            Assert.IsTrue(LoadPresets().Where(c => c.paradigm != DecisionParadigm.DecisionModel).All(c => string.IsNullOrEmpty(c.decisionPrefixHead)),
                          "chat models never prime");
        }

        [Test]
        public void EveryPresetIsUsableAndCitesItsMeasurement()
        {
            foreach (BehaviorLLMModelConfig c in LoadPresets())
            {
                Assert.IsTrue(c.HasModelFile, $"{c.name}: needs a model file name");
                StringAssert.EndsWith(".gguf", c.modelFileName, $"{c.name}: model file should be a GGUF");
                Assert.Greater(c.contextSize, 0, $"{c.name}: context size");
                Assert.Greater(c.maxTokens, 0, $"{c.name}: token ceiling");
                Assert.IsNotEmpty(c.license, $"{c.name}: licence must be recorded before shipping weights");
                StringAssert.Contains("Measured", c.measurementNotes, $"{c.name}: notes must say where the numbers came from");
                Assert.Greater(c.measuredP50LatencyMs, 0f, $"{c.name}: latency measurement");
                Assert.Greater(c.measuredValidActionRate, 0f, $"{c.name}: valid-action measurement");
            }
        }

        [Test]
        public void NativeThinkingIsOffOnEveryPreset()
        {
            // Every model measured so far restated the prompt instead of deciding when its native
            // thinking phase was enabled. If a preset ever flips this, it needs a run behind it.
            foreach (BehaviorLLMModelConfig c in LoadPresets())
            {
                Assert.IsFalse(c.nativeThinkingUsable,
                    $"{c.name}: no measured model has produced usable decisions while thinking; back this with a run before enabling");
            }
        }

        [Test]
        public void OnlyTheLargestPresetRecommendsDeliberation()
        {
            BehaviorLLMModelConfig[] presets = LoadPresets();
            BehaviorLLMModelConfig[] deliberative = presets.Where(c => c.recommendedProfile == DecisionProfile.Deliberative).ToArray();

            Assert.AreEqual(1, deliberative.Length, "only Qwen3.5-4B measured better with deliberation");
            Assert.AreEqual(presets.Max(c => c.parametersB), deliberative[0].parametersB,
                "the deliberative recommendation should be on the largest preset");
        }
    }
}
