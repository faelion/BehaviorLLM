namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// The machine fighter's controls, in one place so the ActionConfig asset, the bindings, the
    /// availability provider and the pad cannot drift apart. These strings are what the model is
    /// offered, so they must stay identifier-shaped.
    ///
    /// They are a gamepad, not a set of moves: there is no "punish" or "counter". The model holds a
    /// direction and presses buttons, as a person would, and the fight comes out of the timing.
    /// Directions are relative to the opponent, because that is the frame a fighter thinks in and
    /// it keeps the menu the same size wherever the two of them are.
    /// </summary>
    public static class BrawlerIds
    {
        // ---------------------------------------------------------------- actions

        /// <summary>Hold a direction until the next decision.</summary>
        public const string Move = "Move";

        /// <summary>Let go of the stick and every button.</summary>
        public const string Stand = "Stand";

        /// <summary>A fast, short punch.</summary>
        public const string Jab = "Jab";

        /// <summary>A slow, long punch that breaks a block. Its wind-up is easy to see.</summary>
        public const string Hook = "Hook";

        /// <summary>Hold a guard until the next decision.</summary>
        public const string Block = "Block";

        /// <summary>
        /// A roll out of the line of the opponent's punch, untouchable for most of it, aimed by
        /// the game: sideways, a little back, toward whichever side has more room. Offered only
        /// while ready.
        ///
        /// The one place the machine has less freedom than the human, who dashes wherever the stick
        /// points. With four dash directions on the menu, replayed fight states showed LiquidAI d1
        /// pick the first-listed one, Dash Toward, for most incoming hooks: straight into the punch.
        /// </summary>
        public const string Evade = "Evade";

        public const string DirectionParameter = "direction";

        // ---------------------------------------------------------------- directions

        public const string Toward = "Toward";
        public const string Away = "Away";
        public const string CircleLeft = "CircleLeft";
        public const string CircleRight = "CircleRight";

        /// <summary>Every direction Move accepts, in the order they are offered.</summary>
        public static readonly string[] Directions = { Toward, Away, CircleLeft, CircleRight };

        // ---------------------------------------------------------------- names

        public const string HumanName = "Human";
        public const string MachineName = "Machine";
    }
}
