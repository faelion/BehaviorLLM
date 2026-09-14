using System.Collections.Generic;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Modules;
using NUnit.Framework;
using UnityEngine;

namespace BehaviorLLM.Tests.Runtime
{
    /// <summary>
    /// The shared blackboard exists to keep the observation block from growing with the square of
    /// the cast: one character reports something once and the others read a short line instead of
    /// perceiving it for themselves. These tests pin the properties that make it safe to put in a
    /// prompt — a hard cap on size, staleness, and no way for a rogue write to flood it.
    /// </summary>
    public class BlackboardTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in cleanup)
                if (o != null) Object.DestroyImmediate(o);
            cleanup.Clear();
        }

        private BehaviorLLMBlackboard MakeBoard(BlackboardConfig cfg)
        {
            GameObject go = new GameObject("Board");
            cleanup.Add(go);
            BehaviorLLMBlackboard board = go.AddComponent<BehaviorLLMBlackboard>();
            board.ApplyConfig(cfg);
            return board;
        }

        private BlackboardConfig MakeCfg(int capacity = 32, float stale = 0f, int maxReported = 6,
                                         int entryMax = 120)
        {
            BlackboardConfig cfg = ScriptableObject.CreateInstance<BlackboardConfig>();
            cfg.capacity = capacity;
            cfg.staleAfterSeconds = stale;
            cfg.maxReportedEntries = maxReported;
            cfg.entryMaxChars = entryMax;
            cleanup.Add(cfg);
            return cfg;
        }

        [Test]
        public void AnActionsFirstValueCanBePostedDirectly_SoOnExecuteBindsToTheBoard()
        {
            // The dispatch event carries ActionArguments, so this is the overload an action
            // binding reaches. A single-parameter "Report" action posts its value as the note.
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg());
            board.Post(BehaviorLLM.Core.Decisions.ActionArguments.Single("Report", "west gate is open"));
            board.Post((BehaviorLLM.Core.Decisions.ActionArguments)null);
            board.Post(BehaviorLLM.Core.Decisions.ActionArguments.Empty);

            Assert.AreEqual(1, board.All.Count, "null and empty arguments post nothing");
            Assert.AreEqual("west gate is open", board.All[0].Text);
            Assert.IsEmpty(board.All[0].Author, "a direct binding cannot know who is speaking");
        }

        [Test]
        public void CapacityIsAHardCeiling_OldestNotesFallOff()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg(capacity: 3));
            for (int i = 0; i < 10; i++) board.Post($"note {i}");

            Assert.AreEqual(3, board.All.Count, "A full board must drop the oldest note, not grow.");
            Assert.AreEqual("note 9", board.All[board.All.Count - 1].Text, "Newest note must survive.");
            Assert.AreEqual("note 7", board.All[0].Text, "The three most recent are what remain.");
        }

        [Test]
        public void LongNoteIsCut_SoOneWriteCannotSwallowThePrompt()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg(entryMax: 20));
            board.Post(new string('x', 500));

            Assert.AreEqual(20, board.All[0].Text.Length,
                "A note longer than the cap must be cut: it competes with the character's own " +
                "observations for room in the prompt.");
        }

        [Test]
        public void BlankNotesAreIgnored()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg());
            board.Post((string)null);
            board.Post("");
            board.Post("   ");
            Assert.AreEqual(0, board.All.Count,
                "An action firing with no argument must not fill the board with empty lines.");
        }

        [Test]
        public void RecentReturnsNewestFirst_AndHonoursTheReportingCap()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg(capacity: 20, maxReported: 3));
            for (int i = 0; i < 8; i++) board.Post($"n{i}");

            List<BehaviorLLMBlackboard.Note> recent = board.Recent();
            Assert.AreEqual(3, recent.Count, "The reporting cap, not the capacity, bounds a prompt.");
            Assert.AreEqual("n7", recent[0].Text, "Newest first: a character reads the latest news.");
            Assert.AreEqual("n5", recent[2].Text);
        }

        [Test]
        public void ExplicitMaxEntriesOverridesTheConfiguredCap()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg(maxReported: 2));
            for (int i = 0; i < 6; i++) board.Post($"n{i}");

            // ObservationComposer passes a per-decision budget; it must win over the asset.
            Assert.AreEqual(4, board.Recent(4).Count);
        }

        [Test]
        public void CapacityOfZeroDoesNotWipeTheBoard()
        {
            // A misconfigured asset should degrade to "keeps one", never to a crash or an
            // empty board that silently swallows every write.
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg(capacity: 0));
            board.Post("only");
            Assert.AreEqual(1, board.All.Count);
            Assert.AreEqual("only", board.All[0].Text);
        }

        [Test]
        public void AuthorIsCarriedThrough_SoCharactersCanTellWhoSaidWhat()
        {
            BehaviorLLMBlackboard board = MakeBoard(MakeCfg());
            board.Post("west gate open", "Guard_02");
            Assert.AreEqual("Guard_02", board.All[0].Author);
        }

        [Test]
        public void ObservationModuleRendersNotes_AndSaysSoWhenThereAreNone()
        {
            BlackboardConfig cfg = MakeCfg();
            cfg.includeAge = false;
            cfg.includeAuthor = true;
            BehaviorLLMBlackboard board = MakeBoard(cfg);

            GameObject go = new GameObject("Reader");
            cleanup.Add(go);
            BlackboardObservationModule module = go.AddComponent<BlackboardObservationModule>();
            module.ApplyConfig(cfg);
            module.ApplyBoard(board);

            StringAssert.Contains("No shared notes", module.GetObservation(),
                "An empty board must say so rather than returning nothing.");

            board.Post("west gate open", "Guard_02");
            string text = module.GetObservation();
            StringAssert.Contains("Guard_02: west gate open", text);
            StringAssert.StartsWith("- ", text, "Entries follow the same list shape as vision and memory.");
        }

        [Test]
        public void ModuleWithNoBoard_DegradesQuietly()
        {
            GameObject go = new GameObject("Orphan");
            cleanup.Add(go);
            BlackboardObservationModule module = go.AddComponent<BlackboardObservationModule>();
            module.ApplyConfig(MakeCfg());
            module.ApplyBoard(null);

            Assert.DoesNotThrow(() => module.GetObservation(),
                "A character pointed at no board must still compose an observation.");
            StringAssert.Contains("No shared notes", module.GetObservation());
        }

        [Test]
        public void InterruptIsConsumedOnce_SoOneNoteIsOneDecision()
        {
            BlackboardConfig cfg = MakeCfg();
            BehaviorLLMBlackboard board = MakeBoard(cfg);

            GameObject go = new GameObject("Reader");
            cleanup.Add(go);
            BlackboardObservationModule module = go.AddComponent<BlackboardObservationModule>();
            module.ApplyConfig(cfg);
            module.ApplyBoard(board);

            // Interrupts are opt-in; a board several characters write to would otherwise
            // interrupt everybody every time anybody posts.
            Assert.IsFalse(module.HasInterrupt(), "Interrupts must be off unless asked for.");

            board.Post("shots fired", "Guard_01");
            Assert.IsFalse(module.HasInterrupt(),
                "With interruptOnNewNote off, a note must not force a decision.");
        }
    }
}
