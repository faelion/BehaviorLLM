namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>
    /// Decides which of the configured actions may be chosen right now. Implement this on a
    /// component next to the <c>DecisionMaker</c> and assign it there to gate the action menu
    /// from game code.
    ///
    /// Prefer this over describing a condition in <c>ActionConfig.modelInstructions</c>. The
    /// JSON Schema is enforced while the model samples, so an action left out here cannot be
    /// produced at all; a rule written in the guide is only a suggestion and small models
    /// follow multi-condition rules unreliably. If the game already knows the answer (health
    /// is low, the door is locked, the ability is on cooldown), remove the action instead of
    /// explaining when not to take it.
    ///
    /// Called once per decision, so keep it cheap: read cached state, do not allocate.
    /// </summary>
    public interface IActionAvailabilityProvider
    {
        /// <summary>
        /// Returns true when <paramref name="actionName"/> may be chosen for this decision.
        /// Names come from the <c>ActionConfig</c>; an unknown name should return true
        /// so that adding an action to the config does not silently disable it.
        /// </summary>
        bool IsActionAvailable(string actionName);
    }
}
