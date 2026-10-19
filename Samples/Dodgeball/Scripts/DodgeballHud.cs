using System.Globalization;
using BehaviorLLM.Core.Decisions;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// The match's interface, built to be read at a glance rather than read. The score is two
    /// seven-segment digits on a scoreboard plate with a light per point toward the win. Under each
    /// player rides a chip with their hands (one ball icon per ball held) and their dodge and catch,
    /// which sweep back like a cooldown dial. A no-throw sign pops over a player who holds a ball
    /// in front of their line. Over the machine, a bubble shows the input it just pressed as an
    /// icon (an arrow for a run, chevrons for a dodge, a hand for a catch), ringed by how sure the
    /// model was, with its decision time beside it. Words are kept for the names and the banners.
    ///
    /// Built in code when the scene starts, from sprites the scene builder paints, so there is no
    /// prefab to keep in step with it.
    /// </summary>
    [AddComponentMenu("Dodgeball/Dodgeball HUD")]
    public class DodgeballHud : MonoBehaviour
    {
        [Tooltip("The match to show.")]
        public DodgeballMatch match;

        [Tooltip("The camera the court is seen through, to place the chips under the players.")]
        public Camera view;

        [Tooltip("The human's colour.")]
        public Color humanColor = new Color(0.3f, 0.6f, 1f);

        [Tooltip("The machine's colour.")]
        public Color machineColor = new Color(1f, 0.36f, 0.3f);

        [Header("Sprites, painted by the scene builder")]
        [Tooltip("Ball.")] public Sprite ball;
        [Tooltip("Thin ring: an empty hand, and the confidence dial.")] public Sprite ring;
        [Tooltip("Filled disc: icon backs and cooldown sweeps.")] public Sprite disc;
        [Tooltip("Open hand: the catch.")] public Sprite glove;
        [Tooltip("Chevrons: the dodge.")] public Sprite dash;
        [Tooltip("Clock: decision time.")] public Sprite clock;
        [Tooltip("No-throw sign.")] public Sprite noThrow;
        [Tooltip("Arrow: a run in some direction.")] public Sprite arrow;
        [Tooltip("Standing still.")] public Sprite stand;
        [Tooltip("Star burst behind the banners.")] public Sprite burst;
        [Tooltip("A point light on the scoreboard.")] public Sprite pip;
        [Tooltip("One bar of a seven-segment digit.")] public Sprite segment;
        [Tooltip("The scoreboard plate.")] public Sprite plate;
        [Tooltip("The banner plate.")] public Sprite banner;
        [Tooltip("The machine's bubble.")] public Sprite bubble;
        [Tooltip("A keycap, sliced.")] public Sprite key;
        [Tooltip("A mouse with its left button lit.")] public Sprite mouseLeft;
        [Tooltip("A mouse with its right button lit.")] public Sprite mouseRight;
        [Tooltip("Restart arrow.")] public Sprite restart;
        [Tooltip("Four-way arrows: moving.")] public Sprite move;

        private static readonly Color Ink = new Color(0.06f, 0.07f, 0.1f, 0.94f);
        private static readonly Color Unlit = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color Muted = new Color(0.66f, 0.7f, 0.78f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Warn = new Color(1f, 0.28f, 0.2f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private Font font;
        private RectTransform canvasRect;
        private SevenSegment[] digits = new SevenSegment[2];
        private Image[][] pips = new Image[2][];
        private float[] pipFlash = new float[2];
        private int[] shownScore = { -1, -1 };
        private Chip[] chips = new Chip[2];
        private Image[] noThrowSigns = new Image[2];
        private float[] refusedAt = { -10f, -10f };
        private Text autopilotTag;

        private RectTransform thought;
        private Image thoughtIcon, thoughtRing;
        private Text thoughtMs;
        private float thoughtAt = -10f;

        private RectTransform bannerRoot;
        private Image bannerPlate, bannerBurst;
        private Text bannerText;
        private float bannerAt = -10f, bannerFor;

        private GameObject overRoot;
        private Text[] overStats = new Text[4];

        private sealed class Chip
        {
            public RectTransform Root;
            public Image[] Balls;
            public Image Dodge, DodgeSweep, Catch, CatchSweep;
            public bool DodgeWasReady = true, CatchWasReady = true;
            public float DodgePop, CatchPop;
        }

        // ---------------------------------------------------------------- lifecycle

        private void Start()
        {
            // A condensed sports face where the system has one; the built-in face otherwise.
            font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "DIN Alternate", "Arial Narrow", "Arial" }, 64);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            if (match != null && match.machinePad != null) match.machinePad.InputApplied += OnMachineInput;
            if (match != null && match.machine != null) match.machine.DecisionTelemetryRecorded += OnMachineDecision;
        }

        private void OnEnable()
        {
            if (match == null) return;
            match.ServeStarted += OnServe;
            match.Go += OnGo;
            match.PointWon += OnPoint;
            match.MatchOver += OnMatchOver;
            match.ThrowRefused += OnThrowRefused;
        }

        private void OnDisable()
        {
            if (match == null) return;
            match.ServeStarted -= OnServe;
            match.Go -= OnGo;
            match.PointWon -= OnPoint;
            match.MatchOver -= OnMatchOver;
            match.ThrowRefused -= OnThrowRefused;
        }

        private void OnDestroy()
        {
            if (match != null && match.machinePad != null) match.machinePad.InputApplied -= OnMachineInput;
            if (match != null && match.machine != null) match.machine.DecisionTelemetryRecorded -= OnMachineDecision;
        }

        // ---------------------------------------------------------------- match events

        private void OnServe()
        {
            if (overRoot != null) overRoot.SetActive(false);
            ShowBanner("READY", new Color(0.3f, 0.33f, 0.4f), match.Rules.pointPause);
        }

        private void OnGo() => ShowBanner("GO!", new Color(0.2f, 0.7f, 0.35f), 0.55f);

        private void OnPoint(int winner, bool byCatch)
        {
            ShowBanner(byCatch ? "CATCH!" : "HIT!", TeamColor(winner), DodgeballMatch.PointReplaySeconds);
            pipFlash[winner] = 1f;
        }

        private void OnMatchOver(int winner)
        {
            ShowBanner(winner == DodgeballMatch.Human ? "YOU WIN" : match.MachineName.ToUpperInvariant() + " WINS", TeamColor(winner), 0f);
            overRoot.SetActive(true);
            match.HumanThrowOutcomes(out _, out int caught, out int dodged, out int hit);
            overStats[0].text = Ms(match.MachineDecisionMedianMs);
            overStats[1].text = caught.ToString(Inv);
            overStats[2].text = dodged.ToString(Inv);
            overStats[3].text = hit.ToString(Inv);
        }

        private void OnThrowRefused(int player) => refusedAt[player] = Time.unscaledTime;

        private void OnMachineInput(string input)
        {
            if (thoughtIcon == null) return;
            int space = input.IndexOf(' ');
            string action = space < 0 ? input : input.Substring(0, space);
            string value = space < 0 ? null : input.Substring(space + 1);
            CourtPlayer me = match.Court.Players[DodgeballMatch.Machine];
            float angle = 0f;
            Sprite icon = stand;
            Color tint = Color.white;
            switch (action)
            {
                case DodgeballIds.Move:
                    icon = arrow;
                    Vector2 d = DodgeballMachinePad.DirectionVector(value, me.Forward);
                    angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    break;
                case DodgeballIds.Dodge:
                    icon = dash;
                    Vector2 dd = DodgeballMachinePad.DirectionVector(value, me.Forward);
                    angle = Mathf.Atan2(dd.y, dd.x) * Mathf.Rad2Deg;
                    tint = new Color(0.75f, 0.9f, 1f);
                    break;
                case DodgeballIds.Catch: icon = glove; tint = Gold; break;
                case DodgeballIds.Throw: icon = ball; break;
            }
            thoughtIcon.sprite = icon;
            thoughtIcon.color = tint;
            thoughtIcon.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            thoughtAt = Time.unscaledTime;
        }

        private void OnMachineDecision(DecisionTelemetry t)
        {
            if (thoughtRing == null) return;
            if (t.choiceProbability > 0f) thoughtRing.fillAmount = Mathf.Clamp01(t.choiceProbability);
            if (t.latencyMs > 0f) thoughtMs.text = Ms(t.latencyMs);
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (match == null || match.Court == null || canvasRect == null) return;
            DodgeballCourt court = match.Court;
            float now = Time.unscaledTime;
            float udt = Time.unscaledDeltaTime;

            for (int i = 0; i < 2; i++)
            {
                if (shownScore[i] != match.Score[i])
                {
                    shownScore[i] = match.Score[i];
                    digits[i].Show(match.Score[i] % 10, TeamColor(i), Unlit);
                    for (int k = 0; k < pips[i].Length; k++)
                        pips[i][k].color = k < match.Score[i] ? TeamColor(i) : Dim;
                }
                // The newest point's light pops.
                pipFlash[i] = Mathf.Max(0f, pipFlash[i] - udt * 1.5f);
                int last = match.Score[i] - 1;
                for (int k = 0; k < pips[i].Length; k++)
                {
                    float s = k == last ? 1f + Mathf.Sin(pipFlash[i] * Mathf.PI * 3f) * 0.6f * pipFlash[i] : 1f;
                    pips[i][k].rectTransform.localScale = new Vector3(s, s, 1f);
                }
                DrawChip(court, i, udt);
                DrawNoThrow(court, i, now);
            }

            autopilotTag.enabled = match.humanAutopilot;
            DrawThought(court, now);
            DrawBanner(now);
        }

        private void DrawChip(DodgeballCourt court, int i, float udt)
        {
            Chip c = chips[i];
            CourtPlayer p = court.Players[i];
            DodgeballRules r = court.Rules;
            Place(c.Root, p.Position + new Vector2(0f, -0.95f));

            for (int k = 0; k < c.Balls.Length; k++) c.Balls[k].enabled = k < p.HeldCount;

            Color team = TeamColor(i);
            float dodgeLeft = p.CanDodge ? 0f : p.Charging ? 1f : p.DodgeCooldown / (r.dodgeDuration + r.dodgeCooldown);
            float catchLeft = p.CanCatch ? 0f : p.Charging || p.Dodging ? 1f : p.CatchCooldown / (r.catchWindow + r.catchCooldown);
            Dial(c.Dodge, c.DodgeSweep, dodgeLeft, team, ref c.DodgeWasReady, ref c.DodgePop, udt);
            Dial(c.Catch, c.CatchSweep, catchLeft, p.Catching ? Gold : team, ref c.CatchWasReady, ref c.CatchPop, udt);
            if (p.Catching) c.Catch.rectTransform.localScale = Vector3.one * 1.35f;
        }

        /// <summary>An icon that greys and sweeps back while recovering, and pops when it is ready again.</summary>
        private static void Dial(Image icon, Image sweep, float left, Color ready, ref bool wasReady, ref float pop, float udt)
        {
            bool isReady = left <= 0.001f;
            if (isReady && !wasReady) pop = 1f;
            wasReady = isReady;
            pop = Mathf.Max(0f, pop - udt * 4f);
            icon.color = isReady ? ready : new Color(0.55f, 0.58f, 0.65f, 0.8f);
            sweep.fillAmount = left;
            float s = 1f + Mathf.Sin(pop * Mathf.PI) * 0.45f;
            icon.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        private void DrawNoThrow(DodgeballCourt court, int i, float now)
        {
            CourtPlayer p = court.Players[i];
            bool inZone = p.Holding && court.InNoThrowZone(i) && match.Playing;
            float sinceRefused = now - refusedAt[i];
            Image sign = noThrowSigns[i];
            sign.enabled = inZone || sinceRefused < 0.8f;
            if (!sign.enabled) return;
            Place(sign.rectTransform, p.Position + new Vector2(0f, 1.15f));
            float shake = sinceRefused < 0.4f ? Mathf.Sin(sinceRefused * 60f) * 8f * (1f - sinceRefused / 0.4f) : 0f;
            sign.rectTransform.anchoredPosition += new Vector2(shake, 0f);
            float s = sinceRefused < 0.8f ? 1.3f - sinceRefused * 0.3f : 1f + Mathf.Sin(now * 9f) * 0.06f;
            sign.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        private void DrawThought(DodgeballCourt court, float now)
        {
            CourtPlayer m = court.Players[DodgeballMatch.Machine];
            bool sign = noThrowSigns[DodgeballMatch.Machine].enabled;
            Place(thought, m.Position + new Vector2(0.35f, sign ? 1.65f : 0.7f));
            float age = now - thoughtAt;
            float s = 1f + Mathf.Exp(-age * 14f) * 0.35f;
            thought.localScale = new Vector3(s, s, 1f);
        }

        private void DrawBanner(float now)
        {
            float age = now - bannerAt;
            bool forever = bannerFor <= 0f && bannerAt > 0f && match.CurrentPhase == DodgeballMatch.Phase.Over;
            float alpha = forever ? 1f : bannerFor <= 0f ? 0f : Mathf.Clamp01(1f - (age - bannerFor * 0.75f) / (bannerFor * 0.25f + 0.001f));
            bannerRoot.gameObject.SetActive(alpha > 0.001f);
            if (alpha <= 0.001f) return;
            float punch = 1f + Mathf.Exp(-age * 10f) * 0.5f;
            bannerRoot.localScale = new Vector3(punch, punch, 1f);
            SetAlpha(bannerPlate, alpha * 0.95f);
            SetAlpha(bannerText, alpha);
            SetAlpha(bannerBurst, alpha * 0.4f);
            bannerBurst.rectTransform.localRotation = Quaternion.Euler(0f, 0f, now * 25f);
        }

        private void ShowBanner(string text, Color color, float seconds)
        {
            if (bannerText == null) return;
            bannerText.text = text;
            bannerPlate.color = Color.Lerp(color, Color.black, 0.15f);
            bannerBurst.color = color;
            bannerAt = Time.unscaledTime;
            bannerFor = seconds;
        }

        // ---------------------------------------------------------------- helpers

        private Color TeamColor(int player) => player == DodgeballMatch.Human ? humanColor : machineColor;

        private static string Ms(float ms) => ms > 0f ? ms.ToString("0", Inv) + " ms" : "-";

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }

        /// <summary>Puts <paramref name="rt"/> over a court position.</summary>
        private void Place(RectTransform rt, Vector2 court)
        {
            if (view == null) return;
            Vector3 screen = view.WorldToScreenPoint(new Vector3(court.x, court.y, 0f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            rt.anchoredPosition = local;
        }

        // ---------------------------------------------------------------- building

        private void Build()
        {
            GameObject canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)canvasGo.transform;

            // Things that follow the players sit in a layer anchored at the center, under the rest.
            RectTransform world = Rect("World", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            world.anchorMin = Vector2.zero;
            world.anchorMax = Vector2.one;
            world.sizeDelta = Vector2.zero;
            for (int i = 0; i < 2; i++)
            {
                chips[i] = BuildChip(world, i);
                noThrowSigns[i] = BuildNoThrow(world);
            }
            BuildThought(world);

            BuildScoreboard();
            BuildBanner();
            BuildLegend();
            BuildMatchOver();

            if (match.CurrentPhase == DodgeballMatch.Phase.Serve) OnServe();
        }

        private void BuildScoreboard()
        {
            RectTransform board = Rect("Scoreboard", canvasRect, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(760f, 190f));
            board.pivot = new Vector2(0.5f, 1f);
            Img(board, "Plate", plate, Ink, Vector2.zero, new Vector2(760f, 190f));

            for (int i = 0; i < 2; i++)
            {
                float side = i == DodgeballMatch.Human ? -1f : 1f;
                digits[i] = new SevenSegment(board, segment, new Vector2(side * 62f, 0f), 0.95f);
                int n = Mathf.Clamp(match.Rules.pointsToWin, 1, 12);
                pips[i] = new Image[n];
                for (int k = 0; k < n; k++)
                    pips[i][k] = Img(board, "Point" + k, pip, Dim, new Vector2(side * (128f + k * 25f), 0f), new Vector2(17f, 17f));
            }
            // Two dots between the digits, as on a clock.
            Img(board, "Colon", pip, new Color(1f, 1f, 1f, 0.5f), new Vector2(0f, 14f), new Vector2(10f, 10f));
            Img(board, "Colon", pip, new Color(1f, 1f, 1f, 0.5f), new Vector2(0f, -14f), new Vector2(10f, 10f));

            Text you = Label(canvasRect, "YOU", 30, humanColor, new Vector2(0.5f, 1f), new Vector2(-250f, -150f), new Vector2(240f, 40f), TextAnchor.MiddleCenter);
            DropShadow(you, 2f);
            autopilotTag = Label(canvasRect, "AUTOPILOT", 16, Muted, new Vector2(0.5f, 1f), new Vector2(-250f, -178f), new Vector2(240f, 24f), TextAnchor.MiddleCenter);
            Text them = Label(canvasRect, match.MachineName, 30, machineColor, new Vector2(0.5f, 1f), new Vector2(250f, -150f), new Vector2(320f, 40f), TextAnchor.MiddleCenter);
            them.resizeTextForBestFit = true;
            them.resizeTextMinSize = 16;
            them.resizeTextMaxSize = 30;
            DropShadow(them, 2f);
        }

        private Chip BuildChip(RectTransform parent, int player)
        {
            Chip c = new Chip();
            c.Root = Rect(player == 0 ? "HumanChip" : "MachineChip", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 40f));
            Img(c.Root, "Back", key, new Color(0.05f, 0.06f, 0.09f, 0.7f), Vector2.zero, new Vector2(150f, 40f)).type = UnityEngine.UI.Image.Type.Sliced;

            int hands = Mathf.Max(1, match.Rules.maxHeld);
            c.Balls = new Image[hands];
            for (int k = 0; k < hands; k++)
            {
                Vector2 at = new Vector2(-52f + k * 28f, 0f);
                Img(c.Root, "Hand", ring, new Color(1f, 1f, 1f, 0.35f), at, new Vector2(26f, 26f));
                c.Balls[k] = Img(c.Root, "Ball", ball, Color.white, at, new Vector2(24f, 24f));
            }
            c.Dodge = Img(c.Root, "Dodge", dash, Color.white, new Vector2(22f, 0f), new Vector2(28f, 28f));
            c.DodgeSweep = Sweep(c.Root, new Vector2(22f, 0f));
            c.Catch = Img(c.Root, "Catch", glove, Color.white, new Vector2(54f, 0f), new Vector2(28f, 28f));
            c.CatchSweep = Sweep(c.Root, new Vector2(54f, 0f));
            return c;
        }

        private Image Sweep(RectTransform parent, Vector2 at)
        {
            Image s = Img(parent, "Sweep", disc, new Color(0f, 0f, 0f, 0.55f), at, new Vector2(32f, 32f));
            s.type = UnityEngine.UI.Image.Type.Filled;
            s.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            s.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            s.fillClockwise = false;
            s.fillAmount = 0f;
            return s;
        }

        private Image BuildNoThrow(RectTransform parent)
        {
            Image back = Img(parent, "NoThrow", disc, new Color(0.08f, 0.02f, 0.02f, 0.85f), Vector2.zero, new Vector2(54f, 54f));
            Img(back.rectTransform, "Ball", ball, new Color(1f, 1f, 1f, 0.8f), Vector2.zero, new Vector2(28f, 28f));
            Img(back.rectTransform, "Sign", noThrow, Warn, Vector2.zero, new Vector2(50f, 50f));
            back.enabled = false;
            // The children follow the back's visibility.
            back.gameObject.AddComponent<CanvasGroup>();
            back.gameObject.AddComponent<FollowEnabled>();
            return back;
        }

        private void BuildThought(RectTransform parent)
        {
            // Pivot at the tail, so the bubble points down at the machine.
            thought = Rect("MachineThought", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
            thought.pivot = new Vector2(0.5f, 0f);
            Img(thought, "Bubble", bubble, new Color(0.96f, 0.96f, 0.98f, 0.95f), Vector2.zero, new Vector2(84f, 84f));
            Img(thought, "Dial", ring, new Color(0f, 0f, 0f, 0.15f), new Vector2(0f, 5f), new Vector2(62f, 62f));
            thoughtRing = Img(thought, "Confidence", ring, machineColor, new Vector2(0f, 5f), new Vector2(62f, 62f));
            thoughtRing.type = UnityEngine.UI.Image.Type.Filled;
            thoughtRing.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            thoughtRing.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            thoughtRing.fillAmount = 0f;
            Image iconBack = Img(thought, "IconBack", disc, new Color(0.12f, 0.13f, 0.18f), new Vector2(0f, 5f), new Vector2(46f, 46f));
            thoughtIcon = Img(iconBack.rectTransform, "Icon", stand, Color.white, Vector2.zero, new Vector2(34f, 34f));
            // Decision time, on a dark tab beside the bubble.
            Image tab = Img(thought, "Latency", key, new Color(0.05f, 0.06f, 0.09f, 0.8f), new Vector2(86f, 0f), new Vector2(92f, 28f));
            tab.type = UnityEngine.UI.Image.Type.Sliced;
            Img(tab.rectTransform, "Clock", clock, Muted, new Vector2(-30f, 0f), new Vector2(18f, 18f));
            thoughtMs = Label(tab.rectTransform, "", 18, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-16f, 1f), new Vector2(70f, 24f), TextAnchor.MiddleLeft);
            thoughtMs.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        private void BuildBanner()
        {
            bannerRoot = Rect("Banner", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(900f, 225f));
            bannerBurst = Img(bannerRoot, "Burst", burst, Color.white, Vector2.zero, new Vector2(460f, 460f));
            bannerPlate = Img(bannerRoot, "Plate", banner, Color.white, Vector2.zero, new Vector2(820f, 205f));
            bannerText = Label(bannerRoot, "", 112, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(800f, 150f), TextAnchor.MiddleCenter);
            bannerText.fontStyle = FontStyle.BoldAndItalic;
            bannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bannerText.verticalOverflow = VerticalWrapMode.Truncate;
            bannerText.resizeTextForBestFit = true;
            bannerText.resizeTextMinSize = 40;
            bannerText.resizeTextMaxSize = 112;
            DropShadow(bannerText, 5f);
            bannerRoot.gameObject.SetActive(false);
        }

        private void BuildLegend()
        {
            RectTransform legend = Rect("Controls", canvasRect, new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(900f, 60f));
            legend.gameObject.AddComponent<CanvasGroup>().alpha = 0.9f;
            Img(legend, "Back", key, new Color(0.05f, 0.06f, 0.09f, 0.85f), new Vector2(20f, 0f), new Vector2(880f, 54f)).type = UnityEngine.UI.Image.Type.Sliced;
            float x = -390f;
            x = KeyItem(legend, x, "WASD", move, null);
            x = MouseItem(legend, x, mouseLeft, ball, "hold");
            x = MouseItem(legend, x, mouseRight, glove, null);
            x = KeyItem(legend, x, "SPACE", dash, null);
            x = KeyItem(legend, x, "T", null, "auto");
            KeyItem(legend, x, "R", restart, null);
        }

        private float KeyItem(RectTransform parent, float x, string keyName, Sprite icon, string word)
        {
            float w = Mathf.Max(38f, 18f + keyName.Length * 14f);
            Image cap = Img(parent, "Key", key, new Color(0.85f, 0.87f, 0.92f), new Vector2(x + w * 0.5f, 0f), new Vector2(w, 38f));
            cap.type = UnityEngine.UI.Image.Type.Sliced;
            Label(cap.rectTransform, keyName, 17, new Color(0.08f, 0.09f, 0.12f), new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(w, 30f), TextAnchor.MiddleCenter);
            x += w + 8f;
            if (icon != null)
            {
                Img(parent, "Icon", icon, Color.white, new Vector2(x + 15f, 0f), new Vector2(30f, 30f));
                x += 34f;
            }
            if (word != null)
            {
                Text t = Label(parent, word, 18, Muted, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(60f, 30f), TextAnchor.MiddleLeft);
                t.rectTransform.pivot = new Vector2(0f, 0.5f);
                x += 50f;
            }
            return x + 34f;
        }

        private float MouseItem(RectTransform parent, float x, Sprite mouse, Sprite icon, string word)
        {
            Img(parent, "Mouse", mouse, new Color(0.85f, 0.87f, 0.92f), new Vector2(x + 14f, 0f), new Vector2(30f, 40f));
            x += 36f;
            Img(parent, "Icon", icon, Color.white, new Vector2(x + 15f, 0f), new Vector2(30f, 30f));
            x += 34f;
            if (word != null)
            {
                Text t = Label(parent, word, 18, Muted, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(60f, 30f), TextAnchor.MiddleLeft);
                t.rectTransform.pivot = new Vector2(0f, 0.5f);
                x += 46f;
            }
            return x + 34f;
        }

        private void BuildMatchOver()
        {
            RectTransform over = Rect("MatchOver", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(760f, 150f));
            Img(over, "Plate", plate, Ink, Vector2.zero, new Vector2(760f, 190f)).rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            Sprite[] icons = { clock, glove, dash, burst };
            Color[] tints = { Muted, Gold, new Color(0.75f, 0.9f, 1f), Warn };
            for (int i = 0; i < 4; i++)
            {
                float x = -255f + i * 170f;
                Img(over, "Icon", icons[i], tints[i], new Vector2(x - 34f, 8f), new Vector2(38f, 38f));
                overStats[i] = Label(over, "", 34, Color.white, new Vector2(0.5f, 0.5f), new Vector2(x + 2f, 8f), new Vector2(120f, 44f), TextAnchor.MiddleLeft);
                overStats[i].rectTransform.pivot = new Vector2(0f, 0.5f);
            }
            Text caption = Label(over, match.MachineName + "  vs  your throws", 18, Muted, new Vector2(0.5f, 0.5f), new Vector2(0f, -38f), new Vector2(700f, 26f), TextAnchor.MiddleCenter);
            caption.fontStyle = FontStyle.Normal;
            RectTransform replay = Rect("Replay", over, new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(120f, 40f));
            KeyItem(replay, -40f, "R", restart, null);
            overRoot = over.gameObject;
            overRoot.SetActive(false);
        }

        // ---------------------------------------------------------------- primitives

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static Image Img(Transform parent, string name, Sprite sprite, Color color, Vector2 position, Vector2 size)
        {
            RectTransform rt = Rect(name, parent, new Vector2(0.5f, 0.5f), position, size);
            Image image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private Text Label(Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 position, Vector2 box, TextAnchor alignment)
        {
            RectTransform rt = Rect("Label", parent, anchor, position, box);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.color = color;
            t.alignment = alignment;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void DropShadow(Text t, float distance)
        {
            Shadow s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.65f);
            s.effectDistance = new Vector2(distance, -distance);
        }

        /// <summary>A digit drawn with seven bars, lit in a colour and dimmed otherwise.</summary>
        private sealed class SevenSegment
        {
            private readonly Image[] bars = new Image[7];

            // Bars a to g: top, upper right, lower right, bottom, lower left, upper left, middle.
            private static readonly int[] Masks = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

            public SevenSegment(RectTransform parent, Sprite bar, Vector2 at, float scale)
            {
                Vector2[] pos = { new Vector2(0f, 62f), new Vector2(31f, 31f), new Vector2(31f, -31f), new Vector2(0f, -62f),
                                  new Vector2(-31f, -31f), new Vector2(-31f, 31f), Vector2.zero };
                bool[] vertical = { false, true, true, false, true, true, false };
                for (int i = 0; i < 7; i++)
                {
                    bars[i] = Img(parent, "Bar", bar, Unlit, at + pos[i] * scale, new Vector2(64f, 16f) * scale);
                    if (vertical[i]) bars[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }

            public void Show(int digit, Color on, Color off)
            {
                int mask = Masks[Mathf.Clamp(digit, 0, 9)];
                for (int i = 0; i < 7; i++) bars[i].color = (mask & (1 << i)) != 0 ? on : off;
            }
        }
    }

    /// <summary>Shows or hides an image's children along with it.</summary>
    internal sealed class FollowEnabled : MonoBehaviour
    {
        private Image image;
        private CanvasGroup group;

        private void Awake()
        {
            image = GetComponent<Image>();
            group = GetComponent<CanvasGroup>();
        }

        private void LateUpdate()
        {
            if (group != null && image != null) group.alpha = image.enabled ? 1f : 0f;
        }
    }
}
