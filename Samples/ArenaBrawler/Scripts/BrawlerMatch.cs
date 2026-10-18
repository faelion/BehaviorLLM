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
    /// draws both fighters.
    ///
    /// It also keeps the numbers the sample exists for: how long each machine decision took, how
    /// quickly the machine answered each of the human's punches, and what came of them. They are
    /// on screen during play and written to a CSV when the match ends.
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
        public BrawlerFighterView humanView;
        public BrawlerFighterView machineView;
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

        private enum Phase { Intro, Fighting, RoundOver, MatchOver }
        private Phase phase;
        private float phaseTimer;
        private int roundWinner = -1;
        private string banner = "";
        private readonly bool[] alerts = new bool[2];

        private const int Human = 0, Machine = 1;

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
        private string lastDecisionCaption = "";

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
            banner = $"Round {Round}";
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
                    if (phaseTimer <= 0f) { phase = Phase.Fighting; banner = ""; }
                    break;

                case Phase.Fighting:
                    if (humanAutopilot) Autopilot();
                    else ReadHuman();
                    Arena.Step(Time.deltaTime);
                    HandleEvents();
                    RoundTimeLeft -= Time.deltaTime;
                    CheckRoundEnd();
                    break;

                case Phase.RoundOver:
                    Arena.Step(Time.deltaTime);
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f) StartRound();
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
                if (Mathf.Approximately(h.Health, m.Health)) { banner = "Time: a draw, the round is replayed"; StartRound(); return; }
                winner = h.Health > m.Health ? Human : Machine;
            }
            if (winner < 0) return;

            roundWinner = winner;
            RoundsWon[winner]++;
            string who = winner == Human ? "You" : MachineName;
            Arena.Fighters[Human].Move = Arena.Fighters[Machine].Move = Vector2.zero;
            Arena.Fighters[Human].BlockHeld = Arena.Fighters[Machine].BlockHeld = false;
            if (RoundsWon[winner] >= rules.roundsToWin)
            {
                phase = Phase.MatchOver;
                banner = $"{who} {(winner == Human ? "win" : "wins")} the match {RoundsWon[winner]}-{RoundsWon[1 - winner]}. Press R to fight again.";
                WriteReport();
            }
            else
            {
                phase = Phase.RoundOver;
                phaseTimer = rules.roundPause;
                banner = $"{who} {(winner == Human ? "take" : "takes")} round {Round}";
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
            lastDecisionCaption = t.actionName + (string.IsNullOrEmpty(t.argument) ? "" : " " + t.argument) +
                                  (t.choiceProbability > 0f ? $"  p={t.choiceProbability.ToString("0.00", CultureInfo.InvariantCulture)}" : "");
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

        // ---------------------------------------------------------------- HUD

        private GUIStyle big, small, caption;
        private Texture2D white;

        private void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                caption = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
                white = Texture2D.whiteTexture;
            }

            HealthBar(new Rect(20, 16, Screen.width * 0.38f, 22), Arena.Fighters[Human].Health / rules.health, new Color(0.3f, 0.6f, 1f), false);
            HealthBar(new Rect(Screen.width * 0.62f - 20, 16, Screen.width * 0.38f, 22), Arena.Fighters[Machine].Health / rules.health, new Color(1f, 0.35f, 0.3f), true);
            GUI.Label(new Rect(0, 8, Screen.width, 40), $"{RoundsWon[Human]}  :  {RoundsWon[Machine]}", big);
            GUI.Label(new Rect(0, 42, Screen.width, 24), $"Round {Round}   {Mathf.CeilToInt(Mathf.Max(0f, RoundTimeLeft))} s",
                      new GUIStyle(small) { alignment = TextAnchor.UpperCenter });
            GUI.Label(new Rect(20, 40, 200, 20), "YOU", small);
            GUI.Label(new Rect(Screen.width - 320, 40, 300, 20), MachineName.ToUpperInvariant(), new GUIStyle(small) { alignment = TextAnchor.UpperRight });
            if (!string.IsNullOrEmpty(banner))
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40), banner, big);

            if (view != null && machineView != null)
            {
                Vector3 s = view.WorldToScreenPoint(machineView.transform.position + Vector3.up * 2.3f);
                GUI.Label(new Rect(s.x - 160, Screen.height - s.y - 24, 320, 22), lastDecisionCaption, caption);
            }

            GUI.Label(new Rect(12, Screen.height - 100, 560, 95),
                (humanAutopilot ? "AUTOPILOT is fighting for you (T to take over).\n" : "") +
                "Move: WASD or left stick.  Jab: J, left click or X.  Hook: K, right click or Y.\n" +
                "Block (hold): L, Shift, RB or LT.  Dash: Space or A, in the direction you are moving.\n" +
                "The ring under a fighter shows a wind-up: yellow jab, red hook. R restarts, T toggles the autopilot.", small);
            GUI.Label(new Rect(Screen.width - 380, Screen.height - 120, 370, 115), Stats(), small);
        }

        private void HealthBar(Rect r, float fraction, Color c, bool rightToLeft)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, white);
            GUI.color = c;
            float w = r.width * Mathf.Clamp01(fraction);
            GUI.DrawTexture(new Rect(rightToLeft ? r.xMax - w : r.x, r.y, w, r.height), white);
            GUI.color = Color.white;
        }

        private string Stats()
        {
            int hooks = 0, hookHit = 0, jabs = 0, jabHit = 0;
            for (int i = 0; i < attacks.Count; i++)
            {
                AttackRecord a = attacks[i];
                if (a.Attacker != Human || a.Outcome == null) continue;
                bool landed = a.Outcome == "hit" || a.Outcome == "guard broken";
                if (a.Heavy) { hooks++; if (landed) hookHit++; }
                else { jabs++; if (landed) jabHit++; }
            }
            return $"Machine decision: {Median(decisionMs):0} ms median ({decisionMs.Count})\n" +
                   $"Machine reaction to your hooks: {Median(hookReactionMs):0} ms median\n" +
                   $"Your hooks landed {hookHit}/{hooks}, jabs landed {jabHit}/{jabs}\n" +
                   $"Machine reaction delay: {rules.machineReactionDelay:0.00} s";
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
