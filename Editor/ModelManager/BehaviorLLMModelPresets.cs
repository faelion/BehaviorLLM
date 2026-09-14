using System;
using System.Collections.Generic;
using System.IO;
using BehaviorLLM.Core.Config;
using UnityEditor;
using UnityEngine;

namespace BehaviorLLM.Editor.ModelManager
{
    /// <summary>
    /// Connects downloaded files to the <see cref="BehaviorLLMModelConfig"/> assets that expect
    /// them.
    ///
    /// This exists because the two drifted apart once already: the catalogue shipped seven models
    /// while the package shipped three presets, and they overlapped in one entry whose download
    /// even arrived under a name its own preset did not look for. Nothing in the window would have
    /// shown that. Now a card can say which preset it satisfies, and a download whose file name
    /// will not match any preset can say so before it is fetched.
    ///
    /// Configs created here go to <see cref="UserConfigFolder"/>, in the project and outside the
    /// package. They used to be written next to the shipped presets, which meant a config made for
    /// a five-minute test sat in the package's own folder as though the project vouched for it,
    /// and the test that counts the shipped presets failed until somebody noticed.
    /// </summary>
    internal static class BehaviorLLMModelPresets
    {
        /// <summary>What every shipped preset uses, and the floor for a config created here.</summary>
        private const int DefaultContextSize = 8192;
        private const int DefaultGpuLayers = 99;

        /// <summary>Where configs made from the window live: the project's, not the package's.</summary>
        internal const string UserConfigFolder = "Assets/BehaviorLLMConfigs/Models";

        internal sealed class Preset
        {
            public BehaviorLLMModelConfig Asset;
            public string AssetPath;
            public string FileName;
            public string DisplayName;
            public bool HasMeasurements;
            /// <summary>True for a preset the package ships, as opposed to one made in this project.</summary>
            public bool IsShipped;
        }

        /// <summary>Every model config in the project, presets and user-authored alike.</summary>
        internal static List<Preset> FindAll()
        {
            List<Preset> found = new List<Preset>();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BehaviorLLMModelConfig));
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BehaviorLLMModelConfig asset = AssetDatabase.LoadAssetAtPath<BehaviorLLMModelConfig>(path);
                if (asset == null) continue;

