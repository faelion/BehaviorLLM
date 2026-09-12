using UnityEngine;
using UnityEngine.AI;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Turns the model's chosen action into movement. Every public method here is bound to one
    /// action name in the agent's Action Bindings, and receives that action's argument as its
    /// string parameter.
    ///
    /// Note how little this knows: it never talks to the model, never sees a prompt, and would
    /// work unchanged behind a behaviour tree. That separation is the point of the package.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Executor")]
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardExecutor : MonoBehaviour
    {
        [Tooltip("Supplies the markers this guard can be sent to, and the intruder it may chase. " +
                 "Auto-found on this GameObject when left empty.")]
        public GuardArgumentOptions options;

        [Tooltip("How close the guard has to get before a destination counts as reached.")]
        public float arrivalDistance = 1.5f;

        [Tooltip("How far to one side of a marker this guard stands, in metres. Three guards sent " +
                 "to the same place would otherwise walk into the same point and end up inside one " +
                 "another; each takes its own spot around it instead. Zero puts it dead centre.")]
        public float markerOffset = 1.4f;

        [Tooltip("Which way round the marker this guard stands, in degrees. The scene builder gives " +
                 "each guard a different angle so their spots never coincide.")]
        public float markerOffsetAngle;

        [Tooltip("While chasing, re-target the intruder this often (seconds), so the guard follows a " +
                 "moving target instead of walking to where it first saw it.")]
        public float chaseRetargetInterval = 0.25f;

        [Tooltip("Read-only: what the guard is doing right now. Shown to the model as part of its " +
                 "self-status, so its own last decision is part of the next prompt.")]
        public string currentActivity = "Idle";

        private NavMeshAgent nav;
        private Transform chaseTarget;
        private float nextRetargetTime;

        private void Awake()
        {
            nav = GetComponent<NavMeshAgent>();
            if (options == null) options = GetComponent<GuardArgumentOptions>();
        }

        private void Update()
        {
            if (chaseTarget == null) return;
            if (Time.time < nextRetargetTime) return;
            nextRetargetTime = Time.time + Mathf.Max(0.05f, chaseRetargetInterval);
            if (IsNavReady) nav.SetDestination(chaseTarget.position);
        }

        private bool IsNavReady => nav != null && nav.enabled && nav.isOnNavMesh;

        /// <summary>
        /// This guard's own spot at a marker: a short step out from the centre, in its own
        /// direction. Falls back to the marker itself when the offset would land off the NavMesh,
        /// so a marker in a tight corner still works.
        /// </summary>
        private Vector3 StandingSpot(Vector3 markerPosition)
        {
            if (markerOffset <= 0.01f) return markerPosition;

            Vector3 offset = Quaternion.Euler(0f, markerOffsetAngle, 0f) * Vector3.forward * markerOffset;
            Vector3 spot = markerPosition + offset;
            return NavMesh.SamplePosition(spot, out NavMeshHit hit, markerOffset, NavMesh.AllAreas)
                ? hit.position
                : markerPosition;
        }

        // ---------------------------------------------------------------- bound actions

        /// <summary>Bound to HoldPosition. The argument is always empty.</summary>
        public void OnHoldPosition(string _)
        {
            chaseTarget = null;
            if (IsNavReady) nav.ResetPath();
            currentActivity = "Holding position";
        }

        /// <summary>Bound to Patrol. The argument is a patrol route marker id.</summary>
        public void OnPatrol(string routeId)
        {
            chaseTarget = null;
            if (GoTo(routeId)) currentActivity = $"Patrolling to {routeId}";
        }

        /// <summary>Bound to Investigate. The argument is a location marker id.</summary>
        public void OnInvestigate(string locationId)
        {
            chaseTarget = null;
            if (GoTo(locationId)) currentActivity = $"Investigating {locationId}";
        }

        /// <summary>Bound to Retreat. The argument is a safe zone marker id.</summary>
        public void OnRetreat(string safeZoneId)
        {
            chaseTarget = null;
            if (GoTo(safeZoneId)) currentActivity = $"Retreating to {safeZoneId}";
        }

        /// <summary>Bound to Chase. The argument is the intruder's id.</summary>
        public void OnChase(string targetId)
        {
            if (options == null || options.intruder == null || options.intruder.target == null)
            {
                Debug.LogWarning("[GuardExecutor] Chase was chosen but no intruder is assigned.");
                return;
            }
            if (!string.Equals(targetId, options.intruder.id, System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[GuardExecutor] Asked to chase unknown target '{targetId}'.");
                return;
            }

            chaseTarget = options.intruder.target;
            nextRetargetTime = 0f;
            currentActivity = $"Chasing {targetId}";
        }

        // ---------------------------------------------------------------- helpers

        private bool GoTo(string markerId)
        {
            if (!IsNavReady)
            {
                Debug.LogWarning($"[GuardExecutor] '{name}' is not on a NavMesh yet; ignoring move to '{markerId}'.");
                return false;
            }

            Transform marker = options != null ? options.FindMarker(markerId) : null;
            if (marker == null)
            {
                // With argument options wired up this should be unreachable, because the schema only
                // offers ids that exist. It stays as a guard for scenes edited without a rebuild.
                Debug.LogWarning($"[GuardExecutor] No marker named '{markerId}' in the scene.");
                return false;
            }

            nav.SetDestination(StandingSpot(marker.position));
            return true;
        }

        /// <summary>Bound to the guard's LLM Context Object, so the prompt says what it is doing.</summary>
        public string ActivityReport => currentActivity;

        /// <summary>Whether the guard has arrived, for the same reason.</summary>
        public string ArrivalReport
        {
            get
            {
                if (!IsNavReady || !nav.hasPath) return "stationary";
                return nav.remainingDistance <= arrivalDistance ? "arrived" : "moving";
            }
        }
    }
}
