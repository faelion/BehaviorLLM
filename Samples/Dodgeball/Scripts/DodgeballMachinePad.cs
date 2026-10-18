using System;
using System.Collections.Generic;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// The machine player's gamepad. Each decision presses it once: a direction stays held until
    /// the next decision, a throw, a catch or a dodge happens once. The action bindings call the
    /// <c>On...</c> methods; nothing here chooses anything.
    ///
    /// A throw is held and released like the human's: the pad charges it for as long as the chosen
    /// aim needs to reach its point with some to spare, following the target while it charges, and
    /// the machine runs slower and cannot dodge or catch meanwhile, as the human cannot. The model
    /// chooses where to throw; the pad does the holding.
    ///
    /// It is also the availability provider, because the court already knows what is possible:
    /// Throw is only offered while a ball can be thrown, Catch and Dodge only while ready and a
    /// ball is coming. A button that would do nothing, or nothing useful, is never shown to the model.
    /// </summary>
    [AddComponentMenu("Dodgeball/Machine Pad")]
    public class DodgeballMachinePad : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("The match this pad plays in.")]
        public DodgeballMatch match;

        [Tooltip("Which player the pad controls: 0 plays the left half, 1 the right.")]
        public int playerIndex = 1;

        /// <summary>Raised when an input actually reaches the court, after any reaction delay.</summary>
        public event Action<string> InputApplied;

        /// <summary>The last input applied, for the on-screen caption.</summary>
        public string LastInput { get; private set; } = "";

        private struct Pending
        {
            public float At;
            public string Action;
            public string Value;
        }

        private readonly Queue<Pending> pending = new Queue<Pending>();

        // The aim of the throw being charged, or null.
        private string chargingAim;

        /// <summary>Metres a throw is charged beyond its aim point, so it still carries past a target that moves away.</summary>
        public const float RangeMargin = 4f;

        // ---------------------------------------------------------------- bindings

        public void OnMove(ActionArguments args) => Queue(DodgeballIds.Move, args[DodgeballIds.DirectionParameter]);
        public void OnStand(ActionArguments args) => Queue(DodgeballIds.Stand, null);
        public void OnThrow(ActionArguments args) => Queue(DodgeballIds.Throw, args[DodgeballIds.AimParameter]);
        public void OnCatch(ActionArguments args) => Queue(DodgeballIds.Catch, null);
        public void OnDodge(ActionArguments args) => Queue(DodgeballIds.Dodge, args[DodgeballIds.DirectionParameter]);

        private void Queue(string action, string value)
        {
            float delay = match != null && match.Rules != null ? match.Rules.machineReactionDelay : 0f;
            pending.Enqueue(new Pending { At = Time.time + delay, Action = action, Value = value });
            if (delay <= 0f) Flush();
        }

        private void Update()
        {
            Flush();
            UpdateCharge();
        }

        /// <summary>Where the throw being charged is aimed right now, for the view to draw. False when not charging.</summary>
        public bool TryGetChargeAim(out Vector2 target)
        {
            target = Vector2.zero;
            if (chargingAim == null || match == null || match.Court == null) return false;
            target = AimPoint(match.Court, playerIndex, chargingAim);
            return true;
        }

        /// <summary>Releases the charging throw once it reaches its aim point with the margin to spare.</summary>
        private void UpdateCharge()
        {
            if (chargingAim == null) return;
            if (match == null || match.Court == null || !match.Playing)
            {
                chargingAim = null;
                return;
            }
            DodgeballCourt court = match.Court;
            CourtPlayer me = court.Players[playerIndex];
            if (!me.Charging)
            {
                chargingAim = null;
                return;
            }
            Vector2 target = AimPoint(court, playerIndex, chargingAim, out float pathLength);
            float needed = court.Rules.ChargeFor(pathLength + RangeMargin);
            if (court.Charge(playerIndex) + 0.0001f >= needed)
            {
                court.ReleaseThrow(playerIndex, target);
                chargingAim = null;
            }
        }

        private void Flush()
        {
            while (pending.Count > 0 && pending.Peek().At <= Time.time)
            {
                Pending p = pending.Dequeue();
                Apply(p.Action, p.Value);
            }
        }

        /// <summary>Drops inputs still waiting out the reaction delay, as at the start of a point.</summary>
        public void Clear()
        {
            pending.Clear();
            chargingAim = null;
        }

        private void Apply(string action, string value)
        {
            if (match == null || match.Court == null || !match.Playing) return;
            DodgeballCourt court = match.Court;
            CourtPlayer me = court.Players[playerIndex];

            switch (action)
            {
                case DodgeballIds.Move:
                    me.Move = DirectionVector(value, me.Forward);
                    break;
                case DodgeballIds.Stand:
                    me.Move = Vector2.zero;
                    break;
                case DodgeballIds.Throw:
                    me.Move = Vector2.zero;
                    if (court.BeginCharge(playerIndex)) chargingAim = value;
                    UpdateCharge();
                    break;
                case DodgeballIds.Catch:
                    me.Move = Vector2.zero;
                    court.OpenCatch(playerIndex);
                    break;
                case DodgeballIds.Dodge:
                    me.Move = Vector2.zero;
                    court.Dodge(playerIndex, DirectionVector(value, me.Forward));
                    break;
            }

            LastInput = string.IsNullOrEmpty(value) ? action : action + " " + value;
            InputApplied?.Invoke(LastInput);
        }

        // ---------------------------------------------------------------- availability

        public bool IsActionAvailable(string actionName)
        {
            if (match == null || match.Court == null) return true;
            CourtPlayer me = match.Court.Players[playerIndex];
            switch (actionName)
            {
                case DodgeballIds.Throw: return match.Court.CanThrow(playerIndex) && !me.Charging && !me.Dodging && !me.Catching;
                // Only against a live ball: offered with nothing coming, d1 caught in 36 of 40
                // states where it held balls, dropping them, and dodged instead of backing off.
                case DodgeballIds.Catch: return me.CanCatch && match.Court.IncomingFor(playerIndex);
                case DodgeballIds.Dodge: return me.CanDodge && match.Court.IncomingFor(playerIndex);
                default: return true;
            }
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>A unit vector in court coordinates for a direction named in the player's own frame.</summary>
        public static Vector2 DirectionVector(string direction, float forward)
        {
            float f = 0f, u = 0f;
            switch (direction)
            {
                case DodgeballIds.Forward: f = 1f; break;
                case DodgeballIds.Back: f = -1f; break;
                case DodgeballIds.Up: u = 1f; break;
                case DodgeballIds.Down: u = -1f; break;
                case DodgeballIds.ForwardUp: f = 1f; u = 1f; break;
                case DodgeballIds.ForwardDown: f = 1f; u = -1f; break;
                case DodgeballIds.BackUp: f = -1f; u = 1f; break;
                case DodgeballIds.BackDown: f = -1f; u = -1f; break;
            }
            return new Vector2(f * forward, u).normalized;
        }

        /// <summary>
        /// Where a throw with <paramref name="aim"/> is pointed, and how far the ball travels to
        /// get there, bounce included.
        /// </summary>
        public static Vector2 AimPoint(DodgeballCourt court, int thrower, string aim, out float pathLength)
        {
            DodgeballRules r = court.Rules;
            CourtPlayer me = court.Players[thrower];
            CourtPlayer them = court.Opponent(thrower);
            Vector2 target = them.Position;
            switch (aim)
            {
                case DodgeballIds.AheadOfOpponent:
                    // Where they will be if they keep running, refined once for the longer flight.
                    for (int i = 0; i < 2; i++)
                    {
                        float flight = Vector2.Distance(me.Position, target) / r.throwSpeed;
                        target = them.Position + them.Velocity * flight;
                    }
                    target = court.ClampToHalf(them, target);
                    break;
                case DodgeballIds.OffsetUp1: target.y += 1f; break;
                case DodgeballIds.OffsetUp2: target.y += 2f; break;
                case DodgeballIds.OffsetDown1: target.y -= 1f; break;
                case DodgeballIds.OffsetDown2: target.y -= 2f; break;
                case DodgeballIds.BankTopWall:
                case DodgeballIds.BankBottomWall:
                    // Aim at the opponent's reflection in the wall: the straight line to it is the
                    // bounce path, unfolded.
                    float wall = (aim == DodgeballIds.BankTopWall ? 1f : -1f) * (r.HalfWidth - r.ballRadius);
                    Vector2 mirrored = new Vector2(target.x, 2f * wall - target.y);
                    pathLength = Vector2.Distance(me.Position, mirrored);
                    return mirrored;
            }
            pathLength = Vector2.Distance(me.Position, target);
            return target;
        }

        /// <summary>Where a throw with <paramref name="aim"/> is pointed.</summary>
        public static Vector2 AimPoint(DodgeballCourt court, int thrower, string aim) => AimPoint(court, thrower, aim, out _);
    }
}
