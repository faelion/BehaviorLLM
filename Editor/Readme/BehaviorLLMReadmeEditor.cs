using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace BehaviorLLM.Editor.Readme
{
    /// <summary>
    /// Draws <see cref="BehaviorLLMReadme"/> as a page rather than as a list of serialized fields,
    /// and owns the delete buttons at the bottom.
    ///
    /// The deletions are the reason this file is more than cosmetic, so they are deliberate about
    /// three things. Nothing is removed without a confirmation dialog that names the folder and
    /// says what is lost. The size of each folder is measured and shown, because "do I need this?"
    /// is really a question about disk. And removing the readme removes its own scripts too, so a
    /// project that has finished with it is left with no trace rather than with a dangling type.
    ///
    /// Everything it touches is located relative to the readme asset itself rather than to a
    /// hardcoded <c>Assets/BehaviorLLM/</c>, because the package does not always live there: a
    /// consumer who installs it through the Package Manager gets it under <c>Packages/</c>, and one
    /// who copies it into their project may rename the folder. See <see cref="Locate"/>.
    /// </summary>
    [CustomEditor(typeof(BehaviorLLMReadme))]
    public class BehaviorLLMReadmeEditor : UnityEditor.Editor
    {
        /// <summary>
        /// This readme's own scripts, deleted along with the asset. Relative to the package root.
        /// </summary>
        private const string ReadmeScriptFolder = "Editor/Readme";

        private static readonly Dictionary<string, long> SizeCache = new Dictionary<string, long>();

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;

        [MenuItem("BehaviorLLM/Readme", priority = 20)]
        private static void ShowReadme()
        {
            BehaviorLLMReadme readme = FindReadme();
            if (readme == null)
            {
                EditorUtility.DisplayDialog("BehaviorLLM",
                    "No readme asset found. It has probably been removed, which is fine: the package " +
                    "works without it. The documentation lives in the package's README.md.", "OK");
                return;
            }
            Selection.activeObject = readme;
        }

        private static BehaviorLLMReadme FindReadme()
        {
            string[] guids = AssetDatabase.FindAssets("t:BehaviorLLMReadme");
            if (guids == null || guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<BehaviorLLMReadme>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ------------------------------------------------------------------ locating the package

        /// <summary>
        /// Where the package actually is, and whether anything in it may be deleted in place.
        /// </summary>
        private readonly struct Location
        {
            /// <summary>Root as the AssetDatabase names it, e.g. <c>Packages/com.faelion.behaviorllm</c>.</summary>
            public readonly string AssetRoot;

            /// <summary>Root as the filesystem names it. Not the same thing for a cached package.</summary>
            public readonly string AbsoluteRoot;

            /// <summary>
            /// True when the package was resolved from a registry or a git URL. Unity treats
            /// those as immutable and unpacks them under <c>Library/PackageCache</c>, which is
            /// derived state it rebuilds from the manifest and lock file. A folder deleted there
            /// comes back on the next resolve, so offering the button would be a lie.
            /// </summary>
            public readonly bool IsImmutable;

            public Location(string assetRoot, string absoluteRoot, bool isImmutable)
            {
                AssetRoot = assetRoot;
                AbsoluteRoot = absoluteRoot;
                IsImmutable = isImmutable;
            }
        }

        /// <summary>
        /// Locates the package from the readme asset's own path. The asset sits at the package root,
        /// so its folder is the root, whether that is <c>Assets/BehaviorLLM</c>, a renamed folder
        /// under <c>Assets/</c>, an embedded package under <c>Packages/</c>, or a read-only one the
        /// Package Manager resolved into its cache.
        /// </summary>
        private static Location Locate(BehaviorLLMReadme readme)
        {
            string assetPath = AssetDatabase.GetAssetPath(readme);
            if (string.IsNullOrEmpty(assetPath)) return new Location(string.Empty, string.Empty, false);

            string assetRoot = Normalize(Path.GetDirectoryName(assetPath));

            // A package the Package Manager resolved is addressed as "Packages/<name>/..." by the
            // AssetDatabase but lives somewhere else entirely on disk, so measuring and deleting
            // need the resolved path rather than the project-relative one.
            PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
            if (package != null)
            {
                bool immutable = package.source != UnityEditor.PackageManager.PackageSource.Embedded
                                 && package.source != UnityEditor.PackageManager.PackageSource.Local;
                return new Location(assetRoot, Normalize(package.resolvedPath), immutable);
            }

            return new Location(assetRoot, Normalize(ProjectRoot() + "/" + assetRoot), false);
        }

        private static string ProjectRoot()
        {
            return Normalize(Path.GetDirectoryName(Application.dataPath) ?? string.Empty);
        }

        private static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Combines a stored path with the package root. Paths already anchored at
        /// <c>Assets/</c> or <c>Packages/</c> are returned unchanged, so a readme authored before
        /// paths became relative keeps working.
        /// </summary>
        public static string ResolveStoredPath(string root, string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return string.Empty;
            string stored = Normalize(storedPath.Trim());
            if (stored.StartsWith("Assets/") || stored == "Assets"
                || stored.StartsWith("Packages/") || stored == "Packages")
                return stored;
            string normalizedRoot = Normalize(root);
            if (string.IsNullOrEmpty(normalizedRoot)) return stored;
            return normalizedRoot + "/" + stored.TrimStart('/');
        }

        private static string AssetPathOf(Location location, string storedPath)
        {
            return ResolveStoredPath(location.AssetRoot, storedPath);
        }

        /// <summary>
        /// The stored path as the filesystem sees it. Legacy project-relative paths are resolved
        /// against the project; everything else against the package's real location on disk.
        /// </summary>
        private static string AbsolutePathOf(Location location, string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return string.Empty;
            string stored = Normalize(storedPath.Trim());
            if (stored.StartsWith("Assets/") || stored.StartsWith("Packages/"))
                return Normalize(ProjectRoot() + "/" + stored);
            if (string.IsNullOrEmpty(location.AbsoluteRoot)) return string.Empty;
            return Normalize(location.AbsoluteRoot + "/" + stored.TrimStart('/'));
        }

        // ------------------------------------------------------------------------------ the page

        /// <summary>
        /// Hides the default inspector header. The asset is a page, and a page with an "Open" button
        /// and a script reference above it reads like a mistake.
        /// </summary>
        protected override void OnHeaderGUI()
        {
            BehaviorLLMReadme readme = (BehaviorLLMReadme)target;
            EnsureStyles();

            GUILayout.Space(12f);
            GUILayout.Label(string.IsNullOrEmpty(readme.title) ? "BehaviorLLM" : readme.title, titleStyle);
            if (!string.IsNullOrWhiteSpace(readme.subtitle))
                GUILayout.Label(readme.subtitle, subtitleStyle);
            GUILayout.Space(6f);
        }

        public override void OnInspectorGUI()
        {
            BehaviorLLMReadme readme = (BehaviorLLMReadme)target;
            EnsureStyles();
            Location location = Locate(readme);

            foreach (BehaviorLLMReadme.Section section in readme.sections)
            {
                if (section == null) continue;
                GUILayout.Space(10f);
                if (!string.IsNullOrWhiteSpace(section.heading))
                    GUILayout.Label(section.heading, headingStyle);
                if (!string.IsNullOrWhiteSpace(section.text))
                    GUILayout.Label(section.text, bodyStyle);
                if (!string.IsNullOrWhiteSpace(section.linkText))
                    DrawLink(location, section.linkText, section.linkTarget);
            }

            GUILayout.Space(18f);
            DrawSeparator();
            GUILayout.Label("Trimming the package", headingStyle);
            GUILayout.Label(
                "The samples are here to be read once and then removed. Nothing in the package " +
                "depends on them, so deleting them cannot break a project that is using BehaviorLLM.",
                bodyStyle);

            if (location.IsImmutable)
            {
                GUILayout.Space(6f);
                EditorGUILayout.HelpBox(
                    "BehaviorLLM is installed as an immutable package, so nothing below can be " +
                    "deleted in place. The Package Manager unpacks it under Library/PackageCache, " +
                    "which it rebuilds from your manifest, so a deletion there comes back on the " +
                    "next resolve.\n\n" +
                    "The sizes are still worth knowing. To drop the samples for good, either " +
                    "remove the dependency from Packages/manifest.json, or install BehaviorLLM by " +
                    "copying it into your own Assets folder, where these buttons work.",
                    MessageType.Info);
            }

            foreach (BehaviorLLMReadme.RemovableFolder folder in readme.removableFolders)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.folderPath)) continue;
                DrawRemovable(location, folder);
            }

            GUILayout.Space(6f);
            DrawRemoveEverything(location, readme);

            // The page hides the serialized fields, so this is the way back to them for anyone who
            // wants to reword a section or add a folder of their own to the list above.
            GUILayout.Space(14f);
            DrawSeparator();
            showRawFields = EditorGUILayout.Foldout(showRawFields, "Edit this readme", true);
            if (showRawFields)
            {
                GUILayout.Space(4f);
                DrawDefaultInspector();
            }
        }

        private static bool showRawFields;

        /// <summary>
        /// True when the folder is on disk, whether or not Unity imports it. A folder whose name ends
        /// in "~" (such as <c>Experiments~</c>) is invisible to the AssetDatabase on purpose, so it
        /// has to be checked and deleted through System.IO instead.
        /// </summary>
        private static bool FolderExists(Location location, string storedPath)
        {
            if (AssetDatabase.IsValidFolder(AssetPathOf(location, storedPath))) return true;
            string absolute = AbsolutePathOf(location, storedPath);
            return !string.IsNullOrEmpty(absolute) && Directory.Exists(absolute);
        }

        private void DrawRemovable(Location location, BehaviorLLMReadme.RemovableFolder folder)
        {
            GUILayout.Space(8f);
            bool exists = FolderExists(location, folder.folderPath);
            bool canDelete = exists && !location.IsImmutable;

            using (new EditorGUILayout.HorizontalScope())
            {
                string label = string.IsNullOrWhiteSpace(folder.displayName)
                    ? folder.folderPath
                    : folder.displayName;
                if (exists) label += "  (" + FormatSize(MeasureFolder(location, folder.folderPath)) + ")";
                GUILayout.Label(label, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!canDelete))
                {
                    string caption = exists
                        ? (location.IsImmutable ? "Read-only" : "Remove")
                        : "Already removed";
                    if (GUILayout.Button(caption, GUILayout.Width(130f)))
                        ConfirmAndDelete(location, new[] { folder.folderPath },
                            string.IsNullOrWhiteSpace(folder.displayName)
                                ? folder.folderPath
                                : folder.displayName,
                            folder.whatItCosts);
                }
            }

            if (exists && !string.IsNullOrWhiteSpace(folder.whatItCosts))
                GUILayout.Label(folder.whatItCosts, bodyStyle);
        }

        private void DrawRemoveEverything(Location location, BehaviorLLMReadme readme)
        {
            List<string> paths = new List<string>();
            foreach (BehaviorLLMReadme.RemovableFolder f in readme.removableFolders)
            {
                if (f != null && !string.IsNullOrWhiteSpace(f.folderPath)
                              && FolderExists(location, f.folderPath))
                    paths.Add(f.folderPath);
            }

            string readmePath = AssetDatabase.GetAssetPath(readme);
            long total = 0L;
            foreach (string p in paths) total += MeasureFolder(location, p);
            if (FolderExists(location, ReadmeScriptFolder))
                total += MeasureFolder(location, ReadmeScriptFolder);

            GUILayout.Space(4f);
            string button = paths.Count > 0
                ? "Remove everything above, this readme and its scripts  (" + FormatSize(total) + ")"
                : "Remove this readme and its scripts  (" + FormatSize(total) + ")";

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.45f, 0.40f);
            bool clicked;
            using (new EditorGUI.DisabledScope(location.IsImmutable))
                clicked = GUILayout.Button(button, GUILayout.Height(24f));
            GUI.backgroundColor = previous;
            if (!clicked) return;

            paths.Add(ReadmeScriptFolder);
            paths.Add(readmePath);
            ConfirmAndDelete(location, paths.ToArray(), "the samples and this readme",
                "The package keeps working. What goes is the sample content, this page, and the two " +
                "scripts that draw it.");
        }

        /// <summary>
        /// One dialog, one delete pass. Everything goes through here so that no path in this file
        /// can remove an asset without the developer having read what it was.
        /// </summary>
        private static void ConfirmAndDelete(Location location, string[] paths, string what, string cost)
        {
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append("Permanently delete ").Append(what).Append("?\n\n");
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                message.Append("  ").Append(AssetPathOf(location, p)).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(cost)) message.Append('\n').Append(cost).Append('\n');
            message.Append("\nThis cannot be undone from the Editor. If the project is under version " +
                           "control the deletion is a normal change and can be reverted there.");

            if (!EditorUtility.DisplayDialog("BehaviorLLM", message.ToString(), "Delete", "Cancel"))
                return;

            // Imported assets go through the AssetDatabase so their .meta files and any references
            // are handled properly. Folders Unity does not import (a trailing "~") are unknown to it
            // and are removed from disk directly.
            List<string> failed = new List<string>();
            List<string> assetPaths = new List<string>();
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string assetPath = AssetPathOf(location, p);
                if (AssetDatabase.IsValidFolder(assetPath)
                    || AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                {
                    assetPaths.Add(assetPath);
                    continue;
                }

                string absolute = AbsolutePathOf(location, p);
                if (string.IsNullOrEmpty(absolute) || !Directory.Exists(absolute)) continue;
                try { Directory.Delete(absolute, true); }
                catch (IOException) { failed.Add(assetPath); }
                catch (System.UnauthorizedAccessException) { failed.Add(assetPath); }
            }
            if (assetPaths.Count > 0) AssetDatabase.DeleteAssets(assetPaths.ToArray(), failed);
            SizeCache.Clear();
            AssetDatabase.Refresh();

            if (failed.Count > 0)
            {
                EditorUtility.DisplayDialog("BehaviorLLM",
                    "These could not be deleted:\n\n" + string.Join("\n", failed), "OK");
            }
        }

        private void DrawLink(Location location, string label, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return;
            GUILayout.Space(2f);
            if (!GUILayout.Button(label, GUILayout.MaxWidth(320f))) return;

            if (targetPath.StartsWith("http://") || targetPath.StartsWith("https://"))
            {
                Application.OpenURL(targetPath);
                return;
            }

            string assetPath = AssetPathOf(location, targetPath);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            else
            {
                Debug.LogWarning("[BehaviorLLM] Readme link points at nothing: " + assetPath);
            }
        }

        /// <summary>Bytes on disk under a package folder, meta files included, cached per folder.</summary>
        private static long MeasureFolder(Location location, string storedPath)
        {
            string absolute = AbsolutePathOf(location, storedPath);
            if (string.IsNullOrEmpty(absolute)) return 0L;
            if (SizeCache.TryGetValue(absolute, out long cached)) return cached;

            long total = 0L;
            if (Directory.Exists(absolute))
            {
                foreach (string file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { /* a file that vanished mid-walk is not worth failing over */ }
                }
            }
            SizeCache[absolute] = total;
            return total;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / (1024f * 1024f)).ToString("0.#") + " MB";
            if (bytes >= 1024L) return (bytes / 1024f).ToString("0") + " KB";
            return bytes + " B";
        }

        private static void DrawSeparator()
        {
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            GUILayout.Space(6f);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            subtitleStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 12 };
            headingStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, wordWrap = true };
            bodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
        }
    }
}
