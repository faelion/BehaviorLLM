using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>One thing somebody said over the radio.</summary>
    public struct RadioMessage
    {
        /// <summary>Who spoke, by the name the model knows them as.</summary>
        public string Sender;
        /// <summary>What was said, already in the words the model will read.</summary>
        public string Text;
        /// <summary>Which section it was about, when it was about one.</summary>
        public PrisonSection? Section;
        /// <summary>Game time it was posted, for the "12s ago" in the prompt.</summary>
        public float Time;
        /// <summary>Ever-increasing id, so a listener can tell what it has not read yet.</summary>
        public int Sequence;
    }

    /// <summary>
    /// The prison's radio channel: a small log that actions write to and observation modules read
    /// from. This is how one decision maker's action reaches another's prompt, and it is the whole
    /// mechanism. Nothing here calls a decision maker; a guard's Report posts a line, and every
    /// listener sees that line in its next state block, exactly as a person would.
    ///
    /// A scene component rather than a singleton, per the package's own rule: find it with
    /// FindFirstObjectByType and hold the reference.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Radio")]
    public class PrisonRadio : MonoBehaviour
    {
        [Tooltip("How many messages to keep. Older ones are forgotten. 32 is far more than any " +
                 "prompt shows; the limit only stops the list growing all run.")]
        public int capacity = 32;

        [Tooltip("Also print every radio message to the Unity console, so you can follow the " +
                 "conversation without reading prompts.")]
        public bool logToConsole = true;

        private readonly List<RadioMessage> messages = new List<RadioMessage>();
        private int nextSequence;

        /// <summary>Everything still on the log, oldest first.</summary>
        public IReadOnlyList<RadioMessage> Messages => messages;

        /// <summary>The id the next message will get, so a listener can record where it got to.</summary>
        public int NextSequence => nextSequence;

        /// <summary>
        /// Says something on the radio. <paramref name="about"/> is the section the message
        /// concerns, which is what decides whose attention it catches.
        /// </summary>
        public void Post(string sender, string text, PrisonSection? about = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            messages.Add(new RadioMessage
            {
                Sender = string.IsNullOrWhiteSpace(sender) ? "Control" : sender,
                Text = text,
                Section = about,
                Time = UnityEngine.Time.time,
                Sequence = nextSequence++
            });

            while (messages.Count > Mathf.Max(1, capacity)) messages.RemoveAt(0);

            if (logToConsole) Debug.Log($"[Radio] {sender}: {text}");
        }

        /// <summary>Forgets everything. Only used when a run restarts.</summary>
        public void Clear()
        {
            messages.Clear();
        }
    }
}
