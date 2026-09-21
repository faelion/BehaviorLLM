using System;
using System.IO;
using UnityEditor;

namespace BehaviorLLM.Editor
{
    /// <summary>Separates installed sample sources from writable generated assets.</summary>
    public static class SampleAssetPaths
    {
        /// <summary>Preserves Assets installations; package samples generate into the consuming project.</summary>
        public static string OutputRoot(string sourceRoot, string sampleName)
        {
            return sourceRoot.StartsWith("Packages/", StringComparison.Ordinal)
                ? "Assets/BehaviorLLMSamples/" + sampleName
                : sourceRoot;
        }

        /// <summary>
        /// Copies optional package art once before a builder edits importers or creates controllers.
        /// Missing art stays missing so each builder's primitive fallback remains available.
        /// </summary>
        public static string PrepareArt(string sourceRoot, string sampleName)
        {
            string source = sourceRoot + "/Art";
            string destination = OutputRoot(sourceRoot, sampleName) + "/Art";
            if (source == destination || !AssetDatabase.IsValidFolder(source) || AssetDatabase.IsValidFolder(destination))
                return destination;

            EnsureFolder(OutputRoot(sourceRoot, sampleName));
            if (!AssetDatabase.CopyAsset(source, destination))
                throw new IOException("Could not copy sample art to " + destination);
            return destination;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
