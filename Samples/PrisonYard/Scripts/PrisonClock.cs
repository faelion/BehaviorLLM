using System;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>The prison's daily routine, compressed into four minutes.</summary>
    public enum PrisonPhase
    {
        Cells = 0,
        Yard = 1,
        Meal = 2,
        Work = 3,
        LightsOut = 4
    }

    /// <summary>
    /// The prison day. Each phase says where prisoners are supposed to be, which is what
    /// FollowSchedule walks them to and what makes "out of place" a thing a guard can notice.
    ///
    /// A whole day is four real minutes: long enough that behaviour is legible, short enough that
    /// a demo shows every phase without waiting.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Clock")]
    public class PrisonClock : MonoBehaviour
    {
        [Tooltip("Seconds each phase lasts, in order: Cells, Yard, Meal, Work, LightsOut. The " +
                 "defaults add up to a four-minute day.")]
        public float[] phaseDurations = { 40f, 60f, 40f, 60f, 40f };

        [Tooltip("Read-only while playing: which part of the day it is now.")]
        [SerializeField] private PrisonPhase currentPhase = PrisonPhase.Cells;

        private float phaseElapsed;

        /// <summary>Raised when the phase changes, so the HUD and the radio can announce it.</summary>
        public event Action<PrisonPhase> PhaseChanged;

        /// <summary>Which part of the day it is.</summary>
        public PrisonPhase CurrentPhase => currentPhase;

        /// <summary>Seconds until the next phase begins.</summary>
        public float SecondsLeftInPhase => Mathf.Max(0f, DurationOf(currentPhase) - phaseElapsed);

        /// <summary>Where prisoners are supposed to be right now.</summary>
        public PrisonSection ScheduledSection => SectionFor(currentPhase);

        /// <summary>Where prisoners are supposed to be during a given phase.</summary>
        public static PrisonSection SectionFor(PrisonPhase phase)
        {
            switch (phase)
            {
                case PrisonPhase.Yard: return PrisonSection.Yard;
                case PrisonPhase.Meal: return PrisonSection.Cafeteria;
                case PrisonPhase.Work: return PrisonSection.Workshop;
                default: return PrisonSection.CellBlock; // Cells and LightsOut
            }
        }

        /// <summary>How long a phase lasts, falling back to 40 s when the array is short.</summary>
        public float DurationOf(PrisonPhase phase)
        {
            int i = (int)phase;
            if (phaseDurations == null || i < 0 || i >= phaseDurations.Length) return 40f;
            return Mathf.Max(1f, phaseDurations[i]);
        }

        private void Update()
        {
            phaseElapsed += Time.deltaTime;
            if (phaseElapsed < DurationOf(currentPhase)) return;

            PrisonPhase next = (PrisonPhase)(((int)currentPhase + 1) % 5);
            SkipTo(next);
        }

        /// <summary>
        /// Jumps straight to a phase. The warden's ReturnToCells announcement uses this, and so
        /// does the director's "cut the lights" key.
        /// </summary>
        public void SkipTo(PrisonPhase phase)
        {
            currentPhase = phase;
            phaseElapsed = 0f;
            PhaseChanged?.Invoke(phase);
        }
    }
}
