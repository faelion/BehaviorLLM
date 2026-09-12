using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// The warden's only sense. Where a guard sees a list of nearby objects, the warden reads one
    /// paragraph summarising the whole prison: who is where, what is locked, what is going wrong
    /// and for how long.
    ///
    /// This is the sample's argument that the package is not only for characters. The warden has
    /// no body, no vision and no NavMesh agent; it is a game system that happens to decide with a
    /// language model, and the only thing that makes it different from the guards is which
    /// observation module it carries.
    /// </summary>
    [AddComponentMenu("PrisonYard/Status Board Observation Module")]
    public class StatusBoardObservationModule : MonoBehaviour, IObservationModule
    {
        [Tooltip("Heading this appears under in the text sent to the AI.")]
        public string topicName = "Prison Status";

        [Tooltip("Interrupt the warden's slow decision loop when trouble starts somewhere new, so " +
                 "it does not sit through its twelve-second wait while a fight runs.")]
        public bool interruptOnNewIncident = true;

        private PrisonStatusBoard board;
        private PrisonClock clock;
        private int lastIncidentCount;
        private bool interruptPending;

        public string TopicName => topicName;

        private void Awake()
        {
            board = FindFirstObjectByType<PrisonStatusBoard>();
            clock = FindFirstObjectByType<PrisonClock>();
            if (board != null) lastIncidentCount = board.OpenIncidents.Count;
        }

        private void Update()
        {
            if (!interruptOnNewIncident || board == null) return;

            int now = board.OpenIncidents.Count;
            if (now > lastIncidentCount) interruptPending = true;
            lastIncidentCount = now;
        }

        public bool HasInterrupt()
        {
            if (!interruptPending) return false;
            interruptPending = false;
            return true;
        }

        public string GetObservation()
        {
            return board != null ? board.Render(clock) : "Status board offline.";
        }
    }
}
