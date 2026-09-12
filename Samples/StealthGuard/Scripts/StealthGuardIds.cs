namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// The action names this sample uses, in one place so the ActionConfig asset, the bindings,
    /// the availability provider and the executor cannot drift apart. These strings are what the
    /// model emits, so they must stay identifier-shaped.
    /// </summary>
    public static class StealthGuardIds
    {
        public const string HoldPosition = "HoldPosition";
        public const string Patrol = "Patrol";
        public const string Investigate = "Investigate";
        public const string Chase = "Chase";
        public const string Retreat = "Retreat";

        public const string IntruderName = "Intruder";
        public const string ExitName = "Exit";

        /// <summary>How many guards the scene builds. Three is enough to make one of them being
        /// somewhere else matter, and few enough to read every decision as it happens.</summary>
        public const int GuardCount = 3;

        /// <summary>The name of guard <paramref name="index"/>, counting from zero.</summary>
        public static string GuardName(int index) => $"Guard_{index + 1:00}";
    }
}
