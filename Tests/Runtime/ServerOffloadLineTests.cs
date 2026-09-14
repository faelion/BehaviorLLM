using BehaviorLLM.Core.Backend;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The server turns llama-server's "offloaded N/M layers to GPU" line into a warning when N
    /// is short of M, because that is the one startup fact that separates a session on the GPU
    /// from one on the CPU, and the project's default log level would otherwise hide it.
    /// </summary>
    public class ServerOffloadLineTests
    {
        [TestCase("load_tensors: offloaded 41/41 layers to GPU", 41, 41)]
        [TestCase("0.00.211.190 I load_tensors: offloaded 12/41 layers to GPU", 12, 41)]
        [TestCase("load_tensors: offloaded 0/29 layers to GPU", 0, 29)]
        public void TheOffloadCountsAreRead(string line, int expectedOffloaded, int expectedTotal)
        {
            Assert.IsTrue(BehaviorLLMServer.TryReadOffload(line, out int offloaded, out int total));
            Assert.AreEqual(expectedOffloaded, offloaded);
            Assert.AreEqual(expectedTotal, total);
        }

        [TestCase("load_tensors:      Vulkan0 model buffer size =  1998.84 MiB")]
        [TestCase("srv  log_server_r: request: POST /v1/chat/completions")]
        [TestCase("")]
        [TestCase(null)]
        public void OtherLinesAreNotOffloadCounts(string line)
        {
            Assert.IsFalse(BehaviorLLMServer.TryReadOffload(line, out _, out _));
        }
    }
}
