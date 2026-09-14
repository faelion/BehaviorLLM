using System.Collections.Generic;
using BehaviorLLM.Editor.ModelManager;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The live catalogue decides what to hide from three things the API says: the task a
    /// repository is listed under, the architecture in its GGUF header and the parameter count
    /// in that header, with the repository name as the fallback for a count. All three decide
    /// whether a model is offered at all, so the reading of each is pinned down here against
    /// recorded bodies rather than a network.
    /// </summary>
    public class HuggingFaceSearchTests
    {
        // ---------------------------------------------------------------- what the API says

        // Two entries as the list endpoint returns them with expand[]=gguf: the chat template
        // is the bulk of a real body and is not read, so it is elided here.
        private const string PageBody =
            "[{\"_id\":\"a\",\"id\":\"ibm-granite/granite-4.1-3b-GGUF\",\"downloads\":6516,\"likes\":12," +
            "\"pipeline_tag\":\"text-generation\",\"gguf\":{\"total\":3402836480,\"architecture\":\"granite\",\"context_length\":131072}}," +
            "{\"_id\":\"b\",\"id\":\"nvidia/nemotron-3.5-asr-streaming-0.6b\",\"downloads\":900,\"likes\":3," +
            "\"pipeline_tag\":\"automatic-speech-recognition\",\"gguf\":{\"total\":648268960,\"architecture\":\"asr\"}}," +
            "{\"_id\":\"c\",\"id\":\"someone/mystery-7B-GGUF\",\"downloads\":5,\"likes\":0}]";

        [Test]
        public void TheParameterCountComesFromTheHeader_NotTheName()
        {
            BehaviorLLMHuggingFace.Page page = BehaviorLLMHuggingFace.ParsePage(PageBody, null);
            BehaviorLLMHuggingFace.Repository granite = page.Repositories[0];

            Assert.AreEqual(3.40f, granite.ParamsB, 0.01f, "3,402,836,480 parameters, not the '3b' in the name");
            Assert.IsTrue(granite.ParamsFromHeader);
            Assert.AreEqual("granite", granite.Architecture);
            Assert.AreEqual("text-generation", granite.PipelineTag);
            Assert.AreEqual(6516, granite.Downloads);
        }

        [Test]
        public void ARepositoryWithoutAHeaderFallsBackToItsName()
        {
            BehaviorLLMHuggingFace.Page page = BehaviorLLMHuggingFace.ParsePage(PageBody, null);
            BehaviorLLMHuggingFace.Repository mystery = page.Repositories[2];

            Assert.AreEqual(7f, mystery.ParamsB, 0.01f);
            Assert.IsFalse(mystery.ParamsFromHeader);
            Assert.IsEmpty(mystery.Architecture);
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.Unverified, mystery.Support);
        }

        [Test]
        public void ASpeechModelIsClassifiedAsNotText()
        {
            BehaviorLLMHuggingFace.Page page = BehaviorLLMHuggingFace.ParsePage(PageBody, null);
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration, page.Repositories[1].Support);
        }

        [TestCase("")]
        [TestCase(null)]
        public void AnEmptyBodyIsAnEmptyPage(string body)
        {
            Assert.AreEqual(0, BehaviorLLMHuggingFace.ParsePage(body, null).Repositories.Count);
        }

        [Test]
        public void ABrokenBodyThrows_SoTheWindowReportsItRatherThanShowingNothing()
        {
            // The window catches this and says "could not reach Hugging Face" with the reason. An
            // empty list would look like a search with no hits, which is a different thing.
            Assert.Throws<System.ArgumentException>(() => BehaviorLLMHuggingFace.ParsePage("not json", null));
        }

        [Test]
        public void TheSearchAsksForTextGenerationAndTheHeader()
        {
            string url = BehaviorLLMHuggingFace.BuildSearchUrl("granite", 25);
            StringAssert.Contains("pipeline_tag=text-generation", url);
            StringAssert.Contains("filter=gguf", url);
            StringAssert.Contains("expand[]=gguf", url);
            // expand[] replaces the default field set, so the sort key has to be asked for by name.
            StringAssert.Contains("expand[]=downloads", url);
            StringAssert.Contains("sort=downloads", url);
        }

        [TestCase("llama")]
        [TestCase("qwen3")]
        [TestCase("Granite")]        // case is the header author's choice
        [TestCase("gemma4")]
        [TestCase("granitehybrid")]
        public void TheShippedServersTextArchitecturesAreSupported(string arch)
        {
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.Supported, BehaviorLLMHuggingFace.ClassifyArchitecture(arch));
        }

        [TestCase("asr")]
        [TestCase("whisper")]
        [TestCase("bert")]
        [TestCase("clip")]
        public void EncodersAndSpeechModelsAreNotText(string arch)
        {
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration, BehaviorLLMHuggingFace.ClassifyArchitecture(arch));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("something-from-next-month")]
        public void AnUnknownArchitectureIsUnverified_SoAToggleCanShowIt(string arch)
        {
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.Unverified, BehaviorLLMHuggingFace.ClassifyArchitecture(arch));
        }

        // ---------------------------------------------------------------- thinking

        [Test]
        public void ATemplateThatAlwaysThinksIsAReasoningModel()
        {
            // LFM2.5's template, abridged: a <think> block and a preserve_thinking flag, no enable_thinking.
            const string lfm = "{{- bos_token -}}{%- set preserve_thinking = preserve_thinking | default(false) -%}" +
                               "{%- if not preserve_thinking %}{{ content | replace('<think>', '') }}{% endif %}";
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks, BehaviorLLMHuggingFace.ClassifyThinking(lfm));
        }

        [Test]
        public void ATemplateWithTheSwitchIsSwitchable_EvenThoughItThinks()
        {
            // Qwen3: thinks by default, but honours enable_thinking, which the package sets per request.
            const string qwen = "{%- if enable_thinking is defined and enable_thinking is false %}{{- '<think>\\n\\n</think>\\n\\n' }}{%- endif %}";
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.Switchable, BehaviorLLMHuggingFace.ClassifyThinking(qwen));
        }

        [Test]
        public void GemmasThoughtChannelCountsAsThinking()
        {
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks,
                BehaviorLLMHuggingFace.ClassifyThinking("{{ '<|channel>thought\\n' }}{{ content }}"));
        }

        [Test]
        public void ATemplateWithoutAThinkingBlockIsNotAReasoningModel()
        {
            const string granite = "{%- for message in messages %}<|start_of_role|>{{ message.role }}<|end_of_role|>{{ message.content }}<|end_of_text|>{% endfor %}";
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.NoThinking, BehaviorLLMHuggingFace.ClassifyThinking(granite));
        }

        [TestCase("")]
        [TestCase(null)]
        public void NoTemplateIsUnknown_NotAVerdict(string template)
        {
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.Unknown, BehaviorLLMHuggingFace.ClassifyThinking(template));
        }

        [Test]
        public void TheThinkingStyleIsReadFromTheListResponse()
        {
            const string body = "[{\"id\":\"LiquidAI/LFM2.5-2.6B-GGUF\",\"downloads\":1,\"gguf\":{\"total\":2697198592," +
                                "\"architecture\":\"lfm2\",\"chat_template\":\"{{ bos_token }}<think>{{ content }}</think>\"}}]";
            BehaviorLLMHuggingFace.Repository repo = BehaviorLLMHuggingFace.ParsePage(body, null).Repositories[0];
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks, repo.Thinking);
            Assert.AreEqual("lfm2", repo.Architecture);
        }

        [Test]
        public void ShardsAndProjectorsAreNotOffered()
        {
            const string tree =
                "[{\"type\":\"file\",\"path\":\"model-Q4_K_M.gguf\",\"size\":10,\"lfs\":{\"oid\":\"abc\",\"size\":2000}}," +
                "{\"type\":\"file\",\"path\":\"model-Q8_0-00001-of-00002.gguf\",\"size\":10}," +
                "{\"type\":\"file\",\"path\":\"mmproj-model-f16.gguf\",\"size\":10}," +
                "{\"type\":\"file\",\"path\":\"README.md\",\"size\":10}]";

            var files = BehaviorLLMHuggingFace.ParseFiles("o/r", tree);
            Assert.AreEqual(1, files.Count);
            Assert.AreEqual("Q4_K_M", files[0].Quant);
            Assert.AreEqual(2000, files[0].SizeBytes, "the LFS size is the real one; the pointer file is ten bytes");
            Assert.AreEqual("abc", files[0].Sha256);
        }

        // ---------------------------------------------------------------- the name fallback

        [TestCase("ibm-granite/granite-4.1-3b-GGUF", 3f)]
        [TestCase("unsloth/Qwen3.5-4B-GGUF", 4f)]
        [TestCase("bartowski/Qwen_Qwen3.5-2B-GGUF", 2f)]
        [TestCase("ibm-granite/granite-4.2-30b-GGUF", 30f)]
        [TestCase("someone/mistral-7B-instruct-GGUF", 7f)]
        public void ParameterCount_IsReadFromTheRepositoryName(string id, float expected)
        {
            Assert.AreEqual(expected, BehaviorLLMHuggingFace.ReadParamsB(id), 0.01f);
        }

        [Test]
        public void VersionNumbersAreNotMistakenForSizes()
        {
            // "granite-4.1-3b" must read 3, not 4: the version comes first and is the trap.
            Assert.AreEqual(3f, BehaviorLLMHuggingFace.ReadParamsB("ibm-granite/granite-4.1-3b-GGUF"), 0.01f);
            Assert.AreEqual(2f, BehaviorLLMHuggingFace.ReadParamsB("Qwen/Qwen3.5-2B-GGUF"), 0.01f);
        }

        [Test]
        public void FractionalSizesSurvive()
        {
            Assert.AreEqual(1.5f, BehaviorLLMHuggingFace.ReadParamsB("someone/tiny-1.5b-GGUF"), 0.01f);
        }

        [TestCase("someone/phi-3-mini-GGUF")]
        [TestCase("someone/an-unnamed-model")]
        [TestCase("")]
        [TestCase(null)]
        public void AnUnreadableNameReportsZero_SoTheFilterKeepsIt(string id)
        {
            // Zero means "could not tell", and the size filter deliberately keeps those rather
            // than hiding a model because its author did not put a number in the name.
            Assert.AreEqual(0f, BehaviorLLMHuggingFace.ReadParamsB(id), 0.01f);
        }

        [Test]
        public void LettersAfterTheBAreNotASize()
        {
            // "bf16" and "-base-" both contain a digit next to a b; neither is a parameter count.
            Assert.AreEqual(0f, BehaviorLLMHuggingFace.ReadParamsB("someone/model-bf16-GGUF"), 0.01f);
        }

        [TestCase(0L, "unknown size")]
        [TestCase(1536L, "2 KB")]
        [TestCase(2_100_000_000L, "1.96 GB")]
        public void SizesReadTheWayAUserJudgesADownload(long bytes, string expected)
        {
            Assert.AreEqual(expected, BehaviorLLMHuggingFace.FormatBytes(bytes));
        }

        [TestCase(386_906, "386.9k")]
        [TestCase(1_240_000, "1.2M")]
        [TestCase(42, "42")]
        public void DownloadCountsAreRounded(int count, string expected)
        {
            Assert.AreEqual(expected, BehaviorLLMHuggingFace.FormatCount(count));
        }

        // ---------------------------------------------------------------- paging

        [Test]
        public void TheNextPageCursorIsReadFromTheLinkHeader()
        {
            const string header = "<https://huggingface.co/api/models?limit=5&cursor=eyJhIjoxfQ%3D%3D>; rel=\"next\"";
            Assert.AreEqual("https://huggingface.co/api/models?limit=5&cursor=eyJhIjoxfQ%3D%3D",
                BehaviorLLMHuggingFace.ReadNextLink(header));
        }

        [Test]
        public void ThePreviousLinkIsNotMistakenForTheNextOne()
        {
            const string header =
                "<https://example.test/prev>; rel=\"prev\", <https://example.test/next>; rel=\"next\"";
            Assert.AreEqual("https://example.test/next", BehaviorLLMHuggingFace.ReadNextLink(header));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("<https://example.test/prev>; rel=\"prev\"")]
        public void NoNextLinkMeansTheEndOfTheResults(string header)
        {
            // Null is what stops the "Load more" button appearing, so it has to be null and not
            // an empty string that later reads as a url.
            Assert.IsNull(BehaviorLLMHuggingFace.ReadNextLink(header));
        }

        // ---------------------------------------------------------------- official repositories

        private static BehaviorLLMHuggingFace.Repository Repo(string id) =>
            new BehaviorLLMHuggingFace.Repository { Id = id };

        [TestCase("ibm-granite/granite-4.1-3b-GGUF")]
        [TestCase("Qwen/Qwen3.5-4B-GGUF")]
        [TestCase("mistralai/Mistral-7B-Instruct-v0.3-GGUF")]
        public void APublishersOwnRepositoryIsOfficial(string id)
        {
            Assert.IsTrue(BehaviorLLMModelManagerWindow.IsOfficial(Repo(id)),
                "The owner's name appears in the model name, which is how vendors publish.");
        }

        [TestCase("bartowski/Qwen_Qwen3.5-4B-GGUF")]          // a re-quantiser
        [TestCase("unsloth/granite-4.1-3b-GGUF")]             // ditto
        [TestCase("AnkitAI/Parable-Granite-4.1-3B-GGUF")]     // a fine-tune on a personal account
        [TestCase("TheBloke/Mistral-7B-Instruct-GGUF")]
        public void ARepublishedCopyIsNotOfficial(string id)
        {
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(Repo(id)),
                "These are valuable and still get listed; they simply do not outrank the publisher.");
        }

        [Test]
        public void AVendorWhoseNameSharesNothingWithItsProductIsNotDetected()
        {
            // google publishes gemma, microsoft publishes phi. There is no lexical link, and the
            // only way to know is a curated vendor list - which is the ageing curated list this
            // whole feature exists to get away from. The toggle ranks, it does not gate: an
            // undetected publisher still appears in the results, just without the boost. That is
            // the right way for this to be wrong.
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(Repo("google/gemma-3-4b-it-GGUF")),
                "Documented limitation, not a defect: no lexical relationship exists to find.");
        }

        [Test]
        public void MalformedIdsDoNotThrow()
        {
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(Repo("no-slash-at-all")));
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(Repo("")));
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(null));
        }

        [Test]
        public void ShortOwnerFragmentsDoNotCountAsAMatch()
        {
            // A two-letter owner appearing incidentally inside a model name would make almost
            // everything "official", so fragments shorter than three characters are ignored.
            Assert.IsFalse(BehaviorLLMModelManagerWindow.IsOfficial(Repo("ai/granite-4.1-3b-GGUF")));
        }

        [Test]
        public void PresetMatchingIsCaseInsensitiveAndSurvivesAnEmptyList()
        {
            List<BehaviorLLMModelPresets.Preset> presets = new List<BehaviorLLMModelPresets.Preset>
            {
                new BehaviorLLMModelPresets.Preset { FileName = "granite-4.1-3b-Q4_K_M.gguf", DisplayName = "Granite" }
            };

            Assert.IsNotNull(BehaviorLLMModelPresets.MatchByFileName(presets, "GRANITE-4.1-3B-Q4_K_M.GGUF"),
                "A download differing only in case still satisfies the preset.");
            Assert.IsNull(BehaviorLLMModelPresets.MatchByFileName(presets, "ibm-granite_granite-4.1-3b-Q4_K_M.gguf"),
                "The prefixed name a mirror produces does not match, which is the warning the card shows.");
            Assert.IsNull(BehaviorLLMModelPresets.MatchByFileName(null, "anything.gguf"));
            Assert.IsNull(BehaviorLLMModelPresets.MatchByFileName(presets, ""));
        }
    }
}
