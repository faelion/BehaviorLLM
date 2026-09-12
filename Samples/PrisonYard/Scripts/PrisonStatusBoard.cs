using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>What kind of trouble an incident is.</summary>
    public enum IncidentKind
    {
        Fight,
        ContrabandFound,
        EscapeAttempt,
        OutOfPlace
    }

    /// <summary>Something happening in a section that the prison would want dealt with.</summary>
    public class Incident
    {
        public IncidentKind Kind;
        public PrisonSection Section;
        public float OpenedAt;
        public float ResolvedAt;
        public bool Resolved;

        /// <summary>Seconds since it started, which is what the warden reads as urgency.</summary>
        public float Age => Time.time - OpenedAt;
    }

    /// <summary>
    /// What is going on in the prison, section by section. Two very different things read it: the
    /// warden, as its only observation, and the availability providers, to decide what is on
    /// anyone's menu.
    ///
    /// It owns no behaviour of its own. Executors open incidents when they do something worth
    /// noticing, guards close them by responding, and everything else is arithmetic over the
    /// section volumes.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Status Board")]
    public class PrisonStatusBoard : MonoBehaviour
    {
        [Tooltip("Seconds an unattended incident stays open before the prison gives up on it. " +
                 "Stops a fight nobody reached from locking the warden into responding forever.")]
        public float incidentTimeoutSeconds = 45f;

        [Tooltip("How often the occupancy counts are recomputed, in seconds. They only feed prompts " +
                 "and menus, so a few times a second is plenty.")]
        public float refreshInterval = 1f;

        private readonly List<Incident> incidents = new List<Incident>();
        private readonly Dictionary<PrisonSection, int> prisonerCounts = new Dictionary<PrisonSection, int>();
        private readonly Dictionary<PrisonSection, int> guardCounts = new Dictionary<PrisonSection, int>();

        private SectionVolume[] volumes;
        private PrisonGate[] gates;
        private PrisonerState[] prisoners;
        private GuardState[] guards;
        private PrisonRadio radio;
        private float nextRefresh;

        /// <summary>Incidents currently open, oldest first.</summary>
        public IReadOnlyList<Incident> OpenIncidents => incidents;

        private void Awake()
        {
            radio = FindFirstObjectByType<PrisonRadio>();
            RefreshSceneReferences();
        }

        // The availability providers call into the board while decision makers are still waking
        // up, so every lookup below tolerates being asked before Awake has run.
        private void EnsureReferences()
        {
            if (volumes == null || gates == null) RefreshSceneReferences();
        }

        /// <summary>Re-finds the scene objects it reads. Call after spawning anyone at runtime.</summary>
        public void RefreshSceneReferences()
        {
            volumes = FindObjectsByType<SectionVolume>(FindObjectsSortMode.None);
            gates = FindObjectsByType<PrisonGate>(FindObjectsSortMode.None);
            prisoners = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            guards = FindObjectsByType<GuardState>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            for (int i = incidents.Count - 1; i >= 0; i--)
            {
                if (incidents[i].Age < incidentTimeoutSeconds) continue;
                CloseIncident(incidents[i], "timed out");
            }

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + Mathf.Max(0.1f, refreshInterval);
            RecountOccupancy();
        }

        // ------------------------------------------------------------------ occupancy

        private void RecountOccupancy()
        {
            prisonerCounts.Clear();
            guardCounts.Clear();

            if (prisoners != null)
            {
                for (int i = 0; i < prisoners.Length; i++)
                {
                    if (prisoners[i] == null) continue;
                    Add(prisonerCounts, SectionAt(prisoners[i].transform.position));
                }
            }
            if (guards != null)
            {
                for (int i = 0; i < guards.Length; i++)
                {
                    if (guards[i] == null) continue;
                    Add(guardCounts, SectionAt(guards[i].transform.position));
                }
            }
        }

        private static void Add(Dictionary<PrisonSection, int> counts, PrisonSection? section)
        {
            if (section == null) return;
            counts.TryGetValue(section.Value, out int n);
            counts[section.Value] = n + 1;
        }

        /// <summary>Which section a world position is in, or null when it is in the corridor.</summary>
        public PrisonSection? SectionAt(Vector3 position)
        {
            EnsureReferences();
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i] != null && volumes[i].Contains(position)) return volumes[i].section;
            }
            return null;
        }

        /// <summary>
        /// Which section a position belongs to for the purpose of reporting trouble. Unlike
        /// <see cref="SectionAt"/> this never returns null: the corridors between sections belong
        /// to whichever section is closest, so a fight that breaks out on the way to the workshop
        /// is still a fight in the workshop rather than a fight nowhere.
        /// </summary>
        public PrisonSection NearestSection(Vector3 position)
        {
            PrisonSection? inside = SectionAt(position);
            if (inside != null) return inside.Value;

            EnsureReferences();
            PrisonSection best = PrisonSection.Yard;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i] == null) continue;
                float d = Vector3.SqrMagnitude(volumes[i].transform.position - position);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = volumes[i].section;
            }
            return best;
        }

        /// <summary>How many prisoners were counted in a section at the last refresh.</summary>
        public int PrisonersIn(PrisonSection section)
        {
            prisonerCounts.TryGetValue(section, out int n);
            return n;
        }

        /// <summary>How many guards were counted in a section at the last refresh.</summary>
        public int GuardsIn(PrisonSection section)
        {
            guardCounts.TryGetValue(section, out int n);
            return n;
        }

        // ------------------------------------------------------------------ lockdowns

        /// <summary>
        /// True while a section is shut. A section may have several doorways (the yard has four)
        /// and they are always locked and unlocked together, so any closed gate means locked.
        /// </summary>
        public bool IsLocked(PrisonSection section)
        {
            EnsureReferences();
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] != null && gates[i].section == section && !gates[i].IsOpen) return true;
            }
            return false;
        }

        /// <summary>True when any section at all is shut, which is what LiftLockdown needs to know.</summary>
        public bool AnyLocked()
        {
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                if (IsLocked(PrisonSections.All[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Opens or closes every one of a section's doorways. Returns false when that section has
        /// no gate at all.
        /// </summary>
        public bool SetLocked(PrisonSection section, bool locked)
        {
            EnsureReferences();
            bool any = false;
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null || gates[i].section != section) continue;
                gates[i].SetOpen(!locked);
                any = true;
            }

            return any;
        }

        // ------------------------------------------------------------------ incidents

        /// <summary>
        /// Records that something is happening in a section, unless the same kind is already open
        /// there. Announces it on the radio, which is how guards find out.
        /// </summary>
        public Incident OpenIncident(IncidentKind kind, PrisonSection section, string reporter = "Control")
        {
            Incident existing = FindOpen(kind, section);
            if (existing != null) return existing;

            Incident incident = new Incident { Kind = kind, Section = section, OpenedAt = Time.time };
            incidents.Add(incident);
            IncidentsOpened++;
            if (radio != null) radio.Post(reporter, $"{Describe(kind)} in {section.Id()}", section);
            return incident;
        }

        /// <summary>True when trouble of any kind is open in a section.</summary>
        public bool HasIncidentIn(PrisonSection section)
        {
            return FindOpenAny(section) != null;
        }

        /// <summary>True when trouble of any kind is open anywhere.</summary>
        public bool HasAnyIncident()
        {
            return incidents.Count > 0;
        }

        /// <summary>Every section with an open incident, in the fixed section order.</summary>
        public List<PrisonSection> SectionsWithIncidents(List<PrisonSection> into)
        {
            into.Clear();
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                if (HasIncidentIn(s)) into.Add(s);
            }
            return into;
        }

        /// <summary>
        /// Marks the trouble in a section as dealt with. A guard arriving calls this, which is what
        /// eventually lets the warden lift a lockdown.
        /// </summary>
        public int ResolveIncidentsIn(PrisonSection section, string resolver = "Control")
        {
            int closed = 0;
            for (int i = incidents.Count - 1; i >= 0; i--)
            {
                if (incidents[i].Section != section) continue;
                CloseIncident(incidents[i], $"handled by {resolver}");
                closed++;
            }
            return closed;
        }

        private void CloseIncident(Incident incident, string why)
        {
            incident.Resolved = true;
            incident.ResolvedAt = Time.time;
            incidents.Remove(incident);
            if (radio != null)
            {
                radio.Post("Control", $"{Describe(incident.Kind)} in {incident.Section.Id()} {why}", incident.Section);
            }
            IncidentsResolved++;
        }

        /// <summary>How many incidents opened this run, for the telemetry report.</summary>
        public int IncidentsOpened { get; private set; }

        /// <summary>How many were dealt with or timed out, for the telemetry report.</summary>
        public int IncidentsResolved { get; private set; }

        private Incident FindOpen(IncidentKind kind, PrisonSection section)
        {
            for (int i = 0; i < incidents.Count; i++)
            {
                if (incidents[i].Kind == kind && incidents[i].Section == section) return incidents[i];
            }
            return null;
        }

        private Incident FindOpenAny(PrisonSection section)
        {
            for (int i = 0; i < incidents.Count; i++)
            {
                if (incidents[i].Section == section) return incidents[i];
            }
            return null;
        }

        private static string Describe(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Fight: return "fight";
                case IncidentKind.ContrabandFound: return "contraband";
                case IncidentKind.EscapeAttempt: return "escape attempt";
                default: return "prisoner out of place";
            }
        }

        // ------------------------------------------------------------------ prompt text

        /// <summary>
        /// Renders the board the way the warden reads it. This is the whole of the warden's view of
        /// the prison: no vision, no list of objects, one aggregate paragraph.
        /// </summary>
        public string Render(PrisonClock clock)
        {
            StringBuilder sb = new StringBuilder();

            if (clock != null)
            {
                sb.Append("Phase: ").Append(clock.CurrentPhase)
                  .Append(" (").Append(Mathf.RoundToInt(clock.SecondsLeftInPhase)).Append("s left). ");
            }

            sb.Append("Lockdowns: ");
            bool anyLock = false;
            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                if (!IsLocked(PrisonSections.All[i])) continue;
                if (anyLock) sb.Append(", ");
                sb.Append(PrisonSections.All[i].Id());
                anyLock = true;
            }
            if (!anyLock) sb.Append("none");
            sb.Append('\n');

            for (int i = 0; i < PrisonSections.All.Length; i++)
            {
                PrisonSection s = PrisonSections.All[i];
                sb.Append(s.Id()).Append(": ")
                  .Append(PrisonersIn(s)).Append(" prisoners, ")
                  .Append(GuardsIn(s)).Append(" guards.");

                Incident open = FindOpenAny(s);
                if (open != null)
                {
                    sb.Append(" Incident: ").Append(Describe(open.Kind))
                      .Append(" (").Append(Mathf.RoundToInt(open.Age)).Append("s).");
                }
                sb.Append('\n');
            }

            return sb.ToString().TrimEnd();
        }
    }
}