                found.Add(new Preset
                {
                    Asset = asset,
                    AssetPath = path,
                    FileName = asset.modelFileName ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(asset.displayName) ? asset.name : asset.displayName,
                    // Measured fields are a claim this project makes. A preset created from a
                    // search result leaves them at zero, and that is the difference the window draws.
                    HasMeasurements = asset.measuredValidActionRate > 0f || asset.measuredExpectedActionRate > 0f,
                    IsShipped = IsPackagePath(path)
                });
            }
            found.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// <summary>Whether an asset path is inside the package, embedded or installed.</summary>
        internal static bool IsPackagePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            string p = assetPath.Replace('\\', '/');
            return p.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                   p.IndexOf("/BehaviorLLM/Runtime/Defaults/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The preset expecting this file name, or null when none does.</summary>
        internal static Preset MatchByFileName(IList<Preset> presets, string fileName)
        {
            if (presets == null || string.IsNullOrWhiteSpace(fileName)) return null;
            for (int i = 0; i < presets.Count; i++)
            {
                if (string.Equals(presets[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return presets[i];
            }
            return null;
        }

        /// <summary>
        /// Creates a model config from a search result. Every field the API can honestly fill is
        /// filled; the measured ones are left at zero, because this project has not run the model.
        /// </summary>
        internal static BehaviorLLMModelConfig CreateFromSearchResult(BehaviorLLMHuggingFace.Repository repo,
                                                                      BehaviorLLMHuggingFace.GgufFile file,
                                                                      int contextSize, int gpuLayers)
        {
            if (repo == null || file == null) return null;

            string quant = string.IsNullOrEmpty(file.Quant) ? string.Empty : file.Quant;
            string arch = string.IsNullOrEmpty(repo.Architecture) ? "not stated" : repo.Architecture;
            return Create(
                displayName: $"{repo.Name} ({(quant.Length == 0 ? "GGUF" : quant)})",
                fileName: file.FileName,
                sourceUrl: repo.PageUrl,
                parametersB: repo.ParamsB,
                quantisation: quant,
                contextSize: contextSize,
                gpuLayers: gpuLayers,
                thinking: repo.Thinking,
                provenance: $"Created from a Hugging Face search on {DateTime.Now:yyyy-MM-dd}. " +
                            $"Architecture {arch}, {FormatParams(repo.ParamsB)} parameters" +
                            (repo.ParamsFromHeader ? " (from the GGUF header)." : " (read from the repository name)."));
        }

        /// <summary>
        /// Creates a model config for a file that is already on disk and that no config points
        /// at - a pasted link, or a search result downloaded without one. The header supplies the
        /// architecture; the file name supplies the rest.
        /// </summary>
        internal static BehaviorLLMModelConfig CreateForInstalledFile(string fileName, BehaviorLLMGgufHeader.Info header,
                                                                      int contextSize, int gpuLayers)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string quant = ReadQuant(stem);
            string arch = header != null && header.Architecture.Length > 0 ? header.Architecture : "not stated";
            string display = header != null && header.Name.Length > 0 ? header.Name : stem;
            if (quant.Length > 0 && display.IndexOf(quant, StringComparison.OrdinalIgnoreCase) < 0) display += $" ({quant})";

            return Create(
                displayName: display,
                fileName: fileName,
                sourceUrl: string.Empty,
                parametersB: BehaviorLLMHuggingFace.ReadParamsB(stem),
                quantisation: quant,
                contextSize: contextSize,
                gpuLayers: gpuLayers,
                thinking: header != null ? header.Thinking : BehaviorLLMHuggingFace.ThinkingStyle.Unknown,
                provenance: $"Created from the installed file on {DateTime.Now:yyyy-MM-dd}. " +
                            $"Architecture {arch} (from the GGUF header); size read from the file name.");
        }

        private static BehaviorLLMModelConfig Create(string displayName, string fileName, string sourceUrl, float parametersB,
                                                     string quantisation, int contextSize, int gpuLayers,
                                                     BehaviorLLMHuggingFace.ThinkingStyle thinking, string provenance)
        {
            BehaviorLLMModelConfig asset = ScriptableObject.CreateInstance<BehaviorLLMModelConfig>();
            asset.displayName = displayName;
            asset.modelFileName = fileName;
            asset.sourceUrl = sourceUrl;
            asset.parametersB = parametersB;
            asset.quantisation = quantisation;
            // Recorded from the template so the decision maker can say at Awake that the Reactive
            // profile will not work, rather than the scene running twelve identical decisions
            // first. The profile recommendation follows: a model that always reasons has to run
            // Deliberative with a thinking budget or it never reaches an answer.
            asset.reasoningModel = thinking == BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks;
            if (asset.reasoningModel)
            {
                asset.recommendedProfile = BehaviorLLM.Core.Decisions.DecisionProfile.Deliberative;
                provenance += " Its chat template always opens with a thinking block and has no switch to turn " +
                              "it off, so Reasoning Model is ticked and Deliberative is recommended.";
            }
            // The backend config is often still at its bare defaults the first time somebody
            // uses this - 2048 context and no GPU offload - and inheriting those silently gives a
            // new model a quarter of the context the shipped presets use and CPU-only inference,
            // which makes it look far slower and more forgetful than it is. Fall back to what the
            // measured presets use whenever the backend config has not been deliberately changed.
            asset.contextSize = contextSize >= 4096 ? contextSize : DefaultContextSize;
            asset.gpuLayers = gpuLayers > 0 ? gpuLayers : DefaultGpuLayers;
            asset.measurementNotes =
                provenance + " Not measured by this project: the expected-action and latency fields are " +
                "blank because nobody has run this model through the harness. Fill them in from your " +
                "own run before trusting them.";

            string folder = EnsureUserConfigFolder();
            string safe = MakeAssetNameSafe(fileName);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Model_{safe}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Creates <see cref="UserConfigFolder"/> one level at a time, as the AssetDatabase requires.</summary>
        internal static string EnsureUserConfigFolder()
        {
            string[] parts = UserConfigFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            return current;
        }

        private static string ReadQuant(string stem)
        {
            var m = System.Text.RegularExpressions.Regex.Match(stem ?? string.Empty,
                @"(IQ\d[A-Z0-9_]*|Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Value.ToUpperInvariant() : string.Empty;
        }

        private static string FormatParams(float paramsB)
        {
            return paramsB > 0f ? paramsB.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B" : "unknown";
        }

        private static string MakeAssetNameSafe(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(stem)) return "Unnamed";
            char[] chars = stem.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                bool ok = char.IsLetterOrDigit(chars[i]) || chars[i] == '-' || chars[i] == '_' || chars[i] == '.';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
