namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>
    /// Optional post-parse hook that normalises or rejects the argument the model chose for an
    /// action before it is dispatched. Prefer <see cref="IArgumentOptionsProvider"/> when the
    /// valid arguments are known up front: constraining decoding is cheaper than repairing.
    /// </summary>
    public interface IActionArgumentPolicy
    {
        /// <summary>
        /// Returns true and a normalised argument when the argument is acceptable; false with a
        /// reason when it must be rejected, after which the fallback action runs.
        /// </summary>
        bool TryNormalizeArgument(string actionName, string rawArgument, out string normalizedArgument, out string rejectionReason);
    }
}
