using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BehaviorLLM.Editor.ModelManager
{
    /// <summary>
    /// Searches Hugging Face for GGUF models, so the catalogue stops ageing the day it ships.
    ///
    /// Two calls, on purpose. A search returns repositories, which is all the list view needs and
    /// is one request; the files inside a repository - their quantisations, sizes and digests -
    /// are fetched only when a row is expanded, because a repository carries a dozen or more GGUF
    /// files and fetching every tree up front would be fifty requests for information nobody has
    /// asked to see yet.
    ///
    /// The search asks the API for two things a repository name cannot tell. The
    /// <c>pipeline_tag</c> filter keeps the results to text generation, which is the only task a
    /// decision maker can use: a speech recogniser or an embedding model is a GGUF file too and
    /// installs without complaint, then answers nothing. And <c>expand[]=gguf</c> returns the
    /// header Hugging Face has already parsed out of the file - the real parameter count and the
    /// model architecture - so the size cap filters on a number rather than on a guess read out of
    /// the name, and a model llama.cpp cannot load is hidden before it is downloaded.
    ///
    /// Responses are read with <c>JsonUtility</c> rather than by hand. The package writes its
    /// llama-server bodies by hand because a schema has to inline as a nested object that
    /// JsonUtility cannot express; reading a response whose field names are known at compile time
    /// is the case JsonUtility is actually good at, and it is Unity's own, so the package stays
    /// free of third-party dependencies either way.
    /// </summary>
    internal static class BehaviorLLMHuggingFace
    {
        private const string SearchEndpoint = "https://huggingface.co/api/models";
        private const string RepoEndpoint = "https://huggingface.co/api/models/";
        private const int RequestTimeoutSeconds = 20;

        /// <summary>The only pipeline a decision maker can drive. Everything else is filtered server-side.</summary>
        internal const string TextGenerationPipeline = "text-generation";

        /// <summary>A repository that contains GGUF files.</summary>
        internal sealed class Repository
        {
            public string Id;
            public int Downloads;
            public int Likes;
            /// <summary>
            /// Parameter count in billions. From the GGUF header when Hugging Face has parsed it,
            /// which is the real number; read from the name otherwise. 0 when neither says.
            /// </summary>
            public float ParamsB;
            /// <summary>True when <see cref="ParamsB"/> came from the file header rather than the name.</summary>
            public bool ParamsFromHeader;
            /// <summary>The <c>general.architecture</c> the GGUF header declares - "llama", "qwen3", "granite". Empty when unknown.</summary>
            public string Architecture = string.Empty;
            /// <summary>The task Hugging Face lists the repository under. Empty when unknown.</summary>
            public string PipelineTag = string.Empty;
            /// <summary>How the model's chat template treats thinking. See <see cref="ClassifyThinking"/>.</summary>
            public ThinkingStyle Thinking = ThinkingStyle.Unknown;
            /// <summary>Files, once <see cref="FetchFilesAsync"/> has run. Null until then.</summary>
            public List<GgufFile> Files;
            public bool FilesLoaded => Files != null;

            public string Owner => Id != null && Id.Contains("/") ? Id.Substring(0, Id.IndexOf('/')) : string.Empty;
            public string Name => Id != null && Id.Contains("/") ? Id.Substring(Id.IndexOf('/') + 1) : Id;
            public string PageUrl => "https://huggingface.co/" + Id;

            /// <summary>Whether llama-server can load this architecture as a text model. See <see cref="ClassifyArchitecture"/>.</summary>
            public ArchitectureSupport Support => ClassifyArchitecture(Architecture);
        }

        /// <summary>One GGUF file inside a repository.</summary>
        internal sealed class GgufFile
        {
            public string RepoId;
            public string Path;
            public long SizeBytes;
            /// <summary>The LFS digest when the API reports one. Empty otherwise.</summary>
            public string Sha256;
            /// <summary>Q4_K_M, IQ4_XS, BF16 ... or empty when the name does not say.</summary>
            public string Quant;

            public string FileName
            {
                get
                {
                    if (string.IsNullOrEmpty(Path)) return string.Empty;
                    int slash = Path.LastIndexOf('/');
                    return slash >= 0 ? Path.Substring(slash + 1) : Path;
                }
            }

            public string DownloadUrl => $"https://huggingface.co/{RepoId}/resolve/main/{Path}";
        }

        /// <summary>What the catalogue knows about a model architecture before anyone downloads it.</summary>
        internal enum ArchitectureSupport
        {
            /// <summary>A text decoder the shipped llama-server build loads.</summary>
            Supported,
            /// <summary>Loads in llama.cpp, but not as a text generator: an encoder, a tokenizer, a vision tower.</summary>
            NotTextGeneration,
            /// <summary>The header did not say, or names something this list has not seen. llama.cpp may well load it.</summary>
            Unverified
        }

        /// <summary>What a model's chat template says about thinking before it answers.</summary>
        internal enum ThinkingStyle
        {
            /// <summary>No template seen.</summary>
            Unknown,
            /// <summary>The template has no thinking block. The measured presets are all this.</summary>
            NoThinking,
            /// <summary>The template thinks but honours <c>enable_thinking</c>, which the package sets per request.</summary>
            Switchable,
            /// <summary>The template opens every answer with a thinking block and has no switch to turn it off.</summary>
            AlwaysThinks
        }

        /// <summary>
        /// Reads the thinking style out of a chat template. A reasoning model whose template has no
        /// <c>enable_thinking</c> variable ignores the package's per-request "do not think", spends
        /// the Reactive profile's 48-token budget on reasoning and returns no answer; the client
        /// then falls back to raw completion, where a model forbidden to think gives the cheapest
        /// legal reply. LFM2.5-2.6B did exactly this for twelve decisions on 2026-09-14. Knowing it
        /// before download is worth a pill; knowing it at Awake is worth a warning.
        /// </summary>
        internal static ThinkingStyle ClassifyThinking(string chatTemplate)
        {
            if (string.IsNullOrWhiteSpace(chatTemplate)) return ThinkingStyle.Unknown;
            if (chatTemplate.IndexOf("enable_thinking", StringComparison.OrdinalIgnoreCase) >= 0) return ThinkingStyle.Switchable;

            bool hasThinkBlock = chatTemplate.IndexOf("<think>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 chatTemplate.IndexOf("</think>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 chatTemplate.IndexOf("<|think|>", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 chatTemplate.IndexOf("<|channel>thought", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasThinkBlock ? ThinkingStyle.AlwaysThinks : ThinkingStyle.NoThinking;
        }

        // ---------------------------------------------------------------- wire shapes

        [Serializable] private class WireGguf { public long total; public string architecture; public long context_length; public string chat_template; }
        [Serializable] private class WireRepo { public string id; public int downloads; public int likes; public string pipeline_tag; public WireGguf gguf; }
        [Serializable] private class WireRepoList { public WireRepo[] items; }
        [Serializable] private class WireLfs { public string oid; public long size; }
        [Serializable] private class WireFile { public string type; public string path; public long size; public WireLfs lfs; }
        [Serializable] private class WireFileList { public WireFile[] items; }

        // A trailing "b" after a number is how every GGUF repository states its size. This is the
        // fallback for a repository whose header Hugging Face has not parsed, and says so: the
        // real count comes from the header whenever it is there.
        private static readonly Regex ParamPattern =
            new Regex(@"(?<n>\d+(?:\.\d+)?)\s*b(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuantPattern =
            new Regex(@"(IQ\d[A-Z0-9_]*|Q\d(?:_[A-Z0-9]+)*|BF16|F16|F32)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The text-decoder architectures the shipped llama-server build loads, as named in the
        // GGUF header's general.architecture. Checked against the strings in llama.dll of build
        // b10809 on 2026-09-14. llama.cpp adds architectures every few weeks, so an architecture
        // missing here is reported as Unverified rather than refused: a user who knows better can
        // show it with one toggle, and the fallback is a download that fails to load, not a wrong
        // decision.
        private static readonly HashSet<string> TextArchitectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "llama", "llama4", "deci", "falcon", "grok", "gpt2", "gptj", "gptneox", "mpt", "baichuan",
            "starcoder", "starcoder2", "refact", "bloom", "stablelm", "qwen", "qwen2", "qwen2moe",
            "qwen2vl", "qwen3", "qwen3moe", "qwen3next", "qwen35", "phi2", "phi3", "phimoe", "plamo",
            "codeshell", "orion", "internlm2", "minicpm", "minicpm3", "gemma", "gemma2", "gemma3",
            "gemma3n", "gemma4", "mamba", "mamba2", "xverse", "command-r", "cohere2", "dbrx", "olmo",
            "olmo2", "olmoe", "openelm", "arctic", "deepseek", "deepseek2", "chatglm", "glm4",
            "glm4moe", "bitnet", "t5", "jais", "nemotron", "nemotron_h", "nemotron_h_moe", "exaone",
            "exaone4", "rwkv6", "rwkv7", "granite", "granitemoe", "granitehybrid", "chameleon", "plm",
            "bailingmoe", "dots1", "arcee", "ernie4.5", "hunyuan-moe", "smollm3", "lfm2",
            "smallthinker", "seed_oss", "apertus", "minimax-m2", "afmoe", "mistral3", "kimi-linear"
        };

        // Architectures llama.cpp does load, but only as an encoder, a decoder for audio tokens
        // or a vision tower. None of them can answer a prompt with an action. Named so that the
        // card can say "not a text model" instead of the vaguer "unverified".
        private static readonly HashSet<string> NonTextArchitectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bert", "nomic-bert", "jina-bert-v2", "t5encoder", "wavtokenizer-dec", "clip", "asr",
            "parakeet", "whisper", "dream", "llada"
        };

        /// <summary>
        /// Where an architecture name stands with the shipped llama-server build. Case does not
        /// matter; an empty name is Unverified, because a header that does not say is not a
        /// header that says no.
        /// </summary>
        internal static ArchitectureSupport ClassifyArchitecture(string architecture)
        {
            if (string.IsNullOrWhiteSpace(architecture)) return ArchitectureSupport.Unverified;
            string a = architecture.Trim();
            if (TextArchitectures.Contains(a)) return ArchitectureSupport.Supported;
            if (NonTextArchitectures.Contains(a)) return ArchitectureSupport.NotTextGeneration;
            return ArchitectureSupport.Unverified;
        }

        /// <summary>One page of results, plus the cursor that reaches the next one.</summary>
        internal sealed class Page
        {
            public List<Repository> Repositories = new List<Repository>();
            /// <summary>Pass back to <see cref="SearchAsync"/> for the next page. Null at the end.</summary>
            public string NextUrl;
            public bool HasMore => !string.IsNullOrEmpty(NextUrl);
        }

        /// <summary>
        /// One page of text-generation GGUF repositories matching <paramref name="query"/>, most
        /// downloaded first. An empty query is valid and asks for the most downloaded overall,
        /// which is the right answer to "what is there?".
        ///
        /// Paging follows the cursor Hugging Face returns in the <c>Link</c> header rather than an
        /// offset, so a list that shifts between requests cannot silently skip or repeat a
        /// repository. Pass <paramref name="continueUrl"/> from a previous page to go on.
        ///
        /// No size filtering happens here: the caller has the parameter count and the file list,
        /// and decides what a cap means.
        /// </summary>
        internal static async Task<Page> SearchAsync(string query, int limit, CancellationToken token,
                                                     string continueUrl = null)
        {
            string url = continueUrl ?? BuildSearchUrl(query, limit);
            HttpResult response = await GetAsync(url, token);
            return ParsePage(response.Body, response.LinkHeader);
        }

        /// <summary>
        /// The search request. <c>expand[]</c> replaces the default field set, so every field the
        /// window reads has to be asked for by name - downloads included, or the sort key vanishes.
        /// </summary>
        internal static string BuildSearchUrl(string query, int limit)
        {
            return $"{SearchEndpoint}?search={UnityWebRequest.EscapeURL(query ?? string.Empty)}" +
                   $"&filter=gguf&pipeline_tag={TextGenerationPipeline}" +
                   "&expand[]=downloads&expand[]=likes&expand[]=pipeline_tag&expand[]=gguf" +
                   $"&sort=downloads&direction=-1&limit={Mathf.Clamp(limit, 1, 100)}";
        }

        /// <summary>
        /// Reads one page of the list response. Separate from the request so the shape of what
        /// comes back can be tested against a recorded body without a network.
        /// </summary>
        internal static Page ParsePage(string json, string linkHeader)
        {
            Page page = new Page { NextUrl = ReadNextLink(linkHeader) };
            if (string.IsNullOrWhiteSpace(json)) return page;

            WireRepoList parsed = JsonUtility.FromJson<WireRepoList>("{\"items\":" + json + "}");
            if (parsed?.items == null) return page;

            for (int i = 0; i < parsed.items.Length; i++)
            {
                WireRepo w = parsed.items[i];
                if (w == null || string.IsNullOrEmpty(w.id)) continue;

                bool header = w.gguf != null && w.gguf.total > 0;
                page.Repositories.Add(new Repository
                {
                    Id = w.id,
                    Downloads = w.downloads,
                    Likes = w.likes,
                    PipelineTag = w.pipeline_tag ?? string.Empty,
                    Architecture = w.gguf?.architecture ?? string.Empty,
                    Thinking = ClassifyThinking(w.gguf?.chat_template),
                    ParamsFromHeader = header,
                    ParamsB = header ? w.gguf.total / 1e9f : ReadParamsB(w.id)
                });
            }
            return page;
        }

        /// <summary>The url marked rel="next" in a Link header, or null when there is no next page.</summary>
        internal static string ReadNextLink(string linkHeader)
        {
            if (string.IsNullOrEmpty(linkHeader)) return null;
            foreach (string part in linkHeader.Split(','))
            {
                if (part.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int open = part.IndexOf('<'), close = part.IndexOf('>');
                if (open >= 0 && close > open) return part.Substring(open + 1, close - open - 1);
            }
            return null;
        }

        /// <summary>
        /// Fills in what the list endpoint would have said about one repository opened by id:
        /// its task and its header. Used when the user pastes an id instead of searching, so a
        /// pasted speech model is called out the same way a searched one is hidden.
        /// </summary>
        internal static async Task FetchMetadataAsync(Repository repo, CancellationToken token)
        {
            if (repo == null || string.IsNullOrEmpty(repo.Id)) return;

            string json = await GetStringAsync(
                $"{RepoEndpoint}{repo.Id}?expand[]=downloads&expand[]=likes&expand[]=pipeline_tag&expand[]=gguf", token);
            WireRepo w = JsonUtility.FromJson<WireRepo>(json);
            if (w == null) return;

            repo.Downloads = w.downloads;
            repo.Likes = w.likes;
            repo.PipelineTag = w.pipeline_tag ?? string.Empty;
            repo.Architecture = w.gguf?.architecture ?? string.Empty;
            repo.Thinking = ClassifyThinking(w.gguf?.chat_template);
            if (w.gguf != null && w.gguf.total > 0)
            {
                repo.ParamsFromHeader = true;
                repo.ParamsB = w.gguf.total / 1e9f;
            }
        }

        /// <summary>
        /// Fills <see cref="Repository.Files"/> with the repository's GGUF files. Multi-part files
        /// (<c>-00001-of-00003</c>) are dropped: the package launches llama-server against a single
        /// file, and offering a shard that cannot be used on its own would be a trap.
        /// </summary>
        internal static async Task FetchFilesAsync(Repository repo, CancellationToken token)
        {
            if (repo == null || string.IsNullOrEmpty(repo.Id)) return;

            string json = await GetStringAsync($"{RepoEndpoint}{repo.Id}/tree/main", token);
            repo.Files = ParseFiles(repo.Id, json);
        }

        /// <summary>Reads a repository tree listing into its usable GGUF files. Testable without a network.</summary>
        internal static List<GgufFile> ParseFiles(string repoId, string json)
        {
            List<GgufFile> files = new List<GgufFile>();
            if (string.IsNullOrWhiteSpace(json)) return files;

            WireFileList parsed = JsonUtility.FromJson<WireFileList>("{\"items\":" + json + "}");
            if (parsed?.items != null)
            {
                for (int i = 0; i < parsed.items.Length; i++)
                {
                    WireFile w = parsed.items[i];
                    if (w?.path == null) continue;
                    if (!w.path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Regex.IsMatch(w.path, @"-\d{5}-of-\d{5}\.gguf$", RegexOptions.IgnoreCase)) continue;
                    if (w.path.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase)) continue;

                    long size = w.lfs != null && w.lfs.size > 0 ? w.lfs.size : w.size;
                    Match q = QuantPattern.Match(w.path);

                    files.Add(new GgufFile
                    {
                        RepoId = repoId,
                        Path = w.path,
                        SizeBytes = size,
                        Sha256 = w.lfs != null && !string.IsNullOrEmpty(w.lfs.oid) ? w.lfs.oid : string.Empty,
                        Quant = q.Success ? q.Value.ToUpperInvariant() : string.Empty
                    });
                }
            }

            files.Sort((a, b) => a.SizeBytes.CompareTo(b.SizeBytes));
            return files;
        }

        /// <summary>Billions of parameters read from a repository name, or 0 when it does not say.</summary>
        internal static float ReadParamsB(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            Match m = ParamPattern.Match(id);
            return m.Success && float.TryParse(m.Groups["n"].Value, NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out float n)
                ? n
                : 0f;
        }

        /// <summary>A body and the headers the caller needs. An async method cannot take an
        /// out parameter, so paging information comes back in the result rather than beside it.</summary>
        private sealed class HttpResult
        {
            public string Body;
            public string LinkHeader;
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            return (await GetAsync(url, token)).Body;
        }

        private static async Task<HttpResult> GetAsync(string url, CancellationToken token)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Accept", "application/json");

                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    if (token.IsCancellationRequested)
                    {
                        request.Abort();
                        throw new OperationCanceledException(token);
                    }
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception(request.error ?? "request failed");

                return new HttpResult
                {
                    Body = request.downloadHandler.text,
                    LinkHeader = request.GetResponseHeader("Link")
                };
            }
        }

        /// <summary>"1.96 GB" and friends, for a UI that should never ask for a blind commitment.</summary>
        internal static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "unknown size";
            if (bytes >= 1L << 30) return (bytes / (float)(1L << 30)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1L << 20) return (bytes / (float)(1L << 20)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            return (bytes / (float)(1L << 10)).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        /// <summary>"48.2k" for a download count, which is only ever read as a rough signal.</summary>
        internal static string FormatCount(int n)
        {
            if (n >= 1_000_000) return (n / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1_000) return (n / 1_000f).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
