namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Every name the model can emit, in one place so the action configs, the bindings, the
    /// availability providers and the executors cannot drift apart. These strings travel in the
    /// JSON Schema, so they must stay identifier-shaped: letters, digits and underscore only.
    /// </summary>
    public static class PrisonYardIds
    {
        // ---------------------------------------------------------------- guard actions
        public const string HoldPosition = "HoldPosition";
        public const string Patrol = "Patrol";
        public const string Respond = "Respond";
        public const string Escort = "Escort";
        public const string Search = "Search";
        public const string Report = "Report";
        public const string Retreat = "Retreat";

        // ---------------------------------------------------------------- prisoner actions
        public const string FollowSchedule = "FollowSchedule";
        public const string Wander = "Wander";
        public const string Talk = "Talk";
        public const string Fight = "Fight";
        public const string Hide = "Hide";
        public const string Sneak = "Sneak";
        public const string Comply = "Comply";

        // ---------------------------------------------------------------- warden actions
        public const string Observe = "Observe";
        public const string Lockdown = "Lockdown";
        public const string LiftLockdown = "LiftLockdown";
        public const string Reinforce = "Reinforce";
        public const string Announce = "Announce";

        // ---------------------------------------------------------------- announcements
        // These are the only arguments in the sample that come from a typed list rather than
        // from the scene, because they are a fixed vocabulary rather than a set of objects.
        public const string ReturnToCells = "ReturnToCells";
        public const string StayCalm = "StayCalm";
        public const string MealTime = "MealTime";
        public const string YardTime = "YardTime";

        // ---------------------------------------------------------------- object names
        public const string Guard01 = "Guard_01";
        public const string Guard02 = "Guard_02";
        public const string Guard03 = "Guard_03";
        public const string Prisoner01 = "Prisoner_01";
        public const string Prisoner02 = "Prisoner_02";
        public const string Prisoner03 = "Prisoner_03";
        public const string Prisoner04 = "Prisoner_04";
        public const string Warden = "Warden";

        // ---------------------------------------------------------------- context object types
        public const string TypeGuard = "Guard";
        public const string TypePrisoner = "Prisoner";
        public const string TypeWarden = "Warden";
    }
}
