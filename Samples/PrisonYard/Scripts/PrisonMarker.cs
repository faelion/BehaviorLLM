using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>What a named place in the prison is for. Each kind feeds a different action's argument list.</summary>
    public enum PrisonMarkerKind
    {
        /// <summary>The centre of a section, where Respond and Wander send someone.</summary>
        SectionCentre,
        /// <summary>A spot a guard patrols to.</summary>
        PatrolPoint,
        /// <summary>A prisoner's bunk in the cell block.</summary>
        Cell,
        /// <summary>Somewhere a prisoner can stash contraband out of sight.</summary>
        HidingSpot,
        /// <summary>An infirmary bed, where the hurt recover.</summary>
        Bed
    }

    /// <summary>
    /// A named place someone can be sent to. The <see cref="id"/> is what the model emits as an
    /// action argument, so it must be a plain identifier. Adding a marker to the scene adds it to
    /// the model's vocabulary on the next schema rebuild, with no code change.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Marker")]
    public class PrisonMarker : MonoBehaviour
    {
        [Tooltip("Identifier the AI uses for this place, for example Yard_Patrol_N. Letters, digits " +
                 "and underscore only: anything else is dropped when the argument list is built, and " +
                 "the AI would never be able to name this marker.")]
        public string id = "Marker";

        [Tooltip("What this place is for. A marker only appears in the argument list of the matching " +
                 "action, so a hiding spot is never offered as a patrol route.")]
        public PrisonMarkerKind kind = PrisonMarkerKind.PatrolPoint;

        [Tooltip("Which section this place is in. Used to offer a guard only the patrol points of " +
                 "the section it is assigned to, and a prisoner only the hiding spots within reach.")]
        public PrisonSection section = PrisonSection.Yard;
    }
}
