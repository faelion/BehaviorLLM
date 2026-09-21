using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Finds the config assets the package ships, so a freshly added component arrives already
    /// working instead of showing empty reference slots to someone who has not read the guide.
    ///
    /// Components call <see cref="FindShipped{T}"/> from <c>Reset()</c>, which the Editor runs
    /// when the component is first added. At runtime, <see cref="OrTransientDefault{T}"/> stands
    /// in for a reference the author cleared, using the type's own field defaults, so a missing
    /// asset degrades to "the documented defaults" rather than to a null-reference exception.
    /// </summary>
    public static class BehaviorLLMDefaults
    {
        /// <summary>Asset name of the shipped decision-maker preset (Reactive, 2 s interval).</summary>
        public const string DecisionMakerConfigAsset = "Config_Reactive";

        /// <summary>Asset name of the shipped server preset (local llama-server on port 8080).</summary>
        public const string ServerConfigAsset = "Server_LocalLlama";

        /// <summary>Asset name of the shipped perception preset (10 m sphere, 5 memories).</summary>
        public const string PerceptionConfigAsset = "Perception_Default";

        /// <summary>Asset name of the shipped blackboard preset.</summary>
        public const string BlackboardConfigAsset = "Blackboard_Default";

        /// <summary>Asset name of the shipped model preset used when no model has been chosen yet.</summary>
        public const string ModelConfigAsset = "Model_ActiveCatalog";

        /// <summary>
        /// Looks up one of the shipped preset assets by name. Editor only: at runtime it returns
        /// null, because assets that nothing references are not included in a build.
        /// </summary>
        public static T FindShipped<T>(string assetName) where T : ScriptableObject
        {
            #if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(T).Name} {assetName}");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != assetName) continue;
                return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            }
            #endif
            return null;
        }

        /// <summary>
        /// Returns <paramref name="assigned"/> when it is set, and otherwise a throwaway instance
        /// carrying the type's declared defaults. Never returns null, so callers can read a config
        /// without a null check on every field.
        /// </summary>
        public static T OrTransientDefault<T>(T assigned) where T : ScriptableObject
        {
            return assigned != null ? assigned : ScriptableObject.CreateInstance<T>();
        }
    }
}
