using System.Collections.Generic;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Supplies the argument values for each action from the markers actually present in the
    /// scene, so the model can only name a place that exists.
    ///
    /// Without this, `arg` would be any identifier-shaped string and the model would happily
    /// invent "NorthGate" or "Route_A". With it, those values are not in the schema, so they
    /// cannot be sampled at all: no repair step, no fallback, no wasted decision.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Argument Options")]
    public class GuardArgumentOptions : MonoBehaviour, IArgumentOptionsProvider
    {
        [Tooltip("The intruder the guard is allowed to chase. Its LLM Context Object name is the only " +
                 "value the model may pass to Chase.")]
        public LLMContextObjectReference intruder;

        [Tooltip("This guard's beat: the ids of the patrol routes it may be sent to. Leave the list " +
                 "empty and it may use every route in the scene. Filling it in is what stops three " +
                 "guards independently picking the same route and standing in the same corner - the " +
                 "options are per guard, so the same Patrol action means different places for each.")]
        public List<string> patrolBeat = new List<string>();

        [System.Serializable]
        public class LLMContextObjectReference
        {
            [Tooltip("Object the guard may chase. Its name below is what the model emits.")]
            public Transform target;

            [Tooltip("Identifier the model uses for that object, e.g. Intruder. Letters, digits and " +
                     "underscore only.")]
            public string id = "Intruder";
        }

        private StealthGuardMarker[] markers;

        private void Awake()
        {
            RefreshMarkers();
        }

        /// <summary>Re-reads the scene's markers. Call after spawning or removing one at runtime.</summary>
        public void RefreshMarkers()
        {
            markers = FindObjectsByType<StealthGuardMarker>(FindObjectsSortMode.None);
        }

        public bool TryGetArgumentOptions(string actionName, List<string> options)
        {
            switch (actionName)
            {
                case StealthGuardIds.Patrol:
                    return Collect(MarkerKind.PatrolRoute, options, patrolBeat);
                case StealthGuardIds.Investigate:
                    // Deliberately not restricted: a guard has to be able to answer a radio call
                    // about anywhere in the compound, whatever its own beat is.
                    return Collect(MarkerKind.InvestigateLocation, options, null);
                case StealthGuardIds.Retreat:
                    return Collect(MarkerKind.SafeZone, options, null);
                case StealthGuardIds.Chase:
                    if (intruder == null || string.IsNullOrWhiteSpace(intruder.id)) return false;
                    options.Add(intruder.id);
                    return true;
                default:
                    // Returning false leaves the action unconstrained, which is the right default
                    // for an action this provider knows nothing about.
                    return false;
            }
        }

        /// <summary>
        /// Every marker of a kind, optionally narrowed to an allow-list. A narrowing that matches
        /// nothing is ignored rather than obeyed: an empty option list would drop the action from
        /// the schema entirely, and a typo in a beat should not silently take Patrol off the menu.
        /// </summary>
        private bool Collect(MarkerKind kind, List<string> options, List<string> only)
        {
            if (markers == null) RefreshMarkers();
            bool narrow = only != null && only.Count > 0;

            for (int i = 0; i < markers.Length; i++)
            {
                StealthGuardMarker m = markers[i];
                if (m == null || m.kind != kind || string.IsNullOrWhiteSpace(m.id)) continue;
                if (narrow && !Allows(only, m.id)) continue;
                if (!options.Contains(m.id)) options.Add(m.id);
            }

            if (options.Count > 0) return true;
            return narrow && Collect(kind, options, null);
        }

        private static bool Allows(List<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (string.Equals(ids[i], id, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Finds the marker with an id, so the executor can turn an argument into a place.</summary>
        public Transform FindMarker(string id)
        {
            if (markers == null) RefreshMarkers();
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && string.Equals(markers[i].id, id, System.StringComparison.OrdinalIgnoreCase))
                    return markers[i].transform;
            }
            return null;
        }
    }
}
