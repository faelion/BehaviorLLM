using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using UnityEditor;
using UnityEngine.Networking;
using UnityEngine;

namespace BehaviorLLM.Editor.ModelManager
{
    [Serializable]
    public class BehaviorLLMModelCatalog
    {
        public List<BehaviorLLMModelDefinition> models = new List<BehaviorLLMModelDefinition>();
    }

    [Serializable]
    public class BehaviorLLMModelDefinition
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        public string downloadUrl;
        public string fileName;
        public string sha256;
        /// <summary>Size of the file, so a card can say what it costs to download and whether it fits the GPU. 0 when unknown.</summary>
        public long sizeBytes;
        public int contextSize = 4096;
        public int gpuLayers = 99;
    }

    internal struct DownloadProgressInfo
    {
        public long downloadedBytes;
        public long totalBytes;
        public float normalized;
    }

    internal enum LlamaServerBuildFlavor
    {
        Cpu = 0,
        Cuda124 = 1,
        Cuda131 = 2,
        Vulkan = 3,
        Sycl = 4,
        HipRadeon = 5
    }

    [Serializable]
    internal class LlamaCppReleaseInfo
    {
        public string tag_name;
        public LlamaCppReleaseAsset[] assets;
    }

    [Serializable]
    internal class LlamaCppReleaseAsset
    {
        public string name;
        public string browser_download_url;
    }

    internal enum BehaviorLLMManagerView
    {
        // Kept for backward compatibility with older serialized window state.
        Combined = 0,
        ModelCatalog = 1,
        LlamaServer = 2,
        RuntimeConfig = 3,
        Installed = 4
    }

    internal static class BehaviorLLMDownloadUtility
    {
        public static async Task DownloadFileAsync(
            string url,
            string destinationFile,
            string expectedSha256,
            IProgress<DownloadProgressInfo> progress,
            CancellationToken token)
        {
            await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL is empty.");
                if (string.IsNullOrWhiteSpace(destinationFile)) throw new ArgumentException("Destination file path is empty.");

                string dir = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                long remoteLength;
                bool acceptsRanges;
                ProbeRemoteFile(url, out remoteLength, out acceptsRanges);

                // Already there and complete: nothing to do.
                if (remoteLength > 0 && File.Exists(destinationFile) &&
                    new FileInfo(destinationFile).Length == remoteLength &&
                    VerifySha256IfPresent(destinationFile, expectedSha256))
                {
                    progress?.Report(new DownloadProgressInfo
                    {
                        downloadedBytes = remoteLength,
                        totalBytes = remoteLength,
                        normalized = 1f
                    });
                    return;
                }

                // Download outside Assets/ and move the finished file in.
                //
                // Streaming straight into StreamingAssets means Unity's importer watches a file
                // that grows for minutes: it reports "does not exist in SourceAssetDB" for the
                // half-written asset, retries, and on a large model ends in an infinite import
                // loop. Library/ is not watched, so nothing sees the file until it is whole, and
                // it is on the same volume as the project, so the move is a rename rather than a
                // second multi-gigabyte copy.
                string stagingFile = GetStagingFilePath(destinationFile);
                string stagingDir = Path.GetDirectoryName(stagingFile);
                if (!string.IsNullOrEmpty(stagingDir) && !Directory.Exists(stagingDir))
                    Directory.CreateDirectory(stagingDir);

                long existingLength = File.Exists(stagingFile) ? new FileInfo(stagingFile).Length : 0;
                if (remoteLength > 0 && existingLength > remoteLength)
                {
                    // A stale part-file from a different build of the same name.
                    File.Delete(stagingFile);
                    existingLength = 0;
                }

                bool resume = acceptsRanges && existingLength > 0;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.AllowAutoRedirect = true;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.UserAgent = "BehaviorLLM-ModelManager";
                if (resume) request.AddRange(existingLength);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream network = response.GetResponseStream())
                using (FileStream fs = new FileStream(stagingFile, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    long downloaded = existingLength;
                    long total = remoteLength > 0 ? remoteLength : -1;
                    int read;

                    while ((read = network.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        fs.Write(buffer, 0, read);
                        downloaded += read;

                        float normalized = total > 0 ? Mathf.Clamp01((float)downloaded / total) : 0f;
                        progress?.Report(new DownloadProgressInfo
                        {
                            downloadedBytes = downloaded,
                            totalBytes = total,
                            normalized = normalized
                        });
                    }
                }

                if (!VerifySha256IfPresent(stagingFile, expectedSha256))
                {
                    File.Delete(stagingFile);
                    throw new Exception($"SHA256 mismatch for '{Path.GetFileName(destinationFile)}'.");
                }

                // Only now does the file appear where Unity is watching, whole and verified.
                if (File.Exists(destinationFile)) File.Delete(destinationFile);
                File.Move(stagingFile, destinationFile);
            }, token);
        }

        public static async Task<string> DownloadTextAsync(string url, CancellationToken token, Dictionary<string, string> headers = null)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL is empty.");
                token.ThrowIfCancellationRequested();

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.AllowAutoRedirect = true;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.UserAgent = "BehaviorLLM-ModelManager";

                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> kv in headers)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                        string key = kv.Key.Trim();
                        if (key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Accept = kv.Value;
                        }
                        else if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                        {
                            request.UserAgent = kv.Value;
                        }
                        else if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            request.ContentType = kv.Value;
                        }
                        else if (key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Referer = kv.Value;
                        }
                        else if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Host = kv.Value;
                        }
                        else
                        {
                            request.Headers[key] = kv.Value;
                        }
                    }
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    token.ThrowIfCancellationRequested();
                    return reader.ReadToEnd();
                }
            }, token);
        }

        public static void ExtractZipFile(string zipFilePath, string destinationDirectory, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath)) throw new ArgumentException("Zip file path is empty.");
            if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("Destination directory is empty.");
            if (!File.Exists(zipFilePath)) throw new FileNotFoundException("Zip archive not found.", zipFilePath);

            Directory.CreateDirectory(destinationDirectory);
            string fullDestination = Path.GetFullPath(destinationDirectory);
            if (!fullDestination.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                fullDestination += Path.DirectorySeparatorChar;
            }

            using (FileStream fs = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.FullName)) continue;

                    string targetPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
                    if (!targetPath.StartsWith(fullDestination, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"Zip entry escapes destination: {entry.FullName}");
                    }

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(targetPath);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    entry.ExtractToFile(targetPath, overwrite);
                }
            }
        }

        /// <summary>
        /// Where a download is assembled before it is moved into the project. Under Library/,
        /// which Unity does not watch and does not ship, so a partially written multi-gigabyte
        /// file is never seen by the asset importer. A resumed download finds its own part-file
        /// here, which is also why an interrupted download does not leave a broken asset behind.
        /// </summary>
        private static string GetStagingFilePath(string destinationFile)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, "Library", "BehaviorLLM", "Downloads",
                                Path.GetFileName(destinationFile) + ".part");
        }

        /// <summary>
        /// Resolves a pointer release to the build that actually holds the binaries.
        ///
        /// Upstream now publishes a stable "latest" release whose only asset is
        /// <c>nightly-tag.txt</c>, containing the tag of the nightly build with the real packages.
        /// A release that already carries zips is returned untouched, so this costs nothing once
        /// they change back.
        /// </summary>
        internal static async Task<LlamaCppReleaseInfo> FollowNightlyPointerIfNeeded(
            LlamaCppReleaseInfo release, Dictionary<string, string> headers, CancellationToken token)
        {
            if (release.assets != null)
            {
                foreach (LlamaCppReleaseAsset asset in release.assets)
                {
                    if (asset?.name != null && asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return release;   // real packages here already
                }
            }

            LlamaCppReleaseAsset pointer = null;
            if (release.assets != null)
            {
                foreach (LlamaCppReleaseAsset asset in release.assets)
                {
                    if (asset?.name != null &&
                        asset.name.IndexOf("nightly-tag", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        pointer = asset;
                        break;
                    }
                }
            }
            if (pointer == null || string.IsNullOrWhiteSpace(pointer.browser_download_url)) return release;

            string tag = (await BehaviorLLMDownloadUtility.DownloadTextAsync(
                pointer.browser_download_url, token, headers)).Trim();
            if (string.IsNullOrWhiteSpace(tag)) return release;

            string json = await BehaviorLLMDownloadUtility.DownloadTextAsync(
                "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/" +
                UnityWebRequest.EscapeURL(tag), token, headers);

            LlamaCppReleaseInfo resolved = JsonUtility.FromJson<LlamaCppReleaseInfo>(json);
            return resolved?.assets != null && resolved.assets.Length > 0 ? resolved : release;
        }

        private static void ProbeRemoteFile(string url, out long contentLength, out bool acceptsRanges)
        {
            contentLength = -1;
            acceptsRanges = false;
            HttpWebRequest head = (HttpWebRequest)WebRequest.Create(url);
            head.Method = "HEAD";
            head.AllowAutoRedirect = true;
            head.Timeout = 15000;
            head.ReadWriteTimeout = 15000;
            head.UserAgent = "BehaviorLLM-ModelManager";

            using (HttpWebResponse response = (HttpWebResponse)head.GetResponse())
            {
                contentLength = response.ContentLength;
                string acceptRanges = response.Headers["Accept-Ranges"];
                acceptsRanges = !string.IsNullOrWhiteSpace(acceptRanges) &&
                                acceptRanges.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool VerifySha256IfPresent(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256)) return true;

            string normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), normalizedExpected, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public class BehaviorLLMModelManagerWindow : EditorWindow
    {
        private const string CatalogResourceName = "BehaviorLLMModelCatalog";
        private const string ModelsSubFolder = "models";
        private const string ServerSubFolder = "llama-server";
        private const string LlamaReleaseApiPrimary = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";
        private const string LlamaReleaseApiFallback = "https://api.github.com/repos/ggerganov/llama.cpp/releases/latest";
        private const string PrefAutoInstallServer = "BehaviorLLM.ModelManager.AutoInstallServer";
        private const string PrefServerFlavor = "BehaviorLLM.ModelManager.ServerFlavor";
        private const string PrefKeepServerArchive = "BehaviorLLM.ModelManager.KeepServerArchive";
        private static readonly Color StatusGreen = new Color(0.20f, 0.78f, 0.35f);
        private static readonly Color StatusRed = new Color(0.86f, 0.26f, 0.26f);

        private BehaviorLLMModelCatalog catalog;
        private BehaviorLLMBackendConfigData config;
        private Vector2 scroll;

        private bool isDownloading;
        private float downloadProgress;
        private string downloadStatus;
        private string downloadTargetFile;
        private CancellationTokenSource cts;

        // --- live search -------------------------------------------------------------
        private const float ParamSliderMax = 72f;
        private static readonly string[] QuantLabels = { "Any quant", "Q4_K_M", "Q4_K_S", "Q5_K_M", "Q6_K", "Q8_0", "IQ4_XS" };
        private static readonly Color DimColor = new Color(0.62f, 0.62f, 0.62f);

        private string searchQuery = "";
        private bool preferOfficial = true;
        private float minParamsB;            // a range, because "under 4B" also hides nothing under 1B
        private float maxParamsB = 8f;       // the package targets small models; 4B is the measured ceiling
        private bool includeUnknownSize;     // a repository whose header and name both fail to state a size
        private bool includeUnverifiedArch;  // an architecture the support list has not seen
        private string nextPageUrl;
        private bool isLoadingMore;
        private int hiddenByFilters;
        private int filesLoaded, filesTotal;
        private int quantIndex = 1;          // Q4_K_M is what every shipped preset uses
        private List<BehaviorLLMHuggingFace.Repository> searchResults;
        private bool resultsArePinnedRepo;   // opened by id: shown whatever the filters say, with its warnings
        private bool isSearching;
        private string searchError;
        private CancellationTokenSource searchCts;
        private readonly HashSet<string> expandedRepos = new HashSet<string>();
        private List<BehaviorLLMModelPresets.Preset> presets;

        // --- installed ---------------------------------------------------------------
        // Headers are read once per file per window life. They are a kilobyte each, but the
        // Installed tab redraws on every mouse move and a file open per repaint would show.
        private readonly Dictionary<string, BehaviorLLMGgufHeader.Info> headerCache =
            new Dictionary<string, BehaviorLLMGgufHeader.Info>(StringComparer.OrdinalIgnoreCase);

        // Which search result a download came from, kept for this window's life so that a config
        // made later in the Installed tab can cite the repository. Lost when the window closes,
        // in which case the config is made from the file alone, which is still enough to run.
        private readonly Dictionary<string, (BehaviorLLMHuggingFace.Repository repo, BehaviorLLMHuggingFace.GgufFile file)> downloadOrigins =
            new Dictionary<string, (BehaviorLLMHuggingFace.Repository, BehaviorLLMHuggingFace.GgufFile)>(StringComparer.OrdinalIgnoreCase);

        // Where the open scene points. Refreshed on a timer rather than per repaint, because the
        // lookup walks every loaded scene.
        private BehaviorLLMServer[] sceneServers = System.Array.Empty<BehaviorLLMServer>();
        private BehaviorLLMClient[] sceneClients = System.Array.Empty<BehaviorLLMClient>();
        private double sceneScanTime = -1d;
        private const double SceneScanInterval = 0.75d;

        // Rough working-set estimate for a model at the shipped presets' context. Weights plus
        // a KV cache and compute buffers: measured at 1,999 MiB of weights and 70 MiB of buffers
        // for granite-3B at 512 tokens; 8192 tokens across four lanes adds a few hundred MiB more.
        private const double VramOverheadFactor = 1.15d;
        private const long VramOverheadBytes = 512L * 1024 * 1024;

        private GUIStyle titleStyle, sectionStyle, dimStyle, wrapDimStyle, monoStyle, monoDimStyle, statStyle;
        private GUIStyle pillStyle, cardStyle, activeCardStyle, rowStyle;

        [SerializeField] private BehaviorLLMManagerView managerView = BehaviorLLMManagerView.ModelCatalog;
        private bool autoInstallServerWithModelDownload = true;
        private LlamaServerBuildFlavor preferredServerFlavor = LlamaServerBuildFlavor.Cpu;
        private bool keepServerArchive = false;
        private string lastResolvedServerPackage = "";

        /// <summary>The window's name in the menu bar and the tab: one entry for four tabs.</summary>
        public const string WindowTitle = "BehaviorLLM Model Manager";

        /// <summary>
        /// One menu item under a top-level BehaviorLLM menu. There used to be three under Tools,
        /// one per tab, which opened three separate windows onto the same state; the tab bar
        /// already is the way to move between them. One instance, reused, keeps the tab you left.
        /// </summary>
        [MenuItem("BehaviorLLM/Model Manager", priority = 0)]
        public static void OpenModelManager()
        {
            var window = GetWindow<BehaviorLLMModelManagerWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(860f, 560f);
            window.Show();
        }

        /// <summary>Opens the window on a given tab, for buttons elsewhere in the Editor.</summary>
        internal static void OpenModelManager(BehaviorLLMManagerView view)
        {
            OpenModelManager();
            GetWindow<BehaviorLLMModelManagerWindow>().SwitchTo(view);
        }

        /// <summary>Kept for callers that used the per-tab entry points. They all open the same window.</summary>
        public static void OpenModelCatalog() => OpenModelManager(BehaviorLLMManagerView.ModelCatalog);
        public static void OpenLlamaServer() => OpenModelManager(BehaviorLLMManagerView.LlamaServer);
        public static void OpenRuntimeConfig() => OpenModelManager(BehaviorLLMManagerView.RuntimeConfig);

        private void OnEnable()
        {
            if (managerView == BehaviorLLMManagerView.Combined)
            {
                managerView = BehaviorLLMManagerView.ModelCatalog;
            }

            catalog = LoadCatalog();
            config = BehaviorLLMBackendConfig.Load() ?? BehaviorLLMBackendConfig.CreateDefault();
            if (string.IsNullOrWhiteSpace(downloadStatus)) downloadStatus = "Idle";
            autoInstallServerWithModelDownload = EditorPrefs.GetBool(PrefAutoInstallServer, true);
            preferredServerFlavor = (LlamaServerBuildFlavor)EditorPrefs.GetInt(PrefServerFlavor, (int)LlamaServerBuildFlavor.Cpu);
            keepServerArchive = EditorPrefs.GetBool(PrefKeepServerArchive, false);
            titleContent = new GUIContent(GetTitleForCurrentView());
        }

        private void OnDisable()
        {
            EditorPrefs.SetBool(PrefAutoInstallServer, autoInstallServerWithModelDownload);
            EditorPrefs.SetInt(PrefServerFlavor, (int)preferredServerFlavor);
            EditorPrefs.SetBool(PrefKeepServerArchive, keepServerArchive);
            CancelCurrentDownload();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (presets == null) presets = BehaviorLLMModelPresets.FindAll();

            DrawTabs();
            DrawStatusStrip();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (managerView)
            {
                case BehaviorLLMManagerView.RuntimeConfig: DrawRuntimeConfig(); break;
                case BehaviorLLMManagerView.LlamaServer: DrawServerInstaller(); break;
                case BehaviorLLMManagerView.Installed: DrawInstalledView(); break;
                default: DrawModelsView(); break;
            }
            EditorGUILayout.EndScrollView();

            DrawProgress();
            DrawDiskFooter();
        }

        // ------------------------------------------------------------------ chrome

        private void DrawTabs()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DrawTab("Catalog", BehaviorLLMManagerView.ModelCatalog);
                DrawTab("Installed", BehaviorLLMManagerView.Installed);
                DrawTab("Server", BehaviorLLMManagerView.LlamaServer);
                DrawTab("Config", BehaviorLLMManagerView.RuntimeConfig);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawTab(string label, BehaviorLLMManagerView view)
        {
            bool selected = managerView == view;
            if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton, GUILayout.Width(86f)) && !selected)
            {
                managerView = view;
                titleContent = new GUIContent(GetTitleForCurrentView());
                GUI.FocusControl(null);
            }
        }

        /// <summary>
        /// The three facts that matter in every tab. They used to live in whichever view happened
        /// to show them, which hid that "which model is active" is just as relevant while you are
        /// installing a server as while you are picking a model.
        /// </summary>
        private void DrawStatusStrip()
        {
            RefreshSceneTargets();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // Two facts where there used to be one, because "active" was a lie by omission:
                // the config file names a model, but a scene whose server carries a Model Config
                // runs that one instead, and a session was once spent on the wrong model with the
                // strip reading green throughout.
                string fileModel = string.IsNullOrWhiteSpace(config.activeModelRelativePath)
                    ? "(none)"
                    : Path.GetFileName(config.activeModelRelativePath);
                bool hasFileModel = !string.IsNullOrWhiteSpace(config.activeModelRelativePath);

                string sceneModel;
                bool sceneGood;
                if (sceneServers.Length == 0) { sceneModel = "no server in scene"; sceneGood = false; }
                else
                {
                    BehaviorLLMModelConfig pinned = PinnedModelConfig(sceneServers[0]);
                    if (pinned == null) { sceneModel = "uses config file"; sceneGood = hasFileModel; }
                    else { sceneModel = pinned.HasModelFile ? Path.GetFileName(pinned.modelFileName) : pinned.name; sceneGood = true; }
                }

                bool serverFound = BehaviorLLMServer.TryLocateExecutable(
                    config.executableRelativePath, out _, out string serverSource);

                DrawFact("In scene", sceneModel, sceneGood);
                DrawFact("Config file", fileModel, hasFileModel);
                DrawFact("Server", serverFound ? serverSource.ToLowerInvariant() : "not found", serverFound);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Config file", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    EnsureConfigFileExists();
                    EditorUtility.RevealInFinder(BehaviorLLMBackendConfig.GetConfigPath());
                }
            }
        }

        private void DrawFact(string key, string value, bool good)
        {
            GUILayout.Label(key, dimStyle, GUILayout.Width(78f));
            Color prior = GUI.color;
            GUI.color = good ? StatusGreen : Color.white;
            GUILayout.Label(value, monoStyle, GUILayout.MaxWidth(240f));
            GUI.color = prior;
            GUILayout.Space(14f);
        }

        // One window, one name. Per-tab titles made sense when each tab had its own menu entry
        // and could be docked separately; with one entry the tab bar says where you are.
        private string GetTitleForCurrentView() => WindowTitle;

        // ------------------------------------------------------------------ models

        /// <summary>
        /// The catalogue downloads and nothing else. Setting a model active, making a config for
        /// it and deleting it all live in the Installed tab, because those are things you do to a
        /// file you have, and a card that offered "set active" next to "download" invited setting
        /// active a file that was still arriving.
        /// </summary>
        private void DrawModelsView()
        {
            DrawSearchRow();
            DrawSection("Measured here", catalog?.models != null
                ? $"{catalog.models.Count} models · every reply was usable · figures from Experiments~"
                : "");

            if (catalog?.models == null || catalog.models.Count == 0)
            {
                EditorGUILayout.HelpBox($"Catalog missing or empty. Add Resources/{CatalogResourceName}.json.", MessageType.Warning);
            }
            else
            {
                foreach (BehaviorLLMModelDefinition model in catalog.models) DrawMeasuredCard(model);
            }

            DrawSection("Found online", SearchSubtitle());
            DrawSearchResults();

        }

        private string SearchSubtitle()
        {
            if (isSearching) return "searching…";
            if (!string.IsNullOrEmpty(searchError)) return "unavailable";
            if (searchResults == null) return "hugging face · gguf";
            return $"{searchResults.Count} repositories · hugging face · gguf";
        }

        /// <summary>
        /// One field, three behaviours. A search term searches; an <c>owner/repo</c> id opens that
        /// repository directly; a direct .gguf link downloads it. The separate "manual URL" box
        /// this replaces asked the developer to decide up front which kind of thing they had, and
        /// then to type a file name for it by hand.
        /// </summary>
        private enum QueryKind { Search, Repository, DirectFile }

        private QueryKind ClassifyQuery(string raw)
        {
            string q = (raw ?? string.Empty).Trim();
            if (q.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                q.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return q.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? QueryKind.DirectFile : QueryKind.Repository;

            // owner/name, the shape every Hugging Face id has, and nothing a search term would be.
            return System.Text.RegularExpressions.Regex.IsMatch(q, @"^[\w.-]+/[\w.-]+$")
                ? QueryKind.Repository
                : QueryKind.Search;
        }

        private void DrawSearchRow()
        {
            QueryKind kind = ClassifyQuery(searchQuery);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName("behaviorllm.search");
                searchQuery = EditorGUILayout.TextField(searchQuery, EditorStyles.toolbarSearchField);

                using (new EditorGUI.DisabledScope(kind != QueryKind.Search))
                {
                    quantIndex = EditorGUILayout.Popup(quantIndex, QuantLabels, EditorStyles.toolbarPopup, GUILayout.Width(90f));
                }

                using (new EditorGUI.DisabledScope(isSearching))
                {
                    // An empty box is a valid search: it asks for the most downloaded GGUF models,
                    // which is the right answer to "what is there?".
                    string label = kind == QueryKind.DirectFile ? "Download"
                                 : kind == QueryKind.Repository ? "Open repo"
                                 : string.IsNullOrWhiteSpace(searchQuery) ? "Browse" : "Search";
                    if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    {
                        switch (kind)
                        {
                            case QueryKind.DirectFile:
                                _ = DownloadModelAsync(searchQuery.Trim(), GuessFileNameFromUrl(searchQuery.Trim()), null);
                                break;
                            case QueryKind.Repository:
                                _ = OpenRepositoryAsync(ExtractRepoId(searchQuery));
                                break;
                            default:
                                _ = RunSearchAsync();
                                break;
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(kind != QueryKind.Search))
            using (new EditorGUILayout.HorizontalScope())
            {
                // A range rather than a ceiling: "under 4B" still shows every 0.5B toy model, and
                // the useful question is usually "between one and four".
                GUILayout.Label(new GUIContent("Model size",
                    "How many parameters the model has, in billions, from the GGUF header Hugging " +
                    "Face has parsed. Only when the header is missing is it read from the name. " +
                    "This is the cap that keeps results runnable on one machine: at Q4 a model " +
                    "takes about 0.6 GB of memory per billion parameters, plus room for context."),
                    dimStyle, GUILayout.Width(62f));

                // Spelled out rather than left to two bare numbers: "1 - 8" beside a slider says
                // nothing about what is being ranged.
                GUILayout.Label(DescribeParamRange(), monoStyle, GUILayout.Width(96f));

                minParamsB = Mathf.Clamp(EditorGUILayout.FloatField(minParamsB, GUILayout.Width(34f)), 0f, ParamSliderMax);
                EditorGUILayout.MinMaxSlider(ref minParamsB, ref maxParamsB, 0f, ParamSliderMax, GUILayout.MinWidth(60f));
                maxParamsB = Mathf.Clamp(EditorGUILayout.FloatField(maxParamsB, GUILayout.Width(34f)), 0f, ParamSliderMax);

                includeUnknownSize = EditorGUILayout.ToggleLeft(
                    new GUIContent("unsized",
                        "Repositories whose header and name both fail to state a parameter count. " +
                        "They are kept out by default, because that is how a 30B model slips past " +
                        "a 4B filter."),
                    includeUnknownSize, GUILayout.Width(70f));

                includeUnverifiedArch = EditorGUILayout.ToggleLeft(
                    new GUIContent("unverified arch",
                        "Models whose architecture is not on this package's list of what the " +
                        "shipped llama-server loads as a text model. llama.cpp adds architectures " +
                        "every few weeks, so a newer one may work; the list was checked against " +
                        "build b10809. Speech, embedding and vision models are never shown."),
                    includeUnverifiedArch, GUILayout.Width(112f));

                bool next = EditorGUILayout.ToggleLeft(
                    new GUIContent("official first",
                        "Ranks a publisher's own repository above re-quantised copies and fine-tunes. " +
                        "Hugging Face sorts by downloads, which often puts a third-party build above " +
                        "the original."),
                    preferOfficial, GUILayout.Width(92f));
                if (next != preferOfficial)
                {
                    preferOfficial = next;
                    if (searchResults != null) SortResults();
                }
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>"1B to 8B", "up to 4B", "8B and larger", "any size" - in words, not two numbers.</summary>
        private string DescribeParamRange()
        {
            bool noFloor = minParamsB <= 0.01f;
            bool noCeiling = maxParamsB >= ParamSliderMax;
            if (noFloor && noCeiling) return "any size";
            if (noFloor) return $"up to {maxParamsB:0.#}B";
            if (noCeiling) return $"{minParamsB:0.#}B and larger";
            return $"{minParamsB:0.#}B to {maxParamsB:0.#}B";
        }

        private static string ExtractRepoId(string raw)
        {
            string q = (raw ?? string.Empty).Trim().TrimEnd('/');
            const string host = "huggingface.co/";
            int at = q.IndexOf(host, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) q = q.Substring(at + host.Length);
            string[] parts = q.Split('/');
            return parts.Length >= 2 ? parts[0] + "/" + parts[1] : q;
        }

        /// <summary>Shows one repository as if it were a one-result search, files already expanded.</summary>
        private async Task OpenRepositoryAsync(string repoId)
        {
            if (string.IsNullOrWhiteSpace(repoId)) return;

            searchError = null;
            isSearching = true;
            Repaint();

            BehaviorLLMHuggingFace.Repository repo = new BehaviorLLMHuggingFace.Repository
            {
                Id = repoId,
                ParamsB = BehaviorLLMHuggingFace.ReadParamsB(repoId)
            };
            searchCts?.Cancel();
            searchCts = new CancellationTokenSource();
            try
            {
                // The header first, so a pasted speech model is called out the way a searched one
                // would have been hidden. A failure here is not fatal: the files still list.
                try { await BehaviorLLMHuggingFace.FetchMetadataAsync(repo, searchCts.Token); }
                catch (OperationCanceledException) { throw; }
                catch { }

                await BehaviorLLMHuggingFace.FetchFilesAsync(repo, searchCts.Token);
                searchResults = new List<BehaviorLLMHuggingFace.Repository> { repo };
                resultsArePinnedRepo = true;
                nextPageUrl = null;
                expandedRepos.Clear();
                expandedRepos.Add(repo.Id);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                searchError = e.Message;
                searchResults = null;
            }
            finally
            {
                isSearching = false;
                Repaint();
            }
        }

        /// <summary>
        /// Puts a publisher's own repository above re-quantised copies. A repository counts as
        /// official when its owner's name appears in the model name - "ibm-granite" publishing
        /// "granite-4.1-3b", "Qwen" publishing "Qwen3.5-4B" - which is how vendors name things and
        /// is not true of a re-quantiser like "bartowski" or a fine-tune under a personal account.
        /// </summary>
        private void SortResults()
        {
            if (searchResults == null) return;
            if (!preferOfficial)
            {
                searchResults.Sort((a, b) => b.Downloads.CompareTo(a.Downloads));
                return;
            }
            searchResults.Sort((a, b) =>
            {
                int byOfficial = IsOfficial(b).CompareTo(IsOfficial(a));
                return byOfficial != 0 ? byOfficial : b.Downloads.CompareTo(a.Downloads);
            });
        }

        internal static bool IsOfficial(BehaviorLLMHuggingFace.Repository repo)
        {
            if (repo == null) return false;
            string owner = Simplify(repo.Owner);
            string name = Simplify(repo.Name);
            if (owner.Length == 0 || name.Length == 0) return false;

            // Publisher name inside the model name: ibm-granite -> granite-4.1-3b.
            foreach (string token in repo.Owner.Split('-', '_', '.'))
            {
                string t = Simplify(token);
                if (t.Length >= 3 && name.Contains(t)) return true;
            }
            if (owner.Length >= 3 && name.Contains(owner)) return true;

            // And the other way round, because an organisation often suffixes its product name:
            // mistralai publishes Mistral-7B. Five characters, so that a short shared fragment
            // like "qwen" or "gguf" cannot make an unrelated account look like the publisher.
            foreach (string token in repo.Name.Split('-', '_', '.'))
            {
                string t = Simplify(token);
                if (t.Length >= 5 && owner.Contains(t)) return true;
            }
            return false;
        }

        private static string Simplify(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private void DrawSearchResults()
        {
            if (isSearching)
            {
                DrawSpinner(filesTotal > 0
                    ? $"Reading file lists…  {filesLoaded} of {filesTotal}"
                    : "Searching Hugging Face…");
                return;
            }

            if (!string.IsNullOrEmpty(searchError))
            {
                EditorGUILayout.HelpBox(
                    "Could not reach Hugging Face: " + searchError +
                    "\n\nThe measured models above are listed offline and install without a search.",
                    MessageType.Warning);
                if (GUILayout.Button("Retry", GUILayout.Width(70f))) _ = RunSearchAsync();
                return;
            }

            if (searchResults == null)
            {
                EditorGUILayout.LabelField(
                    "Search to find more models. Anything found online is unmeasured: it installs, " +
                    "but carries no figures of its own.", wrapDimStyle);
                return;
            }

            if (searchResults.Count == 0)
            {
                EditorGUILayout.LabelField("No GGUF repositories matched.", dimStyle);
                return;
            }

            EditorGUILayout.HelpBox(
                "Not measured by this project. These install and run, but the accuracy and speed " +
                "figures above come from this project's own harness and cannot honestly be filled " +
                "in for a model it has never run.", MessageType.Info);

            hiddenByFilters = 0;
            int shown = 0;
            foreach (BehaviorLLMHuggingFace.Repository repo in searchResults)
            {
                if (!PassesFilters(repo)) { hiddenByFilters++; continue; }
                DrawResultCard(repo);
                shown++;
            }

            if (shown == 0)
                EditorGUILayout.LabelField(
                    $"All {hiddenByFilters} results were filtered out. Widen the size range, pick " +
                    "Any quant, or allow unsized repositories or unverified architectures.", wrapDimStyle);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (hiddenByFilters > 0)
                    GUILayout.Label($"{hiddenByFilters} hidden by filters", monoDimStyle);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(nextPageUrl))
                {
                    using (new EditorGUI.DisabledScope(isLoadingMore))
                    {
                        if (GUILayout.Button(isLoadingMore ? "Loading…" : "Load more",
                                             GUILayout.Height(21f), GUILayout.Width(110f)))
                            _ = LoadMoreAsync();
                    }
                }
                else if (shown > 0)
                {
                    GUILayout.Label("end of results", monoDimStyle);
                }
            }
        }

        private void DrawMeasuredCard(BehaviorLLMModelDefinition model)
        {
            string localPath = GetLocalModelAbsolutePath(model.fileName);
            bool installed = File.Exists(localPath);
            long sizeOnDisk = installed ? SafeFileLength(localPath) : 0L;
            bool isActive = string.Equals(
                NormalizeRelativePath(config.activeModelRelativePath),
                NormalizeRelativePath(Path.Combine(ModelsSubFolder, model.fileName)),
                StringComparison.OrdinalIgnoreCase);

            BehaviorLLMModelPresets.Preset preset = BehaviorLLMModelPresets.MatchByFileName(presets, model.fileName);

            using (new EditorGUILayout.VerticalScope(isActive ? activeCardStyle : cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(string.IsNullOrWhiteSpace(model.displayName) ? model.id : model.displayName, titleStyle);
                    if (isActive) DrawPill("ACTIVE", StatusGreen);
                    GUILayout.FlexibleSpace();
                    DrawPill(installed ? "INSTALLED" : "NOT INSTALLED", installed ? StatusGreen : DimColor);
                }

                // The figures are why one of these is picked over another, so they lead.
                BehaviorLLMModelPresets.Preset measured = preset;
                if (measured != null && measured.Asset != null && measured.HasMeasurements)
                {
                    GUILayout.Space(3f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        float expected = measured.Asset.measuredExpectedActionRate;
                        DrawStat(Mathf.RoundToInt(expected * 100f) + "%", "accuracy", RateColor(expected),
                            "How often it chose the action a designer marked correct, over 16 labelled " +
                            "situations. Not a benchmark score: it is this project's own harness.");
                        DrawStat(Mathf.RoundToInt(measured.Asset.measuredP50LatencyMs) + " ms", "per decision",
                            null,
                            "Typical time for one decision on a mid-range PC, measured at the median. " +
                            "Half of decisions were faster than this.");
                        DrawStat(measured.Asset.recommendedProfile == DecisionProfile.Deliberative
                                     ? "Thinks first" : "Answers fast",
                                 "best used as", null,
                            "Reactive models answer in a beat and suit characters that must respond. " +
                            "Deliberative ones write a short reason first, which helps the largest " +
                            "model and costs latency everywhere.");
                        GUILayout.FlexibleSpace();
                        DrawStat(BehaviorLLMHuggingFace.FormatBytes(installed ? sizeOnDisk : 0L),
                                 installed ? "on disk" : "to download", null,
                            "How much space the model file takes.");
                    }
                    GUILayout.Space(2f);
                }

                if (!string.IsNullOrWhiteSpace(model.description))
                    GUILayout.Label(model.description, wrapDimStyle);

                // The link that was missing when the catalogue and the presets drifted apart.
                if (preset != null)
                    GUILayout.Label($"ctx {model.contextSize}  ·  pairs with {preset.DisplayName}", monoDimStyle);
                else
                    EditorGUILayout.HelpBox(
                        $"No model config expects '{model.fileName}'. Downloading it will work, but no " +
                        "shipped preset points at it.", MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (installed)
                    {
                        if (GUILayout.Button("Manage in Installed", GUILayout.Height(21f), GUILayout.Width(150f)))
                            SwitchTo(BehaviorLLMManagerView.Installed);
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(isDownloading || string.IsNullOrWhiteSpace(model.downloadUrl)))
                        {
                            if (GUILayout.Button("Download", GUILayout.Height(21f), GUILayout.Width(110f)))
                                _ = DownloadModelAsync(model.downloadUrl, model.fileName, model.sha256);
                        }
                        DrawVramFit(model.sizeBytes > 0 ? model.sizeBytes : 0L);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawResultCard(BehaviorLLMHuggingFace.Repository repo)
        {
            bool expanded = expandedRepos.Contains(repo.Id);

            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Owner is provenance and stays quiet; the model name is what is being chosen.
                    GUILayout.Label(repo.Owner + "/", monoDimStyle, GUILayout.ExpandWidth(false));
                    GUILayout.Label(repo.Name, titleStyle, GUILayout.ExpandWidth(false));
                    if (IsOfficial(repo)) DrawPill("OFFICIAL", StatusGreen);
                    DrawArchitecturePill(repo.Architecture, repo.Support);
                    DrawThinkingPill(repo.Thinking);
                    GUILayout.FlexibleSpace();
                    if (repo.ParamsB > 0f)
                        GUILayout.Label(new GUIContent(repo.ParamsB.ToString("0.#") + "B",
                            repo.ParamsFromHeader ? "Parameter count from the GGUF header."
                                                  : "Parameter count read from the name; the header did not say."),
                            repo.ParamsFromHeader ? monoStyle : monoDimStyle);
                    GUILayout.Label("↓ " + BehaviorLLMHuggingFace.FormatCount(repo.Downloads), monoDimStyle);
                }

                if (repo.Support == BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration)
                    EditorGUILayout.HelpBox(
                        $"'{repo.Architecture}' is not a text model. llama-server may load it, but it cannot " +
                        "answer a prompt with an action, so a decision maker gets nothing from it.",
                        MessageType.Warning);
                else if (!string.IsNullOrEmpty(repo.PipelineTag) &&
                         !string.Equals(repo.PipelineTag, BehaviorLLMHuggingFace.TextGenerationPipeline, StringComparison.OrdinalIgnoreCase))
                    EditorGUILayout.HelpBox(
                        $"Listed on Hugging Face as '{repo.PipelineTag}', not text generation.", MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = repo.FilesLoaded
                        ? (expanded ? "Hide files" : $"{repo.Files.Count} GGUF files")
                        : "Show files";
                    if (GUILayout.Button(label, GUILayout.Height(20f), GUILayout.Width(130f)))
                    {
                        if (expanded) expandedRepos.Remove(repo.Id);
                        else
                        {
                            expandedRepos.Add(repo.Id);
                            if (!repo.FilesLoaded) _ = LoadFilesAsync(repo);
                        }
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open on HF", GUILayout.Height(20f), GUILayout.Width(92f)))
                        Application.OpenURL(repo.PageUrl);
                }

                if (!expanded) return;

                if (!repo.FilesLoaded)
                {
                    GUILayout.Label("Reading file list…", dimStyle);
                    return;
                }

                string wanted = QuantLabels[quantIndex];
                int shown = 0;
                foreach (BehaviorLLMHuggingFace.GgufFile file in repo.Files)
                {
                    if (quantIndex > 0 && !string.Equals(file.Quant, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    DrawResultFileRow(repo, file);
                    shown++;
                }

                if (shown == 0)
                    GUILayout.Label($"No {wanted} file here. Set the quantisation filter to Any to see the rest.", dimStyle);
            }
        }

        private void DrawResultFileRow(BehaviorLLMHuggingFace.Repository repo, BehaviorLLMHuggingFace.GgufFile file)
        {
            string localPath = GetLocalModelAbsolutePath(file.FileName);
            bool installed = File.Exists(localPath);
            BehaviorLLMModelPresets.Preset preset = BehaviorLLMModelPresets.MatchByFileName(presets, file.FileName);

            using (new EditorGUILayout.VerticalScope(rowStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(file.Quant)) DrawPill(file.Quant, new Color(0.55f, 0.72f, 0.95f));
                    GUILayout.Label(file.FileName, monoStyle);
                    GUILayout.FlexibleSpace();
                    // Size decides whether this download is worth starting, so it is the loud one.
                    GUILayout.Label(BehaviorLLMHuggingFace.FormatBytes(file.SizeBytes), statStyle, GUILayout.Width(76f));
                    if (installed) DrawPill("INSTALLED", StatusGreen);
                    else if (!string.IsNullOrEmpty(file.Sha256)) DrawPill("SHA256", DimColor);
                }

                if (preset != null)
                    GUILayout.Label("Matches " + preset.DisplayName, monoDimStyle);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (installed)
                    {
                        if (GUILayout.Button("Manage in Installed", GUILayout.Height(20f), GUILayout.Width(140f)))
                            SwitchTo(BehaviorLLMManagerView.Installed);
                    }
                    else
                    {
                        // A file that cannot decide is still downloadable from a pasted id - the
                        // filters never see a pinned repository - but not from a green button.
                        bool notText = repo.Support == BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration;
                        using (new EditorGUI.DisabledScope(isDownloading || notText))
                        {
                            if (GUILayout.Button("Download", GUILayout.Height(20f), GUILayout.Width(86f)))
                            {
                                // Remember where it came from, so the Installed tab can make a config
                                // that cites the repository rather than only the file name.
                                downloadOrigins[file.FileName] = (repo, file);
                                _ = DownloadModelAsync(file.DownloadUrl, file.FileName, file.Sha256);
                            }
                        }
                    }
                    DrawVramFit(file.SizeBytes);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private async Task RunSearchAsync()
        {
            searchError = null;
            isSearching = true;
            hiddenByFilters = 0;
            nextPageUrl = null;
            resultsArePinnedRepo = false;
            expandedRepos.Clear();
            Repaint();

            searchCts?.Cancel();
            searchCts = new CancellationTokenSource();
            try
            {
                BehaviorLLMHuggingFace.Page page =
                    await BehaviorLLMHuggingFace.SearchAsync(searchQuery, 25, searchCts.Token);

                // Read every result's files before showing anything. Rendering first and filtering
                // as the lists arrive makes the list visibly reshuffle for seconds on a slow
                // connection; one wait with a count is calmer than a list that will not sit still.
                await LoadFileListsAsync(page.Repositories, searchCts.Token);

                searchResults = page.Repositories;
                nextPageUrl = page.NextUrl;
                SortResults();
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer search; the newer one owns the UI now
            }
            catch (Exception e)
            {
                searchError = e.Message;
                searchResults = null;
            }
            finally
            {
                isSearching = false;
                Repaint();
            }
        }

        /// <summary>
        /// Fetches the next page and appends it. The cursor comes from Hugging Face, so this
        /// cannot skip or repeat a repository even if the ranking shifts between requests.
        /// </summary>
        private async Task LoadMoreAsync()
        {
            if (string.IsNullOrEmpty(nextPageUrl) || isLoadingMore) return;
            isLoadingMore = true;
            Repaint();
            try
            {
                BehaviorLLMHuggingFace.Page page = await BehaviorLLMHuggingFace.SearchAsync(
                    searchQuery, 25, searchCts.Token, nextPageUrl);
                await LoadFileListsAsync(page.Repositories, searchCts.Token);
                nextPageUrl = page.NextUrl;
                if (searchResults == null) searchResults = new List<BehaviorLLMHuggingFace.Repository>();
                searchResults.AddRange(page.Repositories);
                SortResults();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { downloadStatus = "Could not load more: " + e.Message; }
            finally { isLoadingMore = false; Repaint(); }
        }

        /// <summary>
        /// Reads every result's file list in the background.
        ///
        /// Without this the quantisation filter is a lie: quantisation is a property of a file,
        /// not of a repository, so nothing can be filtered by it until the files are known. The
        /// list stays usable while this runs - rows appear immediately and are filtered as their
        /// contents arrive - and it is one request per result, which is why it is not done for
        /// pages nobody has asked for.
        /// </summary>
        private async Task LoadFileListsAsync(List<BehaviorLLMHuggingFace.Repository> repos, CancellationToken token)
        {
            if (repos == null) return;
            filesLoaded = 0;
            filesTotal = repos.Count;
            try
            {
                for (int i = 0; i < repos.Count; i++)
                {
                    if (token.IsCancellationRequested) return;
                    if (!repos[i].FilesLoaded)
                    {
                        try { await BehaviorLLMHuggingFace.FetchFilesAsync(repos[i], token); }
                        catch (OperationCanceledException) { return; }
                        catch { repos[i].Files = new List<BehaviorLLMHuggingFace.GgufFile>(); }
                    }
                    filesLoaded = i + 1;
                    Repaint();
                }
            }
            finally { filesTotal = 0; }
        }

        /// <summary>True when a repository has at least one file the current filters accept.</summary>
        private bool PassesFilters(BehaviorLLMHuggingFace.Repository repo)
        {
            if (repo == null) return false;

            // A repository opened by id is shown with its warnings rather than hidden: the user
            // asked for this one by name, and an empty list would say nothing about why.
            if (resultsArePinnedRepo) return true;

            // The search already asks Hugging Face for text generation only. This is the second
            // check, against the header, for a repository tagged loosely or not at all.
            switch (repo.Support)
            {
                case BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration: return false;
                case BehaviorLLMHuggingFace.ArchitectureSupport.Unverified: if (!includeUnverifiedArch) return false; break;
            }

            bool sized = repo.ParamsB > 0f;
            if (!sized && !includeUnknownSize) return false;
            if (sized && (repo.ParamsB < minParamsB || (maxParamsB < ParamSliderMax && repo.ParamsB > maxParamsB)))
                return false;

            if (quantIndex == 0) return true;
            if (!repo.FilesLoaded) return true;      // still checking; do not hide it prematurely

            string wanted = QuantLabels[quantIndex];
            for (int i = 0; i < repo.Files.Count; i++)
            {
                if (string.Equals(repo.Files[i].Quant, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private async Task LoadFilesAsync(BehaviorLLMHuggingFace.Repository repo)
        {
            try
            {
                await BehaviorLLMHuggingFace.FetchFilesAsync(repo, searchCts?.Token ?? CancellationToken.None);
            }
            catch (Exception e)
            {
                repo.Files = new List<BehaviorLLMHuggingFace.GgufFile>();
                downloadStatus = "Could not read the file list: " + e.Message;
            }
            Repaint();
        }

        /// <summary>
        /// Removes a downloaded model. There was no way to do this from the Editor before, which
        /// is why a models folder quietly accumulates gigabytes nothing acknowledges.
        /// </summary>
        private void DeleteModelFile(string fileName, bool isActive)
        {
            string absolute = GetLocalModelAbsolutePath(fileName);
            if (!File.Exists(absolute)) return;

            string size = BehaviorLLMHuggingFace.FormatBytes(SafeFileLength(absolute));
            string warning = isActive
                ? "\n\nThis is the active model. Nothing will load until another is set active."
                : string.Empty;

            if (!EditorUtility.DisplayDialog("Delete model",
                    $"Permanently delete {fileName}?\n\nThis frees {size}. It can be downloaded again." + warning,
                    "Delete", "Cancel"))
                return;

            try
            {
                File.Delete(absolute);
                string meta = absolute + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
                headerCache.Remove(absolute);
                downloadOrigins.Remove(fileName);
                if (isActive)
                {
                    config.activeModelRelativePath = string.Empty;
                    BehaviorLLMBackendConfig.Save(config);
                }
                downloadStatus = $"Deleted {fileName} ({size} freed).";

                // A config made in this project for the file is now a config for nothing. Offer to
                // take it too; a shipped preset stays, because the file can come back.
                BehaviorLLMModelPresets.Preset orphan = BehaviorLLMModelPresets.MatchByFileName(presets, fileName);
                if (orphan != null && !orphan.IsShipped && orphan.Asset != null &&
                    EditorUtility.DisplayDialog("Delete its config too?",
                        $"{orphan.DisplayName} ({orphan.AssetPath}) points only at the file you just deleted.",
                        "Delete config", "Keep it"))
                {
                    AssetDatabase.DeleteAsset(orphan.AssetPath);
                    presets = BehaviorLLMModelPresets.FindAll();
                    downloadStatus += $" Removed {orphan.DisplayName}.";
                }
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                downloadStatus = "Could not delete: " + e.Message;
            }
            Repaint();
        }

        // ------------------------------------------------------------------ installed

        /// <summary>
        /// Everything actually on disk, whatever it came from - the curated list, a search result
        /// or a pasted link. The Models tab answers "what could I use"; this one answers "what am
        /// I carrying", which is the question you have when a project folder has grown by four
        /// gigabytes and you cannot remember why.
        /// </summary>
        private void DrawInstalledView()
        {
            string dir = GetModelsDirectoryAbsolute();
            List<string> files = new List<string>();
            if (Directory.Exists(dir))
                files.AddRange(Directory.EnumerateFiles(dir, "*.gguf", SearchOption.TopDirectoryOnly));
            files.Sort((a, b) => SafeFileLength(b).CompareTo(SafeFileLength(a)));   // largest first

            if (files.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No models downloaded yet.\n\nThe Catalog tab lists the three this project has " +
                    "measured, and searches Hugging Face for anything else.", MessageType.Info);
                if (GUILayout.Button("Go to Catalog", GUILayout.Height(22f), GUILayout.Width(120f)))
                    SwitchTo(BehaviorLLMManagerView.ModelCatalog);
                return;
            }

            long total = 0L;
            foreach (string f in files) total += SafeFileLength(f);
            DrawSection("On disk", $"{files.Count} model{(files.Count == 1 ? "" : "s")} · {BehaviorLLMHuggingFace.FormatBytes(total)}");

            foreach (string path in files) DrawInstalledCard(path);
        }

        private void DrawInstalledCard(string absolutePath)
        {
            string fileName = Path.GetFileName(absolutePath);
            long size = SafeFileLength(absolutePath);
            bool isActive = string.Equals(
                NormalizeRelativePath(config.activeModelRelativePath),
                NormalizeRelativePath(Path.Combine(ModelsSubFolder, fileName)),
                StringComparison.OrdinalIgnoreCase);

            BehaviorLLMModelPresets.Preset preset = BehaviorLLMModelPresets.MatchByFileName(presets, fileName);
            BehaviorLLMModelDefinition curated = FindCuratedByFileName(fileName);
            BehaviorLLMGgufHeader.Info header = HeaderFor(absolutePath);
            bool notText = header.Support == BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration;

            // "Active" here means what the open scene will actually run, which is the pinned
            // config when there is one and the config file otherwise.
            bool inScene = preset != null && IsPinnedInScene(preset.Asset);
            bool sceneUsesFile = sceneServers.Length > 0 && PinnedModelConfig(sceneServers[0]) == null;
            bool active = inScene || (isActive && (sceneUsesFile || sceneServers.Length == 0));

            using (new EditorGUILayout.VerticalScope(active ? activeCardStyle : cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(curated != null && !string.IsNullOrWhiteSpace(curated.displayName)
                        ? curated.displayName : fileName, titleStyle);
                    if (inScene) DrawPill("IN SCENE", StatusGreen);
                    else if (isActive) DrawPill(sceneServers.Length > 0 && !sceneUsesFile ? "CONFIG FILE ONLY" : "ACTIVE", sceneServers.Length > 0 && !sceneUsesFile ? DimColor : StatusGreen);
                    if (curated != null) DrawPill("MEASURED", new Color(0.45f, 0.65f, 0.95f));
                    DrawArchitecturePill(header.Architecture, header.Support);
                    DrawThinkingPill(header.Thinking);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(BehaviorLLMHuggingFace.FormatBytes(size), statStyle, GUILayout.Width(78f));
                }

                if (header.Thinking == BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks)
                    EditorGUILayout.HelpBox(
                        "Reasoning model: it thinks before every answer and its template has no switch to stop " +
                        "that. It cannot run the Reactive profile. Set the decision makers that use it to " +
                        "Deliberative with a Thinking Budget, or expect the same cheapest action every time.",
                        MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (curated != null) GUILayout.Label(fileName, monoDimStyle);
                    GUILayout.FlexibleSpace();
                    DrawVramFit(size);
                }

                if (notText)
                    EditorGUILayout.HelpBox(
                        $"Not a text model: the file declares '{header.Architecture}'. llama-server may load it, " +
                        "but it cannot answer a prompt with an action. It takes up space and can be deleted.",
                        MessageType.Error);
                else if (!header.IsGguf && header.Error.Length > 0)
                    EditorGUILayout.HelpBox("Could not read the file header: " + header.Error, MessageType.Warning);

                // The question this tab exists to answer: is this file wired to anything?
                if (preset != null)
                    GUILayout.Label((preset.IsShipped ? "Shipped preset " : "Config ") + preset.DisplayName +
                                    (inScene ? " · assigned in the open scene" : ""), monoDimStyle);
                else if (!notText)
                    EditorGUILayout.HelpBox(
                        "No model config points at this file. Nothing will load it until one does; " +
                        "\"Set active in current scene\" creates one and assigns it.",
                        MessageType.Warning);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isDownloading || inScene || notText))
                    {
                        if (GUILayout.Button(new GUIContent(inScene ? "Active in current scene" : "Set active in current scene",
                                "Assigns this model's config to every Behavior LLM Server and Client in the open " +
                                "scene(s), creating the config first if none exists, and writes the same choice " +
                                "to the StreamingAssets config file for scenes that leave Model Config empty. " +
                                "The scene is marked dirty; save it to keep the change."),
                                GUILayout.Height(21f), GUILayout.Width(176f)))
                            SetActiveInCurrentScene(fileName, preset, header, curated);
                    }

                    if (preset == null && !notText)
                    {
                        using (new EditorGUI.DisabledScope(isDownloading))
                        {
                            if (GUILayout.Button(new GUIContent("Create model config",
                                    $"Creates a BehaviorLLMModelConfig for this file under {BehaviorLLMModelPresets.UserConfigFolder}, " +
                                    "with the measured fields left blank."),
                                    GUILayout.Height(21f), GUILayout.Width(140f)))
                            {
                                BehaviorLLMModelConfig created = CreateConfigFor(fileName, header);
                                if (created != null) { Selection.activeObject = created; EditorGUIUtility.PingObject(created); }
                            }
                        }
                    }

                    if (preset != null && GUILayout.Button("Select config", GUILayout.Height(21f), GUILayout.Width(96f)))
                    {
                        Selection.activeObject = preset.Asset;
                        EditorGUIUtility.PingObject(preset.Asset);
                    }

                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(isDownloading))
                    {
                        if (GUILayout.Button("Delete · " + BehaviorLLMHuggingFace.FormatBytes(size),
                                             GUILayout.Height(21f), GUILayout.Width(150f)))
                            DeleteModelFile(fileName, isActive);
                    }
                }
            }
        }

        private BehaviorLLMModelDefinition FindCuratedByFileName(string fileName)
        {
            if (catalog?.models == null) return null;
            for (int i = 0; i < catalog.models.Count; i++)
            {
                if (string.Equals(catalog.models[i].fileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return catalog.models[i];
            }
            return null;
        }

        // ------------------------------------------------------------------ server

        private void DrawServerInstaller()
        {
            // Resolved exactly as the runtime resolves it, including PATH and the places package
            // managers install into. Checking only StreamingAssets reported "not installed" on a
            // machine where the runtime would have launched a winget install without complaint.
            bool serverFound = BehaviorLLMServer.TryLocateExecutable(
                config.executableRelativePath, out string serverPath, out string serverSource);
            bool bundled = serverFound && string.Equals(serverSource, "StreamingAssets", StringComparison.Ordinal);

            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("llama-server", titleStyle);
                    GUILayout.FlexibleSpace();
                    DrawPill(serverFound ? "FOUND · " + serverSource.ToUpperInvariant() : "NOT FOUND",
                             serverFound ? StatusGreen : StatusRed);
                }

                GUILayout.Label(serverFound ? serverPath : GetServerDirectoryAbsolute(), monoDimStyle);
                if (serverFound && !bundled)
                    EditorGUILayout.HelpBox(
                        "This is a system-wide install, found on " + serverSource + ". It works: the " +
                        "runtime resolves it the same way. Installing a copy under StreamingAssets is " +
                        "only needed to ship the server with a build.", MessageType.None);
                if (!string.IsNullOrWhiteSpace(lastResolvedServerPackage))
                    GUILayout.Label("Last package: " + lastResolvedServerPackage, monoDimStyle);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(isDownloading))
                    {
                        if (GUILayout.Button(bundled ? "Reinstall" : "Install under StreamingAssets", GUILayout.Height(21f)))
                            _ = DownloadAndInstallLlamaServerAsync();
                    }
                    using (new EditorGUI.DisabledScope(!serverFound))
                    {
                        if (GUILayout.Button("Use in config", GUILayout.Height(21f)))
                            ConfigureInstalledServerExecutable();
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open folder", GUILayout.Height(21f), GUILayout.Width(92f)))
                    {
                        EnsureServerDirectory();
                        EditorUtility.RevealInFinder(GetServerDirectoryAbsolute());
                    }
                }
            }

            DrawSection("Build", "");
            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                // Named for what it is: which build to fetch if you install one here. It used to
                // read "Preferred build: CPU", which looks like a statement about what you are
                // running - and says nothing at all about a server found on PATH.
                preferredServerFlavor = (LlamaServerBuildFlavor)EditorGUILayout.EnumPopup(
                    new GUIContent("Build to download", "Only used when installing a copy under " +
                        "StreamingAssets. It does not describe a server already on your machine."),
                    preferredServerFlavor);
                EditorGUILayout.HelpBox(
                    "CPU works everywhere and is the slowest. Pick the build that matches your GPU: " +
                    "CUDA for NVIDIA, Vulkan or HIP for AMD, SYCL for Intel. The wrong one still runs, " +
                    "on the CPU, which usually looks like the model being mysteriously slow.",
                    MessageType.Info);

                autoInstallServerWithModelDownload = EditorGUILayout.ToggleLeft(
                    "Install the server automatically when a model is set active and none is present",
                    autoInstallServerWithModelDownload);
                keepServerArchive = EditorGUILayout.ToggleLeft("Keep the downloaded archive", keepServerArchive);
            }
        }

        // ------------------------------------------------------------------ config

        private void DrawRuntimeConfig()
        {
            if (config == null) config = BehaviorLLMBackendConfig.CreateDefault();

            EditorGUILayout.HelpBox(
                "Written to StreamingAssets/behaviorllm_backend_config.json, which the runtime reads " +
                "at play. Per-character tuning lives in the config assets, not here.", MessageType.Info);

            using (new EditorGUILayout.VerticalScope(cardStyle))
            {
                config.executableRelativePath = EditorGUILayout.TextField("Executable", config.executableRelativePath);
                config.port = Mathf.Max(1, EditorGUILayout.IntField("Port", config.port));
                config.contextSize = Mathf.Max(256, EditorGUILayout.IntField("Context size", config.contextSize));
                config.gpuLayers = Mathf.Max(0, EditorGUILayout.IntField("GPU layers", config.gpuLayers));

                EditorGUILayout.HelpBox(
                    $"Context is divided across parallel slots. At {config.contextSize} with 8 slots each " +
                    $"character gets {config.contextSize / 8} tokens. PrisonYard runs eight.",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save", GUILayout.Height(21f)))
                    {
                        downloadStatus = BehaviorLLMBackendConfig.Save(config)
                            ? "Backend config saved."
                            : "Failed to save backend config.";
                        AssetDatabase.Refresh();
                    }
                    if (GUILayout.Button("Reset to defaults", GUILayout.Height(21f)))
                    {
                        config = BehaviorLLMBackendConfig.CreateDefault();
                        downloadStatus = "Config reset in the editor. Save to persist it.";
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Open file", GUILayout.Height(21f), GUILayout.Width(88f)))
                    {
                        EnsureConfigFileExists();
                        EditorUtility.RevealInFinder(BehaviorLLMBackendConfig.GetConfigPath());
                    }
                }
            }
        }

        // ------------------------------------------------------------------ shared parts

        private void DrawProgress()
        {
            if (!isDownloading && string.IsNullOrWhiteSpace(downloadStatus)) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(downloadStatus ?? "Idle", dimStyle);
                if (!isDownloading) return;

                Rect r = GUILayoutUtility.GetRect(8f, 18f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, downloadProgress, Mathf.RoundToInt(downloadProgress * 100f) + "%");
                if (GUILayout.Button("Cancel", GUILayout.Height(20f), GUILayout.Width(80f))) CancelCurrentDownload();
            }
        }

        /// <summary>Total on disk, and a way to reach it. Both were missing entirely.</summary>
        private void DrawDiskFooter()
        {
            long total = 0L;
            int count = 0;
            string dir = GetModelsDirectoryAbsolute();
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*.gguf", SearchOption.TopDirectoryOnly))
                {
                    total += SafeFileLength(f);
                    count++;
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Models folder", dimStyle, GUILayout.Width(84f));
                GUILayout.Label($"{BehaviorLLMHuggingFace.FormatBytes(total)} in {count} file{(count == 1 ? "" : "s")}", monoStyle);
                GUILayout.Space(14f);
                // Everything under StreamingAssets ships in every player build. Said here, once,
                // beside the number it applies to.
                GUILayout.Label(new GUIContent("GPU", "The graphics device Unity sees and its video memory. Each card " +
                        "says whether its file fits; a model over this runs paged over the bus, slower than the CPU."),
                    dimStyle, GUILayout.Width(28f));
                GUILayout.Label(VramBytes > 0
                        ? $"{SystemInfo.graphicsDeviceName} · {BehaviorLLMHuggingFace.FormatBytes(VramBytes)}"
                        : "unknown", monoStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reveal", EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    EnsureModelsDirectory();
                    EditorUtility.RevealInFinder(dir);
                }
                if (GUILayout.Button("Reload", EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    catalog = LoadCatalog();
                    presets = BehaviorLLMModelPresets.FindAll();
                }
            }
        }

        /// <summary>
        /// Unity's own throbber, which is eleven frames indexed by wall clock. The window asks for
        /// a repaint while it is showing, because IMGUI only redraws on input otherwise and a
        /// still spinner reads as a hang.
        /// </summary>
        private void DrawSpinner(string message)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int frame = (int)(EditorApplication.timeSinceStartup * 12d) % 12;
                GUIContent icon = EditorGUIUtility.IconContent("WaitSpin" + frame.ToString("00"));
                GUILayout.Label(icon, GUILayout.Width(18f), GUILayout.Height(18f));
                GUILayout.Label(message, dimStyle);
                GUILayout.FlexibleSpace();
            }
            Repaint();
        }

        private void DrawSection(string title, string subtitle)
        {
            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(title.ToUpperInvariant(), sectionStyle);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(subtitle)) GUILayout.Label(subtitle, monoDimStyle);
            }
            Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(1f, 1f, 1f, 0.08f));
            GUILayout.Space(3f);
        }

        private void DrawPill(string text, Color color)
        {
            Color prior = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, pillStyle);
            GUI.color = prior;
        }

        private static long SafeFileLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return 0L; }
        }

        /// <summary>
        /// Four levels of emphasis, because a list where every line is the same weight makes the
        /// reader do the sorting. Loudest is the model name; then the figure that decides the
        /// choice; then supporting numbers; then provenance, which matters only when something
        /// looks wrong.
        /// </summary>
        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            bool pro = EditorGUIUtility.isProSkin;
            Color bright = pro ? new Color(0.93f, 0.93f, 0.94f) : new Color(0.06f, 0.06f, 0.08f);
            Color normal = pro ? new Color(0.78f, 0.78f, 0.80f) : new Color(0.16f, 0.16f, 0.18f);
            Color faint  = pro ? new Color(0.55f, 0.56f, 0.59f) : new Color(0.42f, 0.43f, 0.46f);

            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            titleStyle.normal.textColor = bright;

            sectionStyle = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9 };
            sectionStyle.normal.textColor = faint;

            dimStyle = new GUIStyle(EditorStyles.miniLabel);
            dimStyle.normal.textColor = faint;

            wrapDimStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, fontSize = 11 };
            wrapDimStyle.normal.textColor = normal;

            monoStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            monoStyle.normal.textColor = normal;

            monoDimStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
            monoDimStyle.normal.textColor = faint;

            statStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            statStyle.normal.textColor = bright;

            pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(5, 5, 1, 1)
            };
            cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
            activeCardStyle = new GUIStyle(cardStyle);
            rowStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 5, 5),
                margin = new RectOffset(12, 0, 2, 2)
            };
        }

        /// <summary>
        /// A number with its label, the number carrying the weight.
        ///
        /// One reserved rect and two draws rather than a nested vertical group. A fixed-width
        /// group inside a row that also has flexible space is the classic way to end up with a
        /// different control count between the layout and repaint passes, which IMGUI reports as
        /// "getting control 0's position in a group with only 0 controls" and then cascades.
        /// </summary>
        private void DrawStat(string value, string label, Color? valueColor = null, string tooltip = null)
        {
            const float w = 78f;
            Rect r = GUILayoutUtility.GetRect(w, 30f, GUILayout.Width(w), GUILayout.Height(30f));
            if (!string.IsNullOrEmpty(tooltip)) GUI.Label(r, new GUIContent(string.Empty, tooltip));
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;

            Color prior = GUI.color;
            if (valueColor.HasValue) GUI.color = valueColor.Value;
            GUI.Label(new Rect(r.x, r.y, r.width, 16f), value ?? string.Empty, statStyle ?? EditorStyles.boldLabel);
            GUI.color = prior;
            GUI.Label(new Rect(r.x, r.y + 14f, r.width, 14f), label ?? string.Empty, monoDimStyle ?? EditorStyles.miniLabel);
        }

        /// <summary>Green when a rate is good, amber when it is workable, faint when it is not.</summary>
        private static Color RateColor(float rate01)
        {
            if (rate01 >= 0.80f) return StatusGreen;
            if (rate01 >= 0.60f) return new Color(0.85f, 0.62f, 0.25f);
            return new Color(0.62f, 0.62f, 0.62f);
        }


        /// <summary>
        /// Downloads a model into StreamingAssets/models and stops there. What to do with it is
        /// the Installed tab's business. The server is installed alongside when it is missing and
        /// the preference says so, because a model with nothing to run it is not yet usable.
        /// </summary>
        private async Task DownloadModelAsync(string url, string fileName, string sha256)
        {
            if (isDownloading) return;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName))
            {
                downloadStatus = "Invalid URL or filename.";
                Repaint();
                return;
            }

            try
            {
                isDownloading = true;
                downloadProgress = 0f;
                downloadTargetFile = GetLocalModelAbsolutePath(fileName);
                downloadStatus = $"Downloading {fileName}...";
                cts = new CancellationTokenSource();
                Repaint();

                var progress = new Progress<DownloadProgressInfo>(p =>
                {
                    downloadProgress = p.normalized;
                    if (p.totalBytes > 0)
                        downloadStatus = $"Downloading {fileName} ({FormatBytes(p.downloadedBytes)} / {FormatBytes(p.totalBytes)})";
                    else
                        downloadStatus = $"Downloading {fileName} ({FormatBytes(p.downloadedBytes)})";
                    Repaint();
                });

                await BehaviorLLMDownloadUtility.DownloadFileAsync(url, downloadTargetFile, sha256, progress, cts.Token);

                downloadProgress = 1f;
                downloadStatus = $"Downloaded {fileName}. Set it active from the Installed tab.";
                headerCache.Remove(downloadTargetFile);
                AssetDatabase.Refresh();

                if (autoInstallServerWithModelDownload && !File.Exists(GetServerExecutableAbsolute()))
                {
                    await DownloadAndInstallLlamaServerCoreAsync(cts.Token, configureAsDefault: true);
                }
            }
            catch (OperationCanceledException)
            {
                downloadStatus = "Download canceled.";
            }
            catch (Exception e)
            {
                downloadStatus = $"Download failed: {e.Message}";
                Debug.LogError($"[BehaviorLLM Model Manager] {downloadStatus}");
            }
            finally
            {
                isDownloading = false;
                cts?.Dispose();
                cts = null;
                Repaint();
            }
        }

        private async Task DownloadAndInstallLlamaServerAsync()
        {
            if (isDownloading) return;

            try
            {
                isDownloading = true;
                downloadProgress = 0f;
                downloadStatus = "Resolving latest llama.cpp server package...";
                cts = new CancellationTokenSource();
                Repaint();

                await DownloadAndInstallLlamaServerCoreAsync(cts.Token, configureAsDefault: true);
            }
            catch (OperationCanceledException)
            {
                downloadStatus = "Server download canceled.";
            }
            catch (Exception e)
            {
                downloadStatus = $"Server install failed: {e.Message}";
                Debug.LogError($"[BehaviorLLM Model Manager] {downloadStatus}");
            }
            finally
            {
                isDownloading = false;
                cts?.Dispose();
                cts = null;
                Repaint();
            }
        }

        private async Task DownloadAndInstallLlamaServerCoreAsync(CancellationToken token, bool configureAsDefault)
        {
            if (!IsCurrentPlatformSupportedForAutoServerDownload())
            {
                throw new NotSupportedException("Automatic server download currently supports Windows only.");
            }

            LlamaCppReleaseAsset asset = await ResolvePreferredLlamaServerAssetAsync(token);
            if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                throw new InvalidOperationException("No matching llama.cpp binary package found for the selected build.");
            }

            lastResolvedServerPackage = asset.name;

            string tempArchivePath = Path.Combine(Path.GetTempPath(), asset.name);
            downloadTargetFile = tempArchivePath;
            downloadStatus = $"Downloading server package {asset.name}...";
            downloadProgress = 0f;
            Repaint();

            var progress = new Progress<DownloadProgressInfo>(p =>
            {
                downloadProgress = p.normalized;
                if (p.totalBytes > 0)
                {
                    downloadStatus = $"Downloading server {asset.name} ({FormatBytes(p.downloadedBytes)} / {FormatBytes(p.totalBytes)})";
                }
                else
                {
                    downloadStatus = $"Downloading server {asset.name} ({FormatBytes(p.downloadedBytes)})";
                }

                Repaint();
            });

            await BehaviorLLMDownloadUtility.DownloadFileAsync(asset.browser_download_url, tempArchivePath, null, progress, token);

            downloadStatus = $"Extracting {asset.name}...";
            downloadProgress = 1f;
            Repaint();

            string serverDir = GetServerDirectoryAbsolute();
            if (Directory.Exists(serverDir))
            {
                Directory.Delete(serverDir, true);
            }

            Directory.CreateDirectory(serverDir);
            BehaviorLLMDownloadUtility.ExtractZipFile(tempArchivePath, serverDir, overwrite: true);

            if (!keepServerArchive && File.Exists(tempArchivePath))
            {
                File.Delete(tempArchivePath);
            }

            string executablePath = GetServerExecutableAbsolute();
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"llama-server executable not found after extraction: {executablePath}");
            }

            if (configureAsDefault)
            {
                ConfigureInstalledServerExecutable();
            }

            AssetDatabase.Refresh();
            downloadStatus = $"Server installed: {asset.name}";
        }

        private async Task<LlamaCppReleaseAsset> ResolvePreferredLlamaServerAssetAsync(CancellationToken token)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Accept", "application/vnd.github+json" }
            };

            string releaseJson;
            try
            {
                releaseJson = await BehaviorLLMDownloadUtility.DownloadTextAsync(LlamaReleaseApiPrimary, token, headers);
            }
            catch
            {
                releaseJson = await BehaviorLLMDownloadUtility.DownloadTextAsync(LlamaReleaseApiFallback, token, headers);
            }

            LlamaCppReleaseInfo release = JsonUtility.FromJson<LlamaCppReleaseInfo>(releaseJson);
            if (release == null)
            {
                throw new InvalidOperationException("Failed to parse llama.cpp release metadata.");
            }

            // llama.cpp's "latest" release no longer carries the binaries. It is a pointer: a
            // single nightly-tag.txt naming the build tag that does. Follow it, or the installer
            // reports "no matching package" for every flavour on a project that is simply looking
            // at the wrong release.
            release = await BehaviorLLMDownloadUtility.FollowNightlyPointerIfNeeded(release, headers, token);

            if (release.assets == null || release.assets.Length == 0)
            {
                throw new InvalidOperationException(
                    "The llama.cpp release carries no downloadable packages. Their release layout " +
                    "may have changed again; install llama.cpp yourself and the package will find " +
                    "it on PATH.");
            }

            string[] preferredPatterns = GetPreferredServerAssetNamePatterns();
            foreach (string pattern in preferredPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                foreach (LlamaCppReleaseAsset asset in release.assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.name)) continue;
                    if (asset.name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        private string[] GetPreferredServerAssetNamePatterns()
        {
            // Pattern order provides fallback when exact naming changes between releases.
            switch (preferredServerFlavor)
            {
                case LlamaServerBuildFlavor.Cuda124:
                    return new[] { "bin-win-cuda-12.4-x64", "bin-win-cuda-12", "cudart-llama-bin-win-cuda-12.4-x64" };
                case LlamaServerBuildFlavor.Cuda131:
                    return new[] { "bin-win-cuda-13.1-x64", "bin-win-cuda-13", "cudart-llama-bin-win-cuda-13.1-x64" };
                case LlamaServerBuildFlavor.Vulkan:
                    return new[] { "bin-win-vulkan-x64" };
                case LlamaServerBuildFlavor.Sycl:
                    return new[] { "bin-win-sycl-x64" };
                case LlamaServerBuildFlavor.HipRadeon:
                    return new[] { "bin-win-hip-radeon-x64", "bin-win-hip-x64" };
                default:
                    return new[] { "bin-win-cpu-x64" };
            }
        }

        private static bool IsCurrentPlatformSupportedForAutoServerDownload()
        {
#if UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }

        private void CancelCurrentDownload()
        {
            if (!isDownloading || cts == null) return;
            cts.Cancel();
        }

        private void SetActiveModel(string fileName, int contextSize, int gpuLayers)
        {
            config.activeModelRelativePath = NormalizeRelativePath(Path.Combine(ModelsSubFolder, fileName));
            if (contextSize > 0) config.contextSize = contextSize;
            if (gpuLayers >= 0) config.gpuLayers = gpuLayers;

            if (BehaviorLLMBackendConfig.Save(config))
            {
                AssetDatabase.Refresh();
                downloadStatus = $"Active model set: {config.activeModelRelativePath}";
            }
            else
            {
                downloadStatus = "Failed to write backend config.";
            }

            Repaint();
        }

        // ------------------------------------------------------------------ scene

        /// <summary>
        /// Makes <paramref name="fileName"/> the model the open scene runs. Three steps, each
        /// visible in the status line: find or create the config that names the file, assign it
        /// to every server and client in the loaded scenes, and write the same choice to the
        /// StreamingAssets file for scenes that leave Model Config empty.
        ///
        /// The old "Set active" wrote only the file. A scene built by either sample pins a config
        /// on its server, and that config wins, so the button changed nothing and said it had.
        /// Assigning through <see cref="SerializedObject"/> gives undo and marks the scene dirty,
        /// which is the same path the inspector takes.
        /// </summary>
        private void SetActiveInCurrentScene(string fileName, BehaviorLLMModelPresets.Preset preset,
                                             BehaviorLLMGgufHeader.Info header, BehaviorLLMModelDefinition curated)
        {
            BehaviorLLMModelConfig target = preset?.Asset;
            bool created = false;
            if (target == null)
            {
                target = CreateConfigFor(fileName, header);
                if (target == null) { downloadStatus = "Could not create a model config for " + fileName; Repaint(); return; }
                created = true;
            }

            RefreshSceneTargets(force: true);
            int servers = 0, clients = 0;
            foreach (BehaviorLLMServer server in sceneServers) if (Assign(server, target)) servers++;
            foreach (BehaviorLLMClient client in sceneClients) if (Assign(client, target)) clients++;

            // The file too, so a scene whose server has no Model Config follows the same choice,
            // and the Config tab keeps telling the truth about what the file says.
            SetActiveModel(fileName,
                curated != null ? curated.contextSize : target.contextSize,
                curated != null ? curated.gpuLayers : target.gpuLayers);

            string scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(scene)) scene = "Untitled";
            string madeConfig = created ? $" Created {target.name} under {BehaviorLLMModelPresets.UserConfigFolder}." : string.Empty;

            if (servers == 0 && clients == 0)
                downloadStatus = $"No Behavior LLM Server or Client in '{scene}'. The config file now names {fileName}; " +
                                 $"a server left without a Model Config will load it.{madeConfig}";
            else
                downloadStatus = $"'{scene}' now runs {fileName}: {servers} server{(servers == 1 ? "" : "s")} and " +
                                 $"{clients} client{(clients == 1 ? "" : "s")} assigned.{madeConfig} Save the scene to keep it." +
                                 (EditorApplication.isPlaying ? " A running server keeps its old model until restarted." : string.Empty);
            Repaint();
        }

        /// <summary>Assigns through the serialized field so undo and dirtying behave as they would from the inspector.</summary>
        private static bool Assign(Component component, BehaviorLLMModelConfig target)
        {
            if (component == null) return false;
            SerializedObject so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty("modelConfig");
            if (prop == null) return false;
            if (prop.objectReferenceValue == target) return true;
            prop.objectReferenceValue = target;
            so.ApplyModifiedProperties();
            return true;
        }

        private BehaviorLLMModelConfig CreateConfigFor(string fileName, BehaviorLLMGgufHeader.Info header)
        {
            BehaviorLLMModelConfig createdConfig = downloadOrigins.TryGetValue(fileName, out var origin)
                ? BehaviorLLMModelPresets.CreateFromSearchResult(origin.repo, origin.file, config.contextSize, config.gpuLayers)
                : BehaviorLLMModelPresets.CreateForInstalledFile(fileName, header, config.contextSize, config.gpuLayers);
            if (createdConfig != null)
            {
                presets = BehaviorLLMModelPresets.FindAll();
                downloadStatus = $"Created {createdConfig.name} under {BehaviorLLMModelPresets.UserConfigFolder} (measured fields left blank).";
            }
            return createdConfig;
        }

        private void RefreshSceneTargets(bool force = false)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && sceneScanTime >= 0d && now - sceneScanTime < SceneScanInterval) return;
            sceneScanTime = now;
            // The active scene only. "Current scene" has to mean the one whose name is in the
            // hierarchy in bold: with a second scene loaded additively, reaching into it too
            // reassigned a sample scene that happened to be open beside a scratch one.
            UnityEngine.SceneManagement.Scene active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            sceneServers = InActiveScene(FindObjectsByType<BehaviorLLMServer>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID), active);
            sceneClients = InActiveScene(FindObjectsByType<BehaviorLLMClient>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID), active);
        }

        private static T[] InActiveScene<T>(T[] found, UnityEngine.SceneManagement.Scene active) where T : Component
        {
            List<T> kept = new List<T>(found.Length);
            foreach (T c in found) if (c != null && c.gameObject.scene == active) kept.Add(c);
            return kept.ToArray();
        }

        /// <summary>The Model Config a server carries, read from the serialized field because the runtime keeps it private.</summary>
        private static BehaviorLLMModelConfig PinnedModelConfig(Component component)
        {
            if (component == null) return null;
            SerializedProperty prop = new SerializedObject(component).FindProperty("modelConfig");
            return prop?.objectReferenceValue as BehaviorLLMModelConfig;
        }

        private bool IsPinnedInScene(BehaviorLLMModelConfig asset)
        {
            if (asset == null || sceneServers.Length == 0) return false;
            foreach (BehaviorLLMServer server in sceneServers) if (PinnedModelConfig(server) == asset) return true;
            return false;
        }

        private void SwitchTo(BehaviorLLMManagerView view)
        {
            managerView = view;
            titleContent = new GUIContent(GetTitleForCurrentView());
            GUI.FocusControl(null);
            scroll = Vector2.zero;
        }

        // ------------------------------------------------------------------ header and GPU

        private BehaviorLLMGgufHeader.Info HeaderFor(string absolutePath)
        {
            if (!headerCache.TryGetValue(absolutePath, out BehaviorLLMGgufHeader.Info info))
            {
                info = BehaviorLLMGgufHeader.Read(absolutePath);
                headerCache[absolutePath] = info;
            }
            return info;
        }

        private void DrawArchitecturePill(string architecture, BehaviorLLMHuggingFace.ArchitectureSupport support)
        {
            switch (support)
            {
                case BehaviorLLMHuggingFace.ArchitectureSupport.Supported:
                    DrawPill(architecture.ToLowerInvariant(), new Color(0.45f, 0.65f, 0.95f)); break;
                case BehaviorLLMHuggingFace.ArchitectureSupport.NotTextGeneration:
                    DrawPill("NOT A TEXT MODEL", StatusRed); break;
                default:
                    DrawPill(string.IsNullOrEmpty(architecture) ? "ARCH ?" : architecture.ToLowerInvariant() + " ?", DimColor); break;
            }
        }

        /// <summary>
        /// One pill, only for the case that bites. A switchable template is what the package
        /// handles already, and a template with no thinking is the norm; neither needs saying.
        /// </summary>
        private void DrawThinkingPill(BehaviorLLMHuggingFace.ThinkingStyle thinking)
        {
            if (thinking != BehaviorLLMHuggingFace.ThinkingStyle.AlwaysThinks) return;
            Color prior = GUI.color;
            GUI.color = new Color(0.95f, 0.65f, 0.25f);
            GUILayout.Label(new GUIContent("REASONING MODEL",
                    "Its chat template opens every answer with a thinking block and has no enable_thinking " +
                    "switch, so the package cannot ask it not to think. It will not work in the Reactive " +
                    "profile: it spends the 48-token budget reasoning and never answers. Run it Deliberative " +
                    "with a Thinking Budget."),
                pillStyle, GUILayout.ExpandWidth(false));
            GUI.color = prior;
        }

        /// <summary>Video memory on this machine in bytes, or 0 when Unity cannot tell.</summary>
        private static long VramBytes => SystemInfo.graphicsMemorySize > 0 ? SystemInfo.graphicsMemorySize * 1024L * 1024L : 0L;

        /// <summary>What a model of this file size needs resident to run fully on the GPU, roughly.</summary>
        private static long EstimatedVramNeed(long fileBytes) => (long)(fileBytes * VramOverheadFactor) + VramOverheadBytes;

        /// <summary>
        /// Whether a file of this size runs on this machine's GPU. Drawn beside every download
        /// and every installed file, because the failure mode is silent: a model that does not
        /// fit is not refused, it is paged over the bus by the driver and runs at a fraction of
        /// CPU speed while reporting every layer offloaded. Measured on 2026-09-14: 6.5 tokens a
        /// second on the GPU against 24 on the CPU, with the card's memory taken by other
        /// processes.
        /// </summary>
        private void DrawVramFit(long fileBytes)
        {
            if (fileBytes <= 0) return;
            long vram = VramBytes;
            if (vram <= 0) return;

            long need = EstimatedVramNeed(fileBytes);
            bool fits = need <= vram;
            string tip = $"Needs about {BehaviorLLMHuggingFace.FormatBytes(need)} of video memory resident for full GPU " +
                         $"offload; this machine reports {BehaviorLLMHuggingFace.FormatBytes(vram)} on " +
                         $"{SystemInfo.graphicsDeviceName}. Other programs holding video memory count against " +
                         "that, and a model that does not fit runs slower than on the CPU while looking offloaded.";
            Color prior = GUI.color;
            GUI.color = fits ? StatusGreen : StatusRed;
            GUILayout.Label(new GUIContent(fits ? "fits GPU" : "over GPU memory", tip), monoStyle, GUILayout.ExpandWidth(false));
            GUI.color = prior;
        }

        private void EnsureConfigFileExists()
        {
            BehaviorLLMBackendConfigData existing = BehaviorLLMBackendConfig.Load();
            if (existing == null)
            {
                existing = config ?? BehaviorLLMBackendConfig.CreateDefault();
                BehaviorLLMBackendConfig.Save(existing);
                AssetDatabase.Refresh();
            }
        }

        private void ConfigureInstalledServerExecutable()
        {
            string serverExecutableAbsolute = GetServerExecutableAbsolute();
            if (!File.Exists(serverExecutableAbsolute))
            {
                downloadStatus = $"Server executable not found: {serverExecutableAbsolute}";
                Repaint();
                return;
            }

            string relativeToStreaming = Path.GetRelativePath(Application.streamingAssetsPath, serverExecutableAbsolute);
            config.executableRelativePath = NormalizeRelativePath(relativeToStreaming);
            if (BehaviorLLMBackendConfig.Save(config))
            {
                AssetDatabase.Refresh();
                downloadStatus = $"Backend executable set: {config.executableRelativePath}";
            }
            else
            {
                downloadStatus = "Failed to save backend executable path.";
            }

            Repaint();
        }

        private static BehaviorLLMModelCatalog LoadCatalog()
        {
            TextAsset jsonAsset = Resources.Load<TextAsset>(CatalogResourceName);
            if (jsonAsset == null)
            {
                Debug.LogWarning($"[BehaviorLLM Model Manager] Missing Resources/{CatalogResourceName}.json");
                return new BehaviorLLMModelCatalog();
            }

            try
            {
                var loaded = JsonUtility.FromJson<BehaviorLLMModelCatalog>(jsonAsset.text);
                return loaded ?? new BehaviorLLMModelCatalog();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BehaviorLLM Model Manager] Catalog parse failed: {e.Message}");
                return new BehaviorLLMModelCatalog();
            }
        }

        private static string GuessFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string segment = uri.Segments[uri.Segments.Length - 1];
                return segment.Trim('/');
            }
            catch
            {
                return "";
            }
        }

        private static string NormalizeRelativePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/');
        }

        private static void DrawStatusDot(string label, bool status)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);

            Color previous = GUI.contentColor;
            GUI.contentColor = status ? StatusGreen : StatusRed;
            GUILayout.Label("\u25CF", GUILayout.Width(14f));
            GUI.contentColor = previous;

            EditorGUILayout.EndHorizontal();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "?";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024d && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }

        private static string GetModelsDirectoryAbsolute()
        {
            return Path.Combine(Application.streamingAssetsPath, ModelsSubFolder);
        }

        private static string GetServerDirectoryAbsolute()
        {
            return Path.Combine(Application.streamingAssetsPath, ServerSubFolder);
        }

        private static void EnsureModelsDirectory()
        {
            string dir = GetModelsDirectoryAbsolute();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static void EnsureServerDirectory()
        {
            string dir = GetServerDirectoryAbsolute();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        private static string GetLocalModelAbsolutePath(string fileName)
        {
            EnsureModelsDirectory();
            return Path.Combine(GetModelsDirectoryAbsolute(), fileName);
        }

        private static string GetServerExecutableAbsolute()
        {
            EnsureServerDirectory();
            string serverDir = GetServerDirectoryAbsolute();
            string expected = Path.Combine(serverDir, GetExecutableFileNameForCurrentPlatform());
            if (File.Exists(expected)) return expected;

            string[] recursiveMatches = Directory.GetFiles(serverDir, GetExecutableFileNameForCurrentPlatform(), SearchOption.AllDirectories);
            return recursiveMatches.Length > 0 ? recursiveMatches[0] : expected;
        }

        private static string GetExecutableFileNameForCurrentPlatform()
        {
#if UNITY_EDITOR_WIN
            return "llama-server.exe";
#else
            return "llama-server";
#endif
        }
    }
}
