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
using UnityEditor;
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
        RuntimeConfig = 3
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

                long existingLength = File.Exists(destinationFile) ? new FileInfo(destinationFile).Length : 0;

                if (remoteLength > 0 && existingLength == remoteLength)
                {
                    if (VerifySha256IfPresent(destinationFile, expectedSha256))
                    {
                        progress?.Report(new DownloadProgressInfo
                        {
                            downloadedBytes = existingLength,
                            totalBytes = remoteLength,
                            normalized = 1f
                        });
                        return;
                    }
                    File.Delete(destinationFile);
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
                using (FileStream fs = new FileStream(destinationFile, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
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

                if (!VerifySha256IfPresent(destinationFile, expectedSha256))
                {
                    throw new Exception($"SHA256 mismatch for '{Path.GetFileName(destinationFile)}'.");
                }
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

        private string manualUrl = "";
        private string manualFileName = "";
        [SerializeField] private BehaviorLLMManagerView managerView = BehaviorLLMManagerView.ModelCatalog;
        private bool autoInstallServerWithModelDownload = true;
        private LlamaServerBuildFlavor preferredServerFlavor = LlamaServerBuildFlavor.Cpu;
        private bool keepServerArchive = false;
        private string lastResolvedServerPackage = "";

        [MenuItem("Tools/BehaviorLLM/Model Catalog")]
        public static void OpenModelCatalog()
        {
            OpenWindowForView(BehaviorLLMManagerView.ModelCatalog, "BehaviorLLM Model Catalog", new Vector2(860f, 560f));
        }

        [MenuItem("Tools/BehaviorLLM/Llama Server")]
        public static void OpenLlamaServer()
        {
            OpenWindowForView(BehaviorLLMManagerView.LlamaServer, "BehaviorLLM Llama Server", new Vector2(820f, 460f));
        }

        [MenuItem("Tools/BehaviorLLM/Runtime Config")]
        public static void OpenRuntimeConfig()
        {
            OpenWindowForView(BehaviorLLMManagerView.RuntimeConfig, "BehaviorLLM Runtime Config", new Vector2(760f, 360f));
        }

        private static void OpenWindowForView(BehaviorLLMManagerView view, string title, Vector2 minSize)
        {
            var window = CreateInstance<BehaviorLLMModelManagerWindow>();
            window.managerView = view;
            window.titleContent = new GUIContent(title);
            window.minSize = minSize;
            window.Show();
        }

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
            DrawHeader();

            switch (managerView)
            {
                case BehaviorLLMManagerView.RuntimeConfig:
                    DrawRuntimeConfig();
                    break;

                case BehaviorLLMManagerView.LlamaServer:
                    DrawServerInstaller();
                    DrawProgress();
                    break;

                case BehaviorLLMManagerView.ModelCatalog:
                    DrawProgress();
                    DrawCatalog();
                    DrawManualDownload();
                    break;

                default:
                    DrawProgress();
                    DrawCatalog();
                    DrawManualDownload();
                    break;
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetTitleForCurrentView(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Config file", BehaviorLLMBackendConfig.GetConfigPath());
            EditorGUILayout.LabelField("Active model", string.IsNullOrWhiteSpace(config.activeModelRelativePath) ? "(none)" : config.activeModelRelativePath);
            if (managerView == BehaviorLLMManagerView.ModelCatalog)
            {
                EditorGUILayout.LabelField("Models folder", GetModelsDirectoryAbsolute());
            }
            if (managerView == BehaviorLLMManagerView.LlamaServer)
            {
                EditorGUILayout.LabelField("Server folder", GetServerDirectoryAbsolute());
            }

            EditorGUILayout.BeginHorizontal();
            if (managerView == BehaviorLLMManagerView.ModelCatalog &&
                GUILayout.Button("Reload Catalog", GUILayout.Height(24)))
            {
                catalog = LoadCatalog();
                Repaint();
            }

            if (managerView == BehaviorLLMManagerView.ModelCatalog &&
                GUILayout.Button("Open Models Folder", GUILayout.Height(24)))
            {
                EnsureModelsDirectory();
                EditorUtility.RevealInFinder(GetModelsDirectoryAbsolute());
            }

            if (managerView == BehaviorLLMManagerView.LlamaServer &&
                GUILayout.Button("Open Server Folder", GUILayout.Height(24)))
            {
                EnsureServerDirectory();
                EditorUtility.RevealInFinder(GetServerDirectoryAbsolute());
            }

            if (GUILayout.Button("Open Config File", GUILayout.Height(24)))
            {
                EnsureConfigFileExists();
                EditorUtility.RevealInFinder(BehaviorLLMBackendConfig.GetConfigPath());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private string GetTitleForCurrentView()
        {
            switch (managerView)
            {
                case BehaviorLLMManagerView.ModelCatalog:
                    return "BehaviorLLM Model Catalog";
                case BehaviorLLMManagerView.LlamaServer:
                    return "BehaviorLLM Llama Server";
                case BehaviorLLMManagerView.RuntimeConfig:
                    return "BehaviorLLM Runtime Config";
                default:
                    return "BehaviorLLM Model Catalog";
            }
        }

        private void DrawRuntimeConfig()
        {
            if (config == null) config = BehaviorLLMBackendConfig.CreateDefault();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Config", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure local llama.cpp server launch defaults stored in behaviorllm_backend_config.json.", MessageType.Info);

            config.executableRelativePath = EditorGUILayout.TextField("Executable Path", config.executableRelativePath);
            config.port = Mathf.Max(1, EditorGUILayout.IntField("Port", config.port));
            config.contextSize = Mathf.Max(256, EditorGUILayout.IntField("Context Size", config.contextSize));
            config.gpuLayers = Mathf.Max(0, EditorGUILayout.IntField("GPU Layers", config.gpuLayers));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Config", GUILayout.Height(22)))
            {
                if (BehaviorLLMBackendConfig.Save(config))
                {
                    AssetDatabase.Refresh();
                    downloadStatus = "Backend config saved.";
                }
                else
                {
                    downloadStatus = "Failed to save backend config.";
                }
                Repaint();
            }

            if (GUILayout.Button("Reset to Defaults", GUILayout.Height(22)))
            {
                config = BehaviorLLMBackendConfig.CreateDefault();
                downloadStatus = "Config reset in editor (click Save Config to persist).";
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawServerInstaller()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Llama Server Binary", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Downloads llama.cpp prebuilt binaries and installs llama-server into StreamingAssets.", MessageType.Info);

            autoInstallServerWithModelDownload = EditorGUILayout.ToggleLeft("Auto-install server on 'Download + Set Active' when missing", autoInstallServerWithModelDownload);
            preferredServerFlavor = (LlamaServerBuildFlavor)EditorGUILayout.EnumPopup("Preferred Build", preferredServerFlavor);
            keepServerArchive = EditorGUILayout.ToggleLeft("Keep downloaded server archive (.zip)", keepServerArchive);

            string executableRelative = NormalizeRelativePath(Path.Combine(ServerSubFolder, GetExecutableFileNameForCurrentPlatform()));
            EditorGUILayout.LabelField("Server folder", GetServerDirectoryAbsolute());
            EditorGUILayout.LabelField("Configured executable", string.IsNullOrWhiteSpace(config.executableRelativePath) ? "(not set)" : config.executableRelativePath);
            EditorGUILayout.LabelField("Recommended executable", executableRelative);
            if (!string.IsNullOrWhiteSpace(lastResolvedServerPackage))
            {
                EditorGUILayout.LabelField("Last package", lastResolvedServerPackage);
            }

            bool serverInstalled = File.Exists(GetServerExecutableAbsolute());
            DrawStatusDot("Installed", serverInstalled);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isDownloading))
            {
                if (GUILayout.Button(serverInstalled ? "Reinstall Server" : "Download Server", GUILayout.Height(22)))
                {
                    _ = DownloadAndInstallLlamaServerAsync();
                }
            }

            if (GUILayout.Button("Open Server Folder", GUILayout.Height(22)))
            {
                EnsureServerDirectory();
                EditorUtility.RevealInFinder(GetServerDirectoryAbsolute());
            }

            using (new EditorGUI.DisabledScope(!serverInstalled))
            {
                if (GUILayout.Button("Use Installed Server In Config", GUILayout.Height(22)))
                {
                    ConfigureInstalledServerExecutable();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawProgress()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Download", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(downloadStatus);

            Rect r = GUILayoutUtility.GetRect(8f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(r, isDownloading ? downloadProgress : 0f, isDownloading ? $"{Mathf.RoundToInt(downloadProgress * 100f)}%" : "Idle");

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!isDownloading))
            {
                if (GUILayout.Button("Cancel Current Download", GUILayout.Height(22)))
                {
                    CancelCurrentDownload();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawCatalog()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Curated Catalog", EditorStyles.boldLabel);

            if (catalog == null || catalog.models == null || catalog.models.Count == 0)
            {
                EditorGUILayout.HelpBox($"Catalog missing or empty. Add Resources/{CatalogResourceName}.json.", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (BehaviorLLMModelDefinition model in catalog.models)
            {
                DrawModelCard(model);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawModelCard(BehaviorLLMModelDefinition model)
        {
            string localPath = GetLocalModelAbsolutePath(model.fileName);
            bool installed = File.Exists(localPath);
            bool isActive = string.Equals(NormalizeRelativePath(config.activeModelRelativePath), NormalizeRelativePath(Path.Combine(ModelsSubFolder, model.fileName)), StringComparison.OrdinalIgnoreCase);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(model.displayName) ? model.id : model.displayName, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(model.description))
                EditorGUILayout.LabelField(model.description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("File", model.fileName);
            EditorGUILayout.LabelField("URL", model.downloadUrl);
            DrawStatusDot("Installed", installed);
            DrawStatusDot("Active", isActive);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isDownloading || string.IsNullOrWhiteSpace(model.downloadUrl) || string.IsNullOrWhiteSpace(model.fileName)))
            {
                if (GUILayout.Button(installed ? "Re-Download" : "Download", GUILayout.Height(22)))
                {
                    _ = DownloadAndMaybeSetActive(model.downloadUrl, model.fileName, model.sha256, false, null);
                }

                if (GUILayout.Button("Download + Set Active", GUILayout.Height(22)))
                {
                    _ = DownloadAndMaybeSetActive(model.downloadUrl, model.fileName, model.sha256, true, model);
                }
            }

            using (new EditorGUI.DisabledScope(isDownloading || !installed))
            {
                if (GUILayout.Button("Set Active", GUILayout.Height(22)))
                {
                    SetActiveModel(model.fileName, model.contextSize, model.gpuLayers);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawManualDownload()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Manual URL Download", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Use direct GGUF URL (for example Hugging Face resolve/main link).", MessageType.Info);

            manualUrl = EditorGUILayout.TextField("GGUF URL", manualUrl);
            manualFileName = EditorGUILayout.TextField("Output Filename", manualFileName);

            if (string.IsNullOrWhiteSpace(manualFileName) && !string.IsNullOrWhiteSpace(manualUrl))
            {
                manualFileName = GuessFileNameFromUrl(manualUrl);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isDownloading || string.IsNullOrWhiteSpace(manualUrl) || string.IsNullOrWhiteSpace(manualFileName)))
            {
                if (GUILayout.Button("Download", GUILayout.Height(22)))
                {
                    _ = DownloadAndMaybeSetActive(manualUrl, manualFileName, null, false, null);
                }

                if (GUILayout.Button("Download + Set Active", GUILayout.Height(22)))
                {
                    _ = DownloadAndMaybeSetActive(manualUrl, manualFileName, null, true, null);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private async Task DownloadAndMaybeSetActive(string url, string fileName, string sha256, bool setActiveAfter, BehaviorLLMModelDefinition modelForConfig)
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
                downloadStatus = $"Download complete: {fileName}";
                AssetDatabase.Refresh();

                if (setActiveAfter)
                {
                    int context = modelForConfig != null ? modelForConfig.contextSize : config.contextSize;
                    int gpuLayers = modelForConfig != null ? modelForConfig.gpuLayers : config.gpuLayers;
                    SetActiveModel(fileName, context, gpuLayers);

                    if (autoInstallServerWithModelDownload && !File.Exists(GetServerExecutableAbsolute()))
                    {
                        await DownloadAndInstallLlamaServerCoreAsync(cts.Token, configureAsDefault: true);
                    }
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
            if (release == null || release.assets == null || release.assets.Length == 0)
            {
                throw new InvalidOperationException("Failed to parse llama.cpp release metadata.");
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
