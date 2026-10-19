using NUnit.Framework;
using UnityEngine;

namespace Project.Samples.ArenaBrawler.Tests
{
    /// <summary>
    /// The fight's rules, which the human and the machine share. Each test plays a few frames of
    /// the plain simulation, with no scene, so a rule change that breaks the fight fails here.
    /// </summary>
    public class BrawlerArenaTests
    {
        private BrawlerRules rules;
        private BrawlerArena arena;
        private BrawlerFighter a, b;

        private const float Frame = 1f / 60f;

        [SetUp]
        public void SetUp()
        {
            rules = ScriptableObject.CreateInstance<BrawlerRules>();
            arena = new BrawlerArena(rules);
            a = arena.Fighters[0];
            b = arena.Fighters[1];
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rules);
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) arena.Step(Frame);
        }

        private void PlaceApart(float distance)
        {
            a.Position = new Vector2(-distance / 2f, 0f);
            b.Position = new Vector2(distance / 2f, 0f);
            arena.Step(Frame);
        }

        [Test]
        public void APressBetweenSteps_IsReportedByTheNextStep_AndOnlyThatOne()
        {
            // The machine's interrupt on a wind-up comes from this event; cleared by the step that
            // followed the press, it fired only for presses that had been buffered.
            arena.Press(0, FighterPress.Hook);

            arena.Step(Frame);
            Assert.IsTrue(arena.Events.Exists(e => e.Kind == ArenaEventKind.AttackStarted && e.Fighter == 0));

            arena.Step(Frame);
            Assert.IsFalse(arena.Events.Exists(e => e.Kind == ArenaEventKind.AttackStarted));
        }

        [Test]
        public void AJabInReach_Hits()
        {
            PlaceApart(1.2f);
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.windup + rules.jab.active + 0.02f);

            Assert.AreEqual(rules.health - rules.jab.damage, b.Health, 0.01f);
            Assert.AreEqual(FighterState.HitStun, b.State);
        }

        [Test]
        public void AJabOutOfReach_Misses()
        {
            PlaceApart(2.5f);
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.Total);

            Assert.AreEqual(rules.health, b.Health);
        }

        [Test]
        public void AGuard_StopsAJab_ButAHookBreaksIt()
        {
            PlaceApart(1.2f);
            b.BlockHeld = true;
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.Total + 0.05f);
            Assert.AreEqual(rules.health, b.Health, "a jab into a guard does nothing");

            PlaceApart(1.2f);
            b.BlockHeld = true;
            arena.Press(0, FighterPress.Hook);
            Run(rules.hook.windup + rules.hook.active);
            Assert.AreEqual(FighterState.GuardBroken, b.State);
            Assert.AreEqual(rules.health - rules.guardBreakDamage, b.Health, 0.01f);
        }

        [Test]
        public void ADashStartedDuringTheWindup_AvoidsTheHook()
        {
            PlaceApart(1.4f);
            arena.Press(0, FighterPress.Hook);
            Run(rules.hook.windup - 0.1f);
            arena.Press(1, FighterPress.Dash, Vector2.up);
            Run(rules.hook.active + rules.hook.recovery);

            Assert.AreEqual(rules.health, b.Health);
        }

        [Test]
        public void AMissedHook_LeavesTheAttackerOpenToPunish()
        {
            PlaceApart(1.5f);
            arena.Press(0, FighterPress.Hook);
            b.Move = Vector2.right;
            Run(rules.hook.windup + rules.hook.active + 0.05f);
            Assert.AreEqual(rules.health, b.Health, "stepped out of reach");
            Assert.AreEqual(AttackPhase.Recovery, arena.PhaseOf(a));

            b.Move = Vector2.zero;
            PlaceApart(1.2f);
            arena.Press(1, FighterPress.Jab);
            Run(rules.jab.windup + rules.jab.active);
            Assert.Less(a.Health, rules.health);
        }

        [Test]
        public void APressDuringRecovery_ComesOutWhenFree()
        {
            PlaceApart(3f);
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.Total - 0.1f);
            arena.Press(0, FighterPress.Jab);
            Run(0.15f);

            Assert.AreEqual(FighterState.Jab, a.State, "the second jab came out of the buffer");
        }

        [Test]
        public void AJabDuringTheOpponentsWindup_CountsDouble()
        {
            PlaceApart(1.2f);
            arena.Press(1, FighterPress.Hook);
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.windup + rules.jab.active);

            Assert.AreEqual(rules.health - rules.jab.damage * (1f + rules.counterBonus), b.Health, 0.01f);
            Assert.AreEqual(FighterState.HitStun, b.State, "the hook was interrupted");
        }

        [Test]
        public void RunningOutOfHealth_IsAKnockOut()
        {
            PlaceApart(1.2f);
            b.Health = 3f;
            arena.Press(0, FighterPress.Jab);
            Run(rules.jab.Total);

            Assert.AreEqual(FighterState.KO, b.State);
            Assert.AreEqual(0f, b.Health);
        }

        [Test]
        public void FightersStayInTheArena_AndApart()
        {
            a.Move = Vector2.left;
            b.Move = Vector2.left;
            Run(5f);

            Assert.LessOrEqual(a.Position.magnitude, rules.arenaRadius - rules.fighterRadius + 0.001f);
            Assert.GreaterOrEqual(Vector2.Distance(a.Position, b.Position), rules.fighterRadius * 2f - 0.001f);
        }

        [Test]
        public void RelativeDirections_FollowTheOpponent()
        {
            PlaceApart(2f);
            Assert.AreEqual(Vector2.right, arena.Relative(0, BrawlerIds.Toward));
            Assert.AreEqual(Vector2.left, arena.Relative(0, BrawlerIds.Away));
            Assert.AreEqual(Vector2.up, arena.Relative(0, BrawlerIds.CircleLeft));
            Assert.AreEqual(Vector2.down, arena.Relative(0, BrawlerIds.CircleRight));
        }

        [Test]
        public void Evade_RollsSquareToTheOpponent_TowardTheRoomierSide()
        {
            a.Position = new Vector2(-1f, 4f);
            b.Position = new Vector2(1f, 4f);
            Vector2 dir = arena.EvadeDirection(0);

            Assert.Less(dir.y, -0.8f, "toward the arena's middle, not its edge");
            Assert.Less(dir.x, 0f, "and a little back");
        }

        [Test]
        public void Observation_ReportsAnIncomingHook_AndWhetherItReaches()
        {
            PlaceApart(1.5f);
            arena.Press(0, FighterPress.Hook);
            arena.Step(0.1f);

            string text = FightObservationModule.Describe(arena, 1, 0, 0, 1, 60f, 0.15f);

            StringAssert.StartsWith("INCOMING: the opponent's slow swing lands in 0.35 s, 0.20 s after your next input lands. You are inside its reach.", text);
            StringAssert.Contains("Your hook reaches, your jab does not.", text);
            StringAssert.Contains("Opponent: winding up a slow swing", text);
        }
    }
}
