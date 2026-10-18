using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>Which half of the court a player owns. Left is the human's by default.</summary>
    public enum CourtSide { Left, Right }

    public enum BallState { Free, Held, Flying }

    public enum CourtEventKind { Pickup, Throw, Hit, Catch, CatchOpened, Dodge }

    /// <summary>Something that happened during one <see cref="DodgeballCourt.Step"/>.</summary>
    public struct CourtEvent
    {
        public CourtEventKind Kind;
        public int Player;
        public int Ball;

        public CourtEvent(CourtEventKind kind, int player, int ball)
        {
            Kind = kind;
            Player = player;
            Ball = ball;
        }
    }

    public sealed class CourtPlayer
    {
        public readonly int Index;
        public readonly CourtSide Side;

        /// <summary>Center, in court coordinates: x along the court (0 is the center line), y across it.</summary>
        public Vector2 Position;

        /// <summary>The direction the player is pushing, at most length 1. Kept until changed.</summary>
        public Vector2 Move;

        /// <summary>How far the player actually moved per second during the last step.</summary>
        public Vector2 Velocity;

        public int HeldBall = -1;
        public float CatchTimeLeft;
        public float CatchCooldown;
        public float DodgeTimeLeft;
        public float DodgeCooldown;
        public Vector2 DodgeDirection;

        public CourtPlayer(int index, CourtSide side)
        {
            Index = index;
            Side = side;
        }

        public bool Holding => HeldBall >= 0;

        /// <summary>Distance from the center line.</summary>
        public float Depth => Mathf.Abs(Position.x);
        public bool Catching => CatchTimeLeft > 0f;
        public bool Dodging => DodgeTimeLeft > 0f;
        public bool CanCatch => !Holding && CatchCooldown <= 0f;
        public bool CanDodge => DodgeCooldown <= 0f;

        /// <summary>+1 or -1: the x direction from this player's half toward the center line.</summary>
        public float Forward => Side == CourtSide.Left ? 1f : -1f;
    }

    public sealed class CourtBall
    {
        public readonly int Index;
        public Vector2 Position;
        public Vector2 Velocity;
        public BallState State;
        public int Holder = -1;
        public int Thrower = -1;

        public CourtBall(int index)
        {
            Index = index;
        }
    }

    /// <summary>
    /// The whole game of one-on-one dodgeball as plain data and arithmetic, with no Unity objects
    /// in it. The match drives it once a frame and draws what it says; the human's keyboard and the
    /// machine's decisions both reach it through the same three calls (<see cref="Throw"/>,
    /// <see cref="OpenCatch"/>, <see cref="Dodge"/>) and the same <see cref="CourtPlayer.Move"/>, so
    /// neither side can do anything the other cannot.
    /// </summary>
    public sealed class DodgeballCourt
    {
        public readonly DodgeballRules Rules;
        public readonly CourtPlayer[] Players;
        public readonly List<CourtBall> Balls = new List<CourtBall>();

        /// <summary>
        /// What happened during the last step, in order, starting with the throws, catches and
        /// dodges pressed since the step before it.
        /// </summary>
        public readonly List<CourtEvent> Events = new List<CourtEvent>();

        // How many of Events the last step reported. A press between steps adds its event after
        // them, and the next step drops only these, so a throw is reported by the step that
        // follows it instead of being cleared before anyone reads it.
        private int reported;

        /// <summary>The player who won a point during the last step, or -1.</summary>
        public int PointWinner { get; private set; } = -1;

        public DodgeballCourt(DodgeballRules rules)
        {
            Rules = rules;
            Players = new[] { new CourtPlayer(0, CourtSide.Left), new CourtPlayer(1, CourtSide.Right) };
            for (int i = 0; i < rules.ballCount; i++) Balls.Add(new CourtBall(i));
            Reset();
        }

        public CourtPlayer Opponent(int player) => Players[1 - player];

        /// <summary>Puts both players back at their marks and the balls on the center line.</summary>
        public void Reset()
        {
            for (int i = 0; i < Players.Length; i++)
            {
                CourtPlayer p = Players[i];
                p.Position = new Vector2(-p.Forward * Rules.HalfLength * 0.6f, 0f);
                p.Move = Vector2.zero;
                p.Velocity = Vector2.zero;
                p.HeldBall = -1;
                p.CatchTimeLeft = p.CatchCooldown = p.DodgeTimeLeft = p.DodgeCooldown = 0f;
            }

            for (int i = 0; i < Balls.Count; i++)
            {
                CourtBall b = Balls[i];
                float t = Balls.Count == 1 ? 0.5f : i / (float)(Balls.Count - 1);
                b.Position = new Vector2(0f, Mathf.Lerp(-Rules.HalfWidth * 0.6f, Rules.HalfWidth * 0.6f, t));
                b.Velocity = Vector2.zero;
                b.State = BallState.Free;
                b.Holder = b.Thrower = -1;
            }

            Events.Clear();
            reported = 0;
            PointWinner = -1;
        }

        // ---------------------------------------------------------------- the three buttons

        /// <summary>True when <paramref name="player"/> holds a ball and stands behind their throwing line.</summary>
        public bool CanThrow(int player) => Players[player].Holding && Players[player].Depth >= Rules.throwingLine;

        /// <summary>
        /// Throws the held ball toward <paramref name="target"/>. A throw always travels toward the
        /// other half: a target behind the thrower is bent forward, so a throw can never be wasted
        /// on one's own wall. False when not holding a ball or in front of the throwing line.
        /// </summary>
        public bool Throw(int player, Vector2 target)
        {
            CourtPlayer p = Players[player];
            if (!CanThrow(player)) return false;

            Vector2 dir = target - p.Position;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(p.Forward, 0f);
            dir.Normalize();
            if (dir.x * p.Forward < 0.2f)
            {
                dir.x = 0.2f * p.Forward;
                dir.y = Mathf.Sign(dir.y == 0f ? 1f : dir.y) * Mathf.Sqrt(1f - dir.x * dir.x);
            }

            CourtBall ball = Balls[p.HeldBall];
            p.HeldBall = -1;
            ball.State = BallState.Flying;
            ball.Holder = -1;
            ball.Thrower = player;
            ball.Position = p.Position + dir * (Rules.playerRadius + Rules.ballRadius + 0.05f);
            ball.Velocity = dir * Rules.throwSpeed;
            Events.Add(new CourtEvent(CourtEventKind.Throw, player, ball.Index));
            return true;
        }

        /// <summary>Opens a catch window. False while holding a ball or cooling down.</summary>
        public bool OpenCatch(int player)
        {
            CourtPlayer p = Players[player];
            if (!p.CanCatch) return false;
            p.CatchTimeLeft = Rules.catchWindow;
            p.CatchCooldown = Rules.catchWindow + Rules.catchCooldown;
            Events.Add(new CourtEvent(CourtEventKind.CatchOpened, player, -1));
            return true;
        }

        /// <summary>A quick burst in <paramref name="direction"/>. False while cooling down.</summary>
        public bool Dodge(int player, Vector2 direction)
        {
            CourtPlayer p = Players[player];
            if (!p.CanDodge || direction.sqrMagnitude < 0.0001f) return false;
            p.DodgeDirection = direction.normalized;
            p.DodgeTimeLeft = Rules.dodgeDuration;
            p.DodgeCooldown = Rules.dodgeDuration + Rules.dodgeCooldown;
            Events.Add(new CourtEvent(CourtEventKind.Dodge, player, -1));
            return true;
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Advances the court by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            Events.RemoveRange(0, reported);
            PointWinner = -1;
            if (dt > 0f)
            {
                for (int i = 0; i < Players.Length; i++) StepPlayer(Players[i], dt);
                for (int i = 0; i < Balls.Count && PointWinner < 0; i++) StepBall(Balls[i], dt);
                for (int i = 0; i < Players.Length; i++) TryPickUp(Players[i]);
            }
            reported = Events.Count;
        }

        private void StepPlayer(CourtPlayer p, float dt)
        {
            Vector2 before = p.Position;
            p.CatchTimeLeft = Mathf.Max(0f, p.CatchTimeLeft - dt);
            p.CatchCooldown = Mathf.Max(0f, p.CatchCooldown - dt);
            p.DodgeCooldown = Mathf.Max(0f, p.DodgeCooldown - dt);

            if (p.Dodging)
            {
                float t = Mathf.Min(dt, p.DodgeTimeLeft);
                p.Position += p.DodgeDirection * (Rules.dodgeDistance / Rules.dodgeDuration) * t;
                p.DodgeTimeLeft -= t;
            }
            else
            {
                Vector2 move = Vector2.ClampMagnitude(p.Move, 1f);
                float speed = Rules.moveSpeed * (p.Catching ? Rules.catchMoveFactor : 1f);
                p.Position += move * speed * dt;
            }

            p.Position = ClampToHalf(p, p.Position);
            p.Velocity = (p.Position - before) / dt;
        }

        /// <summary>Keeps a player inside the walls and on their own side of the center line.</summary>
        public Vector2 ClampToHalf(CourtPlayer p, Vector2 position)
        {
            float r = Rules.playerRadius;
            float minX = p.Side == CourtSide.Left ? -Rules.HalfLength + r : r;
            float maxX = p.Side == CourtSide.Left ? -r : Rules.HalfLength - r;
            return new Vector2(Mathf.Clamp(position.x, minX, maxX),
                               Mathf.Clamp(position.y, -Rules.HalfWidth + r, Rules.HalfWidth - r));
        }

        private void StepBall(CourtBall ball, float dt)
        {
            switch (ball.State)
            {
                case BallState.Held:
                    CourtPlayer holder = Players[ball.Holder];
                    ball.Position = holder.Position + new Vector2(holder.Forward * 0.35f, -0.3f);
                    return;

                case BallState.Flying:
                    Vector2 from = ball.Position;
                    Vector2 to = from + ball.Velocity * dt;
                    CourtPlayer target = Opponent(ball.Thrower);
                    float reach = Rules.playerRadius + Rules.ballRadius;
                    if (SegmentPointDistance(from, to, target.Position) <= reach)
                    {
                        Land(ball, target);
                        return;
                    }

                    ball.Position = to;
                    float bx = Rules.HalfLength - Rules.ballRadius, by = Rules.HalfWidth - Rules.ballRadius;
                    if (Mathf.Abs(to.x) > bx || Mathf.Abs(to.y) > by)
                    {
                        // A ball that reaches a wall is dead where it stops, and belongs to whoever's
                        // half it stopped in: a missed throw hands the ball to the other side.
                        ball.Position = new Vector2(Mathf.Clamp(to.x, -bx, bx), Mathf.Clamp(to.y, -by, by));
                        ball.Velocity = Vector2.zero;
                        ball.State = BallState.Free;
                        ball.Thrower = -1;
                    }
                    return;
            }
        }

        private void Land(CourtBall ball, CourtPlayer target)
        {
            if (target.Catching)
            {
                ball.State = BallState.Held;
                ball.Holder = target.Index;
                ball.Velocity = Vector2.zero;
                target.HeldBall = ball.Index;
                target.CatchTimeLeft = 0f;
                Events.Add(new CourtEvent(CourtEventKind.Catch, target.Index, ball.Index));
                PointWinner = target.Index;
            }
            else
            {
                Events.Add(new CourtEvent(CourtEventKind.Hit, ball.Thrower, ball.Index));
                PointWinner = ball.Thrower;
                ball.State = BallState.Free;
                ball.Velocity = Vector2.zero;
                ball.Position = target.Position;
            }
            ball.Thrower = -1;
        }

        private void TryPickUp(CourtPlayer p)
        {
            if (p.Holding || p.Catching) return;
            float reach = Rules.playerRadius + Rules.ballRadius + 0.1f;
            for (int i = 0; i < Balls.Count; i++)
            {
                CourtBall b = Balls[i];
                if (b.State != BallState.Free) continue;
                if ((b.Position - p.Position).sqrMagnitude > reach * reach) continue;
                b.State = BallState.Held;
                b.Holder = p.Index;
                p.HeldBall = i;
                Events.Add(new CourtEvent(CourtEventKind.Pickup, p.Index, i));
                return;
            }
        }

        // ---------------------------------------------------------------- geometry

        public static float SegmentPointDistance(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(a + ab * t, p);
        }

        /// <summary>
        /// Where a flying ball passes a player if neither moves: the seconds until it is closest,
        /// and how far from the player's center it is then. Negative time when it is moving away.
        /// </summary>
        public static void ClosestApproach(CourtBall ball, Vector2 player, out float seconds, out float distance, out float sideways)
        {
            Vector2 rel = ball.Position - player;
            float v2 = ball.Velocity.sqrMagnitude;
            seconds = v2 > 0f ? -Vector2.Dot(rel, ball.Velocity) / v2 : -1f;
            Vector2 at = rel + ball.Velocity * Mathf.Max(0f, seconds);
            distance = at.magnitude;
            sideways = at.y;
        }
    }
}
