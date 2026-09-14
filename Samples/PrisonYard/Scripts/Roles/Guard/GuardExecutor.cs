using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using BehaviorLLM.Core.Decisions;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Turns a guard's chosen action into movement and consequences. Every public method here is
    /// bound to one action name in the decision maker's Action Bindings and receives that action's
    /// argument as its string parameter.
    ///
    /// Note how little it knows: it never sees a prompt, never talks to the model, and would work
    /// unchanged behind a behaviour tree. What makes the prison feel connected is that some of
    /// these methods post to the radio or open an incident, which is simply another decision
    /// maker's next observation.
    /// </summary>
    [AddComponentMenu("PrisonYard/Guard Executor")]
    [RequireComponent(typeof(GuardState))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardExecutor : MonoBehaviour
    {
        [Tooltip("How close the guard has to get before a destination counts as reached.")]
        public float arrivalDistance = 1.6f;

        [Tooltip("While escorting, re-target the prisoner this often, in seconds, so the guard " +
                 "follows them rather than walking to where they used to be.")]
        public float escortRetargetInterval = 0.3f;

        private GuardState guard;
        private NavMeshAgent nav;
        private PrisonStatusBoard board;
        private PrisonRadio radio;
        private PrisonClock clock;
        private PrisonMarker[] markers;

        private PrisonSection? respondingTo;
        private float nextEscortRetarget;

        private void Awake()
        {
            guard = GetComponent<GuardState>();
            nav = GetComponent<NavMeshAgent>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
            clock = FindFirstObjectByType<PrisonClock>();
            markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
        }

        private bool IsNavReady => nav != null && nav.enabled && nav.isOnNavMesh;

        private void Update()
        {
            KeepEscorting();
            CheckArrivalAtIncident();
        }

        // ---------------------------------------------------------------- bound actions

        /// <summary>Bound to HoldPosition. The argument is always empty.</summary>
        public void OnHoldPosition(ActionArguments args)
        {
            StopEscorting();
            respondingTo = null;
            if (IsNavReady) nav.ResetPath();
            guard.activity = "Standing watch";
        }

        /// <summary>Bound to Patrol. The argument is a patrol point id.</summary>
        public void OnPatrol(ActionArguments args)
        {
            string patrolPointId = args.First;
            StopEscorting();
            respondingTo = null;
            if (GoToMarker(patrolPointId)) guard.activity = $"Patrolling to {patrolPointId}";
        }

        /// <summary>Bound to Respond. The argument is a section name.</summary>
        public void OnRespond(ActionArguments args)
        {
            string sectionId = args.First;
            StopEscorting();
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;

            respondingTo = section;
            if (GoToSection(section)) guard.activity = $"Responding to {section.Id()}";
        }

        /// <summary>Bound to Escort. The argument is a prisoner's name.</summary>
        public void OnEscort(ActionArguments args)
        {
            string prisonerName = args.First;
            PrisonerState prisoner = FindPrisoner(prisonerName);
            if (prisoner == null) return;

            guard.Escorting = prisoner;
            prisoner.EscortedBy = guard;
            prisoner.activity = PrisonActivities.BeingEscorted;
            prisoner.BusyFor(999f); // held until the guard releases them
            guard.activity = $"Escorting {prisonerName}";

            if (radio != null) radio.Post(guard.name, $"escorting {prisonerName}", CurrentSection());
        }

        /// <summary>Bound to Search. The argument is a prisoner's name.</summary>
        public void OnSearch(ActionArguments args)
        {
            string prisonerName = args.First;
            PrisonerState prisoner = FindPrisoner(prisonerName);
            if (prisoner == null) return;

            guard.activity = $"Searching {prisonerName}";
            prisoner.BusyFor(3f);
            prisoner.activity = PrisonActivities.Complying;

            if (!prisoner.hasContraband)
            {
                if (radio != null) radio.Post(guard.name, $"searched {prisonerName}, nothing found", CurrentSection());
                return;
            }

            prisoner.hasContraband = false;
            if (board != null) board.OpenIncident(IncidentKind.ContrabandFound, board.NearestSection(transform.position), guard.name);
        }

        /// <summary>Bound to Report. The argument is the section being reported on.</summary>
        public void OnReport(ActionArguments args)
        {
            string sectionId = args.First;
            if (!PrisonSections.TryParse(sectionId, out PrisonSection section)) return;

            guard.activity = $"Reporting {section.Id()}";

            // Report what it can actually see, so the radio line is true rather than generic.
            List<PrisonerState> seen = new List<PrisonerState>();
            guard.CollectVisiblePrisoners(seen, float.MaxValue);

            for (int i = 0; i < seen.Count; i++)
            {
                if (seen[i].activity != PrisonActivities.Fighting) continue;
                if (board != null) board.OpenIncident(IncidentKind.Fight, section, guard.name);
                return;
            }

            if (board != null && board.IsLocked(section))
            {
                board.OpenIncident(IncidentKind.OutOfPlace, section, guard.name);
                return;
            }

            if (radio != null) radio.Post(guard.name, $"{section.Id()} looks clear", section);
        }

        /// <summary>Bound to Retreat. The argument is always the infirmary.</summary>
        public void OnRetreat(ActionArguments args)
        {
            StopEscorting();
            respondingTo = null;

            PrisonMarker bed = FindMarker(PrisonMarkerKind.Bed);
            if (bed != null && IsNavReady) nav.SetDestination(bed.transform.position);
            else GoToSection(PrisonSection.Infirmary);

            guard.activity = PrisonActivities.Recovering;
            if (radio != null) radio.Post(guard.name, "hurt, heading to the infirmary", PrisonSection.Infirmary);
        }

        // ---------------------------------------------------------------- ongoing behaviour

        private void KeepEscorting()
        {
            PrisonerState prisoner = guard.Escorting;
            if (prisoner == null) return;

            // Walk the prisoner to where they belong: the infirmary if hurt, the scheduled
            // section otherwise.
            PrisonSection destination = prisoner.currentHealth * 2 < prisoner.maxHealth
                ? PrisonSection.Infirmary
                : (clock != null ? clock.ScheduledSection : PrisonSection.CellBlock);

            Vector3 target = SectionCentre(destination);
            if (Time.time >= nextEscortRetarget && IsNavReady)
            {
                nextEscortRetarget = Time.time + Mathf.Max(0.1f, escortRetargetInterval);
                nav.SetDestination(target);
            }

            // Drag the prisoner along beside the guard.
            NavMeshAgent prisonerNav = prisoner.GetComponent<NavMeshAgent>();
            if (prisonerNav != null && prisonerNav.enabled && prisonerNav.isOnNavMesh)
            {
                prisonerNav.SetDestination(transform.position - transform.forward * 1.2f);
            }

            if (Vector3.Distance(transform.position, target) <= arrivalDistance + 1f)
            {
                if (radio != null) radio.Post(guard.name, $"{prisoner.name} delivered to {destination.Id()}", destination);
                StopEscorting();
                guard.activity = "Standing watch";
            }
        }

        private void StopEscorting()
        {
            PrisonerState prisoner = guard.Escorting;
            if (prisoner == null) return;
            prisoner.EscortedBy = null;
            prisoner.activity = PrisonActivities.Idle;
            prisoner.BusyFor(0f);
            guard.Escorting = null;
        }

        /// <summary>Arriving at a section the guard was responding to is what closes the incident.</summary>
        private void CheckArrivalAtIncident()
        {
            if (respondingTo == null || board == null) return;

            PrisonSection? here = board.SectionAt(transform.position);
            if (here == null || here.Value != respondingTo.Value) return;

            // Do not declare it over while a fight is still going on in front of the guard.
            List<PrisonerState> seen = new List<PrisonerState>();
            guard.CollectVisiblePrisoners(seen, float.MaxValue);
            for (int i = 0; i < seen.Count; i++)
            {
                if (seen[i].activity == PrisonActivities.Fighting) return;
            }

            board.ResolveIncidentsIn(respondingTo.Value, guard.name);
            respondingTo = null;
            guard.activity = "Standing watch";
        }

        // ---------------------------------------------------------------- helpers

        private PrisonSection? CurrentSection()
        {
            return board != null ? board.SectionAt(transform.position) : null;
        }

        private bool GoToSection(PrisonSection section)
        {
            if (!IsNavReady) return false;
            nav.SetDestination(SectionCentre(section));
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

        private bool GoToMarker(string markerId)
        {
            if (markers == null) markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                PrisonMarker m = markers[i];
                if (m == null || !string.Equals(m.id, markerId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsNavReady) return false;
                nav.SetDestination(m.transform.position);
                return true;
            }
            return false;
        }

        private PrisonMarker FindMarker(PrisonMarkerKind kind)
        {
            if (markers == null) markers = FindObjectsByType<PrisonMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && markers[i].kind == kind) return markers[i];
            }
            return null;
        }

        private static PrisonerState FindPrisoner(string prisonerName)
        {
            if (string.IsNullOrWhiteSpace(prisonerName)) return null;
            PrisonerState[] all = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].name, prisonerName, System.StringComparison.OrdinalIgnoreCase)) return all[i];
            }
            return null;
        }
    }
}
