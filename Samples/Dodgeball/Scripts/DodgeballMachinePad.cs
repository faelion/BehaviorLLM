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
    /// It is also the availability provider, because the court already knows what is possible:
    /// Throw is only offered while holding a ball, Catch only while empty-handed and ready, Dodge
    /// only while ready. A button that would do nothing is never shown to the model.
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

        private void Update() => Flush();

        private void Flush()
        {
            while (pending.Count > 0 && pending.Peek().At <= Time.time)
            {
                Pending p = pending.Dequeue();
                Apply(p.Action, p.Value);
            }
        }

        /// <summary>Drops inputs still waiting out the reaction delay, as at the start of a point.</summary>
        public void Clear() => pending.Clear();

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
                    court.Throw(playerIndex, AimPoint(court, playerIndex, value));
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
                case DodgeballIds.Throw: return match.Court.CanThrow(playerIndex);
                case DodgeballIds.Catch: return me.CanCatch;
                case DodgeballIds.Dodge: return me.CanDodge;
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

        /// <summary>Where a throw with <paramref name="aim"/> is pointed.</summary>
        public static Vector2 AimPoint(DodgeballCourt court, int thrower, string aim)
        {
            Vector2 target = court.Opponent(thrower).Position;
            float lead = court.Rules.leadDistance;
            if (aim == DodgeballIds.LeadUp) target.y += lead;
            else if (aim == DodgeballIds.LeadDown) target.y -= lead;
            return target;
        }
    }
}
