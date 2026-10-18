using NUnit.Framework;
using UnityEngine;

namespace Project.Samples.Dodgeball.Tests
{
    /// <summary>
    /// The court's rules, which the human and the machine share. Each test plays a few frames of
    /// the plain simulation, with no scene, so a rule change that breaks the game fails here.
    /// </summary>
    public class DodgeballCourtTests
    {
        private DodgeballRules rules;
        private DodgeballCourt court;

        private const int Human = 0, Machine = 1;

        [SetUp]
        public void SetUp()
        {
            rules = ScriptableObject.CreateInstance<DodgeballRules>();
            court = new DodgeballCourt(rules);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rules);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) court.Step(1f / 60f);
        }

        private void GiveBall(int player)
        {
            CourtBall b = court.Balls[0];
            b.State = BallState.Held;
            b.Holder = player;
            court.Players[player].HeldBall = 0;
        }

        [Test]
        public void Players_StayOnTheirOwnHalf()
        {
            court.Players[Human].Move = Vector2.right;
            court.Players[Machine].Move = Vector2.left;
            Run(5f);

            Assert.LessOrEqual(court.Players[Human].Position.x, -rules.playerRadius + 0.001f);
            Assert.GreaterOrEqual(court.Players[Machine].Position.x, rules.playerRadius - 0.001f);
        }

        [Test]
        public void WalkingOntoAFreeBall_PicksItUp()
        {
            CourtBall b = court.Balls[0];
            b.Position = court.Players[Machine].Position + new Vector2(-1f, 0f);
            court.Players[Machine].Move = Vector2.left;
            Run(0.5f);

            Assert.IsTrue(court.Players[Machine].Holding);
            Assert.AreEqual(BallState.Held, b.State);
        }

        [Test]
        public void AThrowThatReachesTheOpponent_ScoresForTheThrower()
        {
            GiveBall(Human);
            Assert.IsTrue(court.Throw(Human, court.Players[Machine].Position));
            int winner = -1;
            for (int i = 0; i < 120 && winner < 0; i++) { court.Step(1f / 60f); winner = court.PointWinner; }

            Assert.AreEqual(Human, winner);
        }

        [Test]
        public void AnOpenCatch_TakesTheBallAndScoresForTheCatcher()
        {
            GiveBall(Human);
            court.Throw(Human, court.Players[Machine].Position);
            int winner = -1;
            for (int i = 0; i < 120 && winner < 0; i++)
            {
                // Opened when the ball is a tenth of a second away, inside the window.
                float gap = court.Players[Machine].Position.x - court.Balls[0].Position.x;
                if (!court.Players[Machine].Catching && gap < rules.throwSpeed * 0.1f + rules.playerRadius)
                    Assert.IsTrue(court.OpenCatch(Machine));
                court.Step(1f / 60f);
                winner = court.PointWinner;
            }

            Assert.AreEqual(Machine, winner);
            Assert.IsTrue(court.Players[Machine].Holding);
        }

        [Test]
        public void ACatchOpenedTooEarly_HasClosedWhenTheBallArrives()
        {
            GiveBall(Human);
            court.OpenCatch(Machine);
            Run(rules.catchWindow + 0.05f);
            court.Throw(Human, court.Players[Machine].Position);
            int winner = -1;
            for (int i = 0; i < 120 && winner < 0; i++) { court.Step(1f / 60f); winner = court.PointWinner; }

            Assert.AreEqual(Human, winner, "a closed catch does not save the player");
            Assert.IsFalse(court.OpenCatch(Machine), "and cannot be reopened yet");
        }

        [Test]
        public void AMissedThrow_StopsAtTheWallOnTheOtherHalf()
        {
            GiveBall(Human);
            court.Players[Machine].Position = new Vector2(4f, 3f);
            court.Throw(Human, new Vector2(4f, -3f));
            Run(2f);

            CourtBall b = court.Balls[0];
            Assert.AreEqual(BallState.Free, b.State);
            Assert.Greater(b.Position.x, 0f, "the machine gets the ball");
            Assert.AreEqual(-1, court.PointWinner);
        }

        [Test]
        public void NoThrow_InFrontOfTheThrowingLine()
        {
            GiveBall(Machine);
            court.Players[Machine].Position = new Vector2(rules.throwingLine - 0.5f, 0f);
            Assert.IsFalse(court.Throw(Machine, court.Players[Human].Position));

            court.Players[Machine].Position = new Vector2(rules.throwingLine + 0.1f, 0f);
            Assert.IsTrue(court.Throw(Machine, court.Players[Human].Position));
        }

        [Test]
        public void Dodge_MovesFarQuickly_AndThenRecovers()
        {
            Vector2 start = court.Players[Machine].Position;
            Assert.IsTrue(court.Dodge(Machine, Vector2.up));
            Run(rules.dodgeDuration + 0.02f);

            Assert.AreEqual(rules.dodgeDistance, court.Players[Machine].Position.y - start.y, 0.05f);
            Assert.IsFalse(court.Dodge(Machine, Vector2.down), "still recovering");
            Run(rules.dodgeCooldown);
            Assert.IsTrue(court.Dodge(Machine, Vector2.down));
        }

        [Test]
        public void AThrowAtOnesOwnWall_IsBentForward()
        {
            GiveBall(Machine);
            court.Throw(Machine, court.Players[Machine].Position + new Vector2(5f, 0f));

            Assert.Less(court.Balls[0].Velocity.x, 0f, "toward the human's half");
        }
    }

    public class DodgeballMachineTests
    {
        private DodgeballRules rules;
        private DodgeballCourt court;

        [SetUp]
        public void SetUp()
        {
            rules = ScriptableObject.CreateInstance<DodgeballRules>();
            court = new DodgeballCourt(rules);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rules);
        }

        [Test]
        public void Directions_AreInTheMachinesOwnFrame()
        {
            // The machine plays the right half, so its forward is toward smaller x.
            Assert.AreEqual(new Vector2(-1f, 0f), DodgeballMachinePad.DirectionVector(DodgeballIds.Forward, -1f));
            Assert.AreEqual(new Vector2(1f, 0f), DodgeballMachinePad.DirectionVector(DodgeballIds.Back, -1f));
            Assert.AreEqual(new Vector2(0f, 1f), DodgeballMachinePad.DirectionVector(DodgeballIds.Up, -1f));
            Vector2 fu = DodgeballMachinePad.DirectionVector(DodgeballIds.ForwardUp, -1f);
            Assert.Less(fu.x, 0f);
            Assert.Greater(fu.y, 0f);
            Assert.AreEqual(1f, fu.magnitude, 0.001f);
        }

        [Test]
        public void Observation_SaysWhenAndWhetherAThrowWillHit()
        {
            CourtBall b = court.Balls[0];
            b.State = BallState.Held;
            b.Holder = 0;
            court.Players[0].HeldBall = 0;
            court.Throw(0, court.Players[1].Position);

            string text = CourtObservationModule.Describe(court, 1, 0, 0, rules.pointsToWin);

            StringAssert.StartsWith("INCOMING: a ball thrown at you arrives in ", text);
            StringAssert.Contains("will hit you", text);
        }

        [Test]
        public void Observation_SaysAThrowWillMiss_AndWhichSide()
        {
            CourtBall b = court.Balls[0];
            b.State = BallState.Held;
            b.Holder = 0;
            court.Players[0].HeldBall = 0;
            court.Throw(0, court.Players[1].Position + new Vector2(0f, 3f));

            string text = CourtObservationModule.Describe(court, 1, 0, 0, rules.pointsToWin);

            StringAssert.Contains("will miss, passing", text);
            StringAssert.Contains("above you", text);
        }

        [Test]
        public void Observation_DescribesFreeBallsInTheMachinesFrame()
        {
            court.Reset();
            court.Balls[0].Position = court.Players[1].Position + new Vector2(-2f, 0f);

            string text = CourtObservationModule.Describe(court, 1, 2, 3, 7);

            StringAssert.Contains("Free ball on your side 2.0 m forward of you.", text);
            StringAssert.Contains("Score: you 2, opponent 3. First to 7.", text);
        }
    }
}
