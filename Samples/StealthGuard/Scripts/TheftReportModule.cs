using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// The guard's radio. Something else in the game noticed a theft and told this guard where.
    ///
    /// This is the shape of the answer whenever a game already knows something the model should
    /// act on: not a prompt rewrite, but an observation module. The report arrives as one line in
    /// the STATE block, named after a place the guard can already be sent to, so the model can act
    /// on it with the vocabulary it already has - and it still gets to decide whether to. A guard
    /// that is chasing someone may reasonably ignore a radio call, and you can watch it do that.
    ///
    /// <see cref="HasInterrupt"/> fires once per report so the guard re-thinks the moment the call
    /// comes in rather than waiting out the rest of its decision interval.
    /// </summary>
    [AddComponentMenu("StealthGuard/Theft Report Module")]
    public class TheftReportModule : MonoBehaviour, IObservationModule
    {
        [Tooltip("Heading this appears under in the prompt. Keep it short: it is repeated in every " +
                 "prompt that carries a live report.")]
        public string topicName = "Radio";

        [Tooltip("How long a report stays in the prompt, in seconds. After this it drops out and the " +
                 "guard goes back to its own business, which is what stops one theft from pinning a " +
                 "guard to one corner for the whole run.")]
        public float reportLifetimeSeconds = 45f;

        [Tooltip("Make the guard re-think immediately when a report comes in, instead of waiting for " +
                 "its next scheduled decision. Turn this off to see how much slower it feels.")]
        public bool interruptOnReport = true;

        [Tooltip("Read-only: the place named in the live report, or empty when there is none.")]
        [SerializeField] private string reportedPlace = "";

        private float reportedAt = float.NegativeInfinity;
        private Vector3 reportedPosition;
        private bool interruptPending;

        /// <summary>True while a report is live and has not yet expired.</summary>
        public bool HasReport =>
            !string.IsNullOrEmpty(reportedPlace) &&
            Time.time - reportedAt <= Mathf.Max(0f, reportLifetimeSeconds);

        /// <summary>The place named in the live report, or an empty string when there is none.</summary>
        public string ReportedPlace => HasReport ? reportedPlace : "";

        /// <summary>Where the theft happened. Only meaningful while <see cref="HasReport"/> is true.</summary>
        public Vector3 ReportedPosition => reportedPosition;

        /// <summary>
        /// Radios this guard. <paramref name="place"/> should be the id of a marker the guard can
        /// be sent to, so that naming it in the prompt gives the model something it can act on.
        /// </summary>
        public void Report(string place, Vector3 position)
        {
            reportedPlace = place;
            reportedPosition = position;
            reportedAt = Time.time;
            if (interruptOnReport) interruptPending = true;
        }

        /// <summary>Drops the live report, as if the guard had cleared the call.</summary>
        public void Clear()
        {
            reportedPlace = "";
            reportedAt = float.NegativeInfinity;
        }

        public string TopicName => topicName;

        public bool HasInterrupt()
        {
            if (!interruptPending) return false;
            interruptPending = false;
            return true;
        }

        public string GetObservation()
        {
            // Empty means "nothing to say", and the composer leaves the whole topic out of the
            // prompt. A guard with no live call should not be paying for a "Radio: nothing" line.
            if (!HasReport) return "";

            int seconds = Mathf.RoundToInt(Time.time - reportedAt);
            return $"Supplies were stolen near {reportedPlace} about {seconds}s ago. You are the closest guard.";
        }
    }
}
