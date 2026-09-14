using System;
using System.Collections.Generic;
using BehaviorLLM.Core.Config;
using UnityEngine;

namespace BehaviorLLM.Core.Modules
{
    /// <summary>
    /// A small shared noticeboard that decision makers write to and read back as an observation.
    ///
    /// It exists to stop the prompt growing with the square of the cast. Without it, eight
    /// characters who need to coordinate each have to perceive everything the other seven
    /// perceive, and the observation block is the fastest-growing part of a prompt as a scene
    /// fills up. With it, one character reports "the west gate is open" once and the others read
    /// one short line instead of re-deriving it.
    ///
    /// Writing is an ordinary action binding: point an action's On Execute at
    /// <see cref="Post"/> and the model's argument becomes the note. Reading is a
    /// <see cref="BlackboardObservationModule"/> on whichever characters should hear it.
    ///
    /// Deliberately not a singleton, like the rest of the package: a scene may hold several
    /// boards (a radio net, a shared objective, a faction's orders) and a character reads
    /// whichever ones it is pointed at.
    /// </summary>
    [AddComponentMenu("BehaviorLLM/Blackboard")]
    public class BehaviorLLMBlackboard : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Retention and prompt settings for this board. Share one asset between the " +
                 "board and every observation module that reports it. Leave empty for sensible " +
                 "defaults.")]
        [SerializeField] private BlackboardConfig config;

        private BlackboardConfig Cfg => cfg != null ? cfg : (cfg = BehaviorLLMDefaults.OrTransientDefault(config));
        private BlackboardConfig cfg;

        private readonly List<Note> notes = new List<Note>();

        /// <summary>One note on the board.</summary>
        public readonly struct Note
        {
            /// <summary>What the note says. Already trimmed to the configured length.</summary>
            public readonly string Text;
            /// <summary>Who wrote it, or empty when the writer did not say.</summary>
            public readonly string Author;
            /// <summary>Scene time the note was written, for age and staleness.</summary>
            public readonly float TimeSec;

            public Note(string text, string author, float timeSec)
            {
                Text = text;
                Author = author;
                TimeSec = timeSec;
            }
        }

        /// <summary>Raised when a note is posted, so a module can turn it into an interrupt.</summary>
        public event Action<Note> NotePosted;

        private void Reset()
        {
            if (config == null)
                config = BehaviorLLMDefaults.FindShipped<BlackboardConfig>(BehaviorLLMDefaults.BlackboardConfigAsset);
        }

        private void Awake()
        {
            cfg = BehaviorLLMDefaults.OrTransientDefault(config);
        }

        /// <summary>Swaps the configuration at runtime. Null restores the defaults.</summary>
        public void ApplyConfig(BlackboardConfig newConfig)
        {
            config = newConfig;
            cfg = BehaviorLLMDefaults.OrTransientDefault(newConfig);
            TrimToCapacity();
        }

        /// <summary>
        /// Posts a note. Bind this to an action's On Execute and the model's argument arrives as
        /// the text. Blank notes are ignored, so an action that fires with no argument cannot
        /// fill the board with empty lines.
        /// </summary>
        public void Post(string text) => Post(text, null);

        /// <summary>Posts a note attributed to a named writer.</summary>
        public void Post(string text, string author)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string trimmed = text.Trim();
            int max = Mathf.Max(1, Cfg.entryMaxChars);
            if (trimmed.Length > max) trimmed = trimmed.Substring(0, max);

            Note note = new Note(trimmed, author ?? string.Empty, Time.time);
            notes.Add(note);
            TrimToCapacity();
            NotePosted?.Invoke(note);
        }

        /// <summary>
        /// The notes worth reporting, newest first: not yet stale, and no more than
        /// <paramref name="maxEntries"/> of them (0 for the configured cap, negative for all).
        /// </summary>
        public List<Note> Recent(int maxEntries = 0)
        {
            int cap = maxEntries > 0 ? maxEntries : (maxEntries == 0 ? Cfg.maxReportedEntries : int.MaxValue);
            if (cap <= 0) cap = int.MaxValue;

            float cutoff = Cfg.staleAfterSeconds > 0f ? Time.time - Cfg.staleAfterSeconds : float.NegativeInfinity;
            List<Note> result = new List<Note>();
            for (int i = notes.Count - 1; i >= 0 && result.Count < cap; i--)
            {
                if (notes[i].TimeSec < cutoff) break;   // older notes are older still
                result.Add(notes[i]);
            }
            return result;
        }

        /// <summary>Everything on the board, oldest first, stale or not. For tooling and tests.</summary>
        public IReadOnlyList<Note> All => notes;

        /// <summary>Wipes the board.</summary>
        public void Clear() => notes.Clear();

        private void TrimToCapacity()
        {
            int capacity = Mathf.Max(1, Cfg.capacity);
            if (notes.Count <= capacity) return;
            notes.RemoveRange(0, notes.Count - capacity);
        }
    }
}
