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
        public void ThreePresetsShip()
        {
            Assert.AreEqual(3, LoadPresets().Length, $"expected the three measured presets in {PresetFolder}");
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
