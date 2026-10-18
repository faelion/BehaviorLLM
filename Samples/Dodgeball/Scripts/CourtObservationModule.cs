using System.Globalization;
using System.Text;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// What the machine player knows about the court, written the way it can act on it: every
    /// position in its own terms (forward is toward the center line, up is toward the top wall),
    /// every incoming ball with when it arrives and whether it will hit, and every free ball with
    /// how far it is. Nothing is decided for it: the text says a ball will hit, not what to do.
    ///
    /// It raises an interrupt the moment the opponent throws, which cancels whatever decision is
    /// in flight and asks again with the ball in the air. That is the reaction this sample exists
    /// to measure.
    /// </summary>
    [AddComponentMenu("Dodgeball/Court Observation")]
    public class CourtObservationModule : MonoBehaviour, IObservationModule
    {
        [Tooltip("The match whose court this player sees.")]
        public DodgeballMatch match;

        [Tooltip("Which player this is: 0 plays the left half, 1 the right.")]
        public int playerIndex = 1;

        public string TopicName => "Court";

        public string GetObservation()
        {
            if (match == null || match.Court == null) return "The match has not started.";
            return Describe(match.Court, playerIndex, match.Score[playerIndex], match.Score[1 - playerIndex],
                            match.Rules.pointsToWin, match.InputDelaySeconds(playerIndex));
        }

        public bool HasInterrupt() => match != null && match.ConsumeAlert(playerIndex);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// The observation for player <paramref name="self"/>, as plain text. <paramref name="inputDelay"/>
        /// is how long, in seconds, an input chosen now takes to land: the machine's own decision
        /// time. An incoming ball is described both ways, because the arrival time on its own invites
        /// a catch that lands after the ball does.
        /// </summary>
        public static string Describe(DodgeballCourt court, int self, int myScore, int theirScore, int pointsToWin,
                                      float inputDelay = 0f)
        {
            DodgeballRules rules = court.Rules;
            CourtPlayer me = court.Players[self];
            CourtPlayer them = court.Opponent(self);
            StringBuilder sb = new StringBuilder(512);

            // Incoming balls first: they are the one thing that cannot wait.
            for (int i = 0; i < court.Balls.Count; i++)
            {
                CourtBall b = court.Balls[i];
                if (b.State != BallState.Flying || b.Thrower == self) continue;
                DodgeballCourt.ClosestApproach(b, me.Position, out float t, out float miss, out float side);
                if (t <= 0f) continue;
                float reach = rules.playerRadius + rules.ballRadius;
                sb.Append("INCOMING: a ball thrown at you arrives in ").Append(Seconds(t));
                if (inputDelay > 0f)
                {
                    sb.Append(t > inputDelay
                        ? ", " + Seconds(t - inputDelay) + " after your next input lands,"
                        : ", before your next input can land,");
                }
                if (miss <= reach) sb.Append(" and will hit you.");
                else sb.Append(" and will miss, passing ").Append(Metres(miss - rules.playerRadius))
                       .Append(side > 0f ? " above you." : " below you.");
                sb.Append('\n');
            }

            // Stated on its own line, first after any incoming ball, and with the move it needs.
            // Folded into the line about the player, it was read past: replayed on states where
            // the machine held a ball too close to throw, LiquidAI d1 moved back in 5 of 40; with
            // this line, in 34 of 40. The words "move back" appear only while they apply, so they
            // do not pull the model toward Back the rest of the time (see the guide's comment).
            if (me.Holding && !court.CanThrow(self))
            {
                sb.Append("THROWING LINE: you are holding a ball but can only throw from behind your throwing line, ")
                  .Append(Metres(rules.throwingLine - me.Depth)).Append(" back from where you stand. Move back to reach it.\n");
            }

            sb.Append("You: ").Append(Metres(me.Depth)).Append(" from the center line, ")
              .Append(WallDistance(rules, me.Position)).Append(". ");
            if (me.Holding)
            {
                sb.Append(court.CanThrow(self)
                    ? "You are holding a ball, behind your throwing line: you can throw. "
                    : "You are holding a ball. ");
            }
            else sb.Append("You are empty-handed. ");
            if (me.Catching) sb.Append("Your catch is open for ").Append(Seconds(me.CatchTimeLeft)).Append(". ");
            else if (!me.Holding) sb.Append(me.CanCatch ? "Catch ready. " : "Catch recovering. ");
            sb.Append(me.CanDodge ? "Dodge ready." : "Dodge recovering.").Append('\n');

            Vector2 toThem = them.Position - me.Position;
            sb.Append("Opponent: ").Append(Metres(toThem.magnitude)).Append(' ')
              .Append(Direction(toThem, me.Forward)).Append(" of you, ")
              .Append(them.Holding ? "holding a ball" : "empty-handed");
            string moving = Motion(them.Velocity, me.Forward);
            if (them.Dodging) sb.Append(", dodging");
            else if (them.Catching) sb.Append(", with a catch open");
            else if (moving != null) sb.Append(", moving ").Append(moving);
            sb.Append(".\n");

            for (int i = 0; i < court.Balls.Count; i++)
            {
                CourtBall b = court.Balls[i];
                if (b.State == BallState.Held || b.State == BallState.Flying) continue;
                bool mine = Mathf.Sign(b.Position.x) == -me.Forward && Mathf.Abs(b.Position.x) > 0.5f;
                bool middle = Mathf.Abs(b.Position.x) <= 0.5f;
                Vector2 to = b.Position - me.Position;
                if (mine || middle)
                {
                    sb.Append("Free ball ").Append(middle ? "on the center line " : "on your side ")
                      .Append(Metres(to.magnitude)).Append(' ').Append(Direction(to, me.Forward)).Append(" of you.\n");
                }
                else sb.Append("Free ball on the opponent's side.\n");
            }
            for (int i = 0; i < court.Balls.Count; i++)
            {
                CourtBall b = court.Balls[i];
                if (b.State == BallState.Flying && b.Thrower == self) sb.Append("Your throw is in the air.\n");
            }

            sb.Append("Score: you ").Append(myScore).Append(", opponent ").Append(theirScore)
              .Append(". First to ").Append(pointsToWin).Append('.');
            return sb.ToString();
        }

        /// <summary>"forward-up", "back", "down" and so on, in the frame of a player whose forward is <paramref name="forward"/> on x.</summary>
        public static string Direction(Vector2 delta, float forward)
        {
            float d = delta.magnitude;
            if (d < 0.05f) return "right next to";
            float f = delta.x * forward, u = delta.y;
            string along = Mathf.Abs(f) >= 0.35f * d ? (f > 0f ? "forward" : "back") : null;
            string across = Mathf.Abs(u) >= 0.35f * d ? (u > 0f ? "up" : "down") : null;
            if (along != null && across != null) return along + "-" + across;
            return along ?? across ?? "level with";
        }

        private static string Motion(Vector2 velocity, float forward)
        {
            if (velocity.magnitude < 0.5f) return null;
            return Direction(velocity, forward);
        }

        private static string WallDistance(DodgeballRules rules, Vector2 p)
        {
            float toTop = rules.HalfWidth - p.y, toBottom = p.y + rules.HalfWidth;
            return toTop < toBottom
                ? Metres(toTop - rules.playerRadius) + " from the top wall"
                : Metres(toBottom - rules.playerRadius) + " from the bottom wall";
        }

        private static string Metres(float m) => Mathf.Max(0f, m).ToString("0.0", Inv) + " m";
        private static string Seconds(float s) => Mathf.Max(0f, s).ToString("0.00", Inv) + " s";
    }
}
