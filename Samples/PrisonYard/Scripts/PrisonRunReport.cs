using System.Collections.Generic;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Telemetry;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Adds the prison's own numbers to the telemetry summary the package writes at the end of a
    /// run, so a run can be judged on whether the prison worked rather than only on whether the
    /// model answered.
    ///
    /// The package records latency, tokens, parse failures and fallbacks. What it cannot know is
    /// what those decisions were worth: how many incidents opened, how many were dealt with, and
    /// how long a fight ran before a guard turned up. That is what this adds.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Run Report")]
    [RequireComponent(typeof(DecisionTelemetryRecorder))]
    public class PrisonRunReport : MonoBehaviour
    {
        private DecisionTelemetryRecorder recorder;
        private PrisonStatusBoard board;
        private PrisonRadio radio;
        private WardenExecutor warden;

        // Incident open time, per section, so a guard arriving can be timed against it.
        private readonly Dictionary<PrisonSection, float> incidentOpenedAt = new Dictionary<PrisonSection, float>();
        private readonly List<float> responseTimes = new List<float>();
        private int lastIncidentCount;

        private void Awake()
        {
            recorder = GetComponent<DecisionTelemetryRecorder>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
            warden = FindFirstObjectByType<WardenExecutor>();
        }

        private void Update()
        {
            if (board == null) return;

            // Note when trouble starts in a section, and how long it took somebody to reach it.
            var open = board.OpenIncidents;
            for (int i = 0; i < open.Count; i++)
            {
                if (!incidentOpenedAt.ContainsKey(open[i].Section))
                    incidentOpenedAt[open[i].Section] = open[i].OpenedAt;
            }

            if (open.Count < lastIncidentCount)
            {
                // Something closed since the last frame. Time whichever sections are no longer open.
                List<PrisonSection> stillOpen = new List<PrisonSection>();
                for (int i = 0; i < open.Count; i++) stillOpen.Add(open[i].Section);

                List<PrisonSection> closed = new List<PrisonSection>();
                foreach (KeyValuePair<PrisonSection, float> entry in incidentOpenedAt)
                {
                    if (!stillOpen.Contains(entry.Key)) closed.Add(entry.Key);
                }
                for (int i = 0; i < closed.Count; i++)
                {
                    responseTimes.Add(Time.time - incidentOpenedAt[closed[i]]);
                    incidentOpenedAt.Remove(closed[i]);
                }
            }
            lastIncidentCount = open.Count;

            // Keep the summary fields current rather than writing them once at the end. Unity does
            // not promise which component's OnApplicationQuit runs first, and the recorder finalised
            // its report before this one had written anything, so the prison's columns were missing
            // from the CSV entirely.
            if (Time.time < nextPublish) return;
            nextPublish = Time.time + 1f;
            Publish();
        }

        private float nextPublish;

        private void OnApplicationQuit()
        {
            WriteReport();
        }

        private void OnDestroy()
        {
            WriteReport();
        }

        /// <summary>
        /// Adds the prison's columns to the run summary. Called every second while the run is going
        /// and once more on the way out, so the numbers are in place whenever the recorder decides
        /// to write its report.
        /// </summary>
        public void WriteReport()
        {
            Publish();
        }

        private void Publish()
        {
            if (recorder == null) return;

            if (board != null)
            {
                recorder.SetExtraSummaryField("incidents_opened", board.IncidentsOpened);
                recorder.SetExtraSummaryField("incidents_resolved", board.IncidentsResolved);
            }
            if (warden != null)
            {
                recorder.SetExtraSummaryField("lockdowns_ordered", warden.LockdownsOrdered);
            }
            recorder.SetExtraSummaryField("escape_attempts", CountEscapeAttempts());
            recorder.SetExtraSummaryField("radio_messages", radio != null ? radio.Messages.Count : 0);
            recorder.SetExtraSummaryField("mean_incident_seconds", MeanResponseSeconds());
        }

        private int CountEscapeAttempts()
        {
            int total = 0;
            PrisonerExecutor[] prisoners = FindObjectsByType<PrisonerExecutor>(FindObjectsSortMode.None);
            for (int i = 0; i < prisoners.Length; i++) total += prisoners[i].EscapeAttempts;
            return total;
        }

        private float MeanResponseSeconds()
        {
            if (responseTimes.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < responseTimes.Count; i++) sum += responseTimes[i];
            return sum / responseTimes.Count;
        }
    }
}
