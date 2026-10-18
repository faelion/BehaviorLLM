using System.Globalization;
using System.Text;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// What the machine fighter knows about the fight, written the way it can act on it: a punch
    /// coming at it first, with when it lands and whether it will reach; then the distance in terms
    /// of its own reach; then what each fighter is doing. Nothing is decided for it.
    ///
    /// It raises an interrupt the moment the opponent starts a punch or a dash, which cancels
    /// whatever decision is in flight and asks again with the wind-up in view. That is the reaction
    /// the sample measures.
    ///
    /// The wording avoids the words of the menu ("toward", "away", "circle") on purpose: replayed
    /// in the Dodgeball sample, a single "move back" in the text made the model answer Move Back to
    /// nearly everything.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Fight Observation")]
    public class FightObservationModule : MonoBehaviour, IObservationModule
    {
        [Tooltip("The match whose fight this fighter sees.")]
        public BrawlerMatch match;

        [Tooltip("Which fighter this is: 0 is the human's, 1 the machine's.")]
        public int fighterIndex = 1;

        public string TopicName => "Fight";

        public string GetObservation()
        {
            if (match == null || match.Arena == null) return "The fight has not started.";
            return Describe(match.Arena, fighterIndex, match.RoundsWon[fighterIndex], match.RoundsWon[1 - fighterIndex],
                            match.Round, match.RoundTimeLeft, match.InputDelaySeconds(fighterIndex));
        }

        public bool HasInterrupt() => match != null && match.ConsumeAlert(fighterIndex);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The observation for fighter <paramref name="self"/>, as plain text.</summary>
        public static string Describe(BrawlerArena arena, int self, int myRounds, int theirRounds, int round,
                                      float timeLeft, float inputDelay = 0f)
        {
            BrawlerRules r = arena.Rules;
            BrawlerFighter me = arena.Fighters[self];
            BrawlerFighter them = arena.Opponent(self);
            StringBuilder sb = new StringBuilder(512);
            float distance = Vector2.Distance(me.Position, them.Position);

            float impact = arena.TimeToImpact(them);
            if (impact >= 0f)
            {
                bool hook = them.State == FighterState.Hook;
                float range = hook ? r.hook.range : r.jab.range;
                sb.Append("INCOMING: the opponent's ").Append(hook ? "slow swing" : "quick strike").Append(" lands in ").Append(Seconds(impact));
                if (inputDelay > 0f)
                    sb.Append(impact > inputDelay ? ", " + Seconds(impact - inputDelay) + " after your next input lands" : ", before your next input can land");
                sb.Append(arena.InReach(them, me, range) ? ". You are inside its reach.\n" : ". You are outside its reach.\n");
            }

            sb.Append("Distance: ").Append(Metres(distance)).Append(". ");
            if (distance <= r.jab.range) sb.Append("Your jab and your hook both reach.\n");
            else if (distance <= r.hook.range) sb.Append("Your hook reaches, your jab does not.\n");
            else sb.Append("Out of reach of either punch.\n");

            sb.Append("Opponent: ").Append(StateOf(arena, them, me, true)).Append(", health ")
              .Append(Mathf.CeilToInt(them.Health)).Append(" of ").Append(Mathf.RoundToInt(r.health)).Append(".\n");
            sb.Append("You: ").Append(StateOf(arena, me, them, false)).Append(", health ")
              .Append(Mathf.CeilToInt(me.Health)).Append(" of ").Append(Mathf.RoundToInt(r.health)).Append(". ")
              .Append(me.CanDash ? "Evade ready." : "Evade recovering.").Append('\n');

            // The arena's edge only matters behind a fighter: it is where a retreat ends.
            Vector2 behind = (me.Position - them.Position).normalized;
            float edge = DistanceToEdge(r, me.Position, behind);
            if (edge < 1.5f) sb.Append("The arena's edge is ").Append(Metres(edge)).Append(" behind you.\n");

            sb.Append("Round ").Append(round).Append(", first to ").Append(r.roundsToWin).Append(" rounds: you ")
              .Append(myRounds).Append(", opponent ").Append(theirRounds).Append(". ")
              .Append(Mathf.CeilToInt(Mathf.Max(0f, timeLeft))).Append(" s left.");
            return sb.ToString();
        }

        private static string StateOf(BrawlerArena arena, BrawlerFighter f, BrawlerFighter other, bool opponent)
        {
            switch (f.State)
            {
                case FighterState.Jab:
                case FighterState.Hook:
                    // The opponent's punches are named apart from the fighter's own buttons: with
                    // "their hook" in the text, LiquidAI d1 answered Hook to an incoming hook, the
                    // word on the menu that matched the word in the situation.
                    string punch = opponent
                        ? (f.State == FighterState.Hook ? "a slow swing" : "a quick strike")
                        : (f.State == FighterState.Hook ? "your hook" : "your jab");
                    switch (arena.PhaseOf(f))
                    {
                        case AttackPhase.Windup: return "winding up " + punch;
                        case AttackPhase.Active: return opponent ? "swinging" : "throwing " + punch;
                        default:
                            return "recovering from " + punch + " for " + Seconds(f.TimeLeft) +
                                   (opponent ? ", open to a punch" : ", unable to act");
                    }
                case FighterState.Dash: return "rolling";
                case FighterState.HitStun: return "stunned for " + Seconds(f.TimeLeft);
                case FighterState.GuardBroken: return "guard broken, stunned for " + Seconds(f.TimeLeft);
                case FighterState.KO: return "knocked out";
            }

            if (f.Blocking) return "blocking";
            if (f.Velocity.magnitude < 0.5f) return opponent ? "standing" : "standing, free to act";
            Vector2 to = (other.Position - f.Position).normalized;
            float along = Vector2.Dot(f.Velocity.normalized, to);
            string motion = along > 0.5f ? "closing in" : along < -0.5f ? "backing off" : "stepping sideways";
            return opponent ? motion : motion + ", free to act";
        }

        private static float DistanceToEdge(BrawlerRules r, Vector2 p, Vector2 dir)
        {
            float R = r.arenaRadius - r.fighterRadius;
            float b = Vector2.Dot(p, dir);
            float c = p.sqrMagnitude - R * R;
            return Mathf.Max(0f, -b + Mathf.Sqrt(Mathf.Max(0f, b * b - c)));
        }

        private static string Metres(float m) => Mathf.Max(0f, m).ToString("0.0", Inv) + " m";
        private static string Seconds(float s) => Mathf.Max(0f, s).ToString("0.00", Inv) + " s";
    }
}
