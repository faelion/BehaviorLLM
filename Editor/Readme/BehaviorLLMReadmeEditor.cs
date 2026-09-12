using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
    /// </summary>
    [CustomEditor(typeof(BehaviorLLMReadme))]
    public class BehaviorLLMReadmeEditor : UnityEditor.Editor
    {
        /// <summary>Folder holding this readme's own scripts, deleted along with the asset.</summary>
        private const string ReadmeScriptFolder = "Assets/BehaviorLLM/Editor/Readme";

        private static readonly Dictionary<string, long> SizeCache = new Dictionary<string, long>();

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;

        [MenuItem("Tools/BehaviorLLM/Readme")]
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

            foreach (BehaviorLLMReadme.Section section in readme.sections)
            {
                if (section == null) continue;
                GUILayout.Space(10f);
                if (!string.IsNullOrWhiteSpace(section.heading))
                    GUILayout.Label(section.heading, headingStyle);
                if (!string.IsNullOrWhiteSpace(section.text))
                    GUILayout.Label(section.text, bodyStyle);
                if (!string.IsNullOrWhiteSpace(section.linkText))
                    DrawLink(section.linkText, section.linkTarget);
            }

            GUILayout.Space(18f);
            DrawSeparator();
            GUILayout.Label("Trimming the package", headingStyle);
            GUILayout.Label(
                "The samples are here to be read once and then removed. Nothing in the package " +
                "depends on them, so deleting them cannot break a project that is using BehaviorLLM.",
                bodyStyle);

            foreach (BehaviorLLMReadme.RemovableFolder folder in readme.removableFolders)
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.folderPath)) continue;
                DrawRemovable(folder);
            }

            GUILayout.Space(6f);
            DrawRemoveEverything(readme);

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
        private static bool FolderExists(string projectRelativePath)
        {
            if (AssetDatabase.IsValidFolder(projectRelativePath)) return true;
            return Directory.Exists(ToAbsolute(projectRelativePath));
        }

        private static string ToAbsolute(string projectRelativePath)
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                projectRelativePath);
        }

        private void DrawRemovable(BehaviorLLMReadme.RemovableFolder folder)
        {
            GUILayout.Space(8f);
            bool exists = FolderExists(folder.folderPath);

            using (new EditorGUILayout.HorizontalScope())
            {
                string label = string.IsNullOrWhiteSpace(folder.displayName)
                    ? folder.folderPath
                    : folder.displayName;
                if (exists) label += "  (" + FormatSize(MeasureFolder(folder.folderPath)) + ")";
                GUILayout.Label(label, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button(exists ? "Remove" : "Already removed", GUILayout.Width(130f)))
                        ConfirmAndDelete(new[] { folder.folderPath },
                            string.IsNullOrWhiteSpace(folder.displayName)
                                ? folder.folderPath
                                : folder.displayName,
                            folder.whatItCosts);
                }
            }

            if (exists && !string.IsNullOrWhiteSpace(folder.whatItCosts))
                GUILayout.Label(folder.whatItCosts, bodyStyle);
        }

        private void DrawRemoveEverything(BehaviorLLMReadme readme)
        {
            List<string> paths = new List<string>();
            foreach (BehaviorLLMReadme.RemovableFolder f in readme.removableFolders)
            {
                if (f != null && !string.IsNullOrWhiteSpace(f.folderPath)
                              && FolderExists(f.folderPath))
                    paths.Add(f.folderPath);
            }

            string readmePath = AssetDatabase.GetAssetPath(readme);
            long total = 0L;
            foreach (string p in paths) total += MeasureFolder(p);
            if (AssetDatabase.IsValidFolder(ReadmeScriptFolder)) total += MeasureFolder(ReadmeScriptFolder);

            GUILayout.Space(4f);
            string button = paths.Count > 0
                ? "Remove everything above, this readme and its scripts  (" + FormatSize(total) + ")"
                : "Remove this readme and its scripts  (" + FormatSize(total) + ")";

            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.45f, 0.40f);
            bool clicked = GUILayout.Button(button, GUILayout.Height(24f));
            GUI.backgroundColor = previous;
            if (!clicked) return;

            paths.Add(ReadmeScriptFolder);
            paths.Add(readmePath);
            ConfirmAndDelete(paths.ToArray(), "the samples and this readme",
                "The package keeps working. What goes is the sample content, this page, and the two " +
                "scripts that draw it.");
        }

        /// <summary>
        /// One dialog, one delete pass. Everything goes through here so that no path in this file
        /// can remove an asset without the developer having read what it was.
        /// </summary>
        private static void ConfirmAndDelete(string[] paths, string what, string cost)
        {
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append("Permanently delete ").Append(what).Append("?\n\n");
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                message.Append("  ").Append(p).Append('\n');
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
                if (AssetDatabase.IsValidFolder(p) || AssetDatabase.LoadAssetAtPath<Object>(p) != null)
                {
                    assetPaths.Add(p);
                    continue;
                }

                string absolute = ToAbsolute(p);
                if (!Directory.Exists(absolute)) continue;
                try { Directory.Delete(absolute, true); }
                catch (IOException) { failed.Add(p); }
                catch (System.UnauthorizedAccessException) { failed.Add(p); }
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

        private void DrawLink(string label, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return;
            GUILayout.Space(2f);
            if (!GUILayout.Button(label, GUILayout.MaxWidth(320f))) return;

            if (targetPath.StartsWith("http://") || targetPath.StartsWith("https://"))
            {
                Application.OpenURL(targetPath);
                return;
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<Object>(targetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
            else
            {
                Debug.LogWarning("[BehaviorLLM] Readme link points at nothing: " + targetPath);
            }
        }

        /// <summary>Bytes on disk under a project folder, meta files included, cached per folder.</summary>
        private static long MeasureFolder(string projectRelativePath)
        {
            if (SizeCache.TryGetValue(projectRelativePath, out long cached)) return cached;

            long total = 0L;
            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty, projectRelativePath);
            if (Directory.Exists(absolute))
            {
                foreach (string file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { /* a file that vanished mid-walk is not worth failing over */ }
                }
            }
            SizeCache[projectRelativePath] = total;
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
