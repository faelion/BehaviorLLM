using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// Every number that decides how a dodgeball match plays, in one asset, so a human and the
    /// machine are always held to the same rules and a balance change is one edit.
    /// </summary>
    [CreateAssetMenu(fileName = "DodgeballRules", menuName = "BehaviorLLM/Samples/Dodgeball Rules", order = 200)]
    public class DodgeballRules : ScriptableObject
    {
        [Header("Court")]
        [Tooltip("Length of the court from the human's back wall to the machine's, in metres. Each " +
                 "player owns one half and cannot cross the center line.")]
        public float courtLength = 16f;

        [Tooltip("Width of the court from the bottom wall to the top wall, in metres.")]
        public float courtWidth = 9f;

        [Tooltip("How many balls are in play. Three keeps both players busy without one player " +
                 "being able to hoard them all.")]
        [Range(1, 5)] public int ballCount = 3;

        [Tooltip("How far behind the center line a player must be to throw, in metres. Without it " +
                 "both players rush the balls on the center line and whoever touches one first " +
                 "throws from a couple of metres, which nobody can react to; at 3 m every throw is " +
                 "in the air for at least about 0.4 s.")]
        public float throwingLine = 3f;

        [Header("Players")]
        [Tooltip("A player's radius in metres. A ball that passes this close to a player's center, " +
                 "plus its own radius, hits them.")]
        public float playerRadius = 0.45f;

        [Tooltip("Running speed in metres per second.")]
        public float moveSpeed = 5f;

        [Tooltip("How far a dodge carries a player, in metres.")]
        public float dodgeDistance = 2.6f;

        [Tooltip("How long a dodge takes, in seconds. The distance is covered evenly over this time.")]
        public float dodgeDuration = 0.18f;

        [Tooltip("Seconds after a dodge before the next one is allowed.")]
        public float dodgeCooldown = 1.2f;

        [Header("Balls")]
        [Tooltip("A ball's radius in metres.")]
        public float ballRadius = 0.25f;

        [Tooltip("Speed of a thrown ball in metres per second. At 15 a throw across the court " +
                 "takes about half a second: long enough to react to, short enough to need it.")]
        public float throwSpeed = 15f;

        [Tooltip("How far ahead of the opponent a leading throw aims, in metres, toward the top or " +
                 "the bottom wall.")]
        public float leadDistance = 1.8f;

        [Header("Catching")]
        [Tooltip("How long a catch stays open after pressing it, in seconds. A ball that arrives " +
                 "inside the window is caught and the thrower loses the point; one that arrives " +
                 "after it hits. Short on purpose: the catch is a timing test.")]
        public float catchWindow = 0.35f;

        [Tooltip("Seconds after a catch window closes before another can be opened.")]
        public float catchCooldown = 1.0f;

        [Tooltip("Movement speed while a catch is open, as a fraction of the running speed.")]
        [Range(0f, 1f)] public float catchMoveFactor = 0.4f;

        [Header("Match")]
        [Tooltip("Points needed to win the match. A hit scores for the thrower, a catch for the catcher.")]
        public int pointsToWin = 7;

        [Tooltip("Seconds of the serve countdown, with both players back in place and nobody able to " +
                 "move, before the balls go live. It follows a short freeze on the play that won the point.")]
        public float pointPause = 1.2f;

        [Header("Machine")]
        [Tooltip("Extra delay, in seconds, before the machine's choice takes effect. 0 lets it play " +
                 "at the speed of the model. Around 0.15 makes its total reaction time roughly " +
                 "human (decision plus delay of about 0.25 to 0.3 s), so the match measures choices " +
                 "rather than speed.")]
        [Range(0f, 0.5f)] public float machineReactionDelay = 0f;

        /// <summary>Half the court's length: the x of each back wall.</summary>
        public float HalfLength => courtLength * 0.5f;

        /// <summary>Half the court's width: the y of the top wall.</summary>
        public float HalfWidth => courtWidth * 0.5f;
    }
}
