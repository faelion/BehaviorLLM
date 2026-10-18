using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Decisions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// Runs a one-on-one dodgeball match between a human (left half) and the machine (right half):
    /// reads the human's controls, lets the machine's pad apply whatever the decision maker last
    /// chose, steps the court, and scores. A hit scores for the thrower, a catch for the catcher,
    /// first to <see cref="DodgeballRules.pointsToWin"/>.
    ///
    /// It draws nothing. <see cref="DodgeballView"/> draws the court and does the feedback;
    /// <see cref="DodgeballHud"/> draws the interface. Both read the court and listen to the events
    /// here, so the rules never wait on the presentation.
    ///
    /// It also keeps the numbers the sample exists for: how long each machine decision took, how
    /// quickly the machine responded to each of the human's throws, and what came of those throws.
    /// They are written to a CSV when the match ends.
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

        [Tooltip("The client the machine decides through. Its model's name is what the interface " +
                 "calls the machine.")]
        public BehaviorLLMClient client;

        [Header("Input")]
        [Tooltip("The camera the court is seen through, used to turn the mouse into a court position.")]
        public Camera view;

        [Header("Practice")]
        [Tooltip("Let a simple scripted player take the human's side, for watching or for testing " +
                 "the machine without a person at the keyboard. Toggle it in play with T.")]
        public bool humanAutopilot = false;

        public enum Phase { Serve, Playing, Point, Over }

        /// <summary>Seconds the court stays frozen on the play that won a point, before the next serve.</summary>
        public const float PointReplaySeconds = 1.1f;

        public DodgeballCourt Court { get; private set; }
        public DodgeballRules Rules => rules;
        public readonly int[] Score = new int[2];
        public Phase CurrentPhase { get; private set; } = Phase.Serve;
        public bool Playing => CurrentPhase == Phase.Playing;

        /// <summary>Seconds until the ball is live, during a serve.</summary>
        public float ServeTimeLeft => CurrentPhase == Phase.Serve ? phaseTimer : 0f;

        /// <summary>Where the human is aiming, in court coordinates.</summary>
        public Vector2 HumanAim { get; private set; }

        /// <summary>True while the aim follows a pointer, rather than defaulting to the machine.</summary>
        public bool HumanAimsWithPointer { get; private set; }

        /// <summary>What the interface calls the machine: its model's name, without the file details.</summary>
        public string MachineName { get; private set; } = "Machine";

        /// <summary>The machine's last decision as a short caption, with the probability the model gave it.</summary>
        public string MachineCaption { get; private set; } = "";

        // ---------------------------------------------------------------- events

        /// <summary>A point was won: the winner, and whether by a catch.</summary>
        public event Action<int, bool> PointWon;

        /// <summary>A serve began; the ball is live after <see cref="DodgeballRules.pointPause"/>.</summary>
        public event Action ServeStarted;

        /// <summary>The ball went live.</summary>
        public event Action Go;

        /// <summary>The match ended, with its winner.</summary>
        public event Action<int> MatchOver;

        /// <summary>A player pressed throw while holding a ball in front of their throwing line.</summary>
        public event Action<int> ThrowRefused;

        /// <summary>
        /// The court has just stepped and its <see cref="DodgeballCourt.Events"/> are this step's.
        /// Raised before the point is scored, so a listener sees the hit while the players are still
        /// where it happened.
        /// </summary>
        public event Action<DodgeballCourt> Stepped;

        /// <summary>Who won the last point, or -1 before the first.</summary>
        public int LastPointWinner { get; private set; } = -1;

        /// <summary>True when the last point was won by a catch.</summary>
        public bool LastPointByCatch { get; private set; }

        private float phaseTimer;
        private readonly bool[] alerts = new bool[2];
        private readonly System.Random autopilotRandom = new System.Random(7);
        private float autopilotReactAt = -1f;

        public const int Human = 0, Machine = 1;

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

        public float MachineDecisionMedianMs => Median(decisionMs);
        public float MachineReactionMedianMs => Median(reactionMs);

        /// <summary>The human's throws that would have hit, and how many the machine caught, dodged, or took.</summary>
        public void HumanThrowOutcomes(out int onTarget, out int caught, out int dodged, out int hit)
        {
            onTarget = caught = dodged = hit = 0;
            for (int i = 0; i < throws.Count; i++)
            {
                ThrowRecord r = throws[i];
                if (r.Thrower != Human || !r.Threat) continue;
                onTarget++;
                if (r.Outcome == "caught") caught++;
                else if (r.Outcome == "hit") hit++;
                else if (r.Outcome == "avoided") dodged++;
            }
        }

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Court = new DodgeballCourt(rules);
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
            Score[0] = Score[1] = 0;
            LastPointWinner = -1;
            throws.Clear();
            decisionMs.Clear();
            reactionMs.Clear();
            matchStart = Time.time;
            StartServe();
        }

        private void StartServe()
        {
            Court.Reset();
            if (machinePad != null) machinePad.Clear();
            CurrentPhase = Phase.Serve;
            phaseTimer = rules.pointPause;
            ServeStarted?.Invoke();
        }

        private void Update()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.rKey.wasPressedThisFrame) Restart();
            if (k != null && k.tKey.wasPressedThisFrame) humanAutopilot = !humanAutopilot;

            ReadAim();
            switch (CurrentPhase)
            {
                case Phase.Serve:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f)
                    {
                        CurrentPhase = Phase.Playing;
                        Go?.Invoke();
                    }
                    break;

                case Phase.Playing:
                    if (humanAutopilot) Autopilot();
                    else ReadHuman();
                    Court.Step(Time.deltaTime);
                    Stepped?.Invoke(Court);
                    HandleEvents();
                    break;

                case Phase.Point:
                    phaseTimer -= Time.deltaTime;
                    if (phaseTimer <= 0f) StartServe();
                    break;
            }
        }

        // ---------------------------------------------------------------- human controls

        private void ReadAim()
        {
            CourtPlayer me = Court.Players[Human];
            Mouse m = Mouse.current;
            Gamepad g = Gamepad.current;
            if (g != null && g.rightStick.ReadValue().sqrMagnitude > 0.1f)
            {
                HumanAim = me.Position + g.rightStick.ReadValue().normalized * 4f;
                HumanAimsWithPointer = true;
            }
            else if (m != null && view != null && !humanAutopilot)
            {
                Vector3 w = view.ScreenToWorldPoint(m.position.ReadValue());
                HumanAim = new Vector2(w.x, w.y);
                HumanAimsWithPointer = true;
            }
            else
            {
                HumanAim = Court.Players[Machine].Position;
                HumanAimsWithPointer = false;
            }
        }

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

            bool throwAtPointer = (m != null && m.leftButton.wasPressedThisFrame) || (g != null && g.rightTrigger.wasPressedThisFrame);
            bool throwAtMachine = (k != null && k.jKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
            if (throwAtPointer || throwAtMachine)
            {
                Vector2 target = throwAtPointer && HumanAimsWithPointer ? HumanAim : Court.Players[Machine].Position;
                if (me.Holding && !Court.CanThrow(Human)) ThrowRefused?.Invoke(Human);
                else Court.Throw(Human, target);
            }

            if ((m != null && m.rightButton.wasPressedThisFrame) || (k != null && k.kKey.wasPressedThisFrame) ||
                (g != null && g.buttonEast.wasPressedThisFrame))
                Court.OpenCatch(Human);

            if ((k != null && (k.spaceKey.wasPressedThisFrame || k.leftShiftKey.wasPressedThisFrame)) ||
                (g != null && g.rightShoulder.wasPressedThisFrame))
                Court.Dodge(Human, move.sqrMagnitude > 0.01f ? move : new Vector2(-me.Forward, 0f));
        }

        /// <summary>
        /// A deliberately plain opponent for practice: it fetches the nearest ball, backs behind its
        /// line, throws straight at the machine, and dodges a ball that will hit it a quarter of a
        /// second after it is thrown, about as fast as a person reacts.
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
                    case CourtEventKind.Hit: Close(e.Ball, "hit"); break;
                    case CourtEventKind.Catch: Close(e.Ball, "caught"); break;
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

            if (Court.PointWinner < 0) return;

            int winner = Court.PointWinner;
            bool byCatch = Court.Events.Exists(e => e.Kind == CourtEventKind.Catch);
            Score[winner]++;
            LastPointWinner = winner;
            LastPointByCatch = byCatch;
            PointWon?.Invoke(winner, byCatch);
            if (Score[winner] >= rules.pointsToWin)
            {
                CurrentPhase = Phase.Over;
                WriteReport();
                MatchOver?.Invoke(winner);
            }
            else
            {
                CurrentPhase = Phase.Point;
                phaseTimer = PointReplaySeconds;
            }
        }

        private void Close(int ball, string outcome)
        {
            for (int i = throws.Count - 1; i >= 0; i--)
            {
                if (throws[i].Ball == ball && throws[i].Outcome == null) { throws[i].Outcome = outcome; return; }
            }
        }

        /// <summary>True once per throw at <paramref name="player"/>, then false until the next one.</summary>
        public bool ConsumeAlert(int player)
        {
            if (!alerts[player]) return false;
            alerts[player] = false;
            return true;
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

        private void OnMachineDecision(DecisionTelemetry t)
        {
            if (t.resultType == DecisionResultType.ModelAction || t.resultType == DecisionResultType.FallbackAction)
                decisionMs.Add(t.latencyMs);
            MachineCaption = t.actionName + (string.IsNullOrEmpty(t.argument) ? "" : " " + t.argument) +
                             (t.choiceProbability > 0f ? $"  ·  p {t.choiceProbability.ToString("0.00", CultureInfo.InvariantCulture)}" : "");
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
                sb.Append($"# score human {Score[Human]} machine {Score[Machine]} ({MachineName}), machine decision median {Median(decisionMs):0} ms over {decisionMs.Count}\n");
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
