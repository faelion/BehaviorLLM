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
        /// Copies missing package art before a builder edits importers or creates controllers.
        /// Missing art stays missing so each builder's primitive fallback remains available.
        /// </summary>
        public static string PrepareArt(string sourceRoot, string sampleName)
        {
            string source = sourceRoot + "/Art";
            string destination = OutputRoot(sourceRoot, sampleName) + "/Art";
            if (source == destination || !AssetDatabase.IsValidFolder(source))
                return destination;

            EnsureFolder(OutputRoot(sourceRoot, sampleName));
            CopyMissing(source, destination);
            return destination;
        }

        private static void CopyMissing(string source, string destination)
        {
            if (!AssetDatabase.IsValidFolder(destination))
            {
                if (!AssetDatabase.CopyAsset(source, destination))
                    throw new IOException("Could not copy sample art to " + destination);
                return;
            }
            foreach (string folder in AssetDatabase.GetSubFolders(source))
                CopyMissing(folder, destination + "/" + Path.GetFileName(folder));
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { source }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path) || Path.GetDirectoryName(path).Replace('\\', '/') != source) continue;
                string target = destination + "/" + Path.GetFileName(path);
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(target))) continue;
                if (!AssetDatabase.CopyAsset(path, target)) throw new IOException("Could not copy " + path);
            }
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
