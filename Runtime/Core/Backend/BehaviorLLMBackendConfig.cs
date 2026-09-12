using BehaviorLLM.Core.Config;
using System.IO;
using System;
using UnityEngine;

namespace BehaviorLLM.Core.Backend
{
    [Serializable]
    public class BehaviorLLMBackendConfigData
    {
        [Tooltip("Relative path from StreamingAssets to active model file.")]
        public string activeModelRelativePath = "models/my-model.gguf";

        [Tooltip("Relative path from StreamingAssets to llama-server executable (without extension).")]
        public string executableRelativePath = "llama-server";

        [Tooltip("Default completion endpoint port for llama-server.")]
        public int port = 8080;

        [Tooltip("Default context size used for llama-server.")]
        public int contextSize = 2048;

        [Tooltip("Default number of GPU layers for llama-server.")]
        public int gpuLayers = 0;
    }

    public static class BehaviorLLMBackendConfig
    {
        public const string DefaultFileName = "behaviorllm_backend_config.json";

        public static string GetConfigPath(string fileName = null)
        {
            string resolvedName = string.IsNullOrWhiteSpace(fileName) ? DefaultFileName : fileName;
            return Path.Combine(Application.streamingAssetsPath, resolvedName);
        }

        public static BehaviorLLMBackendConfigData CreateDefault()
        {
            return new BehaviorLLMBackendConfigData();
        }

        public static BehaviorLLMBackendConfigData Load(string fileName = null)
        {
            string path = GetConfigPath(fileName);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                // JsonUtility ignores field initializers when fields are absent.
                // Overwrite a default instance to preserve future-compatible defaults.
                var cfg = CreateDefault();
                JsonUtility.FromJsonOverwrite(json, cfg);
                return cfg;
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Warn(() => $"[BehaviorLLMBackendConfig] Failed to read config '{path}': {e.Message}");
                return null;
            }
        }

        public static bool Save(BehaviorLLMBackendConfigData config, string fileName = null)
        {
            if (config == null) return false;

            string path = GetConfigPath(fileName);
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception e)
            {
                BehaviorLLMLog.Error(() => $"[BehaviorLLMBackendConfig] Failed to save config '{path}': {e.Message}");
                return false;
            }
        }
    }
}
