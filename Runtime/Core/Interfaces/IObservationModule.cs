namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>
    /// Interface for any component that provides a textual description of the game state.
    /// Decision makers will collect all observations from these modules to form the prompt.
    /// </summary>
    public interface IObservationModule
    {
        /// <summary>
        /// Returns a formatted string describing what this module observes.
        /// Return empty or null if there is nothing relevant to report.
        /// </summary>
        string GetObservation();

        /// <summary>
        /// Header or "topic" of this observation, useful for structuring the prompt.
        /// Example: "Visuals", "Self-Status", "Zone".
        /// </summary>
        string TopicName { get; }

        /// <summary>
        /// Returns true if this module detects something critical that should force the decision maker to re-think immediately.
        /// e.g. "Just saw an enemy", "Health dropped below 20%".
        /// </summary>
        bool HasInterrupt();
    }

    /// <summary>
    /// Which prompt budget applies to a budgeted observation.
    /// </summary>
    public enum ObservationKind
    {
        Other = 0,
        Vision = 1,
        Memory = 2
    }

    /// <summary>
    /// Optional companion to <see cref="IObservationModule"/> for modules whose output is a
    /// list that can be truncated to fit a prompt budget. The decision maker asks for at most
    /// <c>maxEntries</c> entries (the module decides which ones matter most, e.g. nearest
    /// objects, most recent memories).
    /// </summary>
    public interface IBudgetedObservation
    {
        ObservationKind Kind { get; }
        string GetObservation(int maxEntries);
    }
}
