using BehaviorLLM.Core.Backend;
using NUnit.Framework;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// When the client asks the server to keep a character's constant prefix. The order matters:
    /// priming before the first decision put the request in another character's slot and wiped its
    /// saved point (5 of 8 in PrisonYard), so it follows the first decision instead.
    /// </summary>
    public class DecisionPrefixPrimingTests
    {
        [Test]
        public void APrefix_IsPrimedAfterItsFirstDecision_ThenEveryRefreshInterval()
        {
            DecisionPrefixPriming priming = new DecisionPrefixPriming();

            Assert.IsTrue(priming.AfterDecision("Guard_01\n"), "after the first decision");
            for (int i = 1; i < DecisionPrefixPriming.RefreshEvery; i++)
                Assert.IsFalse(priming.AfterDecision("Guard_01\n"), $"decision {i + 1}");
            Assert.IsTrue(priming.AfterDecision("Guard_01\n"), "refreshed in case the server lost it");
        }

        [Test]
        public void EachCharacter_IsTrackedOnItsOwn()
        {
            DecisionPrefixPriming priming = new DecisionPrefixPriming();

            Assert.IsTrue(priming.AfterDecision("Guard_01\n"));
            Assert.IsTrue(priming.AfterDecision("Guard_02\n"));
            Assert.IsFalse(priming.AfterDecision("Guard_01\n"));
        }

        [Test]
        public void NoPrefix_IsNeverPrimed()
        {
            DecisionPrefixPriming priming = new DecisionPrefixPriming();

            Assert.IsFalse(priming.AfterDecision(null));
            Assert.IsFalse(priming.AfterDecision(string.Empty));
        }

        [Test]
        public void Forget_PrimesEverythingAgain()
        {
            DecisionPrefixPriming priming = new DecisionPrefixPriming();
            priming.AfterDecision("Guard_01\n");

            priming.Forget();

            Assert.IsTrue(priming.AfterDecision("Guard_01\n"));
        }

        [Test]
        public void Disable_StopsPrimingForGood()
        {
            DecisionPrefixPriming priming = new DecisionPrefixPriming();

            priming.Disable();
            priming.Forget();

            Assert.IsTrue(priming.Disabled);
            Assert.IsFalse(priming.AfterDecision("Guard_01\n"));
        }
    }
}
