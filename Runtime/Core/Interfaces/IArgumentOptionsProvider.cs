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
        /// Appends the valid values for one parameter of <paramref name="actionName"/> to
        /// <paramref name="options"/>. Return false (or an empty list) to use the parameter's
        /// authored values. If those are also empty, any identifier is accepted. Use an
        /// availability provider to withhold actions that currently have no valid targets.
        ///
        /// <paramref name="parameterName"/> lets one provider serve an action that takes several
        /// values, for example a destination drawn from the map and a speed drawn from a fixed
        /// set. A provider that only handles single-argument actions can ignore it.
        /// </summary>
        bool TryGetArgumentOptions(string actionName, string parameterName, List<string> options);
    }
}
