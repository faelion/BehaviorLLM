using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BehaviorLLM.Core.Decisions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// Runs a one-on-one dodgeball match between a human (left half) and the machine (right half).
    ///
    /// Each frame it reads the human's controls, lets the machine's pad apply whatever the decision
    /// maker last chose, steps the court, and draws it. A hit scores for the thrower, a catch for
    /// the catcher, first to <see cref="DodgeballRules.pointsToWin"/>.
    ///
    /// It also keeps the numbers the sample exists for: how long each machine decision took, how
    /// quickly the machine responded to each of the human's throws, and what came of those throws.
    /// They are on screen during play and written to a CSV when the match ends.
    /// </summary>
    [AddComponentMenu("Dodgeball/Dodgeball Match")]
    public class DodgeballMatch : MonoBehaviour
    {
        [Header("Rules")]
        [Tooltip("The rules both players are held to.")]
        public DodgeballRules rules;

        [Header("Machine")]
        [Tooltip("The machine player's decision maker, read for its decision times.")]
        public DecisionMaker machine;

        [Tooltip("The machine player's pad, read for when its inputs land.")]
        public DodgeballMachinePad machinePad;

        [Header("Views")]
        public Transform humanView;
        public Transform machineView;
        public Transform humanCatchRing;
        public Transform machineCatchRing;
        public Transform[] ballViews;
        public Camera view;

        [Header("Practice")]
        [Tooltip("Let a simple scripted player take the human's side, for watching or for testing " +
                 "the machine without a person at the keyboard. Toggle it in play with T.")]
        public bool humanAutopilot = false;

        public DodgeballCourt Court { get; private set; }
        public DodgeballRules Rules => rules;
        public readonly int[] Score = new int[2];
        public bool Playing => phase == Phase.Playing;

        private enum Phase { Serve, Playing, Over }
        private Phase phase = Phase.Serve;
        private float phaseTimer;
        private readonly bool[] alerts = new bool[2];
        private string banner = "";
        private System.Random autopilotRandom = new System.Random(7);
        private float autopilotReactAt = -1f;

        private const int Human = 0, Machine = 1;

        // ---------------------------------------------------------------- stats

        private sealed class ThrowRecord
        {
            public int Ball;
            public int Thrower;
            public float Time;
            public bool Threat;
            public float ReactionMs = -1f;
            public string FirstInput = "";
            public string Outcome;
        }

        private readonly List<ThrowRecord> throws = new List<ThrowRecord>();
        private readonly List<float> decisionMs = new List<float>();
        private readonly List<float> reactionMs = new List<float>();
        private float matchStart;
        private string lastDecisionCaption = "";

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Court = new DodgeballCourt(rules);
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
            Score[0] = Score[1] = 0;
            throws.Clear();
            decisionMs.Clear();
            reactionMs.Clear();
            matchStart = Time.time;
            StartServe("First to " + rules.pointsToWin);
        }

        private void StartServe(string message)
        {
            Court.Reset();
            if (machinePad != null) machinePad.Clear();
            phase = Phase.Serve;
            phaseTimer = rules.pointPause;
            banner = message;
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.rKey.wasPressedThisFrame) Restart();
            if (k != null && k.tKey.wasPressedThisFrame) humanAutopilot = !humanAutopilot;

            switch (phase)
            {
                case Phase.Serve:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f) { phase = Phase.Playing; banner = ""; }
                    break;

                case Phase.Playing:
                    if (humanAutopilot) Autopilot();
                    else ReadHuman();
                    Court.Step(Time.deltaTime);
                    HandleEvents();
                    break;
            }

            Draw();
        }

        // ---------------------------------------------------------------- human controls

        private void ReadHuman()
        {
            CourtPlayer me = Court.Players[Human];
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

            bool throwAtOpponent = (k != null && k.jKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
            if (m != null && m.leftButton.wasPressedThisFrame && view != null)
            {
                Ray ray = view.ScreenPointToRay(m.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
                {
                    Vector3 hit = ray.GetPoint(d);
                    Court.Throw(Human, new Vector2(hit.x, hit.z));
                }
            }
            else if (throwAtOpponent) Court.Throw(Human, Court.Players[Machine].Position);

            if ((m != null && m.rightButton.wasPressedThisFrame) || (k != null && k.kKey.wasPressedThisFrame) ||
                (g != null && g.buttonEast.wasPressedThisFrame))
                Court.OpenCatch(Human);

            if ((k != null && (k.spaceKey.wasPressedThisFrame || k.leftShiftKey.wasPressedThisFrame)) ||
                (g != null && g.rightShoulder.wasPressedThisFrame))
                Court.Dodge(Human, move.sqrMagnitude > 0.01f ? move : new Vector2(-me.Forward, 0f));
        }

        /// <summary>
        /// A deliberately plain opponent for practice: it fetches the nearest ball, throws straight
        /// at the machine, and dodges a ball that will hit it a quarter of a second after it is
        /// thrown, about as fast as a person reacts.
        /// </summary>
        private void Autopilot()
        {
            CourtPlayer me = Court.Players[Human];
            CourtPlayer them = Court.Players[Machine];

            for (int i = 0; i < Court.Balls.Count; i++)
            {
                CourtBall b = Court.Balls[i];
                if (b.State != BallState.Flying || b.Thrower == Human) continue;
                DodgeballCourt.ClosestApproach(b, me.Position, out float t, out float miss, out float side);
                if (t <= 0f || miss > rules.playerRadius + rules.ballRadius) continue;
                if (autopilotReactAt < 0f) autopilotReactAt = Time.time + 0.25f;
                if (Time.time >= autopilotReactAt)
                {
                    Court.Dodge(Human, new Vector2(0f, side >= 0f ? -1f : 1f));
                    autopilotReactAt = -1f;
                }
                return;
            }
            autopilotReactAt = -1f;

            if (me.Holding)
            {
                // Back behind the throwing line first, then throw after a short random wait.
                if (!Court.CanThrow(Human)) { me.Move = new Vector2(-me.Forward, 0f); return; }
                me.Move = Vector2.zero;
                if (autopilotRandom.NextDouble() < Time.deltaTime * 1.5) Court.Throw(Human, them.Position);
                return;
            }

            CourtBall nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < Court.Balls.Count; i++)
            {
                CourtBall b = Court.Balls[i];
                if (b.State != BallState.Free || b.Position.x > 0.5f) continue;
                float d = (b.Position - me.Position).sqrMagnitude;
                if (d < best) { best = d; nearest = b; }
            }
            me.Move = nearest != null ? (nearest.Position - me.Position).normalized : new Vector2(0f, -me.Position.y * 0.3f);
        }

        // ---------------------------------------------------------------- events and scoring

        private void HandleEvents()
        {
            for (int i = 0; i < Court.Events.Count; i++)
            {
                CourtEvent e = Court.Events[i];
                switch (e.Kind)
                {
                    case CourtEventKind.Throw:
                        alerts[1 - e.Player] = true;
                        CourtBall ball = Court.Balls[e.Ball];
                        DodgeballCourt.ClosestApproach(ball, Court.Opponent(e.Player).Position, out float t, out float miss, out _);
                        throws.Add(new ThrowRecord
                        {
                            Ball = e.Ball,
                            Thrower = e.Player,
                            Time = Time.time,
                            Threat = t > 0f && miss <= rules.playerRadius + rules.ballRadius
                        });
                        break;
                    case CourtEventKind.Hit:
                        Close(e.Ball, "hit");
                        break;
                    case CourtEventKind.Catch:
                        Close(e.Ball, "caught");
                        break;
                }
            }

            // A throw that stopped against a wall missed; one that would have hit a player standing
            // still was avoided.
            for (int i = 0; i < throws.Count; i++)
            {
                ThrowRecord r = throws[i];
                if (r.Outcome == null && Court.Balls[r.Ball].State != BallState.Flying)
                    r.Outcome = r.Threat ? "avoided" : "missed";
            }

            if (Court.PointWinner >= 0)
            {
                int winner = Court.PointWinner;
                Score[winner]++;
                string how = Court.Events.Exists(e => e.Kind == CourtEventKind.Catch) ? "catch" : "hit";
                string who = winner == Human ? "Human" : "Machine";
                if (Score[winner] >= rules.pointsToWin)
                {
                    phase = Phase.Over;
                    banner = $"{who} wins {Score[winner]}-{Score[1 - winner]}. Press R to play again.";
                    WriteReport();
                }
                else StartServe($"{who} scores by {how}");
            }
        }

        private void Close(int ball, string outcome)
        {
            for (int i = throws.Count - 1; i >= 0; i--)
            {
                if (throws[i].Ball == ball && throws[i].Outcome == null) { throws[i].Outcome = outcome; return; }
            }
        }

        /// <summary>
        /// Seconds between the machine choosing an input and the input reaching the court: the
        /// median of its last twenty decisions plus the configured reaction delay. Zero for the human.
        /// </summary>
        public float InputDelaySeconds(int player)
        {
            if (player != Machine || decisionMs.Count == 0) return 0f;
            int from = Mathf.Max(0, decisionMs.Count - 20);
            List<float> recent = decisionMs.GetRange(from, decisionMs.Count - from);
            return Median(recent) / 1000f + rules.machineReactionDelay;
        }

        /// <summary>True once per throw at <paramref name="player"/>, then false until the next one.</summary>
        public bool ConsumeAlert(int player)
        {
            if (!alerts[player]) return false;
            alerts[player] = false;
            return true;
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
            for (int i = 0; i < throws.Count; i++)
            {
                ThrowRecord r = throws[i];
                if (r.Thrower != Human || r.ReactionMs >= 0f || r.Outcome != null) continue;
                r.ReactionMs = (Time.time - r.Time) * 1000f;
                r.FirstInput = input;
                reactionMs.Add(r.ReactionMs);
            }
        }

        // ---------------------------------------------------------------- drawing

        private void Draw()
        {
            Place(humanView, Court.Players[Human].Position, 0f);
            Place(machineView, Court.Players[Machine].Position, 0f);
            Ring(humanCatchRing, Court.Players[Human]);
            Ring(machineCatchRing, Court.Players[Machine]);
            if (ballViews != null)
            {
                for (int i = 0; i < ballViews.Length && i < Court.Balls.Count; i++)
                    Place(ballViews[i], Court.Balls[i].Position, Court.Balls[i].State == BallState.Held ? 0.6f : 0.25f);
            }
        }

        private static void Place(Transform t, Vector2 p, float height)
        {
            if (t != null) t.position = new Vector3(p.x, height, p.y);
        }

        private static void Ring(Transform ring, CourtPlayer p)
        {
            if (ring == null) return;
            ring.gameObject.SetActive(p.Catching);
            ring.position = new Vector3(p.Position.x, 0.02f, p.Position.y);
        }

        // ---------------------------------------------------------------- HUD

        private GUIStyle big, small, caption;

        private void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
                caption = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerCenter };
            }

            GUI.Label(new Rect(0, 8, Screen.width, 44), $"HUMAN  {Score[Human]} : {Score[Machine]}  MACHINE", big);
            if (!string.IsNullOrEmpty(banner))
                GUI.Label(new Rect(0, 52, Screen.width, 30), banner, new GUIStyle(small) { alignment = TextAnchor.UpperCenter, fontSize = 18 });

            if (view != null && machineView != null)
            {
                Vector3 s = view.WorldToScreenPoint(machineView.position + Vector3.forward * 0.9f);
                GUI.Label(new Rect(s.x - 160, Screen.height - s.y - 26, 320, 24), lastDecisionCaption, caption);
            }

            GUI.Label(new Rect(12, Screen.height - 96, 520, 90),
                (humanAutopilot ? "AUTOPILOT is playing for you (T to take over).\n" : "") +
                "Move: WASD or left stick.  Throw from behind your dashed line: left click at a spot, or J / A at the machine.\n" +
                "Catch: right click, K or B (opens briefly: time it).  Dodge: Space, Shift or RB.\n" +
                "R restarts.  T toggles the autopilot.", small);

            GUI.Label(new Rect(Screen.width - 360, Screen.height - 120, 350, 115), Stats(), small);
        }

        private string Stats()
        {
            int caught = 0, avoided = 0, hit = 0, threats = 0;
            for (int i = 0; i < throws.Count; i++)
            {
                ThrowRecord r = throws[i];
                if (r.Thrower != Human || !r.Threat) continue;
                threats++;
                if (r.Outcome == "caught") caught++;
                else if (r.Outcome == "hit") hit++;
                else if (r.Outcome == "avoided") avoided++;
            }
            return $"Machine decision: {Median(decisionMs):0} ms median ({decisionMs.Count})\n" +
                   $"Machine reaction to your throws: {Median(reactionMs):0} ms median\n" +
                   $"Your on-target throws: {threats}  caught {caught}, dodged {avoided}, hit {hit}\n" +
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
                string dir = Path.Combine(Application.persistentDataPath, "BehaviorLLM", "Dodgeball");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"match_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                StringBuilder sb = new StringBuilder("time_s,thrower,on_target,outcome,machine_reaction_ms,machine_first_input\n");
                for (int i = 0; i < throws.Count; i++)
                {
                    ThrowRecord r = throws[i];
                    sb.Append((r.Time - matchStart).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                      .Append(r.Thrower == Human ? "human" : "machine").Append(',')
                      .Append(r.Threat ? "yes" : "no").Append(',')
                      .Append(r.Outcome ?? "").Append(',')
                      .Append(r.ReactionMs >= 0f ? r.ReactionMs.ToString("0", CultureInfo.InvariantCulture) : "").Append(',')
                      .Append(r.FirstInput).Append('\n');
                }
                sb.Append($"# score human {Score[Human]} machine {Score[Machine]}, machine decision median {Median(decisionMs):0} ms over {decisionMs.Count}\n");
                File.WriteAllText(path, sb.ToString());
                Debug.Log($"[Dodgeball] Match report written to {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Dodgeball] Could not write the match report: {e.Message}");
            }
        }
    }
}
