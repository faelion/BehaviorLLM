using BehaviorLLM.Editor.Readme;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The readme's delete buttons and links used to assume the package sat at
    /// <c>Assets/BehaviorLLM/</c>. It does not: a consumer who installs through the Package Manager
    /// gets it under <c>Packages/</c>, and one who copies it in may rename the folder. Paths are now
    /// stored relative to the package root and combined with wherever the readme asset actually is,
    /// which is what these tests pin down.
    /// </summary>
    public class ReadmePathResolutionTests
    {
        [Test]
        public void RelativePath_IsCombinedWithTheProjectLocalRoot()
        {
            Assert.AreEqual("Assets/BehaviorLLM/Samples/StealthGuard",
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/BehaviorLLM", "Samples/StealthGuard"));
        }

        [Test]
        public void RelativePath_IsCombinedWithAPackageRoot()
        {
            Assert.AreEqual("Packages/com.faelion.behaviorllm/Samples/PrisonYard",
                BehaviorLLMReadmeEditor.ResolveStoredPath(
                    "Packages/com.faelion.behaviorllm", "Samples/PrisonYard"));
        }

        [Test]
        public void RelativePath_SurvivesTheFolderBeingRenamed()
        {
            // The whole point of anchoring on the asset: the root is whatever the consumer called it.
            Assert.AreEqual("Assets/Vendor/LLM/Experiments~",
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/Vendor/LLM", "Experiments~"));
        }

        [Test]
        public void HiddenFolderName_KeepsItsTrailingTilde()
        {
            // Experiments~ is invisible to the AssetDatabase, so the "~" has to survive intact for
            // the System.IO fallback to find it.
            StringAssert.EndsWith("/Experiments~",
                BehaviorLLMReadmeEditor.ResolveStoredPath("Packages/com.faelion.behaviorllm", "Experiments~"));
        }

        [TestCase("Assets/BehaviorLLM/Samples/StealthGuard")]
        [TestCase("Packages/com.faelion.behaviorllm/Samples/StealthGuard")]
        public void AlreadyAnchoredPath_IsLeftAlone(string stored)
        {
            // A readme authored before paths became relative keeps working rather than being
            // silently re-rooted into nonsense like Assets/BehaviorLLM/Assets/BehaviorLLM/...
            Assert.AreEqual(stored,
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/BehaviorLLM", stored));
        }

        [Test]
        public void BackslashesAreNormalised()
        {
            Assert.AreEqual("Assets/BehaviorLLM/Samples/StealthGuard",
                BehaviorLLMReadmeEditor.ResolveStoredPath(
                    "Assets\\BehaviorLLM", "Samples\\StealthGuard"));
        }

        [Test]
        public void EmptyStoredPath_ResolvesToEmpty_SoNothingIsEverDeletedByAccident()
        {
            Assert.AreEqual(string.Empty,
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/BehaviorLLM", ""));
            Assert.AreEqual(string.Empty,
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/BehaviorLLM", "   "));
            Assert.AreEqual(string.Empty,
                BehaviorLLMReadmeEditor.ResolveStoredPath("Assets/BehaviorLLM", null));
        }

        [Test]
        public void MissingRoot_LeavesTheStoredPathUnrooted_RatherThanRootingItAtTheProject()
        {
            Assert.AreEqual("Samples/StealthGuard",
                BehaviorLLMReadmeEditor.ResolveStoredPath("", "Samples/StealthGuard"));
        }
    }
}
