namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// The machine player's controls, in one place so the ActionConfig asset, the bindings, the
    /// availability provider and the pad cannot drift apart. These strings are what the model is
    /// offered, so they must stay identifier-shaped.
    ///
    /// They are a gamepad, not a set of behaviours: there is no "go and get the ball" or "throw at
    /// the human". The model steers the player one input at a time, as a person holding a pad
    /// would, and has to get to a ball, line up a throw and time a catch by itself.
    /// </summary>
    public static class DodgeballIds
    {
        // ---------------------------------------------------------------- actions

        /// <summary>Hold a direction until the next decision.</summary>
        public const string Move = "Move";

        /// <summary>Let go of the stick.</summary>
        public const string Stand = "Stand";

        /// <summary>Charge and throw the ball in the throwing hand. Offered only while one can be thrown.</summary>
        public const string Throw = "Throw";

        /// <summary>Open a short catch window, dropping any balls in hand. Offered only while ready.</summary>
        public const string Catch = "Catch";

        /// <summary>A quick burst sideways or back. Offered only while ready.</summary>
        public const string Dodge = "Dodge";

        // ---------------------------------------------------------------- parameter names

        public const string DirectionParameter = "direction";
        public const string AimParameter = "aim";

        // ---------------------------------------------------------------- directions
        // In the machine's own frame: Forward is toward the center line, Up is toward the top wall
        // of the court as the camera shows it. The human and the machine see the same up.

        public const string Forward = "Forward";
        public const string Back = "Back";
        public const string Up = "Up";
        public const string Down = "Down";
        public const string ForwardUp = "ForwardUp";
        public const string ForwardDown = "ForwardDown";
        public const string BackUp = "BackUp";
        public const string BackDown = "BackDown";

        /// <summary>Every direction Move accepts, in the order they are offered.</summary>
        public static readonly string[] MoveDirections =
            { Forward, Back, Up, Down, ForwardUp, ForwardDown, BackUp, BackDown };

        /// <summary>Every direction Dodge accepts. A dodge forward would only run into the throw.</summary>
        public static readonly string[] DodgeDirections = { Up, Down, Back };

        // ---------------------------------------------------------------- aims
        // Up and Down are across the court, as for Move. The pad charges each throw just enough to
        // reach its aim point with some to spare, so the model chooses where, not how hard.

        /// <summary>At where the opponent is now.</summary>
        public const string AtOpponent = "AtOpponent";

        /// <summary>At where the opponent will be when the ball arrives, if they keep running.</summary>
        public const string AheadOfOpponent = "AheadOfOpponent";

        /// <summary>One metre above the opponent.</summary>
        public const string OffsetUp1 = "OffsetUp1";

        /// <summary>Two metres above the opponent.</summary>
        public const string OffsetUp2 = "OffsetUp2";

        /// <summary>One metre below the opponent.</summary>
        public const string OffsetDown1 = "OffsetDown1";

        /// <summary>Two metres below the opponent.</summary>
        public const string OffsetDown2 = "OffsetDown2";

        /// <summary>Off the top wall, so the bounce comes back at the opponent.</summary>
        public const string BankTopWall = "BankTopWall";

        /// <summary>Off the bottom wall, so the bounce comes back at the opponent.</summary>
        public const string BankBottomWall = "BankBottomWall";

        public static readonly string[] Aims =
            { AtOpponent, AheadOfOpponent, OffsetUp1, OffsetUp2, OffsetDown1, OffsetDown2, BankTopWall, BankBottomWall };

        // ---------------------------------------------------------------- names

        public const string HumanName = "Human";
        public const string MachineName = "Machine";
    }
}
