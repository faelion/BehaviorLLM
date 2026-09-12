using System.Collections.Generic;

namespace BehaviorLLM.Core.Interfaces
{
    /// <summary>
    /// Supplies the set of valid argument values for an action at schema-build time, so the
    /// model can only emit one of them (scene-derived IDs such as zone or route names). When
    /// an <see cref="Actions.ActionDefinition"/> also lists static <c>allowedArguments</c>, the
    /// provider's non-empty values win. Values are refreshed each decision and checked again
    /// before dispatch, so callers do not need to rebuild the schema when the options change.
    /// </summary>
    public interface IArgumentOptionsProvider
    {
        /// <summary>
        /// Appends the valid argument values for <paramref name="actionName"/> to
        /// <paramref name="options"/>. Return false (or an empty list) to use the action's static
        /// allowed arguments. If those are also empty, any identifier is accepted. Use an
        /// availability provider to withhold actions that currently have no valid targets.
        /// </summary>
        bool TryGetArgumentOptions(string actionName, List<string> options);
    }
}
