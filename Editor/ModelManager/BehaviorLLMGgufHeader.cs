using System;
using System.IO;
using System.Text;

namespace BehaviorLLM.Editor.ModelManager
{
    /// <summary>
    /// Reads the few header fields the Installed tab needs out of a GGUF file on disk, without
    /// reading the file.
    ///
    /// This exists because a downloaded file says nothing about itself: a speech recogniser and a
    /// chat model are both a <c>.gguf</c> in the same folder, and the first one was set active by
    /// mistake once, ran a whole session, and was only found out from the telemetry. The header
    /// answers the question in the first kilobyte. <c>general.architecture</c> is the key
    /// llama.cpp itself dispatches on, so it is the honest thing to show.
    ///
    /// Only what is needed is parsed: the magic, the version, and the metadata key-value pairs up
    /// to the ones asked for. Values of every type are skipped rather than decoded, and the walk
    /// gives up after a byte budget so that a file whose first entries are the tokenizer's
    /// hundred-thousand-string vocabulary costs milliseconds, not a read of the vocabulary.
    /// </summary>
    internal static class BehaviorLLMGgufHeader
    {
        internal sealed class Info
        {
            /// <summary>True when the file is a GGUF and the header was walked far enough to answer.</summary>
            public bool IsGguf;
            /// <summary><c>general.architecture</c>, or empty when the header did not say.</summary>
            public string Architecture = string.Empty;
            /// <summary><c>general.name</c>, or empty.</summary>
            public string Name = string.Empty;
            /// <summary><c>tokenizer.chat_template</c>, or empty. Read for its thinking markers, not shown.</summary>
            public string ChatTemplate = string.Empty;
            /// <summary>Why reading stopped, for a tooltip. Empty on success.</summary>
            public string Error = string.Empty;

            public BehaviorLLMHuggingFace.ArchitectureSupport Support =>
                BehaviorLLMHuggingFace.ClassifyArchitecture(Architecture);

            public BehaviorLLMHuggingFace.ThinkingStyle Thinking =>
                BehaviorLLMHuggingFace.ClassifyThinking(ChatTemplate);
        }

        private const uint Magic = 0x46554747;               // "GGUF" little-endian
        // The chat template sits after the tokenizer's vocabulary and merges, which for a
        // 150k-token vocabulary is a few megabytes of strings that are skipped, not decoded.
        private const int MaxBytesToWalk = 24 * 1024 * 1024;
        private const int MaxKeyValues = 512;

        // GGUF metadata value types, from the spec.
        private const uint TypeUInt8 = 0, TypeInt8 = 1, TypeUInt16 = 2, TypeInt16 = 3, TypeUInt32 = 4,
                           TypeInt32 = 5, TypeFloat32 = 6, TypeBool = 7, TypeString = 8, TypeArray = 9,
                           TypeUInt64 = 10, TypeInt64 = 11, TypeFloat64 = 12;

        /// <summary>Reads the header of the file at <paramref name="path"/>. Never throws; failures are reported in <see cref="Info.Error"/>.</summary>
        internal static Info Read(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new Info { Error = "file not found" };

                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
                    return Read(stream);
            }
            catch (Exception e)
            {
                return new Info { Error = e.Message };
            }
        }

        /// <summary>Reads a header from any stream positioned at the start of the file.</summary>
        internal static Info Read(Stream stream)
        {
            Info info = new Info();
            try
            {
                using (BinaryReader r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                {
                    if (r.ReadUInt32() != Magic) { info.Error = "not a GGUF file"; return info; }
                    uint version = r.ReadUInt32();
                    if (version < 2) { info.Error = $"GGUF version {version} is older than llama-server reads"; return info; }
                    info.IsGguf = true;

                    r.ReadUInt64();                         // tensor count, not needed
                    ulong kvCount = r.ReadUInt64();
                    ulong limit = Math.Min(kvCount, (ulong)MaxKeyValues);

                    for (ulong i = 0; i < limit; i++)
                    {
                        if (stream.CanSeek && stream.Position > MaxBytesToWalk) { info.Error = "header longer than expected"; break; }

                        string key = ReadString(r);
                        uint type = r.ReadUInt32();

                        if (type == TypeString && key == "general.architecture") info.Architecture = ReadString(r);
                        else if (type == TypeString && key == "general.name") info.Name = ReadString(r);
                        else if (type == TypeString && key == "tokenizer.chat_template") info.ChatTemplate = ReadString(r);
                        else SkipValue(r, type);

                        if (info.Architecture.Length > 0 && info.Name.Length > 0 && info.ChatTemplate.Length > 0) break;
                    }
                }
            }
            catch (EndOfStreamException) { info.Error = "header truncated"; }
            catch (Exception e) { info.Error = e.Message; }
            return info;
        }

        private static string ReadString(BinaryReader r)
        {
            ulong length = r.ReadUInt64();
            if (length > int.MaxValue) throw new InvalidDataException("string length out of range");
            byte[] bytes = r.ReadBytes((int)length);
            if (bytes.Length != (int)length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void SkipValue(BinaryReader r, uint type)
        {
            switch (type)
            {
                case TypeUInt8: case TypeInt8: case TypeBool: r.ReadByte(); break;
                case TypeUInt16: case TypeInt16: r.ReadUInt16(); break;
                case TypeUInt32: case TypeInt32: case TypeFloat32: r.ReadUInt32(); break;
                case TypeUInt64: case TypeInt64: case TypeFloat64: r.ReadUInt64(); break;
                case TypeString: ReadString(r); break;
                case TypeArray:
                {
                    uint elementType = r.ReadUInt32();
                    ulong count = r.ReadUInt64();
                    int fixedSize = FixedSize(elementType);
                    if (fixedSize > 0)
                    {
                        // One seek instead of a million reads for the token-type array.
                        long bytes = (long)count * fixedSize;
                        if (r.BaseStream.CanSeek) r.BaseStream.Seek(bytes, SeekOrigin.Current);
                        else for (long b = 0; b < bytes; b++) r.ReadByte();
                    }
                    else
                    {
                        for (ulong i = 0; i < count; i++) SkipValue(r, elementType);
                    }
                    break;
                }
                default:
                    throw new InvalidDataException($"unknown GGUF value type {type}");
            }
        }

        private static int FixedSize(uint type)
        {
            switch (type)
            {
                case TypeUInt8: case TypeInt8: case TypeBool: return 1;
                case TypeUInt16: case TypeInt16: return 2;
                case TypeUInt32: case TypeInt32: case TypeFloat32: return 4;
                case TypeUInt64: case TypeInt64: case TypeFloat64: return 8;
                default: return 0;
            }
        }
    }
}
