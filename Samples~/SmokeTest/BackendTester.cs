using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace BehaviorLLM.Core.Tests
{
    /// <summary>
    /// Minimal check that the configured backend is reachable: start the server (context menu),
    /// then send one unconstrained prompt and log the answer.
    /// </summary>
    public class BackendTester : MonoBehaviour
    {
        public BehaviorLLMClient client;
        public BehaviorLLMServer server;
        [TextArea] public string testPrompt = "Reply with one short sentence: who are you?";

        [ContextMenu("Start Server")]
        public void StartServer()
        {
            if (server == null) server = FindFirstObjectByType<BehaviorLLMServer>(FindObjectsInactive.Include);
            if (server == null)
            {
                Debug.LogWarning("[BackendTester] No BehaviorLLMServer in the scene; assign one or add the component.");
                return;
            }
            server.StartServer();
        }

        [ContextMenu("Test Completion")]
        public async void TestCompletion()
        {
            if (client == null) client = GetComponent<BehaviorLLMClient>();
            if (client == null)
            {
                Debug.LogWarning("[BackendTester] No BehaviorLLMClient on this GameObject.");
                return;
            }

            Debug.Log($"[BackendTester] Sending prompt to {client.Endpoint}: {testPrompt}");
            LLMResponse result = await client.CompleteAsync(new LLMRequest
            {
                SystemPrompt = "You are a test assistant.",
                UserPrompt = testPrompt,
                MaxTokens = 48
            });

            if (result.Succeeded)
                Debug.Log($"[BackendTester] Result ({result.LatencyMs:F0} ms, {result.CompletionTokens} tokens): {result.Text}");
            else
                Debug.LogError($"[BackendTester] Failed: {result.Error}");
        }
    }
}
