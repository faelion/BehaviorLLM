using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Samples.StealthGuard.Editor
{
    /// <summary>
    /// Where this sample's own files are, worked out from where this script is.
    ///
    /// The scene builder used to hold the sample's location as a literal string, which was true
    /// exactly as long as nobody moved the folder. A sample that ships inside a package is moved by
    /// definition: it sits under <c>Assets/</c> in the development project and under
    /// <c>Packages/</c> for anyone who installs the package, and it landed in a third place when the
    /// samples were moved into the package on 2026-09-10. Asking the asset database where this file
    /// is costs one lookup, once, and cannot go stale.
    /// </summary>
    internal static class StealthGuardPaths
    {
        private static string cachedRoot;

        /// <summary>Sample root folder, e.g. <c>Assets/BehaviorLLM/Samples/StealthGuard</c>.</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedRoot)) return cachedRoot;
                cachedRoot = Resolve();
                return cachedRoot;
            }
        }

        public static string OutputRoot => BehaviorLLM.Editor.SampleAssetPaths.OutputRoot(Root, "StealthGuard");
        public static string Scenes => OutputRoot + "/Scenes";
        public static string Data => OutputRoot + "/Data";
        private static string Art => BehaviorLLM.Editor.SampleAssetPaths.PrepareArt(Root, "StealthGuard");
        public static string CharacterArt => Art + "/Characters";
        public static string PropArt => Art + "/Props";

        /// <summary>
        /// This file lives at <c>&lt;root&gt;/Scripts/Editor/StealthGuardPaths.cs</c>, so the root is
        /// three levels up. If the lookup ever fails, the old literal is a reasonable last resort:
        /// it is where the sample sits in this repository.
        /// </summary>
        private static string Resolve()
        {
            const string fallback = "Assets/BehaviorLLM/Samples/StealthGuard";
            string[] guids = AssetDatabase.FindAssets("t:MonoScript StealthGuardPaths");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (Path.GetFileName(path) != "StealthGuardPaths.cs") continue;

                string dir = Path.GetDirectoryName(path);              // .../Scripts/Editor
                string scripts = Path.GetDirectoryName(dir);           // .../Scripts
                string root = Path.GetDirectoryName(scripts);          // the sample root
                if (!string.IsNullOrEmpty(root)) return root.Replace('\\', '/');
            }

            Debug.LogWarning("[StealthGuard] Could not locate the sample folder from its own scripts; " +
                             "falling back to " + fallback + ".");
            return fallback;
        }
    }
}
