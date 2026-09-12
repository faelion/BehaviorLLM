using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Project.Samples.PrisonYard.Tests
{
    /// <summary>
    /// Covers the parts of the prison that decide what the model is allowed to do, without a model
    /// or a scene file. Everything is built in code, so a failure here points at one rule rather
    /// than at "the sample misbehaved".
    ///
    /// The gating is what these are really about. If a prisoner can be offered Fight while a guard
    /// is watching, the sample's central claim is false, and no amount of prompt wording fixes it.
    /// </summary>
    public class PrisonYardTests
    {
        private readonly List<GameObject> cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < cleanup.Count; i++)
            {
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
            cleanup.Clear();
        }

        // ================================================================ prisoner gating

        [Test]
        public void Fight_IsOnlyForAHotheadWhoIsNotBeingWatched()
        {
            World world = new World(this);
            Prisoner hothead = world.AddPrisoner("Prisoner_03", PrisonerTemperament.Hothead, world.YardCentre);
            world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre + new Vector3(1f, 0f, 0f));
            world.Tick();

            Assert.IsTrue(hothead.Availability.IsActionAvailable(PrisonYardIds.Fight), "unwatched hothead with someone in reach");

            hothead.SetObserved(true);
            Assert.IsFalse(hothead.Availability.IsActionAvailable(PrisonYardIds.Fight), "a guard is watching");
        }

        [Test]
        public void Fight_IsNeverOfferedToACompliantPrisoner()
        {
            World world = new World(this);
            Prisoner calm = world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre);
            world.AddPrisoner("Prisoner_02", PrisonerTemperament.Compliant, world.YardCentre + new Vector3(1f, 0f, 0f));
            world.Tick();

            Assert.IsFalse(calm.Availability.IsActionAvailable(PrisonYardIds.Fight));
        }

        [Test]
        public void Fight_NeedsSomebodyWithinReach()
        {
            World world = new World(this);
            Prisoner hothead = world.AddPrisoner("Prisoner_03", PrisonerTemperament.Hothead, world.YardCentre);
            world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre + new Vector3(20f, 0f, 0f));
            world.Tick();

            Assert.IsFalse(hothead.Availability.IsActionAvailable(PrisonYardIds.Fight), "the only other prisoner is far away");
        }

        [Test]
        public void Sneak_IsOnlyForAnEscapeeWhoIsNotBeingWatched()
        {
            World world = new World(this);
            Prisoner escapee = world.AddPrisoner("Prisoner_04", PrisonerTemperament.Escapee, world.YardCentre);
            world.Tick();

            Assert.IsTrue(escapee.Availability.IsActionAvailable(PrisonYardIds.Sneak));

            escapee.SetObserved(true);
            Assert.IsFalse(escapee.Availability.IsActionAvailable(PrisonYardIds.Sneak));
        }

        [Test]
        public void Hide_NeedsContrabandAndAHidingSpotInThisSection()
        {
            World world = new World(this);
            Prisoner escapee = world.AddPrisoner("Prisoner_04", PrisonerTemperament.Escapee, world.YardCentre);
            world.Tick();

            Assert.IsFalse(escapee.Availability.IsActionAvailable(PrisonYardIds.Hide), "nothing to hide yet");

            escapee.State.hasContraband = true;
            Assert.IsTrue(escapee.Availability.IsActionAvailable(PrisonYardIds.Hide), "the yard has a hiding spot");

            escapee.State.hasContraband = true;
            escapee.Move(world.CafeteriaCentre); // no hiding spot there
            Assert.IsFalse(escapee.Availability.IsActionAvailable(PrisonYardIds.Hide));
        }

        [Test]
        public void FollowSchedule_IsAlwaysAvailable_AndUnknownActionsStayAvailable()
        {
            World world = new World(this);
            Prisoner p = world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre);
            world.Tick();

            Assert.IsTrue(p.Availability.IsActionAvailable(PrisonYardIds.FollowSchedule));
            Assert.IsTrue(p.Availability.IsActionAvailable("SomethingAddedLater"),
                "an action this provider does not know about must not be silently disabled");
        }

        [Test]
        public void BeingEscorted_LeavesOnlyComply()
        {
            World world = new World(this);
            Prisoner p = world.AddPrisoner("Prisoner_01", PrisonerTemperament.Hothead, world.YardCentre);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            p.State.EscortedBy = g.State;

            Assert.IsTrue(p.Availability.IsActionAvailable(PrisonYardIds.Comply));
            Assert.IsFalse(p.Availability.IsActionAvailable(PrisonYardIds.FollowSchedule));
            Assert.IsFalse(p.Availability.IsActionAvailable(PrisonYardIds.Wander));
            Assert.IsFalse(p.Availability.IsActionAvailable(PrisonYardIds.Fight));
        }

        // ================================================================ prisoner options

        [Test]
        public void Wander_ExcludesLockedSections_ButAlwaysOffersTheScheduledOne()
        {
            World world = new World(this);
            Prisoner p = world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre);
            world.Tick();

            List<string> options = new List<string>();
            Assert.IsTrue(p.Options.TryGetArgumentOptions(PrisonYardIds.Wander, options));
            CollectionAssert.Contains(options, PrisonSection.Cafeteria.Id());

            world.SetLocked(PrisonSection.Cafeteria, true);
            options.Clear();
            p.Options.TryGetArgumentOptions(PrisonYardIds.Wander, options);
            CollectionAssert.DoesNotContain(options, PrisonSection.Cafeteria.Id());

            // The clock starts in Cells, so the cell block is the scheduled section; locking it
            // must not leave the prisoner with nowhere legitimate to go.
            world.SetLocked(PrisonSection.CellBlock, true);
            options.Clear();
            p.Options.TryGetArgumentOptions(PrisonYardIds.Wander, options);
            CollectionAssert.Contains(options, PrisonSection.CellBlock.Id());
        }

        [Test]
        public void EveryOptionIsIdentifierShaped()
        {
            World world = new World(this);
            Prisoner p = world.AddPrisoner("Prisoner_04", PrisonerTemperament.Escapee, world.YardCentre);
            p.State.hasContraband = true;
            world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre + new Vector3(1f, 0f, 0f));
            world.Tick();

            string[] actions = { PrisonYardIds.Wander, PrisonYardIds.Sneak, PrisonYardIds.Talk, PrisonYardIds.Hide };
            foreach (string action in actions)
            {
                List<string> options = new List<string>();
                p.Options.TryGetArgumentOptions(action, options);
                foreach (string option in options)
                {
                    // Anything outside this set is dropped when the schema is built, which would
                    // silently make the value unreachable.
                    StringAssert.IsMatch("^[A-Za-z0-9_]+$", option, $"{action} offered '{option}'");
                }
            }
        }

        // ================================================================ guard gating

        [Test]
        public void Retreat_IsOnlyOfferedBelowHalfHealth()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            Assert.IsFalse(g.Availability.IsActionAvailable(PrisonYardIds.Retreat), "a healthy guard");

            g.State.currentHealth = 30;
            Assert.IsTrue(g.Availability.IsActionAvailable(PrisonYardIds.Retreat));
        }

        [Test]
        public void Respond_NeedsAnIncidentOrARecentRadioCall()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            Assert.IsFalse(g.Availability.IsActionAvailable(PrisonYardIds.Respond), "a quiet prison");

            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Workshop, "Control");
            Assert.IsTrue(g.Availability.IsActionAvailable(PrisonYardIds.Respond));
        }

        [Test]
        public void Respond_OffersTheSectionWithTheTrouble()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();
            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Workshop, "Control");

            List<string> options = new List<string>();
            Assert.IsTrue(g.Options.TryGetArgumentOptions(PrisonYardIds.Respond, options));
            CollectionAssert.Contains(options, PrisonSection.Workshop.Id());
        }

        [Test]
        public void Patrol_OffersTheGuardsOwnSectionFirst()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_03", PrisonSection.Workshop, world.WorkshopCentre);
            world.Tick();

            List<string> options = new List<string>();
            Assert.IsTrue(g.Options.TryGetArgumentOptions(PrisonYardIds.Patrol, options));
            CollectionAssert.Contains(options, "Workshop_Patrol_N");
            CollectionAssert.DoesNotContain(options, "Yard_Patrol_N", "a guard patrols its own section");
        }

        // ================================================================ warden gating

        [Test]
        public void Lockdown_NeedsAnIncident_AndLiftLockdownNeedsALock()
        {
            World world = new World(this);
            Warden w = world.AddWarden();
            world.Tick();

            Assert.IsFalse(w.Availability.IsActionAvailable(PrisonYardIds.Lockdown), "a quiet prison");
            Assert.IsFalse(w.Availability.IsActionAvailable(PrisonYardIds.LiftLockdown), "nothing is shut");
            Assert.IsTrue(w.Availability.IsActionAvailable(PrisonYardIds.Observe), "doing nothing is always allowed");

            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Yard, "Control");
            Assert.IsTrue(w.Availability.IsActionAvailable(PrisonYardIds.Lockdown));

            world.SetLocked(PrisonSection.Yard, true);
            Assert.IsTrue(w.Availability.IsActionAvailable(PrisonYardIds.LiftLockdown));
        }

        [Test]
        public void LockdownOptions_ListOnlyOpenSections_AndLiftOnlyShutOnes()
        {
            World world = new World(this);
            Warden w = world.AddWarden();
            world.Tick();
            world.SetLocked(PrisonSection.Yard, true);

            List<string> toLock = new List<string>();
            w.Options.TryGetArgumentOptions(PrisonYardIds.Lockdown, toLock);
            CollectionAssert.DoesNotContain(toLock, PrisonSection.Yard.Id());

            List<string> toLift = new List<string>();
            w.Options.TryGetArgumentOptions(PrisonYardIds.LiftLockdown, toLift);
            CollectionAssert.AreEqual(new[] { PrisonSection.Yard.Id() }, toLift);
        }

        [Test]
        public void Reinforce_OnlyOffersSectionsWithTroubleAndTooFewGuards()
        {
            World world = new World(this);
            Warden w = world.AddWarden();
            world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.AddGuard("Guard_02", PrisonSection.Yard, world.YardCentre + new Vector3(2f, 0f, 0f));
            world.Tick();

            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Workshop, "Control");
            world.Tick(); // recount occupancy

            List<string> options = new List<string>();
            bool any = w.Options.TryGetArgumentOptions(PrisonYardIds.Reinforce, options);

            Assert.IsTrue(any);
            CollectionAssert.Contains(options, PrisonSection.Workshop.Id(), "trouble and no guards there");
            CollectionAssert.DoesNotContain(options, PrisonSection.Yard.Id(), "no trouble in the yard");
        }

        // ================================================================ radio

        [Test]
        public void AListenerNeverHearsItsOwnMessages()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            world.Radio.Post("Guard_01", "fight in Yard", PrisonSection.Yard);
            world.Radio.Post("Guard_02", "all quiet in CellBlock", PrisonSection.CellBlock);

            string heard = g.Radio.GetObservation(5);
            StringAssert.Contains("Guard_02", heard);
            StringAssert.DoesNotContain("Guard_01", heard);
        }

        [Test]
        public void ARelevantMessageInterruptsOnce()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            world.Radio.Post("Guard_02", "fight in Yard", PrisonSection.Yard);
            World.Call(g.Radio, "Update");

            Assert.IsTrue(g.Radio.HasInterrupt(), "a call about this guard's section");
            Assert.IsFalse(g.Radio.HasInterrupt(), "the same message must not interrupt twice");
        }

        [Test]
        public void AMessageAboutSomewhereElseDoesNotInterrupt()
        {
            World world = new World(this);
            Guard g = world.AddGuard("Guard_01", PrisonSection.Yard, world.YardCentre);
            world.Tick();

            world.Radio.Post("Guard_02", "all quiet in Cafeteria", PrisonSection.Cafeteria);
            World.Call(g.Radio, "Update");

            Assert.IsFalse(g.Radio.HasInterrupt());
        }

        [Test]
        public void TheRadioIsBounded()
        {
            World world = new World(this);
            world.Radio.capacity = 32;

            for (int i = 0; i < 100; i++) world.Radio.Post("Control", $"message {i}", null);

            Assert.AreEqual(32, world.Radio.Messages.Count);
        }

        // ================================================================ clock and board

        [Test]
        public void EachPhaseHasAScheduledSection()
        {
            Assert.AreEqual(PrisonSection.CellBlock, PrisonClock.SectionFor(PrisonPhase.Cells));
            Assert.AreEqual(PrisonSection.Yard, PrisonClock.SectionFor(PrisonPhase.Yard));
            Assert.AreEqual(PrisonSection.Cafeteria, PrisonClock.SectionFor(PrisonPhase.Meal));
            Assert.AreEqual(PrisonSection.Workshop, PrisonClock.SectionFor(PrisonPhase.Work));
            Assert.AreEqual(PrisonSection.CellBlock, PrisonClock.SectionFor(PrisonPhase.LightsOut));
        }

        [Test]
        public void SkippingAPhaseChangesWherePrisonersShouldBe()
        {
            World world = new World(this);
            world.Clock.SkipTo(PrisonPhase.Meal);
            Assert.AreEqual(PrisonSection.Cafeteria, world.Clock.ScheduledSection);
        }

        [Test]
        public void SectionVolumesDecideWhoIsWhere()
        {
            World world = new World(this);
            world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre);
            world.AddPrisoner("Prisoner_02", PrisonerTemperament.Compliant, world.CafeteriaCentre);
            world.Tick();

            Assert.AreEqual(1, world.Board.PrisonersIn(PrisonSection.Yard));
            Assert.AreEqual(1, world.Board.PrisonersIn(PrisonSection.Cafeteria));
            Assert.AreEqual(0, world.Board.PrisonersIn(PrisonSection.Workshop));
        }

        [Test]
        public void OpeningTheSameTroubleTwiceIsOneIncident()
        {
            World world = new World(this);
            world.Tick();

            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Yard, "Guard_01");
            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Yard, "Guard_02");

            Assert.AreEqual(1, world.Board.OpenIncidents.Count);
            Assert.AreEqual(1, world.Board.IncidentsOpened);
        }

        [Test]
        public void RespondingToASectionClosesItsIncidents()
        {
            World world = new World(this);
            world.Tick();
            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Yard, "Guard_01");

            int closed = world.Board.ResolveIncidentsIn(PrisonSection.Yard, "Guard_01");

            Assert.AreEqual(1, closed);
            Assert.IsFalse(world.Board.HasIncidentIn(PrisonSection.Yard));
            Assert.AreEqual(1, world.Board.IncidentsResolved);
        }

        [Test]
        public void TheStatusBoardRendersEverySectionForTheWarden()
        {
            World world = new World(this);
            world.AddPrisoner("Prisoner_01", PrisonerTemperament.Compliant, world.YardCentre);
            world.Tick();
            world.Board.OpenIncident(IncidentKind.Fight, PrisonSection.Yard, "Guard_01");

            string text = world.Board.Render(world.Clock);

            StringAssert.Contains("Phase:", text);
            StringAssert.Contains("Lockdowns: none", text);
            foreach (PrisonSection s in PrisonSections.All) StringAssert.Contains(s.Id(), text);
            StringAssert.Contains("Incident: fight", text);
        }

        // ================================================================ fixture

        /// <summary>
        /// A prison in miniature: the systems, six section volumes and six gates, laid out the way
        /// the scene builder lays them out, with no rendering, no NavMesh and no models.
        /// </summary>
        private class World
        {
            public readonly PrisonRadio Radio;
            public readonly PrisonClock Clock;
            public readonly PrisonStatusBoard Board;

            public readonly Vector3 YardCentre = new Vector3(0f, 0f, 0f);
            public readonly Vector3 CafeteriaCentre = new Vector3(18f, 0f, 16f);
            public readonly Vector3 WorkshopCentre = new Vector3(-18f, 0f, -16f);

            private readonly PrisonYardTests owner;
            private readonly Dictionary<PrisonSection, PrisonGate> gates = new Dictionary<PrisonSection, PrisonGate>();

            public World(PrisonYardTests owner)
            {
                this.owner = owner;

                GameObject systems = owner.New("_Systems");
                Radio = systems.AddComponent<PrisonRadio>();
                Radio.logToConsole = false;
                Clock = systems.AddComponent<PrisonClock>();
                Board = systems.AddComponent<PrisonStatusBoard>();

                AddSection(PrisonSection.CellBlock, new Vector3(-18f, 0f, 18f), new Vector2(20f, 14f));
                AddSection(PrisonSection.Yard, YardCentre, new Vector2(22f, 22f));
                AddSection(PrisonSection.Cafeteria, CafeteriaCentre, new Vector2(16f, 12f));
                AddSection(PrisonSection.Workshop, WorkshopCentre, new Vector2(18f, 12f));
                AddSection(PrisonSection.Infirmary, new Vector3(18f, 0f, -16f), new Vector2(12f, 10f));
                AddSection(PrisonSection.ControlRoom, new Vector3(0f, 0f, 26f), new Vector2(10f, 6f));

                // One hiding spot in the yard and one in the workshop, as the scene has.
                AddMarker("Yard_Bleachers", PrisonMarkerKind.HidingSpot, PrisonSection.Yard, YardCentre + new Vector3(-9f, 0f, -9f));
                AddMarker("Workshop_Corner", PrisonMarkerKind.HidingSpot, PrisonSection.Workshop, WorkshopCentre + new Vector3(-7f, 0f, -4f));

                foreach (PrisonSection s in PrisonSections.All)
                {
                    AddMarker(s.Id(), PrisonMarkerKind.SectionCentre, s, CentreOf(s));
                    AddMarker($"{s.Id()}_Patrol_N", PrisonMarkerKind.PatrolPoint, s, CentreOf(s) + new Vector3(-3f, 0f, 3f));
                    AddMarker($"{s.Id()}_Patrol_S", PrisonMarkerKind.PatrolPoint, s, CentreOf(s) + new Vector3(3f, 0f, -3f));
                }

                Board.RefreshSceneReferences();
            }

            private readonly Dictionary<PrisonSection, Vector3> centres = new Dictionary<PrisonSection, Vector3>();

            private Vector3 CentreOf(PrisonSection section) => centres[section];

            private void AddSection(PrisonSection section, Vector3 centre, Vector2 size)
            {
                centres[section] = centre;

                GameObject volumeGo = owner.New($"Volume_{section}");
                volumeGo.transform.position = centre;
                SectionVolume volume = volumeGo.AddComponent<SectionVolume>();
                volume.section = section;
                volume.size = size;

                GameObject gateGo = owner.New($"Gate_{section}");
                gateGo.transform.position = centre;
                PrisonGate gate = gateGo.AddComponent<PrisonGate>();
                gate.section = section;
                gate.startOpen = true;
                gate.SetOpen(true);
                gates[section] = gate;
            }

            private void AddMarker(string id, PrisonMarkerKind kind, PrisonSection section, Vector3 position)
            {
                GameObject go = owner.New("Marker_" + id);
                go.transform.position = position;
                PrisonMarker marker = go.AddComponent<PrisonMarker>();
                marker.id = id;
                marker.kind = kind;
                marker.section = section;
            }

            /// <summary>Opens or closes a gate directly, standing in for the warden's order.</summary>
            public void SetLocked(PrisonSection section, bool locked)
            {
                gates[section].SetOpen(!locked);
            }

            /// <summary>
            /// Runs the board's own refresh. Edit mode never calls Update, so anything that would
            /// normally happen over time is triggered by hand here.
            /// </summary>
            public void Tick()
            {
                Board.RefreshSceneReferences();
                Call(Board, "RecountOccupancy");
            }

            public Prisoner AddPrisoner(string prisonerName, PrisonerTemperament temperament, Vector3 position)
            {
                GameObject go = owner.New(prisonerName);
                go.transform.position = position;

                PrisonerState state = go.AddComponent<PrisonerState>();
                state.temperament = temperament;
                PrisonerAvailability availability = go.AddComponent<PrisonerAvailability>();
                PrisonerArgumentOptions options = go.AddComponent<PrisonerArgumentOptions>();

                Wake(state, availability, options);
                return new Prisoner { State = state, Availability = availability, Options = options };
            }

            public Guard AddGuard(string guardName, PrisonSection section, Vector3 position)
            {
                GameObject go = owner.New(guardName);
                go.transform.position = position;

                GuardState state = go.AddComponent<GuardState>();
                state.assignedSection = section;
                GuardAvailability availability = go.AddComponent<GuardAvailability>();
                GuardArgumentOptions options = go.AddComponent<GuardArgumentOptions>();
                RadioObservationModule radio = go.AddComponent<RadioObservationModule>();
                radio.listenerName = guardName;

                Wake(state, availability, options, radio);
                return new Guard { State = state, Availability = availability, Options = options, Radio = radio };
            }

            public Warden AddWarden()
            {
                GameObject go = owner.New(PrisonYardIds.Warden);
                WardenAvailability availability = go.AddComponent<WardenAvailability>();
                WardenArgumentOptions options = go.AddComponent<WardenArgumentOptions>();

                Wake(availability, options);
                return new Warden { Availability = availability, Options = options };
            }

            /// <summary>
            /// Edit mode does not run Awake on AddComponent, and these components cache their scene
            /// references there, so the fixture calls it explicitly.
            /// </summary>
            private static void Wake(params MonoBehaviour[] components)
            {
                for (int i = 0; i < components.Length; i++) Call(components[i], "Awake");
            }

            /// <summary>
            /// Calls a private lifecycle method directly. Unity's SendMessage asserts in edit mode,
            /// because the object is not actually running, so reflection is the only way to drive
            /// these components without entering play mode.
            /// </summary>
            public static void Call(MonoBehaviour component, string methodName)
            {
                System.Reflection.MethodInfo method = component.GetType().GetMethod(methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
                if (method != null) method.Invoke(component, null);
            }
        }

        private class Prisoner
        {
            public PrisonerState State;
            public PrisonerAvailability Availability;
            public PrisonerArgumentOptions Options;

            /// <summary>Forces the watched flag, which the game normally recomputes from the guards.</summary>
            public void SetObserved(bool observed)
            {
                typeof(PrisonerState)
                    .GetField("observedByGuard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(State, observed);
            }

            public void Move(Vector3 position)
            {
                State.transform.position = position;
            }
        }

        private class Guard
        {
            public GuardState State;
            public GuardAvailability Availability;
            public GuardArgumentOptions Options;
            public RadioObservationModule Radio;
        }

        private class Warden
        {
            public WardenAvailability Availability;
            public WardenArgumentOptions Options;
        }

        private GameObject New(string objectName)
        {
            GameObject go = new GameObject(objectName);
            cleanup.Add(go);
            return go;
        }
    }
}
