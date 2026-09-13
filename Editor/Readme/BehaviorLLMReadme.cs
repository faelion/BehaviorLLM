using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehaviorLLM.Editor.Readme
{
    /// <summary>
    /// The package's landing page, shown in the inspector by
    /// <c>BehaviorLLMReadmeEditor</c>. It exists so that a developer who has just installed
    /// BehaviorLLM has somewhere to land that is not a folder tree, in the same way Unity's own
    /// templates ship a Readme asset at the root of the project.
    ///
    /// It is deliberately an Editor-only type. The asset is documentation, it is never referenced
    /// by a scene, and it must not travel into a player build.
    ///
    /// Its second job is disposal. The two gameplay samples are the largest thing in the package by
    /// a wide margin and most projects will want them gone once they have been read, so the
    /// inspector offers to delete them, and then to delete the readme itself.
    /// </summary>
    public class BehaviorLLMReadme : ScriptableObject
    {
        [Tooltip("Heading shown at the top of the inspector. Normally the package name.")]
        public string title = "BehaviorLLM";

        [Tooltip("One or two sentences under the title saying what the package is. Shown before " +
                 "every section.")]
        [TextArea(2, 5)]
        public string subtitle =
            "Local-LLM decision making for Unity characters and game systems.";

        [Tooltip("The blocks of text shown down the page, in order. Each one is a heading, some " +
                 "body text and an optional link.")]
        public List<Section> sections = new List<Section>();

        [Tooltip("Folders this readme offers to delete, each with the label shown on its button. " +
                 "Paths are relative to the package root, so they keep working wherever the package " +
                 "is installed, and are checked before anything is removed: an entry that no longer " +
                 "exists is shown as already removed rather than failing.")]
        public List<RemovableFolder> removableFolders = new List<RemovableFolder>();

        [Serializable]
        public class Section
        {
            [Tooltip("Bold heading for this block.")]
            public string heading = "";

            [Tooltip("Body text. Written for someone who has not read any of the code yet.")]
            [TextArea(2, 12)]
            public string text = "";

            [Tooltip("Optional label for a link button under the text. Leave empty for no link.")]
            public string linkText = "";

            [Tooltip("Where the link button goes. A URL opens in a browser; a path selects that " +
                     "asset in the Project window instead. Paths are relative to the package root, " +
                     "e.g. Samples/StealthGuard/Scenes/StealthGuard.unity.")]
            public string linkTarget = "";
        }

        [Serializable]
        public class RemovableFolder
        {
            [Tooltip("Label on the delete button, e.g. \"StealthGuard sample\".")]
            public string displayName = "";

            [Tooltip("Folder to delete, relative to the package root, e.g. " +
                     "Samples/StealthGuard. Deleting it is permanent unless the project is under " +
                     "version control, and the inspector says so before it acts. A path starting " +
                     "with Assets/ or Packages/ is still honoured as-is, for readmes authored " +
                     "before paths became relative.")]
            public string folderPath = "";

            [Tooltip("One line explaining what is lost, shown next to the button and in the " +
                     "confirmation dialog.")]
            public string whatItCosts = "";
        }
    }
}
