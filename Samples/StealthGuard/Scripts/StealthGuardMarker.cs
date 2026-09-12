using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// What a marker in the scene is for. The guard's argument options are derived from these,
    /// so adding a waypoint to the scene adds it to the model's vocabulary with no code change.
    /// </summary>
    public enum MarkerKind
    {
        PatrolRoute,
        InvestigateLocation,
        SafeZone
    }

    /// <summary>
    /// A named place the guard can be sent to. The <see cref="id"/> is what the model actually
    /// emits as the action's argument, so it must be a plain identifier: letters, digits and
    /// underscore only.
    /// </summary>
    [AddComponentMenu("StealthGuard/Stealth Guard Marker")]
    public class StealthGuardMarker : MonoBehaviour
    {
        [Tooltip("Identifier the model uses for this place, e.g. Route_North. Letters, digits and " +
                 "underscore only: anything else is dropped when the argument list is built, and the " +
                 "model would never be able to name this marker.")]
        public string id = "Marker";

        [Tooltip("Which action can send the guard here. A marker only appears in the argument list of " +
                 "the matching action, so a safe zone is never offered as a patrol route.")]
        public MarkerKind kind = MarkerKind.PatrolRoute;
    }
}
