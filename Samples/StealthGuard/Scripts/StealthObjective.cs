using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// The point of playing: steal every crate in the compound and carry them out through the gate.
    ///
    /// The sample's real subject is the guards' decision loop, and this exists so that loop has
    /// visible stakes. It also plays the part of the game system that notices things and tells
    /// somebody: when a crate goes, the nearest guard gets a radio call naming the place, and what
    /// that guard does about it is the model's business, not this component's.
    ///
    /// Nothing here talks to the package except through <see cref="TheftReportModule"/>, which is
    /// an ordinary observation module. Delete the whole thing and the scene still demonstrates
    /// everything it demonstrated before, just with no reason to walk anywhere in particular.
    /// </summary>
    [AddComponentMenu("StealthGuard/Stealth Objective")]
    public class StealthObjective : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The player. Found by name in the scene when left empty.")]
        public Transform intruder;

        [Tooltip("Where a thief carrying every crate has to reach to win. Found by name when left empty.")]
        public Transform exit;

        [Tooltip("Turns green once every crate has been taken, to show the gate is now the way out.")]
        public Light exitGlow;

        [Header("Tuning")]
        [Tooltip("How close the thief must get to the gate to escape, in metres.")]
        public float exitRange = 3f;

        [Tooltip("How close a chasing guard has to get to catch the thief, in metres. Only a guard " +
                 "that has actually decided to chase can catch anyone; one that is merely walking " +
                 "past is not a threat.")]
        public float catchRange = 2.2f;

        [Tooltip("Radio the nearest guard when a crate is taken, naming the place it went missing " +
                 "from. Turn this off to see how much longer a theft goes unnoticed when the guards " +
                 "have only their own eyes.")]
        public bool reportThefts = true;

        [Tooltip("Seconds a message stays on screen.")]
        public float messageSeconds = 4f;

        [Tooltip("Colour of the gate light before every crate has been stolen.")]
        public Color lockedColour = new Color(0.9f, 0.25f, 0.2f);

        [Tooltip("Colour of the gate light once the way out is open.")]
        public Color openColour = new Color(0.3f, 0.95f, 0.4f);

        private readonly List<StealthLoot> loot = new List<StealthLoot>();
        private readonly List<GuardExecutor> guards = new List<GuardExecutor>();
        private readonly List<StealthGuardMarker> places = new List<StealthGuardMarker>();
        private int caughtCount;
        private float messageUntil;
        private string message = "";

        /// <summary>True once the thief has carried every crate out through the gate.</summary>
        public bool Escaped { get; private set; }

        /// <summary>How many crates are being carried right now.</summary>
        public int Carried
        {
            get
            {
                int n = 0;
                for (int i = 0; i < loot.Count; i++) if (loot[i].Taken) n++;
                return n;
            }
        }

        private void Awake()
        {
            if (intruder == null)
            {
                GameObject go = GameObject.Find(StealthGuardIds.IntruderName);
                if (go != null) intruder = go.transform;
            }
            if (exit == null)
            {
                GameObject go = GameObject.Find(StealthGuardIds.ExitName);
                if (go != null) exit = go.transform;
            }

            loot.Clear();
            loot.AddRange(FindObjectsByType<StealthLoot>(FindObjectsSortMode.None));

            guards.Clear();
            guards.AddRange(FindObjectsByType<GuardExecutor>(FindObjectsSortMode.None));
            // Stable order, so the on-screen list does not shuffle between runs.
            guards.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            places.Clear();
            foreach (StealthGuardMarker marker in FindObjectsByType<StealthGuardMarker>(FindObjectsSortMode.None))
                if (marker.kind == MarkerKind.InvestigateLocation) places.Add(marker);
        }

        private void Update()
        {
            if (Escaped || intruder == null) return;

            Collect();
            CheckCaught();
            CheckEscape();
            PaintExit();
        }

        private void Collect()
        {
            for (int i = 0; i < loot.Count; i++)
            {
                if (!loot[i].InReach(intruder.position)) continue;

                Vector3 where = loot[i].transform.position;
                loot[i].Take();
                Say($"Crate taken: {Carried} of {loot.Count}");
                if (reportThefts) ReportTheft(where);
            }
        }

        /// <summary>
        /// Radios the guard closest to the theft, naming the nearest place it can be sent to.
        ///
        /// Only the closest one is told, on purpose: the interesting question is what a single
        /// guard does with a piece of information, and telling all three turns every theft into
        /// the same three-guard convergence.
        /// </summary>
        private void ReportTheft(Vector3 where)
        {
            TheftReportModule nearest = null;
            float best = float.MaxValue;

            for (int i = 0; i < guards.Count; i++)
            {
                TheftReportModule radio = guards[i].GetComponent<TheftReportModule>();
                if (radio == null) continue;

                float distance = Vector3.Distance(Flat(guards[i].transform.position), Flat(where));
                if (distance >= best) continue;
                best = distance;
                nearest = radio;
            }
            if (nearest == null) return;

            string place = NearestPlace(where);
            nearest.Report(place, where);
            Say($"{nearest.name} radioed: crate taken near {place}.");
        }

        /// <summary>
        /// The investigate marker closest to a point. Naming a marker rather than a coordinate is
        /// what makes the report actionable: the model can only send a guard to a place that is in
        /// its argument list, and the markers are where that list comes from.
        /// </summary>
        private string NearestPlace(Vector3 where)
        {
            string best = "the yard";
            float bestDistance = float.MaxValue;

            for (int i = 0; i < places.Count; i++)
            {
                float distance = Vector3.Distance(Flat(places[i].transform.position), Flat(where));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = places[i].id;
            }
            return best;
        }

        /// <summary>
        /// A guard that has decided to chase and has closed the distance takes back what the thief
        /// is carrying. It is the only consequence in the sample, and it is deliberately mild: the
        /// interesting thing to watch is the decision, not a game-over screen.
        /// </summary>
        private void CheckCaught()
        {
            if (Carried == 0) return;

            for (int i = 0; i < guards.Count; i++)
            {
                if (!IsChasing(guards[i].currentActivity)) continue;
                if (Vector3.Distance(Flat(guards[i].transform.position), Flat(intruder.position)) > catchRange) continue;

                int lost = Carried;
                for (int c = 0; c < loot.Count; c++) loot[c].ReturnHome();
                caughtCount++;
                Say($"{guards[i].name} caught you. {lost} crate{(lost == 1 ? "" : "s")} put back.");
                return;
            }
        }

        private void CheckEscape()
        {
            if (exit == null || loot.Count == 0 || Carried < loot.Count) return;
            if (Vector3.Distance(Flat(exit.position), Flat(intruder.position)) > exitRange) return;

            Escaped = true;
            Say("Out through the gate with everything. Run over.");
        }

        private void PaintExit()
        {
            if (exitGlow == null) return;
            bool open = loot.Count > 0 && Carried >= loot.Count;
            exitGlow.color = open ? openColour : lockedColour;
            exitGlow.intensity = open ? 4.5f : 1.6f;
        }

        private static bool IsChasing(string activity) =>
            !string.IsNullOrEmpty(activity) && activity.StartsWith("Chasing");

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private void Say(string text)
        {
            message = text;
            messageUntil = Time.time + messageSeconds;
        }

        // ---------------------------------------------------------------- on-screen panel

        // One panel for the whole sample: what to press, what the objective is, and what each guard
        // is doing and whether its action menu is gated - which is the lesson the scene exists for.
        private void OnGUI()
        {
            int height = 96 + guards.Count * 20;
            const int w = 620;
            GUI.Box(new Rect(10, 10, w, height), "StealthGuard");

            GUI.Label(new Rect(20, 32, w - 20, 20),
                      "WASD to move. Hold Space near a guard to attack it.");

            if (Escaped)
            {
                GUI.Label(new Rect(20, 52, w - 20, 20), "ESCAPED. Every crate is out of the compound.");
            }
            else
            {
                string where = loot.Count > 0 && Carried >= loot.Count
                    ? "carry them out through the green gate"
                    : "walk over the glowing crates to take them";
                GUI.Label(new Rect(20, 52, w - 20, 20),
                          $"Objective: steal {loot.Count} crates - {Carried}/{loot.Count} taken, {where}.");
            }

            for (int i = 0; i < guards.Count; i++)
            {
                GuardHealth health = guards[i].GetComponent<GuardHealth>();
                string menu = health == null
                    ? ""
                    : health.IsHealthy ? "  [Chase offered]" : "  [Chase withheld, Retreat offered]";
                string hp = health != null ? $"{health.HealthReport}  " : "";
                GUI.Label(new Rect(20, 74 + i * 20, w - 20, 20),
                          $"{guards[i].name}: {hp}{guards[i].currentActivity}{menu}");
            }

            int line = 74 + guards.Count * 20;
            if (Time.time < messageUntil || Escaped)
                GUI.Label(new Rect(20, line, w - 20, 20), message);
            else if (caughtCount > 0)
                GUI.Label(new Rect(20, line, w - 20, 20), $"Caught {caughtCount} time{(caughtCount == 1 ? "" : "s")} so far.");
        }
    }
}
