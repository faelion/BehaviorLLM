using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Samples.Dodgeball.Editor
{
    /// <summary>
    /// Where this sample's own files are, worked out from where this script is, so the builder
    /// works wherever the package is installed. The same approach as StealthGuard's, kept in
    /// this sample because samples share nothing.
    /// </summary>
    internal static class DodgeballPaths
    {
        private static string cachedRoot;

        /// <summary>Sample root folder, e.g. <c>Assets/BehaviorLLM/Samples/Dodgeball</c>.</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedRoot)) return cachedRoot;
                cachedRoot = Resolve();
                return cachedRoot;
            }
        }

        public static string OutputRoot => BehaviorLLM.Editor.SampleAssetPaths.OutputRoot(Root, "Dodgeball");
        public static string Scenes => OutputRoot + "/Scenes";
        public static string Data => OutputRoot + "/Data";

        /// <summary>
        /// This file lives at <c>&lt;root&gt;/Scripts/Editor/DodgeballPaths.cs</c>, so the root is
        /// three levels up. If the lookup ever fails, the old literal is a reasonable last resort:
        /// it is where the sample sits in this repository.
        /// </summary>
        private static string Resolve()
        {
            const string fallback = "Assets/BehaviorLLM/Samples/Dodgeball";
            string[] guids = AssetDatabase.FindAssets("t:MonoScript DodgeballPaths");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (Path.GetFileName(path) != "DodgeballPaths.cs") continue;

                string dir = Path.GetDirectoryName(path);              // .../Scripts/Editor
                string scripts = Path.GetDirectoryName(dir);           // .../Scripts
                string root = Path.GetDirectoryName(scripts);          // the sample root
                if (!string.IsNullOrEmpty(root)) return root.Replace('\\', '/');
            }

            Debug.LogWarning("[Dodgeball] Could not locate the sample folder from its own scripts; " +
                             "falling back to " + fallback + ".");
            return fallback;
        }
    }
}
