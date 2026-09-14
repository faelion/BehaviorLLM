using UnityEngine;
using UnityEngine.AI;
using BehaviorLLM.Core.Decisions;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Turns a prisoner's chosen action into movement and consequences. Bound to the action names
    /// one for one, like the guard's executor.
    ///
    /// Fight is the interesting one: it changes this prisoner's <c>activity</c> to Fighting, which
    /// is a field a guard's vision reads through the context bindings. No code tells the guard
    /// anything; the guard simply sees it on its next look.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prisoner Executor")]
    [RequireComponent(typeof(PrisonerState))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class PrisonerExecutor : MonoBehaviour
    {
        [Tooltip("Seconds a fight lasts before both prisoners go back to normal. It has to outlast a " +
                 "guard's reaction time or nobody ever catches one in progress: a guard decides every " +
                 "2.5 seconds and the model takes about a second to answer, so anything under about " +
                 "8 seconds is over before it can be reported.")]
        public float fightSeconds = 12f;

        [Tooltip("Damage each prisoner takes over the course of one fight.")]
        public int fightDamage = 15;

        [Tooltip("Seconds two prisoners stand still talking.")]
        public float talkSeconds = 6f;

        [Tooltip("How far from a section's centre a wandering prisoner stops, in metres, so four " +
                 "of them do not pile onto the same spot.")]
        public float wanderSpread = 3f;

        private PrisonerState state;
        private NavMeshAgent nav;
        private PrisonStatusBoard board;
        private PrisonRadio radio;
        private PrisonClock clock;
        private PrisonMarker[] markers;

        private float fightEndsAt;
        private PrisonSection? sneakingTo;

        private void Awake()
        {
            state = GetComponent<PrisonerState>();
            nav = GetComponent<NavMeshAgent>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
            clock = FindFirstObjectByType<PrisonClock>();
            markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
        }

        private bool IsNavReady => nav != null && nav.enabled && nav.isOnNavMesh;

        private void Update()
        {
            EndFightWhenDue();
            CheckEscapeArrival();
        }

        // ---------------------------------------------------------------- bound actions

        /// <summary>Bound to FollowSchedule. The argument is always empty.</summary>
        public void OnFollowSchedule(ActionArguments args)
        {
            sneakingTo = null;
            PrisonSection target = clock != null ? clock.ScheduledSection : PrisonSection.CellBlock;
            if (GoToSection(target)) state.activity = $"Heading to {target.Id()}";
        }

        /// <summary>Bound to Wander. The argument is a section name.</summary>
        public void OnWander(ActionArguments args)
        {
            string sectionId = args.First;
            sneakingTo = null;
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;
            if (GoToSection(section)) state.activity = $"Wandering to {section.Id()}";
        }

        /// <summary>Bound to Talk. The argument is another prisoner's name.</summary>
        public void OnTalk(ActionArguments args)
        {
            string prisonerName = args.First;
            PrisonerState other = FindPrisoner(prisonerName);
            if (other == null) return;

            if (IsNavReady) nav.ResetPath();
            state.activity = PrisonActivities.Talking;
            state.BusyFor(talkSeconds);
            other.activity = PrisonActivities.Talking;
            other.BusyFor(talkSeconds);
        }

        /// <summary>Bound to Fight. The argument is the prisoner being attacked.</summary>
        public void OnFight(ActionArguments args)
        {
            string prisonerName = args.First;
            PrisonerState other = FindPrisoner(prisonerName);
            if (other == null) return;

            if (IsNavReady) nav.ResetPath();

            state.activity = PrisonActivities.Fighting;
            other.activity = PrisonActivities.Fighting;
            state.BusyFor(fightSeconds);
            other.BusyFor(fightSeconds);
            state.TakeDamage(fightDamage);
            other.TakeDamage(fightDamage);
            fightEndsAt = Time.time + fightSeconds;

            // The incident is what the warden reads and what guards can respond to. Note that no
            // guard is notified here: one has to see it, or hear it called in. NearestSection
            // rather than SectionAt, so a fight in a corridor still belongs somewhere.
            if (board != null) board.OpenIncident(IncidentKind.Fight, board.NearestSection(transform.position), name);
        }

        /// <summary>Bound to Hide. The argument is a hiding spot id.</summary>
        public void OnHide(ActionArguments args)
        {
            string spotId = args.First;
            PrisonMarker spot = FindMarker(spotId);
            if (spot == null) return;

            if (IsNavReady) nav.SetDestination(spot.transform.position);
            state.hasContraband = false;
            state.activity = PrisonActivities.Hiding;
            state.BusyFor(3f);
        }

        /// <summary>Bound to Sneak. The argument is the section being slipped into.</summary>
        public void OnSneak(ActionArguments args)
        {
            string sectionId = args.First;
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;
            if (!GoToSection(section)) return;

            sneakingTo = section;
            state.activity = PrisonActivities.Sneaking;
        }

        /// <summary>Bound to Comply. The argument is always empty.</summary>
        public void OnComply(ActionArguments args)
        {
            if (IsNavReady) nav.ResetPath();
            state.activity = PrisonActivities.Complying;
        }

        // ---------------------------------------------------------------- ongoing behaviour

        private void EndFightWhenDue()
        {
            if (fightEndsAt <= 0f || Time.time < fightEndsAt) return;
            fightEndsAt = 0f;
            if (state.activity == PrisonActivities.Fighting) state.activity = PrisonActivities.Idle;
        }

        /// <summary>Reaching the workshop unseen after lights out is what counts as a real attempt.</summary>
        private void CheckEscapeArrival()
        {
            if (sneakingTo == null || board == null) return;

            PrisonSection? here = board.SectionAt(transform.position);
            if (here == null || here.Value != sneakingTo.Value) return;

            PrisonSection reached = sneakingTo.Value;
            sneakingTo = null;
            state.activity = PrisonActivities.Idle;

            bool afterHours = clock != null && clock.CurrentPhase == PrisonPhase.LightsOut;
            if (reached != PrisonSection.Workshop || !afterHours) return;

            EscapeAttempts++;
            board.OpenIncident(IncidentKind.EscapeAttempt, reached, name);
        }

        /// <summary>How many escape attempts this prisoner completed, for the run report.</summary>
        public int EscapeAttempts { get; private set; }

        // ---------------------------------------------------------------- helpers

        private bool GoToSection(PrisonSection section)
        {
            if (!IsNavReady) return false;
            Vector3 centre = SectionCentre(section);
            Vector2 offset = Random.insideUnitCircle * wanderSpread;
            nav.SetDestination(centre + new Vector3(offset.x, 0f, offset.y));
            return true;
        }

        private Vector3 SectionCentre(PrisonSection section)
        {
            if (markers == null) markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m != null && m.kind == PrisonMarkerKind.SectionCentre && m.section == section)
                    return m.transform.position;
            }
            return transform.position;
        }

        private PrisonMarker FindMarker(string markerId)
        {
            if (markers == null) markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && string.Equals(markers[i].id, markerId, System.StringComparison.OrdinalIgnoreCase))
                    return markers[i];
            }
            return null;
        }

        private PrisonerState FindPrisoner(string prisonerName)
        {
            if (string.IsNullOrWhiteSpace(prisonerName)) return null;
            PrisonerState[] all = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != state && string.Equals(all[i].name, prisonerName, System.StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }
    }
}
