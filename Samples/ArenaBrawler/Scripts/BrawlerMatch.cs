using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Decisions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// Runs a one-on-one fight between a human and the machine, first to
    /// <see cref="BrawlerRules.roundsToWin"/> rounds. Each frame it reads the human's controls,
    /// lets the machine's pad apply whatever the decision maker last chose, steps the arena and
    /// poses both fighters. <see cref="BrawlerFx"/> adds the feedback and <see cref="BrawlerHud"/>
    /// the interface; both listen to the events here.
    ///
    /// It also keeps the numbers the sample exists for: how long each machine decision took, how
    /// quickly the machine answered each of the human's punches, and what came of them. They are
    /// shown at the end of the match and written to a CSV.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Brawler Match")]
    public class BrawlerMatch : MonoBehaviour
    {
        [Header("Rules")]
        [Tooltip("The rules both fighters are held to.")]
        public BrawlerRules rules;

        [Header("Machine")]
        [Tooltip("The machine fighter's decision maker, read for its decision times.")]
        public DecisionMaker machine;

        [Tooltip("The machine fighter's pad, read for when its inputs land.")]
        public BrawlerMachinePad machinePad;

        [Tooltip("The client the machine decides through. Its model's name is what the interface " +
                 "calls the machine. Found in the scene when left empty.")]
        public BehaviorLLMClient client;

        [Header("Views")]
        [Tooltip("The human fighter's view.")]
        public BrawlerFighterView humanView;

        [Tooltip("The machine fighter's view.")]
        public BrawlerFighterView machineView;

        [Tooltip("The camera the fight is seen through.")]
        public Camera view;

        [Header("Practice")]
        [Tooltip("Let a simple scripted fighter take the human's side, for watching or for testing " +
                 "the machine without a person at the keyboard. Toggle it in play with T.")]
        public bool humanAutopilot = false;

        public BrawlerArena Arena { get; private set; }

        /// <summary>What the interface calls the machine: its model's name, without the file details.</summary>
        public string MachineName { get; private set; } = "Machine";
        public BrawlerRules Rules => rules;
        public readonly int[] RoundsWon = new int[2];
        public int Round { get; private set; } = 1;
        public float RoundTimeLeft { get; private set; }
        public bool Fighting => phase == Phase.Fighting;

        public enum Phase { Intro, Fighting, RoundOver, MatchOver }
        public Phase CurrentPhase => phase;

        /// <summary>Who won the round just ended, or -1 while one is under way.</summary>
        public int RoundWinner => roundWinner;

        /// <summary>Seconds left of the current intro or celebration.</summary>
        public float PhaseTimeLeft => phaseTimer;

        private Phase phase;
        private float phaseTimer;
        private int roundWinner = -1;
        private readonly bool[] alerts = new bool[2];

        public const int Human = 0, Machine = 1;

        // ---------------------------------------------------------------- events

        /// <summary>A round is about to start: its number. The fight starts after the intro.</summary>
        public event Action<int> RoundIntro;

        /// <summary>The fighters can move.</summary>
        public event Action Fight;

        /// <summary>A round was won: the winner, and whether by knockout rather than on time.</summary>
        public event Action<int, bool> RoundWon;

        /// <summary>The match was won.</summary>
        public event Action<int> MatchWon;

        /// <summary>The arena has just stepped; its events are this step's.</summary>
        public event Action<BrawlerArena> Stepped;

        /// <summary>A time-out with both fighters level: the round is replayed.</summary>
        public event Action Draw;

        // ---------------------------------------------------------------- stats

        private sealed class AttackRecord
        {
            public int Attacker;
            public bool Heavy;
            public float Time;
            public float ReactionMs = -1f;
            public string FirstInput = "";
            public string Outcome;
        }

        private readonly List<AttackRecord> attacks = new List<AttackRecord>();
        private readonly List<float> decisionMs = new List<float>();
        private readonly List<float> hookReactionMs = new List<float>();
        private float matchStart;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Arena = new BrawlerArena(rules);
            if (client == null) client = FindFirstObjectByType<BehaviorLLMClient>();
            MachineName = ShortName(client != null ? client.ModelConfig.displayName : null);
        }

        /// <summary>"LiquidAI d1 3B (Q4_K_M)" becomes "LiquidAI d1 3B"; nothing becomes "Machine".</summary>
        public static string ShortName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return "Machine";
            int paren = displayName.IndexOf('(');
            string name = (paren > 0 ? displayName.Substring(0, paren) : displayName).Trim();
            return name.Length > 0 ? name : "Machine";
        }

        private void OnEnable()
        {
            if (machine != null) machine.DecisionTelemetryRecorded += OnMachineDecision;
            if (machinePad != null) machinePad.InputApplied += OnMachineInput;
        }

        private void OnDisable()
        {
            if (machine != null) machine.DecisionTelemetryRecorded -= OnMachineDecision;
            if (machinePad != null) machinePad.InputApplied -= OnMachineInput;
        }

        private void Start() => Restart();

        private void Restart()
        {
            RoundsWon[0] = RoundsWon[1] = 0;
            Round = 1;
            attacks.Clear();
            decisionMs.Clear();
            hookReactionMs.Clear();
            matchStart = Time.time;
            StartRound();
        }

        private void StartRound()
        {
            Arena.Reset();
            if (machinePad != null) machinePad.Clear();
            RoundTimeLeft = rules.roundTime;
            roundWinner = -1;
            phase = Phase.Intro;
            phaseTimer = rules.roundPause;
            RoundIntro?.Invoke(Round);
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.rKey.wasPressedThisFrame) Restart();
            if (k != null && k.tKey.wasPressedThisFrame) humanAutopilot = !humanAutopilot;

            switch (phase)
            {
                case Phase.Intro:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f)
                    {
                        phase = Phase.Fighting;
                        Fight?.Invoke();
                    }
                    break;

                case Phase.Fighting:
                    if (humanAutopilot) Autopilot();
                    else ReadHuman();
                    Arena.Step(Time.deltaTime);
                    Stepped?.Invoke(Arena);
                    HandleEvents();
                    RoundTimeLeft -= Time.deltaTime;
                    CheckRoundEnd();
                    break;

                case Phase.RoundOver:
                    Arena.Step(Time.deltaTime);
                    Stepped?.Invoke(Arena);
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f) StartRound();
                    break;

                case Phase.MatchOver:
                    Arena.Step(Time.deltaTime);
                    Stepped?.Invoke(Arena);
                    break;
            }

            if (humanView != null) humanView.Show(Arena, Arena.Fighters[Human], phase != Phase.Fighting && roundWinner == Human);
            if (machineView != null) machineView.Show(Arena, Arena.Fighters[Machine], phase != Phase.Fighting && roundWinner == Machine);
        }

        private void CheckRoundEnd()
        {
            BrawlerFighter h = Arena.Fighters[Human], m = Arena.Fighters[Machine];
            int winner = -1;
            if (h.State == FighterState.KO) winner = Machine;
            else if (m.State == FighterState.KO) winner = Human;
            else if (RoundTimeLeft <= 0f)
            {
                if (Mathf.Approximately(h.Health, m.Health)) { Draw?.Invoke(); StartRound(); return; }
                winner = h.Health > m.Health ? Human : Machine;
            }
            if (winner < 0) return;

            roundWinner = winner;
            RoundsWon[winner]++;
            bool knockout = Arena.Fighters[1 - winner].State == FighterState.KO;
            Arena.Fighters[Human].Move = Arena.Fighters[Machine].Move = Vector2.zero;
            Arena.Fighters[Human].BlockHeld = Arena.Fighters[Machine].BlockHeld = false;
            if (RoundsWon[winner] >= rules.roundsToWin)
            {
                phase = Phase.MatchOver;
                WriteReport();
                RoundWon?.Invoke(winner, knockout);
                MatchWon?.Invoke(winner);
            }
            else
            {
                phase = Phase.RoundOver;
                phaseTimer = rules.celebrationTime;
                RoundWon?.Invoke(winner, knockout);
                Round++;
            }
        }

        // ---------------------------------------------------------------- human controls

        private void ReadHuman()
        {
            BrawlerFighter me = Arena.Fighters[Human];
            Keyboard k = Keyboard.current;
            Mouse m = Mouse.current;
            Gamepad g = Gamepad.current;

            Vector2 move = Vector2.zero;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) move.y += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) move.y -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) move.x += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) move.x -= 1f;
            }
            if (g != null && g.leftStick.ReadValue().sqrMagnitude > 0.04f) move = g.leftStick.ReadValue();
            me.Move = Vector2.ClampMagnitude(move, 1f);

            me.BlockHeld = (k != null && (k.lKey.isPressed || k.leftShiftKey.isPressed)) ||
                           (g != null && (g.rightShoulder.isPressed || g.leftTrigger.ReadValue() > 0.5f));

            if ((k != null && k.jKey.wasPressedThisFrame) || (m != null && m.leftButton.wasPressedThisFrame) ||
                (g != null && g.buttonWest.wasPressedThisFrame))
                Arena.Press(Human, FighterPress.Jab);
            if ((k != null && k.kKey.wasPressedThisFrame) || (m != null && m.rightButton.wasPressedThisFrame) ||
                (g != null && g.buttonNorth.wasPressedThisFrame))
                Arena.Press(Human, FighterPress.Hook);
            if ((k != null && k.spaceKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame))
                Arena.Press(Human, FighterPress.Dash, move);
        }

        private float autopilotNext;
        private float autopilotSawAt = -1f;
        private readonly System.Random autopilotRandom = new System.Random(7);

        /// <summary>
        /// A deliberately plain opponent for practice. It closes in, jabs and now and then hooks,
        /// punishes a missed hook, and answers a hook it sees coming a quarter of a second after it
        /// starts, about as fast as a person reacts: a roll out of the way, or a block for a jab.
        /// </summary>
        private void Autopilot()
        {
            BrawlerFighter me = Arena.Fighters[Human];
            BrawlerFighter them = Arena.Fighters[Machine];
            float d = Vector2.Distance(me.Position, them.Position);

            if (Arena.PhaseOf(them) == AttackPhase.Windup && Arena.InReach(them, me, rules.hook.range + 0.3f))
            {
                if (autopilotSawAt < 0f) autopilotSawAt = Time.time;
                if (Time.time - autopilotSawAt >= 0.25f)
                {
                    me.Move = Vector2.zero;
                    if (them.State == FighterState.Hook && me.CanDash) Arena.Press(Human, FighterPress.Dash, Arena.Relative(Human, BrawlerIds.Away));
                    else me.BlockHeld = true;
                }
                return;
            }
            autopilotSawAt = -1f;
            me.BlockHeld = false;

            if (Arena.PhaseOf(them) == AttackPhase.Recovery && them.State == FighterState.Hook && d <= rules.hook.range)
            {
                Arena.Press(Human, FighterPress.Hook);
                return;
            }

            if (d > rules.jab.range * 0.9f) { me.Move = Arena.Relative(Human, BrawlerIds.Toward); return; }
            me.Move = Vector2.zero;
            if (Time.time < autopilotNext) return;
            autopilotNext = Time.time + 0.35f + (float)autopilotRandom.NextDouble() * 0.4f;
            double roll = autopilotRandom.NextDouble();
            if (roll < 0.6) Arena.Press(Human, FighterPress.Jab);
            else if (roll < 0.8) Arena.Press(Human, FighterPress.Hook);
            else me.Move = Arena.Relative(Human, autopilotRandom.NextDouble() < 0.5 ? BrawlerIds.CircleLeft : BrawlerIds.CircleRight);
        }

        // ---------------------------------------------------------------- events

        private void HandleEvents()
        {
            for (int i = 0; i < Arena.Events.Count; i++)
            {
                ArenaEvent e = Arena.Events[i];
                switch (e.Kind)
                {
                    case ArenaEventKind.AttackStarted:
                        alerts[1 - e.Fighter] = true;
                        attacks.Add(new AttackRecord { Attacker = e.Fighter, Heavy = e.Heavy, Time = Time.time });
                        break;
                    case ArenaEventKind.DashStarted:
                        alerts[1 - e.Fighter] = true;
                        break;
                    case ArenaEventKind.Hit: Close(e.Fighter, "hit"); break;
                    case ArenaEventKind.Blocked: Close(e.Fighter, "blocked"); break;
                    case ArenaEventKind.GuardBreak: Close(e.Fighter, "guard broken"); break;
                    case ArenaEventKind.Whiff: Close(e.Fighter, "missed"); break;
                }
            }
        }

        private void Close(int attacker, string outcome)
        {
            for (int i = attacks.Count - 1; i >= 0; i--)
            {
                if (attacks[i].Attacker == attacker && attacks[i].Outcome == null) { attacks[i].Outcome = outcome; return; }
            }
        }

        /// <summary>True once after each punch or dash by <paramref name="fighter"/>'s opponent.</summary>
        public bool ConsumeAlert(int fighter)
        {
            if (!alerts[fighter]) return false;
            alerts[fighter] = false;
            return true;
        }

        /// <summary>
        /// Seconds between the machine choosing an input and the input reaching the fight: the
        /// median of its last twenty decisions plus the configured reaction delay. Zero for the human.
        /// </summary>
        public float InputDelaySeconds(int fighter)
        {
            if (fighter != Machine || decisionMs.Count == 0) return 0f;
            int from = Mathf.Max(0, decisionMs.Count - 20);
            return Median(decisionMs.GetRange(from, decisionMs.Count - from)) / 1000f + rules.machineReactionDelay;
        }

        private void OnMachineDecision(DecisionTelemetry t)
        {
            if (t.resultType == DecisionResultType.ModelAction || t.resultType == DecisionResultType.FallbackAction)
                decisionMs.Add(t.latencyMs);
        }

        private void OnMachineInput(string input)
        {
            for (int i = 0; i < attacks.Count; i++)
            {
                AttackRecord r = attacks[i];
                if (r.Attacker != Human || r.ReactionMs >= 0f || r.Outcome != null) continue;
                r.ReactionMs = (Time.time - r.Time) * 1000f;
                r.FirstInput = input;
                if (r.Heavy) hookReactionMs.Add(r.ReactionMs);
            }
        }

        // ---------------------------------------------------------------- stats

        public float MachineDecisionMedianMs => Median(decisionMs);
        public float MachineHookReactionMedianMs => Median(hookReactionMs);

        /// <summary>How many of the human's hooks and jabs landed, of how many that were resolved.</summary>
        public void HumanPunches(out int hooks, out int hooksLanded, out int jabs, out int jabsLanded)
        {
            hooks = hooksLanded = jabs = jabsLanded = 0;
            for (int i = 0; i < attacks.Count; i++)
            {
                AttackRecord a = attacks[i];
                if (a.Attacker != Human || a.Outcome == null) continue;
                bool landed = a.Outcome == "hit" || a.Outcome == "guard broken";
                if (a.Heavy) { hooks++; if (landed) hooksLanded++; }
                else { jabs++; if (landed) jabsLanded++; }
            }
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0) return 0f;
            List<float> sorted = new List<float>(values);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        // ---------------------------------------------------------------- report

        private void WriteReport()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "BehaviorLLM", "ArenaBrawler");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                StringBuilder sb = new StringBuilder("time_s,attacker,punch,outcome,machine_reaction_ms,machine_first_input\n");
                for (int i = 0; i < attacks.Count; i++)
                {
                    AttackRecord a = attacks[i];
                    sb.Append((a.Time - matchStart).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(a.Attacker == Human ? "human" : "machine").Append(',')
                      .Append(a.Heavy ? "hook" : "jab").Append(',')
                      .Append(a.Outcome ?? "").Append(',')
                      .Append(a.ReactionMs >= 0f ? a.ReactionMs.ToString("0", CultureInfo.InvariantCulture) : "").Append(',')
                      .Append(a.FirstInput).Append('\n');
                }
                sb.Append($"# rounds human {RoundsWon[Human]} machine {RoundsWon[Machine]}, machine decision median {Median(decisionMs):0} ms over {decisionMs.Count}\n");
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[ArenaBrawler] Match report written to {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArenaBrawler] Could not write the match report: {e.Message}");
            }
        }
    }
}
