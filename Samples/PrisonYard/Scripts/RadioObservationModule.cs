using System.Text;
using BehaviorLLM.Core.Interfaces;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Puts the radio into one decision maker's prompt. This is a custom sense: the package finds
    /// it at Awake like any other observation module, and from then on this character hears the
    /// radio exactly as it sees the yard.
    ///
    /// It implements two interfaces. <see cref="IObservationModule"/> is the required one: a
    /// heading, some text, and whether something just happened that is worth interrupting for.
    /// <see cref="IBudgetedObservation"/> is optional and says "my output is a list, trim it if the
    /// prompt is too long", which is what lets Max Memory Entries cap the radio too.
    ///
    /// The interrupt is what makes the prison feel alive: a guard hears its section named and
    /// decides immediately, rather than finishing its two-and-a-half second wait.
    /// </summary>
    [AddComponentMenu("PrisonYard/Radio Observation Module")]
    public class RadioObservationModule : MonoBehaviour, IObservationModule, IBudgetedObservation
    {
        [Tooltip("Heading this appears under in the text sent to the AI.")]
        public string topicName = "Radio";

        [Tooltip("The name this listener is known by on the radio. It never hears its own messages, " +
                 "so this must match the name its own actions post under. Left empty, the " +
                 "GameObject's name is used.")]
        public string listenerName = "";

        [Tooltip("How many messages to show when the prompt budget does not say otherwise.")]
        public int defaultMessageCount = 4;

        [Tooltip("Interrupt the decision loop when a message names this listener, or the section it " +
                 "is currently in or assigned to. This is what makes a guard answer a radio call " +
                 "immediately. Turn it off and it will only hear the radio on its next scheduled tick.")]
        public bool interruptOnRelevantMessage = true;

        private PrisonRadio radio;
        private PrisonStatusBoard board;
        private GuardState guard;

        // Everything up to this id has already been considered for an interrupt. Without it the
        // same message would interrupt on every frame for as long as it stayed on the log.
        private int readUpTo;
        private bool interruptPending;

        public string TopicName => topicName;
        public ObservationKind Kind => ObservationKind.Memory;

        private string Listener => string.IsNullOrWhiteSpace(listenerName) ? gameObject.name : listenerName;

        private void Awake()
        {
            radio = FindFirstObjectByType<PrisonRadio>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            guard = GetComponent<GuardState>();
            if (radio != null) readUpTo = radio.NextSequence;
        }

        private void Update()
        {
            if (!interruptOnRelevantMessage || radio == null) return;

            var messages = radio.Messages;
            for (int i = 0; i < messages.Count; i++)
            {
                RadioMessage m = messages[i];
                if (m.Sequence < readUpTo) continue;
                readUpTo = m.Sequence + 1;
                if (IsFrom(m, Listener)) continue;
                if (IsRelevant(m)) interruptPending = true;
            }
        }

        /// <summary>True when a message is about this listener or the part of the prison it cares about.</summary>
        private bool IsRelevant(RadioMessage message)
        {
            if (!string.IsNullOrEmpty(message.Text) && message.Text.Contains(Listener)) return true;
            if (message.Section == null) return false;

            // A guard cares about the section it is assigned to; anyone else about the one they
            // are standing in.
            if (guard != null) return message.Section.Value == guard.assignedSection;
            if (board != null)
            {
                PrisonSection? here = board.SectionAt(transform.position);
                return here != null && here.Value == message.Section.Value;
            }
            return false;
        }

        private static bool IsFrom(RadioMessage message, string listener)
        {
            return string.Equals(message.Sender, listener, System.StringComparison.OrdinalIgnoreCase);
        }

        public bool HasInterrupt()
        {
            if (!interruptPending) return false;
            interruptPending = false;
            return true;
        }

        public string GetObservation()
        {
            return GetObservation(0);
        }

        /// <summary>The most recent messages this listener did not send, oldest first.</summary>
        public string GetObservation(int maxEntries)
        {
            if (radio == null) return "Radio silent.";

            int wanted = maxEntries > 0 ? maxEntries : Mathf.Max(1, defaultMessageCount);
            var messages = radio.Messages;

            // Walk backwards to collect the newest N that are not this listener's own, then print
            // them oldest-first so the log reads in the order things happened.
            int found = 0;
            int start = messages.Count;
            for (int i = messages.Count - 1; i >= 0 && found < wanted; i--)
            {
                if (IsFrom(messages[i], Listener)) continue;
                start = i;
                found++;
            }
            if (found == 0) return "Radio silent.";

            StringBuilder sb = new StringBuilder();
            for (int i = start; i < messages.Count; i++)
            {
                RadioMessage m = messages[i];
                if (IsFrom(m, Listener)) continue;
                int ago = Mathf.RoundToInt(Time.time - m.Time);
                sb.Append("- [").Append(ago).Append("s ago] ").Append(m.Sender).Append(": ").Append(m.Text).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
