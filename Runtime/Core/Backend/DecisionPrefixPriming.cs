using System;
using System.Collections.Generic;

namespace BehaviorLLM.Core.Backend
{
    /// <summary>
    /// When to ask the server to keep a character's constant prefix (see
    /// <see cref="SystemOneProtocol.WritePrimingRequest"/>). Pure bookkeeping, so it can be tested
    /// without a server.
    ///
    /// A prefix is sent after the character's first successful decision, not before it. The server
    /// gives each request the idle slot whose cached text shares the longest opening with it, and a
    /// priming request is short: the generic opening every character shares ("You are ...") is a
    /// large enough share of it to win a slot that holds another character's decisions, and the
    /// priming then wipes that character's saved point. Measured in PrisonYard, priming before the
    /// first decision lost 5 of the 8 saved points, and eight characters at once gained 11% throughput
    /// instead of 27%. After a decision, the character's own slot holds a prompt that starts with the
    /// whole prefix, so the priming lands there.
    ///
    /// The prefix is sent again every <see cref="RefreshEvery"/> decisions. Nothing in a decision's
    /// answer says whether the server still had the saved point, and it can lose it (a server
    /// restart, more characters than slots); resending one that is still there costs a few tokens.
    /// </summary>
    internal sealed class DecisionPrefixPriming
    {
        /// <summary>Decisions between two primings of the same prefix.</summary>
        internal const int RefreshEvery = 20;

        // More distinct prefixes than any scene has characters means they are being rebuilt (a
        // persona edited at runtime); starting over keeps this bounded.
        private const int MaxTracked = 256;

        private readonly Dictionary<string, int> decisionsSincePriming = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>True once the server refused a priming request; nothing more is sent.</summary>
        public bool Disabled { get; private set; }

        /// <summary>
        /// Called after each successful decision that carried <paramref name="prefix"/>. True when the
        /// prefix should be sent now, which also counts as having sent it.
        /// </summary>
        public bool AfterDecision(string prefix)
        {
            if (Disabled || string.IsNullOrEmpty(prefix)) return false;

            if (decisionsSincePriming.TryGetValue(prefix, out int count) && count < RefreshEvery)
            {
                decisionsSincePriming[prefix] = count + 1;
                return false;
            }

            if (decisionsSincePriming.Count >= MaxTracked) decisionsSincePriming.Clear();
            decisionsSincePriming[prefix] = 1;
            return true;
        }

        /// <summary>Everything is sent again on the next decision, as after a server restart.</summary>
        public void Forget()
        {
            decisionsSincePriming.Clear();
        }

        /// <summary>Stops priming for good, for a server that cannot take the request.</summary>
        public void Disable()
        {
            Disabled = true;
            decisionsSincePriming.Clear();
        }
    }
}
