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
            // The spare balls are moved off the middle of the center line, where a throw from one
            // mark to the other would knock into them.
            court.Balls[1].Position = new Vector2(0f, 4f);
            court.Balls[2].Position = new Vector2(0f, -4f);
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

        private void GiveBall(int player, int ball = 0)
        {
            CourtBall b = court.Balls[ball];
            b.State = BallState.Held;
            b.Holder = player;
            court.Players[player].Held.Add(ball);
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
        public void AMissedThrow_BouncesOffTheWalls_AndDiesWhenItsRangeRunsOut()
        {
            GiveBall(Human);
            court.Players[Machine].Position = new Vector2(4f, -3f);
            Assert.IsTrue(court.Throw(Human, new Vector2(4f, 3.5f), 0.5f));
            CourtBall b = court.Balls[0];

            bool bounced = false;
            for (int i = 0; i < 45; i++)
            {
                court.Step(1f / 60f);
                bounced |= court.Events.Exists(e => e.Kind == CourtEventKind.WallBounce);
            }
            Assert.IsTrue(bounced, "it reached the top wall");
            Assert.AreEqual(BallState.Flying, b.State, "and is still live after the bounce");

            Run(3f);
            Assert.AreEqual(BallState.Free, b.State);
            Assert.AreEqual(-1, b.Thrower);
            Assert.AreEqual(-1, court.PointWinner);
        }

        [Test]
        public void TheCharge_DecidesHowFarTheBallGoes()
        {
            GiveBall(Human);
            Assert.IsTrue(court.BeginCharge(Human));
            for (float t = 0f; t < rules.chargeTime * 0.5f - 0.001f; t += 1f / 60f) court.Step(1f / 60f);
            float charge = court.Charge(Human);
            Assert.AreEqual(0.5f, charge, 0.05f);

            Assert.IsTrue(court.ReleaseThrow(Human, court.Players[Machine].Position + Vector2.up * 3f));
            Assert.AreEqual(rules.RangeFor(charge), court.Balls[0].RangeLeft, 0.01f);
        }

        [Test]
        public void Charging_SlowsThePlayer_AndRulesOutDodgingAndCatching()
        {
            GiveBall(Human);
            court.BeginCharge(Human);
            Assert.IsFalse(court.Dodge(Human, Vector2.up));
            Assert.IsFalse(court.OpenCatch(Human));

            Vector2 start = court.Players[Human].Position;
            court.Players[Human].Move = Vector2.up;
            court.Step(0.1f);
            Assert.AreEqual(rules.moveSpeed * rules.chargeMoveFactor * 0.1f, court.Players[Human].Position.y - start.y, 0.001f);
        }

        [Test]
        public void AReleaseInTheNoThrowZone_IsRefused_AndKeepsTheBall()
        {
            GiveBall(Human);
            court.Players[Human].Position = new Vector2(-rules.throwingLine + 1f, 0f);
            Assert.IsTrue(court.BeginCharge(Human));
            court.Step(0.2f);

            Assert.IsFalse(court.ReleaseThrow(Human, court.Players[Machine].Position));
            court.Step(1f / 60f);
            Assert.IsTrue(court.Events.Exists(e => e.Kind == CourtEventKind.ThrowRefused && e.Player == Human));
            Assert.IsTrue(court.Players[Human].Holding);
            Assert.IsFalse(court.Players[Human].Charging);
        }

        [Test]
        public void TwoBallsInHand_AreTwoThrowsInARow()
        {
            GiveBall(Human, 0);
            GiveBall(Human, 1);
            Assert.IsTrue(court.Throw(Human, court.Players[Machine].Position));
            Assert.AreEqual(1, court.Players[Human].HeldBall, "the second ball is in the throwing hand at once");
            Assert.IsTrue(court.Throw(Human, court.Players[Machine].Position + Vector2.up * 2f));
            Assert.IsFalse(court.Players[Human].Holding);
        }

        [Test]
        public void Pickups_StopAtTheHandLimit()
        {
            CourtPlayer me = court.Players[Machine];
            for (int i = 0; i < court.Balls.Count; i++) court.Balls[i].Position = me.Position + new Vector2(-0.3f, 0.1f * i);
            court.Step(1f / 60f);

            Assert.AreEqual(rules.maxHeld, me.HeldCount);
        }

        [Test]
        public void CatchingWithBallsInHand_DropsThem_LockedForAWhile()
        {
            GiveBall(Machine, 0);
            GiveBall(Machine, 1);
            Assert.IsTrue(court.OpenCatch(Machine));

            Assert.IsFalse(court.Players[Machine].Holding);
            Assert.IsTrue(court.Balls[0].Locked && court.Balls[1].Locked);
            Run(rules.catchWindow + 0.1f);
            Assert.IsFalse(court.Players[Machine].Holding, "a locked ball is not picked up");

            Run(rules.pickupLock);
            court.Players[Machine].Position = court.Balls[0].Position;
            court.Step(1f / 60f);
            Assert.IsTrue(court.Players[Machine].Holding, "and is once the lock runs out");
        }

        [Test]
        public void ALiveBall_KnocksAFreeBallAway()
        {
            CourtBall free = court.Balls[1];
            free.Position = new Vector2(0f, 0f);
            court.Balls[2].Position = new Vector2(0f, -4f);
            GiveBall(Human, 0);
            court.Players[Machine].Position = new Vector2(6f, 3f);
            court.Throw(Human, new Vector2(4f, 0f));

            bool bounced = false;
            for (int i = 0; i < 40 && !bounced; i++)
            {
                court.Step(1f / 60f);
                bounced = court.Events.Exists(e => e.Kind == CourtEventKind.BallBounce);
            }
            Assert.IsTrue(bounced);
            Assert.Greater(free.Velocity.x, 1f, "pushed along the throw");
            Assert.AreEqual(BallState.Free, free.State, "and still harmless");
        }

        [Test]
        public void AThrowBetweenSteps_IsReportedByTheNextStep_AndOnlyThatOne()
        {
            // The match presses buttons between steps and reads the events after one; a press
            // cleared by the step that follows it never raised the machine's interrupt.
            GiveBall(Human);
            court.Players[Human].Position = new Vector2(-rules.throwingLine - 1f, 0f);
            court.Throw(Human, court.Players[Machine].Position);

            court.Step(1f / 60f);
            Assert.IsTrue(court.Events.Exists(e => e.Kind == CourtEventKind.Throw && e.Player == Human));

            court.Step(1f / 60f);
            Assert.IsFalse(court.Events.Exists(e => e.Kind == CourtEventKind.Throw));
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
            court.Players[0].Held.Add(0);
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
            court.Players[0].Held.Add(0);
            court.Throw(0, court.Players[1].Position + new Vector2(0f, 3f), rules.ChargeFor(11f));

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

        [Test]
        public void Observation_InFrontOfTheThrowingLine_SaysSoOnEveryDecision()
        {
            court.Reset();
            court.Players[1].Position = new Vector2(rules.throwingLine - 1f, 0f);

            string text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.DoesNotContain("THROWING LINE", text, "nothing to throw, nothing to say");
            StringAssert.DoesNotContain("back", text, "no pull toward Back while it should fetch a ball");

            court.Balls[0].State = BallState.Held;
            court.Balls[0].Holder = 1;
            court.Players[1].Held.Add(0);
            text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.StartsWith("THROWING LINE: you are holding 1 ball but can only throw from behind your throwing line, " +
                                    "1.0 m back from where you stand. Move back to reach it.", text);
            StringAssert.DoesNotContain("you can throw", text);
            StringAssert.DoesNotContain("Free ball", text, "free balls are left out while holding one");

            court.Players[1].Position = new Vector2(rules.throwingLine + 0.5f, 0f);
            text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.DoesNotContain("THROWING LINE", text);
            StringAssert.DoesNotContain("back", text);
            StringAssert.Contains("You hold 1 ball, behind your throwing line: you can throw.", text);
        }

        [Test]
        public void Observation_MentionsCatchAndDodge_OnlyWithABallComing()
        {
            court.Reset();
            string text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.DoesNotContain("Catch", text);
            StringAssert.DoesNotContain("Dodge", text);

            court.Balls[0].State = BallState.Held;
            court.Balls[0].Holder = 0;
            court.Players[0].Held.Add(0);
            court.Throw(0, court.Players[1].Position);
            text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.Contains("Catch ready. Dodge ready.", text);
        }

        [Test]
        public void Observation_GivesTheOpponentsSpeed()
        {
            court.Reset();
            court.Players[0].Velocity = new Vector2(0f, 4.5f);

            string text = CourtObservationModule.Describe(court, 1, 0, 0, 7);
            StringAssert.Contains("running up at 4.5 m/s, so throw ahead of them, ", text);
        }

        [Test]
        public void BankShots_AimAtTheOpponentsReflection()
        {
            court.Reset();
            Vector2 aim = DodgeballMachinePad.AimPoint(court, 1, DodgeballIds.BankTopWall, out float length);
            float wall = rules.HalfWidth - rules.ballRadius;

            Assert.AreEqual(2f * wall - court.Players[0].Position.y, aim.y, 0.001f);
            Assert.AreEqual(Vector2.Distance(court.Players[1].Position, aim), length, 0.001f);
        }
    }
}
