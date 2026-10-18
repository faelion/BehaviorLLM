using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    public enum FighterState { Free, Jab, Hook, Dash, HitStun, GuardBroken, KO }

    public enum AttackPhase { None, Windup, Active, Recovery }

    /// <summary>A button that fires once, as opposed to a held direction or a held guard.</summary>
    public enum FighterPress { None, Jab, Hook, Dash }

    public enum ArenaEventKind { AttackStarted, DashStarted, Hit, Blocked, GuardBreak, Whiff, KO }

    /// <summary>Something that happened during one <see cref="BrawlerArena.Step"/>.</summary>
    public struct ArenaEvent
    {
        public ArenaEventKind Kind;
        public int Fighter;
        public bool Heavy;
        public float Damage;

        public ArenaEvent(ArenaEventKind kind, int fighter, bool heavy = false, float damage = 0f)
        {
            Kind = kind;
            Fighter = fighter;
            Heavy = heavy;
            Damage = damage;
        }
    }

    public sealed class BrawlerFighter
    {
        public readonly int Index;

        /// <summary>Position on the arena floor: x across, y away from the camera.</summary>
        public Vector2 Position;

        /// <summary>Unit vector the fighter faces. Toward the opponent except while an attack is under way.</summary>
        public Vector2 Facing = Vector2.right;

        /// <summary>The direction being pushed, at most length 1, in arena coordinates. Kept until changed.</summary>
        public Vector2 Move;

        /// <summary>True while the guard button is held.</summary>
        public bool BlockHeld;

        public FighterState State;
        public float StateTime;
        public float StateDuration;
        public float Health;
        public float DashCooldown;
        public Vector2 DashDirection;
        public Vector2 Velocity;

        internal bool attackResolved;
        internal FighterPress buffered;
        internal Vector2 bufferedDirection;
        internal float bufferedAge;
        internal Vector2 knockVelocity;
        internal float knockTime;

        public BrawlerFighter(int index)
        {
            Index = index;
        }

        public bool Blocking => State == FighterState.Free && BlockHeld;
        public bool Attacking => State == FighterState.Jab || State == FighterState.Hook;
        public bool CanDash => DashCooldown <= 0f && State != FighterState.KO;

        /// <summary>Seconds left in the current timed state, or 0.</summary>
        public float TimeLeft => Mathf.Max(0f, StateDuration - StateTime);
    }

    /// <summary>
    /// The whole of a one-on-one fight as plain data and arithmetic, with no Unity objects in it.
    /// The match drives it once a frame and draws what it says; the human's controls and the
    /// machine's decisions both reach it through <see cref="Press"/> and through each fighter's
    /// <see cref="BrawlerFighter.Move"/> and <see cref="BrawlerFighter.BlockHeld"/>, so neither
    /// side can do anything the other cannot.
    ///
    /// The fight is built around tells. A punch has a wind-up the opponent can see, a moment it can
    /// land, and a recovery that leaves the attacker open. The jab is quick and blocked without
    /// harm; the hook is slow, breaks a block, and leaves a long opening when it misses. A dash is
    /// untouchable for most of its length. Reading the wind-up and answering in time is the game.
    /// </summary>
    public sealed class BrawlerArena
    {
        public readonly BrawlerRules Rules;
        public readonly BrawlerFighter[] Fighters = { new BrawlerFighter(0), new BrawlerFighter(1) };

        /// <summary>What happened during the last step, in order.</summary>
        public readonly List<ArenaEvent> Events = new List<ArenaEvent>();

        public BrawlerArena(BrawlerRules rules)
        {
            Rules = rules;
            Reset();
        }

        public BrawlerFighter Opponent(int fighter) => Fighters[1 - fighter];

        /// <summary>Both fighters back in their corners at full health.</summary>
        public void Reset()
        {
            for (int i = 0; i < Fighters.Length; i++)
            {
                BrawlerFighter f = Fighters[i];
                f.Position = new Vector2(i == 0 ? -2.5f : 2.5f, 0f);
                f.Facing = new Vector2(i == 0 ? 1f : -1f, 0f);
                f.Move = Vector2.zero;
                f.BlockHeld = false;
                f.Health = Rules.health;
                f.DashCooldown = 0f;
                f.Velocity = Vector2.zero;
                f.buffered = FighterPress.None;
                f.knockTime = 0f;
                Enter(f, FighterState.Free, 0f);
            }
            Events.Clear();
        }

        // ---------------------------------------------------------------- input

        /// <summary>
        /// Presses a one-shot button. A free fighter acts at once; a busy one keeps the press for
        /// <see cref="BrawlerRules.inputBuffer"/> seconds and acts as soon as it is free. A dash
        /// goes in <paramref name="direction"/>, or straight back from the opponent when none is given.
        /// </summary>
        public void Press(int fighter, FighterPress press, Vector2 direction = default)
        {
            BrawlerFighter f = Fighters[fighter];
            if (press == FighterPress.None || f.State == FighterState.KO) return;
            f.buffered = press;
            f.bufferedDirection = direction;
            f.bufferedAge = 0f;
            if (f.State == FighterState.Free) Consume(f);
        }

        private void Consume(BrawlerFighter f)
        {
            FighterPress press = f.buffered;
            f.buffered = FighterPress.None;
            BrawlerFighter them = Opponent(f.Index);
            switch (press)
            {
                case FighterPress.Jab:
                case FighterPress.Hook:
                    f.Facing = FaceToward(f, them);
                    bool heavy = press == FighterPress.Hook;
                    BrawlerAttack a = heavy ? Rules.hook : Rules.jab;
                    Enter(f, heavy ? FighterState.Hook : FighterState.Jab, a.Total);
                    f.attackResolved = false;
                    Events.Add(new ArenaEvent(ArenaEventKind.AttackStarted, f.Index, heavy));
                    break;

                case FighterPress.Dash:
                    if (f.DashCooldown > 0f) return;
                    Vector2 dir = f.bufferedDirection.sqrMagnitude > 0.0001f
                        ? f.bufferedDirection.normalized
                        : -FaceToward(f, them);
                    f.DashDirection = dir;
                    f.DashCooldown = Rules.dashDuration + Rules.dashCooldown;
                    Enter(f, FighterState.Dash, Rules.dashDuration);
                    Events.Add(new ArenaEvent(ArenaEventKind.DashStarted, f.Index));
                    break;
            }
        }

        // ---------------------------------------------------------------- simulation

        /// <summary>Advances the fight by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            Events.Clear();
            if (dt <= 0f) return;

            for (int i = 0; i < Fighters.Length; i++) StepFighter(Fighters[i], dt);
            for (int i = 0; i < Fighters.Length; i++) ResolveAttack(Fighters[i], dt);
            Separate();
        }

        private void StepFighter(BrawlerFighter f, float dt)
        {
            Vector2 before = f.Position;
            BrawlerFighter them = Opponent(f.Index);
            f.DashCooldown = Mathf.Max(0f, f.DashCooldown - dt);
            f.StateTime += dt;

            if (f.buffered != FighterPress.None)
            {
                f.bufferedAge += dt;
                if (f.bufferedAge > Rules.inputBuffer) f.buffered = FighterPress.None;
            }

            switch (f.State)
            {
                case FighterState.Free:
                    if (f.buffered != FighterPress.None) Consume(f);
                    if (f.State != FighterState.Free) break;
                    f.Facing = FaceToward(f, them);
                    float speed = Rules.moveSpeed * (f.BlockHeld ? Rules.blockMoveFactor : 1f);
                    f.Position += Vector2.ClampMagnitude(f.Move, 1f) * speed * dt;
                    break;

                case FighterState.Dash:
                    f.Position += f.DashDirection * (Rules.dashDistance / Rules.dashDuration) * dt;
                    f.Facing = FaceToward(f, them);
                    if (f.StateTime >= f.StateDuration) Enter(f, FighterState.Free, 0f);
                    break;

                case FighterState.Jab:
                case FighterState.Hook:
                case FighterState.HitStun:
                case FighterState.GuardBroken:
                    if (f.StateTime >= f.StateDuration) Enter(f, FighterState.Free, 0f);
                    break;
            }

            if (f.knockTime > 0f)
            {
                float t = Mathf.Min(dt, f.knockTime);
                f.Position += f.knockVelocity * t;
                f.knockTime -= t;
            }

            f.Position = ClampToArena(f.Position);
            f.Velocity = (f.Position - before) / dt;
        }

        private void ResolveAttack(BrawlerFighter a, float dt)
        {
            if (!a.Attacking || a.attackResolved) return;
            bool heavy = a.State == FighterState.Hook;
            BrawlerAttack atk = heavy ? Rules.hook : Rules.jab;
            AttackPhase phase = PhaseOf(a);
            if (phase == AttackPhase.Windup) return;

            // A long frame can step over the whole active window; the punch still gets its one
            // chance to land rather than counting as a miss it never had a chance to avoid.
            bool wasActiveThisStep = a.StateTime - dt < atk.windup + atk.active;
            BrawlerFighter d = Opponent(a.Index);
            bool reaches = InReach(a, d, atk.range);
            if (phase == AttackPhase.Recovery && !(wasActiveThisStep && reaches))
            {
                a.attackResolved = true;
                Events.Add(new ArenaEvent(ArenaEventKind.Whiff, a.Index, heavy));
                return;
            }

            if (!reaches) return;
            if (d.State == FighterState.KO) return;
            if (d.State == FighterState.Dash && d.StateTime < Rules.dashInvulnerable) return;

            a.attackResolved = true;
            Vector2 push = (d.Position - a.Position).normalized;

            if (d.Blocking && Vector2.Angle(d.Facing, -push) <= Rules.blockArc)
            {
                if (heavy)
                {
                    d.Health -= Rules.guardBreakDamage;
                    d.BlockHeld = false;
                    Enter(d, FighterState.GuardBroken, Rules.guardBreakStun);
                    Events.Add(new ArenaEvent(ArenaEventKind.GuardBreak, a.Index, true, Rules.guardBreakDamage));
                    Knock(d, push, 0.6f);
                }
                else
                {
                    Events.Add(new ArenaEvent(ArenaEventKind.Blocked, a.Index, false));
                    Knock(d, push, 0.15f);
                }
            }
            else
            {
                bool counter = d.Attacking && PhaseOf(d) == AttackPhase.Windup;
                float damage = atk.damage * (counter ? 1f + Rules.counterBonus : 1f);
                d.Health -= damage;
                Enter(d, FighterState.HitStun, atk.hitStun);
                Events.Add(new ArenaEvent(ArenaEventKind.Hit, a.Index, heavy, damage));
                Knock(d, push, atk.knockback);
            }

            if (d.Health <= 0f)
            {
                d.Health = 0f;
                Enter(d, FighterState.KO, 0f);
                Events.Add(new ArenaEvent(ArenaEventKind.KO, a.Index, heavy));
            }
        }

        private void Knock(BrawlerFighter f, Vector2 direction, float distance)
        {
            const float duration = 0.15f;
            f.knockVelocity = direction * (distance / duration);
            f.knockTime = duration;
        }

        private static void Enter(BrawlerFighter f, FighterState state, float duration)
        {
            f.State = state;
            f.StateTime = 0f;
            f.StateDuration = duration;
        }

        /// <summary>
        /// Pushes overlapping fighters apart, half each. Repeated a few times because the arena's
        /// edge can take back one fighter's half of the push, and the other then has to give more.
        /// </summary>
        private void Separate()
        {
            BrawlerFighter a = Fighters[0], b = Fighters[1];
            float min = Rules.fighterRadius * 2f;
            for (int i = 0; i < 8; i++)
            {
                Vector2 d = b.Position - a.Position;
                float dist = d.magnitude;
                if (dist >= min - 0.0001f) return;
                Vector2 n = dist > 0.0001f ? d / dist : Vector2.right;
                float overlap = (min - dist) * 0.5f;
                a.Position = ClampToArena(a.Position - n * overlap);
                b.Position = ClampToArena(b.Position + n * overlap);
            }
        }

        // ---------------------------------------------------------------- queries

        /// <summary>Which part of its current attack <paramref name="f"/> is in.</summary>
        public AttackPhase PhaseOf(BrawlerFighter f)
        {
            if (!f.Attacking) return AttackPhase.None;
            BrawlerAttack a = f.State == FighterState.Hook ? Rules.hook : Rules.jab;
            if (f.StateTime < a.windup) return AttackPhase.Windup;
            if (f.StateTime < a.windup + a.active) return AttackPhase.Active;
            return AttackPhase.Recovery;
        }

        /// <summary>Seconds until <paramref name="f"/>'s punch can land, or -1 when it is not winding up.</summary>
        public float TimeToImpact(BrawlerFighter f)
        {
            if (PhaseOf(f) != AttackPhase.Windup) return -1f;
            BrawlerAttack a = f.State == FighterState.Hook ? Rules.hook : Rules.jab;
            return a.windup - f.StateTime;
        }

        /// <summary>True when <paramref name="d"/> is within <paramref name="range"/> and inside <paramref name="a"/>'s punching arc.</summary>
        public bool InReach(BrawlerFighter a, BrawlerFighter d, float range)
        {
            Vector2 to = d.Position - a.Position;
            return to.magnitude <= range && Vector2.Angle(a.Facing, to) <= Rules.attackArc;
        }

        public Vector2 ClampToArena(Vector2 p)
        {
            float max = Rules.arenaRadius - Rules.fighterRadius;
            return p.sqrMagnitude > max * max ? p.normalized * max : p;
        }

        private static Vector2 FaceToward(BrawlerFighter f, BrawlerFighter them)
        {
            Vector2 d = them.Position - f.Position;
            return d.sqrMagnitude > 0.0001f ? d.normalized : f.Facing;
        }

        /// <summary>
        /// The direction an evade rolls: square to the line between the fighters and a little back,
        /// toward whichever side leaves more room before the arena's edge.
        /// </summary>
        public Vector2 EvadeDirection(int fighter)
        {
            Vector2 toward = Relative(fighter, BrawlerIds.Toward);
            Vector2 left = new Vector2(-toward.y, toward.x);
            Vector2 p = Fighters[fighter].Position;
            Vector2 side = (p + left).sqrMagnitude <= (p - left).sqrMagnitude ? left : -left;
            return (side - toward * 0.35f).normalized;
        }

        /// <summary>A unit vector for a direction named relative to the opponent.</summary>
        public Vector2 Relative(int fighter, string direction)
        {
            BrawlerFighter f = Fighters[fighter];
            Vector2 toward = FaceToward(f, Opponent(fighter));
            switch (direction)
            {
                case BrawlerIds.Toward: return toward;
                case BrawlerIds.Away: return -toward;
                case BrawlerIds.CircleLeft: return new Vector2(-toward.y, toward.x);
                case BrawlerIds.CircleRight: return new Vector2(toward.y, -toward.x);
                default: return Vector2.zero;
            }
        }
    }
}
