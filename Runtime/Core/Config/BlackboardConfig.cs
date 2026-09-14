using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Settings for a shared blackboard: how much it remembers, how long an entry stays worth
    /// reporting, and how it reads in the prompt. Shared by the board and by every observation
    /// module that reports it, so one asset describes the whole channel.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBlackboardConfig", menuName = "BehaviorLLM/Blackboard Config", order = 5)]
    public class BlackboardConfig : ScriptableObject
    {
        [Header("Retention")]
        [Tooltip("How many notes the board keeps. The oldest is dropped when a new one arrives " +
                 "and the board is full. This is a ceiling on memory, not on what a character " +
                 "reads: how many of them reach a prompt is capped separately below. 32 is " +
                 "plenty for a scene of eight characters leaving each other short notes.")]
        public int capacity = 32;

        [Tooltip("Seconds after which a note stops being reported to characters. The note stays " +
                 "on the board, it simply stops being news. 0 means notes never go stale. Set " +
                 "this to roughly the time it takes a character to act on something: too long " +
                 "and characters react to a world that has moved on, which is the same failure " +
                 "as a stale action menu.")]
        public float staleAfterSeconds = 30f;

        [Header("Prompt")]
        [Tooltip("Heading the notes appear under in the state block, e.g. \"Radio\", \"Orders\" " +
                 "or \"Shared Notes\". Name it after what it is in your game, because the model " +
                 "reads this heading as a description of what the lines beneath it mean.")]
        public string topicName = "Shared Notes";

        [Tooltip("The most notes a single decision may be told about, newest first. 0 means no " +
                 "limit. This is the number that matters for prompt size, because it multiplies " +
                 "by every character reading the board every decision.")]
        public int maxReportedEntries = 6;

        [Tooltip("Longest a single note may be, in characters. Longer notes are cut. A blackboard " +
                 "note is a headline, not a paragraph: it competes with the character's own " +
                 "observations for room in the prompt, and the point of the board is to replace " +
                 "perceiving everything with being told the short version.")]
        public int entryMaxChars = 120;

        [Tooltip("Print who wrote each note. Worth it when characters need to know who said " +
                 "something ('Guard_02 reported a disturbance'), wasted tokens when they only " +
                 "need to know that it happened.")]
        public bool includeAuthor = true;

        [Tooltip("Print how long ago each note was written. Helps a character tell a fresh " +
                 "report from an old one, which matters whenever notes outlive a single decision.")]
        public bool includeAge = true;
    }
}
