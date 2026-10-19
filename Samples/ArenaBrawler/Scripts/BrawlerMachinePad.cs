using System;
using System.Collections.Generic;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// The machine fighter's gamepad. Each decision presses it once: a direction or a guard stays
    /// held until the next decision, a jab, a hook or an evade fires once. The action bindings call
    /// the <c>On...</c> methods; nothing here chooses anything.
    ///
    /// A held direction is relative to the opponent and is turned into a world direction every
    /// frame, so "toward" keeps tracking a moving opponent between decisions, as a person's thumb
    /// would.
    ///
    /// It is also the availability provider: Evade is offered only while it is ready. Punches stay
    /// on the menu while the fighter is busy, because a press is buffered and comes out the moment
    /// the fighter is free.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Machine Pad")]
    public class BrawlerMachinePad : MonoBehaviour, IActionAvailabilityProvider
    {
        [Tooltip("The match this pad fights in.")]
        public BrawlerMatch match;

        [Tooltip("Which fighter the pad controls: 0 is the human's, 1 the machine's.")]
        public int fighterIndex = 1;

        /// <summary>Raised when an input actually reaches the fight, after any reaction delay.</summary>
        public event Action<string> InputApplied;

        /// <summary>The last input applied, for the on-screen caption.</summary>
        public string LastInput { get; private set; } = "";

        private string heldDirection;

        private struct Pending
        {
            public float At;
            public string Action;
            public string Value;
        }

        private readonly Queue<Pending> pending = new Queue<Pending>();

        // ---------------------------------------------------------------- bindings

        public void OnMove(ActionArguments args) => Queue(BrawlerIds.Move, args[BrawlerIds.DirectionParameter]);
        public void OnStand(ActionArguments args) => Queue(BrawlerIds.Stand, null);
        public void OnJab(ActionArguments args) => Queue(BrawlerIds.Jab, null);
        public void OnHook(ActionArguments args) => Queue(BrawlerIds.Hook, null);
        public void OnBlock(ActionArguments args) => Queue(BrawlerIds.Block, null);
        public void OnEvade(ActionArguments args) => Queue(BrawlerIds.Evade, null);

        private void Queue(string action, string value)
        {
            float delay = match != null && match.Rules != null ? match.Rules.machineReactionDelay : 0f;
            pending.Enqueue(new Pending { At = Time.time + delay, Action = action, Value = value });
            if (delay <= 0f) Flush();
        }

        /// <summary>Drops waiting inputs and lets go of everything, as at the start of a round.</summary>
        public void Clear()
        {
            pending.Clear();
            heldDirection = null;
        }

        private void Update()
        {
            Flush();
            if (match == null || match.Arena == null) return;
            BrawlerFighter me = match.Arena.Fighters[fighterIndex];
            me.Move = heldDirection != null ? match.Arena.Relative(fighterIndex, heldDirection) : Vector2.zero;
        }

        private void Flush()
        {
            while (pending.Count > 0 && pending.Peek().At <= Time.time)
            {
                Pending p = pending.Dequeue();
                Apply(p.Action, p.Value);
            }
        }

        private void Apply(string action, string value)
        {
            if (match == null || match.Arena == null || !match.Fighting) return;
            BrawlerArena arena = match.Arena;
            BrawlerFighter me = arena.Fighters[fighterIndex];

            heldDirection = action == BrawlerIds.Move ? value : null;
            me.BlockHeld = action == BrawlerIds.Block;
            switch (action)
            {
                case BrawlerIds.Jab: arena.Press(fighterIndex, FighterPress.Jab); break;
                case BrawlerIds.Hook: arena.Press(fighterIndex, FighterPress.Hook); break;
                case BrawlerIds.Evade: arena.Press(fighterIndex, FighterPress.Dash, arena.EvadeDirection(fighterIndex)); break;
            }
            me.Move = heldDirection != null ? arena.Relative(fighterIndex, heldDirection) : Vector2.zero;

            LastInput = string.IsNullOrEmpty(value) ? action : action + " " + value;
            InputApplied?.Invoke(LastInput);
        }

        public bool IsActionAvailable(string actionName)
        {
            if (match == null || match.Arena == null) return true;
            return actionName != BrawlerIds.Evade || match.Arena.Fighters[fighterIndex].CanDash;
        }
    }
}
