using System;
using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>The timing and reach of one attack.</summary>
    [Serializable]
    public class BrawlerAttack
    {
        [Tooltip("Seconds from the button to the moment the punch can land. This is the tell: the " +
                 "opponent sees the wind-up and has this long to react.")]
        public float windup = 0.15f;

        [Tooltip("Seconds during which the punch can land.")]
        public float active = 0.08f;

        [Tooltip("Seconds after the punch before the attacker can act again. A long recovery is a " +
                 "window for the opponent to punish a miss.")]
        public float recovery = 0.22f;

        [Tooltip("How far the punch reaches, in metres, center to center.")]
        public float range = 1.35f;

        [Tooltip("Damage dealt by a clean hit, out of the health set below.")]
        public float damage = 6f;

        [Tooltip("Seconds the defender is stunned by a clean hit, unable to act.")]
        public float hitStun = 0.3f;

        [Tooltip("How far a clean hit pushes the defender back, in metres.")]
        public float knockback = 0.3f;

        /// <summary>Wind-up, active and recovery together.</summary>
        public float Total => windup + active + recovery;
    }

    /// <summary>
    /// Every number that decides how a fight plays, in one asset, so a human and the machine are
    /// always held to the same rules and a balance change is one edit.
    /// </summary>
    [CreateAssetMenu(fileName = "BrawlerRules", menuName = "BehaviorLLM/Samples/Brawler Rules", order = 201)]
    public class BrawlerRules : ScriptableObject
    {
        [Header("Arena")]
        [Tooltip("Radius of the circular arena in metres. Fighters cannot leave it.")]
        public float arenaRadius = 6f;

        [Tooltip("A fighter's radius in metres. Two fighters never come closer than twice this.")]
        public float fighterRadius = 0.4f;

        [Header("Movement")]
        [Tooltip("Running speed in metres per second.")]
        public float moveSpeed = 3.4f;

        [Tooltip("Movement speed while blocking, as a fraction of the running speed.")]
        [Range(0f, 1f)] public float blockMoveFactor = 0.35f;

        [Header("Attacks")]
        [Tooltip("The jab: fast, short, light. Hard to react to, blocked without harm.")]
        public BrawlerAttack jab = new BrawlerAttack
        {
            windup = 0.15f, active = 0.08f, recovery = 0.22f, range = 1.35f, damage = 6f, hitStun = 0.3f, knockback = 0.3f
        };

        [Tooltip("The hook: slow, long, heavy, and it breaks a block. Its 0.45 s wind-up is long " +
                 "enough to see and answer, and its recovery leaves the attacker open after a miss.")]
        public BrawlerAttack hook = new BrawlerAttack
        {
            windup = 0.45f, active = 0.12f, recovery = 0.5f, range = 1.7f, damage = 16f, hitStun = 0.55f, knockback = 1.2f
        };

        [Tooltip("Half-angle, in degrees, of the arc in front of an attacker that a punch covers. A " +
                 "defender who sidesteps out of it during the wind-up makes the punch miss.")]
        [Range(10f, 90f)] public float attackArc = 50f;

        [Tooltip("Extra damage for hitting an opponent in the middle of their own wind-up, as a " +
                 "fraction. Rewards interrupting a slow hook with a fast jab.")]
        [Range(0f, 1f)] public float counterBonus = 0.25f;

        [Header("Blocking")]
        [Tooltip("Half-angle, in degrees, of the front a block covers.")]
        [Range(30f, 180f)] public float blockArc = 70f;

        [Tooltip("Seconds a hook into a block leaves the blocker stunned with their guard broken.")]
        public float guardBreakStun = 0.7f;

        [Tooltip("Damage a hook does through a block.")]
        public float guardBreakDamage = 4f;

        [Header("Dash")]
        [Tooltip("How far a dash carries a fighter, in metres.")]
        public float dashDistance = 2.8f;

        [Tooltip("How long a dash takes, in seconds.")]
        public float dashDuration = 0.3f;

        [Tooltip("Seconds at the start of a dash during which nothing can hit the dasher.")]
        public float dashInvulnerable = 0.22f;

        [Tooltip("Seconds after a dash before the next one is allowed.")]
        public float dashCooldown = 1.0f;

        [Header("Match")]
        [Tooltip("Health each fighter starts a round with.")]
        public float health = 100f;

        [Tooltip("Seconds in a round. When time runs out the fighter with more health wins it.")]
        public float roundTime = 60f;

        [Tooltip("Rounds needed to win the match.")]
        public int roundsToWin = 3;

        [Tooltip("Seconds between rounds, with both fighters back in their corners.")]
        public float roundPause = 2f;

        [Tooltip("Seconds a pressed button waits for a busy fighter to become free, so a jab pressed " +
                 "near the end of a recovery still comes out.")]
        public float inputBuffer = 0.2f;

        [Header("Machine")]
        [Tooltip("Extra delay, in seconds, before the machine's choice takes effect. 0 lets it fight " +
                 "at the speed of the model. Around 0.1 to 0.15 makes its total reaction time roughly " +
                 "human, so the match measures choices rather than speed.")]
        [Range(0f, 0.5f)] public float machineReactionDelay = 0f;
    }
}
