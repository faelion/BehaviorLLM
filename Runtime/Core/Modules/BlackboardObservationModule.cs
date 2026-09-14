using System.Collections.Generic;
using System.Text;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace BehaviorLLM.Core.Modules
{
    /// <summary>
    /// Reports what a <see cref="BehaviorLLMBlackboard"/> is holding, so a character can be told
    /// something instead of having to perceive it.
    ///
    /// Put one on every character that should hear the board, and point it at the board it
    /// listens to. A character can carry several, one per board, which is how a scene models a
    /// radio net a guard hears and a set of orders only officers read.
    ///
    /// Implements <see cref="IBudgetedObservation"/>, so notes are trimmed under a prompt budget
    /// on the same terms as vision and memory: newest first, oldest dropped.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Perception/Blackboard Observation")]
    public class BlackboardObservationModule : MonoBehaviour, IObservationModule, IBudgetedObservation
    {
        [Header("Scene wiring")]
        [Tooltip("The board this character reads. Leave empty to use one found in the scene, " +
                 "which is convenient while there is only one.")]
        [SerializeField] private BehaviorLLMBlackboard board;

        [Header("Configuration")]
        [Tooltip("Retention and prompt settings. Use the same asset as the board itself, so one " +
                 "asset describes the whole channel. Leave empty for sensible defaults.")]
        [SerializeField] private BlackboardConfig config;

        [Header("Reflexes")]
        [Tooltip("Decide immediately when a new note is posted, instead of waiting for the next " +
                 "tick. Turn this on for a channel that carries urgent news such as a radio " +
                 "call, and off for one that carries standing information: a board several " +
                 "characters write to will otherwise interrupt everybody every time anybody " +
                 "posts, which is a decision storm.")]
        [SerializeField] private bool interruptOnNewNote;

        [Tooltip("Ignore notes this character wrote itself when deciding whether to interrupt. " +
                 "Without it a character that posts a note immediately interrupts itself. Only " +
                 "works when the writer identifies itself when posting.")]
        [SerializeField] private string selfAuthorId = "";

        private BlackboardConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(config));
        private BlackboardConfig cfg;
        private bool pendingInterrupt;

        private void Reset()
        {
            if (config == null)
                config = BehaviorLLMDefaults.FindShipped<BlackboardConfig>(BehaviorLLMDefaults.BlackboardConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
            if (board == null) board = FindFirstObjectByType<BehaviorLLMBlackboard>();
        }

        private void OnEnable()
        {
            if (board != null) board.NotePosted += OnNotePosted;
        }

        private void OnDisable()
        {
            if (board != null) board.NotePosted -= OnNotePosted;
        }

        private void OnNotePosted(BehaviorLLMBlackboard.Note note)
        {
            if (!interruptOnNewNote) return;
            // A character that reacts to its own note decides twice about the same event.
            if (!string.IsNullOrEmpty(selfAuthorId) && note.Author == selfAuthorId) return;
            pendingInterrupt = true;
        }

        /// <summary>Swaps the configuration at runtime. Null restores the defaults.</summary>
        public void ApplyConfig(BlackboardConfig newConfig)
        {
            config = newConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newConfig);
        }

        /// <summary>Points this character at a different board at runtime.</summary>
        public void ApplyBoard(BehaviorLLMBlackboard newBoard)
        {
            if (board != null) board.NotePosted -= OnNotePosted;
            board = newBoard;
            if (board != null && isActiveAndEnabled) board.NotePosted += OnNotePosted;
        }

        public string TopicName => Cfg.topicName;
        public ObservationKind Kind => ObservationKind.Memory;

        public bool HasInterrupt()
        {
            if (!pendingInterrupt) return false;
            pendingInterrupt = false;   // consumed: one new note is one interrupt
            return true;
        }

        public string GetObservation() => GetObservation(0);

        /// <summary>Newest <paramref name="maxEntries"/> notes (0 uses the configured cap).</summary>
        public string GetObservation(int maxEntries)
        {
            if (board == null) return "No shared notes.";

            List<BehaviorLLMBlackboard.Note> recent = board.Recent(maxEntries);
            if (recent.Count == 0) return "No shared notes.";

            StringBuilder sb = new StringBuilder();
            float now = Time.time;
            for (int i = 0; i < recent.Count; i++)
            {
                BehaviorLLMBlackboard.Note n = recent[i];
                sb.Append("- ");
                if (Cfg.includeAge) sb.Append($"[{Mathf.Max(0f, now - n.TimeSec):F1}s ago] ");
                if (Cfg.includeAuthor && !string.IsNullOrEmpty(n.Author)) sb.Append(n.Author).Append(": ");
                sb.AppendLine(n.Text);
            }
            return sb.ToString();
        }
    }
}
