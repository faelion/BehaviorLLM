using System.IO;
using System.Text;
using BehaviorLLM.Editor.ModelManager;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Editor
{
    /// <summary>
    /// The Installed tab decides whether a file can drive a decision maker from its GGUF header,
    /// and disables "Set active" on the strength of it. That is a binary format read by hand, so
    /// the reader is pinned against files built here byte by byte.
    /// </summary>
    public class GgufHeaderTests
    {
        private const uint TypeUInt32 = 4, TypeString = 8, TypeArray = 9, TypeFloat32 = 6;

        private static byte[] Gguf(uint version, params (string key, object value)[] kvs)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Encoding.ASCII.GetBytes("GGUF"));
                w.Write(version);
                w.Write(0UL);                       // tensors
                w.Write((ulong)kvs.Length);
                foreach ((string key, object value) in kvs)
                {
                    WriteString(w, key);
                    switch (value)
                    {
                        case string s: w.Write(TypeString); WriteString(w, s); break;
                        case uint u: w.Write(TypeUInt32); w.Write(u); break;
                        case float f: w.Write(TypeFloat32); w.Write(f); break;
                        case string[] arr:
                            w.Write(TypeArray); w.Write(TypeString); w.Write((ulong)arr.Length);
                            foreach (string s in arr) WriteString(w, s);
                            break;
                        case uint[] arr:
                            w.Write(TypeArray); w.Write(TypeUInt32); w.Write((ulong)arr.Length);
                            foreach (uint u in arr) w.Write(u);
                            break;
                    }
                }
                return ms.ToArray();
            }
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            w.Write((ulong)bytes.Length);
            w.Write(bytes);
        }

        private static BehaviorLLMGgufHeader.Info Read(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes)) return BehaviorLLMGgufHeader.Read(ms);
        }

        [Test]
        public void ArchitectureAndNameAreRead()
        {
            BehaviorLLMGgufHeader.Info info = Read(Gguf(3,
                ("general.architecture", "granite"),
                ("general.name", "Granite 4.1 3B")));

            Assert.IsTrue(info.IsGguf);
            Assert.AreEqual("granite", info.Architecture);
            Assert.AreEqual("Granite 4.1 3B", info.Name);
            Assert.IsEmpty(info.Error);
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.Supported, info.Support);
        }

        [Test]
        public void ValuesOfEveryShapeBeforeTheArchitectureAreSkipped()
        {
            // Real files rarely lead with anything else, but the reader must not depend on it:
            // a string array (a vocabulary), a fixed-width array and scalars all come first here.
            BehaviorLLMGgufHeader.Info info = Read(Gguf(3,
                ("tokenizer.ggml.tokens", new[] { "<s>", "</s>", "hello" }),
                ("tokenizer.ggml.token_type", new uint[] { 1, 2, 3, 4 }),
                ("general.file_type", 15u),
                ("some.float", 1.5f),
                ("general.architecture", "qwen3")));

            Assert.AreEqual("qwen3", info.Architecture);
        }

        [Test]
        public void TheChatTemplateIsReadPastTheVocabulary_AndClassifiedForThinking()
        {
            // The template sits after the tokenizer's arrays in every real file. A reasoning
            // model's is the one fact the Installed tab cannot get from anywhere else.
            string[] vocab = new string[2000];
            for (int i = 0; i < vocab.Length; i++) vocab[i] = "tok" + i;

            BehaviorLLMGgufHeader.Info info = Read(Gguf(3,
                ("general.architecture", "lfm2"),
                ("general.name", "LFM2.5 2.6B"),
                ("tokenizer.ggml.tokens", vocab),
                ("tokenizer.chat_template", "{{ bos_token }}<think>{{ content }}</think>")));

            Assert.IsNotEmpty(info.ChatTemplate);
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks, info.Thinking);
            Assert.IsEmpty(info.Error);
        }

        [Test]
        public void AFileWithoutATemplateHasUnknownThinking()
        {
            BehaviorLLMGgufHeader.Info info = Read(Gguf(3, ("general.architecture", "granite")));
            Assert.AreEqual(BehaviorLLMHuggingFace.ThinkingStyle.Unknown, info.Thinking);
        }

        [Test]
        public void ASpeechModelIsReportedAsNotText()
        {
            BehaviorLLMGgufHeader.Info info = Read(Gguf(3, ("general.architecture", "asr")));
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration, info.Support);
        }

        [Test]
        public void AMissingArchitectureIsUnverified_NotRefused()
        {
            BehaviorLLMGgufHeader.Info info = Read(Gguf(3, ("general.name", "mystery")));
            Assert.IsTrue(info.IsGguf);
            Assert.IsEmpty(info.Architecture);
            Assert.AreEqual(BehaviorLLMHuggingFace.ArchitectureSupport.Unverified, info.Support);
        }

        [Test]
        public void NotAGgufFileIsSaidPlainly()
        {
            BehaviorLLMGgufHeader.Info info = Read(Encoding.ASCII.GetBytes("PK this is a zip, not a model"));
            Assert.IsFalse(info.IsGguf);
            StringAssert.Contains("not a GGUF", info.Error);
        }

        [Test]
        public void ATruncatedHeaderDoesNotThrow()
        {
            byte[] whole = Gguf(3, ("general.architecture", "llama"));
            byte[] cut = new byte[whole.Length - 4];
            System.Array.Copy(whole, cut, cut.Length);

            BehaviorLLMGgufHeader.Info info = Read(cut);
            Assert.IsTrue(info.IsGguf);
            Assert.IsNotEmpty(info.Error);
        }

        [Test]
        public void VersionOneIsTooOld()
        {
            BehaviorLLMGgufHeader.Info info = Read(Gguf(1, ("general.architecture", "llama")));
            Assert.IsFalse(info.IsGguf);
            StringAssert.Contains("version 1", info.Error);
        }

        [Test]
        public void AMissingFileIsReportedNotThrown()
        {
            BehaviorLLMGgufHeader.Info info = BehaviorLLMGgufHeader.Read(Path.Combine(Path.GetTempPath(), "does-not-exist.gguf"));
            Assert.IsFalse(info.IsGguf);
            Assert.AreEqual("file not found", info.Error);
        }
    }
}
