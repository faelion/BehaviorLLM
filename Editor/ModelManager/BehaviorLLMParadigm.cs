using System.IO;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using UnityEditor;
using UnityEngine;

namespace BehaviorLLM.Editor.ModelManager
{
    /// <summary>
    /// Reads and changes the project's decision paradigm from the Editor.
    ///
    /// The paradigm lives on <see cref="BehaviorLLMSettings"/>, and a change is always written to
    /// the project's own settings asset, never to the preset the package ships: a package installed
    /// from a git URL is read-only, and one copied into the project would carry this project's
    /// choice into every project that copies it next. When the project has no settings asset yet,
    /// one is created from the shipped preset's values, which is exactly what the package was using
    /// until now, so nothing else changes.
    /// </summary>
    internal static class BehaviorLLMParadigm
    {
        /// <summary>Where a project settings asset is created when there is none.</summary>
        internal const string ProjectSettingsPath = "Assets/Resources/" + BehaviorLLMSettings.ResourceName + ".asset";

        internal static DecisionParadigm Current => BehaviorLLMSettings.Current.paradigm;

        /// <summary>The name a paradigm goes by in the window.</summary>
        internal static string Label(DecisionParadigm paradigm) =>
            paradigm == DecisionParadigm.DecisionModel ? "Decision model (Jev-like)" : "Generative LLM";

        /// <summary>
        /// Sets the project's paradigm. Returns the path of the asset written, so the caller can say
        /// where the choice now lives.
        /// </summary>
        internal static string Set(DecisionParadigm paradigm)
        {
            BehaviorLLMSettings settings = FindOrCreateProjectSettings(out string path);
            if (settings.paradigm != paradigm)
            {
                Undo.RecordObject(settings, "Change BehaviorLLM paradigm");
                settings.paradigm = paradigm;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            BehaviorLLMSettings.Use(null);   // search again, so the runtime finds this asset
            return path;
        }

        private static BehaviorLLMSettings FindOrCreateProjectSettings(out string path)
        {
            BehaviorLLMSettings existing = Resources.Load<BehaviorLLMSettings>(BehaviorLLMSettings.ResourceName);
            if (existing != null)
            {
                path = AssetDatabase.GetAssetPath(existing);
                if (!BehaviorLLMModelPresets.IsPackagePath(path) && !IsInsidePackage(path)) return existing;
            }

            BehaviorLLMSettings shipped = Resources.Load<BehaviorLLMSettings>(BehaviorLLMSettings.DefaultResourcePath);
            BehaviorLLMSettings created = shipped != null
                ? Object.Instantiate(shipped)
                : ScriptableObject.CreateInstance<BehaviorLLMSettings>();

            string folder = Path.GetDirectoryName(ProjectSettingsPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");
            path = AssetDatabase.GenerateUniqueAssetPath(ProjectSettingsPath);
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            return created;
        }

        // The package's own Resources folder also resolves "BehaviorLLMSettings" by name only if a
        // file of that name sits there, which it never should; checked so a stray one is not edited.
        private static bool IsInsidePackage(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) && assetPath.Replace('\\', '/').IndexOf("/BehaviorLLM/Resources/", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
