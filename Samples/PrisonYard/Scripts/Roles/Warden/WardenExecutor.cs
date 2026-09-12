using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Carries out the warden's orders. Everything here changes the world other decision makers
    /// observe: closing a gate removes a section from every prisoner's argument list, reassigning
    /// a guard changes which patrol points that guard is offered, and every order goes out on the
    /// radio where the guards will read it.
    ///
    /// This is the sample's argument that a decision maker need not be a character. The warden has
    /// no body and no vision; it reads an aggregate and issues orders, which is what a difficulty
    /// director or an economy manager would do.
    /// </summary>
    [AddComponentMenu("PrisonYard/Warden Executor")]
    public class WardenExecutor : MonoBehaviour
    {
        [Tooltip("The name the warden speaks under on the radio. Must match the name its own radio " +
                 "module listens as, or it will hear itself.")]
        public string wardenName = PrisonYardIds.Warden;

        private PrisonStatusBoard board;
        private PrisonRadio radio;
        private PrisonClock clock;

        /// <summary>How many lockdowns the warden ordered this run, for the report.</summary>
        public int LockdownsOrdered { get; private set; }

        private void Awake()
        {
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
            clock = FindFirstObjectByType<PrisonClock>();
        }

        /// <summary>Bound to Observe. Deliberately does nothing: "no change" is a real decision.</summary>
        public void OnObserve(string _)
        {
        }

        /// <summary>Bound to Lockdown. The argument is the section to shut.</summary>
        public void OnLockdown(string sectionId)
        {
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;
            if (board == null || !board.SetLocked(section, true)) return;

            LockdownsOrdered++;
            if (radio != null) radio.Post(wardenName, $"{section.Id()} is on lockdown", section);
        }

        /// <summary>Bound to LiftLockdown. The argument is the section to reopen.</summary>
        public void OnLiftLockdown(string sectionId)
        {
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;
            if (board == null || !board.SetLocked(section, false)) return;

            if (radio != null) radio.Post(wardenName, $"lockdown lifted in {section.Id()}", section);
        }

        /// <summary>
        /// Bound to Reinforce. The argument is the section needing help; the warden picks which
        /// guard goes, because an action carries one argument and this one needs two.
        /// </summary>
        public void OnReinforce(string sectionId)
        {
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;

            GuardState chosen = NearestAvailableGuard(section);
            if (chosen == null) return;

            chosen.assignedSection = section;
            if (radio != null) radio.Post(wardenName, $"{chosen.name} to {section.Id()}", section);
        }

        /// <summary>Bound to Announce. The argument is one of the fixed announcements.</summary>
        public void OnAnnounce(string announcement)
        {
            if (string.IsNullOrWhiteSpace(announcement)) return;

            string text;
            switch (announcement)
            {
                case PrisonYardIds.ReturnToCells:
                    text = "all prisoners return to cells";
                    if (clock != null) clock.SkipTo(PrisonPhase.Cells);
                    break;
                case PrisonYardIds.MealTime:
                    text = "meal service is open";
                    break;
                case PrisonYardIds.YardTime:
                    text = "yard time";
                    break;
                default:
                    text = "stay calm and carry on";
                    break;
            }

            if (radio != null) radio.Post(wardenName, text, null);
        }

        /// <summary>
        /// The guard closest to a section that is not already busy escorting someone. Escorting
        /// guards are left alone so an order never abandons a prisoner half-way.
        /// </summary>
        private GuardState NearestAvailableGuard(PrisonSection section)
        {
            GuardState[] guards = FindObjectsByType<GuardState>(FindObjectsSortMode.None);
            if (guards.Length == 0) return null;

            Vector3 target = SectionCentre(section);
            GuardState best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < guards.Length; i++)
            {
                GuardState g = guards[i];
                if (g == null || g.Escorting != null) continue;
                if (g.assignedSection == section) continue; // already covering it

                float d = Vector3.Distance(g.transform.position, target);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = g;
            }
            return best;
        }

        private Vector3 SectionCentre(PrisonSection section)
        {
            PrisonMarker[] markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m != null && m.kind == PrisonMarkerKind.SectionCentre && m.section == section)
                    return m.transform.position;
            }
            return transform.position;
        }
    }
}
