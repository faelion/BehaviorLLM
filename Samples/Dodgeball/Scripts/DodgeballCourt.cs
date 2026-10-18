using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>Which half of the court a player owns. Left is the human's by default.</summary>
    public enum CourtSide { Left, Right }

    /// <summary>
    /// Free is on the floor, possibly still rolling, and harmless; Held is in a hand; Flying is a
    /// live throw that hits the thrower's opponent until its range runs out.
    /// </summary>
    public enum BallState { Free, Held, Flying }

    public enum CourtEventKind { Pickup, ChargeStarted, Throw, ThrowRefused, Hit, Catch, CatchOpened, Drop, Dodge, WallBounce, BallBounce, BallDied }

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

        /// <summary>The balls in hand, throwing hand first.</summary>
        public readonly List<int> Held = new List<int>();

        public float CatchTimeLeft;
        public float CatchCooldown;
        public float DodgeTimeLeft;
        public float DodgeCooldown;
        public Vector2 DodgeDirection;

        /// <summary>Seconds the throw button has been held, or -1 when not charging.</summary>
        public float ChargeTime = -1f;

        public CourtPlayer(int index, CourtSide side)
        {
            Index = index;
            Side = side;
        }

        public bool Holding => Held.Count > 0;
        public int HeldCount => Held.Count;

        /// <summary>The ball in the throwing hand, or -1.</summary>
        public int HeldBall => Held.Count > 0 ? Held[0] : -1;

        /// <summary>Distance from the center line.</summary>
        public float Depth => Mathf.Abs(Position.x);
        public bool Catching => CatchTimeLeft > 0f;
        public bool Dodging => DodgeTimeLeft > 0f;
        public bool Charging => ChargeTime >= 0f;

        /// <summary>A catch can be opened with balls in hand: it drops them.</summary>
        public bool CanCatch => CatchCooldown <= 0f && !Charging && !Dodging;
        public bool CanDodge => DodgeCooldown <= 0f && !Charging;

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

        /// <summary>Metres a live ball can still travel before it drops dead.</summary>
        public float RangeLeft;

        /// <summary>Seconds before a dropped ball can be picked up again.</summary>
        public float LockTime;

        public CourtBall(int index)
        {
            Index = index;
        }

        public bool Locked => LockTime > 0f;
    }

    /// <summary>
    /// The whole game of one-on-one dodgeball as plain data and arithmetic, with no Unity objects
    /// in it. The match drives it once a frame and draws what it says; the human's controls and the
    /// machine's decisions both reach it through the same calls (<see cref="BeginCharge"/>,
    /// <see cref="ReleaseThrow"/>, <see cref="OpenCatch"/>, <see cref="Dodge"/>) and the same
    /// <see cref="CourtPlayer.Move"/>, so neither side can do anything the other cannot.
    ///
    /// A throw is charged: the longer the button is held, the further the ball travels, bounces off
    /// walls and other balls included, before it drops dead. That limit is what keeps a ball that
    /// bounces off every wall from staying dangerous forever.
    /// </summary>
    public sealed class DodgeballCourt
    {
        public readonly DodgeballRules Rules;
        public readonly CourtPlayer[] Players;
        public readonly List<CourtBall> Balls = new List<CourtBall>();

        /// <summary>
        /// What happened during the last step, in order, starting with the presses made since the
        /// step before it.
        /// </summary>
        public readonly List<CourtEvent> Events = new List<CourtEvent>();

        // How many of Events the last step reported. A press between steps adds its event after
        // them, and the next step drops only these, so a throw is reported by the step that
        // follows it instead of being cleared before anyone reads it.
        private int reported;

        /// <summary>The player who won a point during the last step, or -1.</summary>
        public int PointWinner { get; private set; } = -1;

        /// <summary>A rolling ball slower than this, in metres per second, is at rest.</summary>
        private const float RestSpeed = 0.05f;

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
                p.Held.Clear();
                p.CatchTimeLeft = p.CatchCooldown = p.DodgeTimeLeft = p.DodgeCooldown = 0f;
                p.ChargeTime = -1f;
            }

            for (int i = 0; i < Balls.Count; i++)
            {
                CourtBall b = Balls[i];
                float t = Balls.Count == 1 ? 0.5f : i / (float)(Balls.Count - 1);
                b.Position = new Vector2(0f, Mathf.Lerp(-Rules.HalfWidth * 0.6f, Rules.HalfWidth * 0.6f, t));
                b.Velocity = Vector2.zero;
                b.State = BallState.Free;
                b.Holder = b.Thrower = -1;
                b.RangeLeft = 0f;
                b.LockTime = 0f;
            }

            Events.Clear();
            reported = 0;
            PointWinner = -1;
        }

        // ---------------------------------------------------------------- the buttons

        /// <summary>True when <paramref name="player"/> holds a ball and stands behind their throwing line.</summary>
        public bool CanThrow(int player) => Players[player].Holding && Players[player].Depth >= Rules.throwingLine;

        /// <summary>True when <paramref name="player"/> stands between the center line and their throwing line.</summary>
        public bool InNoThrowZone(int player) => Players[player].Depth < Rules.throwingLine;

        /// <summary>True while a live ball thrown by <paramref name="player"/>'s opponent is in the air.</summary>
        public bool IncomingFor(int player)
        {
            for (int i = 0; i < Balls.Count; i++)
                if (Balls[i].State == BallState.Flying && Balls[i].Thrower == 1 - player) return true;
            return false;
        }

        /// <summary>The charge of <paramref name="player"/>'s throw so far, 0 to 1.</summary>
        public float Charge(int player)
        {
            CourtPlayer p = Players[player];
            return p.Charging ? Mathf.Clamp01(p.ChargeTime / Mathf.Max(0.01f, Rules.chargeTime)) : 0f;
        }

        /// <summary>
        /// Starts charging a throw: hold the button. Needs a ball in hand and no dodge or catch under
        /// way. It can start anywhere; only the release must be behind the throwing line.
        /// </summary>
        public bool BeginCharge(int player)
        {
            CourtPlayer p = Players[player];
            if (!p.Holding || p.Charging || p.Dodging || p.Catching) return false;
            p.ChargeTime = 0f;
            Events.Add(new CourtEvent(CourtEventKind.ChargeStarted, player, p.HeldBall));
            return true;
        }

        /// <summary>Lets go of a charge without throwing.</summary>
        public void CancelCharge(int player) => Players[player].ChargeTime = -1f;

        /// <summary>
        /// Releases a charging throw toward <paramref name="target"/>, as far as the charge allows.
        /// Refused, and the charge lost, in front of the throwing line.
        /// </summary>
        public bool ReleaseThrow(int player, Vector2 target)
        {
            CourtPlayer p = Players[player];
            if (!p.Charging) return false;
            float charge = Charge(player);
            p.ChargeTime = -1f;
            if (!CanThrow(player))
            {
                Events.Add(new CourtEvent(CourtEventKind.ThrowRefused, player, p.HeldBall));
                return false;
            }
            return Throw(player, target, charge);
        }

        /// <summary>
        /// Throws the ball in the throwing hand toward <paramref name="target"/> with
        /// <paramref name="charge"/> (0 to 1); the next ball moves into the hand. A throw always
        /// travels toward the other half: a target behind the thrower is bent forward. False when
        /// not holding a ball or in front of the throwing line.
        /// </summary>
        public bool Throw(int player, Vector2 target, float charge = 1f)
        {
            CourtPlayer p = Players[player];
            if (!CanThrow(player)) return false;
            p.ChargeTime = -1f;

            Vector2 dir = ThrowDirection(p, target);
            CourtBall ball = Balls[p.Held[0]];
            p.Held.RemoveAt(0);
            ball.State = BallState.Flying;
            ball.Holder = -1;
            ball.Thrower = player;
            ball.Position = p.Position + dir * (Rules.playerRadius + Rules.ballRadius + 0.05f);
            ball.Velocity = dir * Rules.throwSpeed;
            ball.RangeLeft = Rules.RangeFor(charge);
            Events.Add(new CourtEvent(CourtEventKind.Throw, player, ball.Index));
            return true;
        }

        /// <summary>The unit direction a throw from <paramref name="p"/> at <paramref name="target"/> takes.</summary>
        public static Vector2 ThrowDirection(CourtPlayer p, Vector2 target)
        {
            Vector2 dir = target - p.Position;
            if (dir.sqrMagnitude < 0.0001f) dir = new Vector2(p.Forward, 0f);
            dir.Normalize();
            if (dir.x * p.Forward < 0.2f)
            {
                dir.x = 0.2f * p.Forward;
                dir.y = Mathf.Sign(dir.y == 0f ? 1f : dir.y) * Mathf.Sqrt(1f - dir.x * dir.x);
            }
            return dir;
        }

        /// <summary>
        /// Opens a catch window. Any balls in hand drop to the floor around the player and cannot
        /// be picked up for <see cref="DodgeballRules.pickupLock"/>. False while cooling down,
        /// charging or dodging.
        /// </summary>
        public bool OpenCatch(int player)
        {
            CourtPlayer p = Players[player];
            if (!p.CanCatch) return false;
            DropAll(p);
            p.CatchTimeLeft = Rules.catchWindow;
            p.CatchCooldown = Rules.catchWindow + Rules.catchCooldown;
            Events.Add(new CourtEvent(CourtEventKind.CatchOpened, player, -1));
            return true;
        }

        private void DropAll(CourtPlayer p)
        {
            for (int k = 0; k < p.Held.Count; k++)
            {
                CourtBall b = Balls[p.Held[k]];
                // Out to the sides and a little back, so they do not land where the catch is.
                float side = k % 2 == 0 ? 1f : -1f;
                Vector2 dir = new Vector2(-p.Forward * 0.5f, side).normalized;
                b.State = BallState.Free;
                b.Holder = -1;
                b.Position = ClampBall(p.Position + dir * (Rules.playerRadius + Rules.ballRadius + 0.05f));
                b.Velocity = dir * 3f;
                b.LockTime = Rules.pickupLock;
                Events.Add(new CourtEvent(CourtEventKind.Drop, p.Index, b.Index));
            }
            p.Held.Clear();
        }

        /// <summary>A quick burst in <paramref name="direction"/>. False while cooling down or charging.</summary>
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
                // Balls move in a few substeps so a fast ball cannot pass through another.
                const int Substeps = 3;
                float h = dt / Substeps;
                for (int s = 0; s < Substeps && PointWinner < 0; s++)
                {
                    for (int i = 0; i < Balls.Count && PointWinner < 0; i++) StepBall(Balls[i], h);
                    if (PointWinner < 0) CollideBalls();
                }
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
            if (p.Charging) p.ChargeTime = p.Holding ? p.ChargeTime + dt : -1f;

            if (p.Dodging)
            {
                float t = Mathf.Min(dt, p.DodgeTimeLeft);
                p.Position += p.DodgeDirection * (Rules.dodgeDistance / Rules.dodgeDuration) * t;
                p.DodgeTimeLeft -= t;
            }
            else
            {
                Vector2 move = Vector2.ClampMagnitude(p.Move, 1f);
                float factor = p.Catching ? Rules.catchMoveFactor : p.Charging ? Rules.chargeMoveFactor : 1f;
                p.Position += move * Rules.moveSpeed * factor * dt;
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

        private Vector2 ClampBall(Vector2 p)
        {
            float bx = Rules.HalfLength - Rules.ballRadius, by = Rules.HalfWidth - Rules.ballRadius;
            return new Vector2(Mathf.Clamp(p.x, -bx, bx), Mathf.Clamp(p.y, -by, by));
        }

        private void StepBall(CourtBall ball, float dt)
        {
            switch (ball.State)
            {
                case BallState.Held:
                {
                    CourtPlayer holder = Players[ball.Holder];
                    ball.Position = holder.Position + new Vector2(holder.Forward * 0.35f, -0.3f);
                    ball.Velocity = Vector2.zero;
                    return;
                }

                case BallState.Flying:
                {
                    Vector2 from = ball.Position;
                    float travel = Mathf.Min(ball.Velocity.magnitude * dt, ball.RangeLeft);
                    Vector2 dir = ball.Velocity.sqrMagnitude > 0f ? ball.Velocity.normalized : Vector2.zero;
                    Vector2 to = from + dir * travel;
                    CourtPlayer target = Opponent(ball.Thrower);
                    float reach = Rules.playerRadius + Rules.ballRadius;
                    if (SegmentPointDistance(from, to, target.Position) <= reach)
                    {
                        Land(ball, target);
                        return;
                    }
                    ball.Position = to;
                    ball.RangeLeft -= travel;
                    if (Bounce(ball, Rules.wallBounce))
                        Events.Add(new CourtEvent(CourtEventKind.WallBounce, ball.Thrower, ball.Index));
                    if (ball.RangeLeft <= 0.0001f) Die(ball);
                    return;
                }

                default:
                {
                    if (ball.LockTime > 0f) ball.LockTime = Mathf.Max(0f, ball.LockTime - dt);
                    float speed = ball.Velocity.magnitude;
                    if (speed <= RestSpeed)
                    {
                        ball.Velocity = Vector2.zero;
                        return;
                    }
                    ball.Position += ball.Velocity * dt;
                    Bounce(ball, 0.6f);
                    float slowed = Mathf.Max(0f, speed - Rules.rollFriction * dt);
                    ball.Velocity = ball.Velocity.normalized * slowed;
                    return;
                }
            }
        }

        /// <summary>A live ball that has run out of range drops and rolls on, harmless.</summary>
        private void Die(CourtBall ball)
        {
            Events.Add(new CourtEvent(CourtEventKind.BallDied, ball.Thrower, ball.Index));
            ball.State = BallState.Free;
            ball.Thrower = -1;
            ball.RangeLeft = 0f;
            ball.Velocity *= 0.35f;
        }

        /// <summary>Reflects a ball that has crossed a wall back inside. True when it bounced.</summary>
        private bool Bounce(CourtBall ball, float keep)
        {
            float bx = Rules.HalfLength - Rules.ballRadius, by = Rules.HalfWidth - Rules.ballRadius;
            bool bounced = false;
            Vector2 p = ball.Position, v = ball.Velocity;
            if (p.x > bx) { p.x = 2f * bx - p.x; v.x = -Mathf.Abs(v.x); bounced = true; }
            else if (p.x < -bx) { p.x = -2f * bx - p.x; v.x = Mathf.Abs(v.x); bounced = true; }
            if (p.y > by) { p.y = 2f * by - p.y; v.y = -Mathf.Abs(v.y); bounced = true; }
            else if (p.y < -by) { p.y = -2f * by - p.y; v.y = Mathf.Abs(v.y); bounced = true; }
            if (bounced)
            {
                ball.Position = ClampBall(p);
                ball.Velocity = v * keep;
            }
            return bounced;
        }

        /// <summary>
        /// Two balls on the floor or in the air that touch bounce off each other like billiard
        /// balls of equal weight. A live ball stays live; a ball on the floor stays harmless and
        /// rolls away.
        /// </summary>
        private void CollideBalls()
        {
            float min = Rules.ballRadius * 2f;
            for (int i = 0; i < Balls.Count; i++)
            {
                CourtBall a = Balls[i];
                if (a.State == BallState.Held) continue;
                for (int j = i + 1; j < Balls.Count; j++)
                {
                    CourtBall b = Balls[j];
                    if (b.State == BallState.Held) continue;
                    Vector2 d = b.Position - a.Position;
                    float dist = d.magnitude;
                    if (dist >= min || dist < 0.0001f) continue;
                    Vector2 n = d / dist;
                    float closing = Vector2.Dot(a.Velocity - b.Velocity, n);
                    // Pushed apart either way, so two resting balls never stay merged.
                    Vector2 separate = n * ((min - dist) * 0.5f);
                    a.Position = ClampBall(a.Position - separate);
                    b.Position = ClampBall(b.Position + separate);
                    if (closing <= 0f) continue;
                    float impulse = closing * (1f + Rules.ballBounce) * 0.5f;
                    a.Velocity -= n * impulse;
                    b.Velocity += n * impulse;
                    CourtBall live = a.State == BallState.Flying ? a : b;
                    Events.Add(new CourtEvent(CourtEventKind.BallBounce, live.Thrower, live.Index));
                }
            }
        }

        private void Land(CourtBall ball, CourtPlayer target)
        {
            if (target.Catching)
            {
                ball.State = BallState.Held;
                ball.Holder = target.Index;
                ball.Velocity = Vector2.zero;
                target.Held.Insert(0, ball.Index);
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
            ball.RangeLeft = 0f;
        }

        private void TryPickUp(CourtPlayer p)
        {
            if (p.Catching || p.Held.Count >= Rules.maxHeld) return;
            float reach = Rules.playerRadius + Rules.ballRadius + 0.1f;
            for (int i = 0; i < Balls.Count && p.Held.Count < Rules.maxHeld; i++)
            {
                CourtBall b = Balls[i];
                if (b.State != BallState.Free || b.Locked) continue;
                if ((b.Position - p.Position).sqrMagnitude > reach * reach) continue;
                b.State = BallState.Held;
                b.Holder = p.Index;
                b.Velocity = Vector2.zero;
                p.Held.Add(i);
                Events.Add(new CourtEvent(CourtEventKind.Pickup, p.Index, i));
            }
        }

        // ---------------------------------------------------------------- prediction

        /// <summary>
        /// The path a ball starting at <paramref name="from"/> going <paramref name="direction"/>
        /// takes over <paramref name="range"/> metres, reflecting off the walls: the start, every
        /// bounce, and the end. Other balls are not taken into account.
        /// </summary>
        public void PredictPath(Vector2 from, Vector2 direction, float range, List<Vector2> points)
        {
            points.Clear();
            points.Add(from);
            float bx = Rules.HalfLength - Rules.ballRadius, by = Rules.HalfWidth - Rules.ballRadius;
            Vector2 p = from, d = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.right;
            float left = range;
            for (int bounce = 0; bounce < 16 && left > 0.0001f; bounce++)
            {
                float tx = d.x > 0f ? (bx - p.x) / d.x : d.x < 0f ? (-bx - p.x) / d.x : float.MaxValue;
                float ty = d.y > 0f ? (by - p.y) / d.y : d.y < 0f ? (-by - p.y) / d.y : float.MaxValue;
                float t = Mathf.Max(0f, Mathf.Min(tx, ty));
                if (t >= left)
                {
                    points.Add(p + d * left);
                    return;
                }
                p += d * t;
                left -= t;
                if (tx <= ty) d.x = -d.x;
                if (ty <= tx) d.y = -d.y;
                points.Add(p);
            }
        }

        private readonly List<Vector2> scratch = new List<Vector2>();

        /// <summary>
        /// Where a live ball passes a player standing still, following its bounces until its range
        /// runs out: the seconds until it is closest, how far from the player's center it is then,
        /// and on which side (positive is above). Seconds is negative for a ball that is not live.
        /// </summary>
        public void ClosestApproach(CourtBall ball, Vector2 player, out float seconds, out float distance, out float sideways)
        {
            seconds = -1f;
            distance = float.MaxValue;
            sideways = 0f;
            float speed = ball.Velocity.magnitude;
            if (ball.State != BallState.Flying || speed <= 0f) return;
            PredictPath(ball.Position, ball.Velocity, ball.RangeLeft, scratch);
            float along = 0f;
            for (int i = 1; i < scratch.Count; i++)
            {
                Vector2 a = scratch[i - 1], b = scratch[i];
                Vector2 ab = b - a;
                float len = ab.magnitude;
                float t = len > 0f ? Mathf.Clamp01(Vector2.Dot(player - a, ab) / (len * len)) : 0f;
                Vector2 at = a + ab * t;
                float d = Vector2.Distance(at, player);
                if (d < distance)
                {
                    distance = d;
                    seconds = (along + len * t) / speed;
                    sideways = at.y - player.y;
                }
                along += len;
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
    }
}
