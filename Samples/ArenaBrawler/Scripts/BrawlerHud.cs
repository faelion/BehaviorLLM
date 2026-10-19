using System.Collections.Generic;
using System.Globalization;
using BehaviorLLM.Core.Decisions;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// The fight's interface, made to be read at a glance: slanted health bars that drop at once
    /// and drain a pale trail behind them, a star per round won, the round clock in seven-segment
    /// digits, damage numbers that jump off the fighter who took them, banners for the round, the
    /// fight and the knockout, and over the machine a bubble with the input it just pressed as an
    /// icon (a fist for a jab, a swung fist for a hook, a shield, a roll, an arrow for a step),
    /// ringed by how sure the model was, with its decision time beside it.
    ///
    /// Built in code when the scene starts, from sprites the scene builder paints. Kept in this
    /// sample rather than shared with Dodgeball's, because samples share nothing.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Brawler HUD")]
    public class BrawlerHud : MonoBehaviour
    {
        [Tooltip("The match to show.")]
        public BrawlerMatch match;

        [Tooltip("The camera the fight is seen through, to place the bubble and the damage numbers.")]
        public Camera view;

        [Tooltip("The human's colour.")]
        public Color humanColor = new Color(0.3f, 0.55f, 1f);

        [Tooltip("The machine's colour.")]
        public Color machineColor = new Color(1f, 0.32f, 0.26f);

        [Header("Sprites, painted by the scene builder")]
        [Tooltip("Jab.")] public Sprite fist;
        [Tooltip("Hook.")] public Sprite hook;
        [Tooltip("Block.")] public Sprite shield;
        [Tooltip("Evade.")] public Sprite roll;
        [Tooltip("A step in some direction.")] public Sprite arrow;
        [Tooltip("Standing still.")] public Sprite stand;
        [Tooltip("Star burst behind the banners.")] public Sprite burst;
        [Tooltip("A round won.")] public Sprite star;
        [Tooltip("One bar of a seven-segment digit.")] public Sprite segment;
        [Tooltip("The clock's plate.")] public Sprite plate;
        [Tooltip("A slanted bar: health, and the banners.")] public Sprite bar;
        [Tooltip("The machine's bubble.")] public Sprite bubble;
        [Tooltip("A keycap, sliced.")] public Sprite key;
        [Tooltip("A mouse with its left button lit.")] public Sprite mouseLeft;
        [Tooltip("A mouse with its right button lit.")] public Sprite mouseRight;
        [Tooltip("Restart arrow.")] public Sprite restart;
        [Tooltip("Four-way arrows: moving.")] public Sprite move;
        [Tooltip("Clock: decision time.")] public Sprite clock;
        [Tooltip("Filled disc.")] public Sprite disc;
        [Tooltip("Thin ring: the confidence dial.")] public Sprite ring;

        private static readonly Color Ink = new Color(0.06f, 0.07f, 0.1f, 0.94f);
        private static readonly Color Unlit = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.13f);
        private static readonly Color Muted = new Color(0.68f, 0.71f, 0.78f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private Font font;
        private RectTransform canvasRect;
        private RectTransform world;
        private readonly Image[] health = new Image[2];
        private readonly Image[] trail = new Image[2];
        private readonly float[] trailValue = { 1f, 1f };
        private readonly float[] trailHold = new float[2];
        private readonly float[] shake = new float[2];
        private readonly RectTransform[] barRoots = new RectTransform[2];
        private readonly Image[][] stars = new Image[2][];
        private readonly int[] shownRounds = { -1, -1 };
        private SevenSegment[] clockDigits = new SevenSegment[2];
        private int shownSeconds = -1;

        private RectTransform thought;
        private Image thoughtIcon, thoughtRing;
        private Text thoughtMs;
        private float thoughtAt = -10f;

        private RectTransform bannerRoot;
        private Image bannerPlate, bannerBurst;
        private Text bannerText;
        private float bannerAt = -10f, bannerFor;
        private bool bannerStays;

        private GameObject overRoot;
        private readonly Text[] overStats = new Text[4];

        private sealed class Popup
        {
            public RectTransform Root;
            public Text Text;
            public Image Icon;
            public Vector3 World;
            public float Born = -10f;
            public float Life;
        }

        private readonly List<Popup> popups = new List<Popup>();

        // ---------------------------------------------------------------- lifecycle

        private void Start()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "DIN Alternate", "Arial Narrow", "Arial" }, 64);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            if (match.machinePad != null) match.machinePad.InputApplied += OnMachineInput;
            if (match.machine != null) match.machine.DecisionTelemetryRecorded += OnMachineDecision;
        }

        private void OnEnable()
        {
            if (match == null) return;
            match.Stepped += OnStepped;
            match.RoundIntro += OnRoundIntro;
            match.Fight += OnFight;
            match.RoundWon += OnRoundWon;
            match.MatchWon += OnMatchWon;
            match.Draw += OnDraw;
        }

        private void OnDisable()
        {
            if (match == null) return;
            match.Stepped -= OnStepped;
            match.RoundIntro -= OnRoundIntro;
            match.Fight -= OnFight;
            match.RoundWon -= OnRoundWon;
            match.MatchWon -= OnMatchWon;
            match.Draw -= OnDraw;
        }

        private void OnDestroy()
        {
            if (match == null) return;
            if (match.machinePad != null) match.machinePad.InputApplied -= OnMachineInput;
            if (match.machine != null) match.machine.DecisionTelemetryRecorded -= OnMachineDecision;
        }

        // ---------------------------------------------------------------- match events

        private void OnRoundIntro(int round)
        {
            if (overRoot != null) overRoot.SetActive(false);
            ShowBanner("ROUND " + round, new Color(0.25f, 0.28f, 0.36f), match.Rules.roundPause, false);
        }

        private void OnFight() => ShowBanner("FIGHT!", new Color(0.85f, 0.2f, 0.15f), 0.7f, false);

        private void OnDraw() => ShowBanner("DRAW", new Color(0.3f, 0.3f, 0.35f), 1.5f, false);

        private void OnRoundWon(int winner, bool knockout)
        {
            string who = winner == BrawlerMatch.Human ? "YOU" : match.MachineName.ToUpperInvariant();
            ShowBanner(knockout ? "K.O." : "TIME", Color.Lerp(TeamColor(winner), Color.black, 0.2f), 1.6f, false);
            // After the knockout's moment, the winner's name for the rest of the celebration.
            pendingWinner = who;
            pendingWinnerColor = TeamColor(winner);
            pendingAt = Time.unscaledTime + 1.4f;
        }

        private string pendingWinner;
        private Color pendingWinnerColor;
        private float pendingAt = -1f;

        private void OnMatchWon(int winner)
        {
            pendingWinner = winner == BrawlerMatch.Human ? "YOU WIN" : match.MachineName.ToUpperInvariant() + " WINS";
            pendingAt = Time.unscaledTime + 1.4f;
            match.HumanPunches(out int hooks, out int hooksLanded, out int jabs, out int jabsLanded);
            overStats[0].text = Ms(match.MachineDecisionMedianMs);
            overStats[1].text = Ms(match.MachineHookReactionMedianMs);
            overStats[2].text = jabsLanded + "/" + jabs;
            overStats[3].text = hooksLanded + "/" + hooks;
        }

        private void OnStepped(BrawlerArena arena)
        {
            for (int i = 0; i < arena.Events.Count; i++)
            {
                ArenaEvent e = arena.Events[i];
                BrawlerFighter d = arena.Opponent(e.Fighter);
                Vector3 at = new Vector3(d.Position.x, 2.1f, d.Position.y);
                switch (e.Kind)
                {
                    case ArenaEventKind.Hit:
                    {
                        BrawlerAttack atk = e.Heavy ? arena.Rules.hook : arena.Rules.jab;
                        bool counter = e.Damage > atk.damage + 0.01f;
                        Pop(at, Mathf.RoundToInt(e.Damage).ToString(Inv), null, counter ? Gold : Color.white, e.Heavy ? 64 : 44);
                        if (counter) Pop(at + Vector3.up * 0.45f, "COUNTER", null, Gold, 26);
                        shake[d.Index] = 1f;
                        trailHold[d.Index] = 0.45f;
                        break;
                    }
                    case ArenaEventKind.Blocked:
                        Pop(at, null, shield, new Color(0.5f, 0.8f, 1f), 48);
                        break;
                    case ArenaEventKind.GuardBreak:
                        Pop(at, "BREAK", null, new Color(0.85f, 0.55f, 1f), 46);
                        shake[d.Index] = 1f;
                        trailHold[d.Index] = 0.45f;
                        break;
                }
            }
        }

        private void OnMachineInput(string input)
        {
            if (thoughtIcon == null) return;
            int space = input.IndexOf(' ');
            string action = space < 0 ? input : input.Substring(0, space);
            string value = space < 0 ? null : input.Substring(space + 1);
            Sprite icon = stand;
            Color tint = Color.white;
            float angle = 0f;
            switch (action)
            {
                case BrawlerIds.Move:
                    icon = arrow;
                    angle = ScreenAngle(match.Arena.Relative(BrawlerMatch.Machine, value));
                    break;
                case BrawlerIds.Jab: icon = fist; tint = new Color(1f, 0.85f, 0.4f); break;
                case BrawlerIds.Hook: icon = hook; tint = new Color(1f, 0.45f, 0.3f); break;
                case BrawlerIds.Block: icon = shield; tint = new Color(0.55f, 0.8f, 1f); break;
                case BrawlerIds.Evade: icon = roll; tint = new Color(0.8f, 0.95f, 0.8f); break;
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

        /// <summary>The on-screen angle of a direction on the arena floor, as seen from the machine.</summary>
        private float ScreenAngle(Vector2 direction)
        {
            if (view == null) return 0f;
            BrawlerFighter m = match.Arena.Fighters[BrawlerMatch.Machine];
            Vector3 from = view.WorldToScreenPoint(new Vector3(m.Position.x, 0f, m.Position.y));
            Vector3 to = view.WorldToScreenPoint(new Vector3(m.Position.x + direction.x, 0f, m.Position.y + direction.y));
            Vector2 d = to - from;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (match == null || match.Arena == null || canvasRect == null) return;
            BrawlerArena arena = match.Arena;
            float now = Time.unscaledTime;
            float udt = Time.unscaledDeltaTime;

            for (int i = 0; i < 2; i++)
            {
                float value = Mathf.Clamp01(arena.Fighters[i].Health / match.Rules.health);
                health[i].fillAmount = value;
                // The trail holds a moment, then drains to the new value.
                trailHold[i] -= udt;
                if (value > trailValue[i]) trailValue[i] = value;
                else if (trailHold[i] <= 0f) trailValue[i] = Mathf.MoveTowards(trailValue[i], value, udt * 0.6f);
                trail[i].fillAmount = trailValue[i];
                health[i].color = value < 0.25f ? Color.Lerp(TeamColor(i), Color.white, 0.5f + 0.5f * Mathf.Sin(now * 10f)) : TeamColor(i);
                shake[i] = Mathf.Max(0f, shake[i] - udt * 4f);
                barRoots[i].anchoredPosition = BarHome(i) + new Vector2(Mathf.Sin(now * 70f) * 9f * shake[i], Mathf.Cos(now * 63f) * 5f * shake[i]);

                if (shownRounds[i] != match.RoundsWon[i])
                {
                    shownRounds[i] = match.RoundsWon[i];
                    for (int k = 0; k < stars[i].Length; k++) stars[i][k].color = k < match.RoundsWon[i] ? Gold : Dim;
                }
            }

            int seconds = Mathf.Clamp(Mathf.CeilToInt(match.RoundTimeLeft), 0, 99);
            if (seconds != shownSeconds)
            {
                shownSeconds = seconds;
                Color c = seconds <= 10 ? new Color(1f, 0.35f, 0.25f) : Color.white;
                clockDigits[0].Show(seconds / 10, c, Unlit);
                clockDigits[1].Show(seconds % 10, c, Unlit);
            }

            if (pendingAt > 0f && now >= pendingAt)
            {
                bool over = match.CurrentPhase == BrawlerMatch.Phase.MatchOver;
                ShowBanner(pendingWinner, Color.Lerp(pendingWinnerColor, Color.black, 0.2f), over ? 0f : match.Rules.celebrationTime - 1.6f, over);
                if (over) overRoot.SetActive(true);
                pendingAt = -1f;
            }

            DrawThought(now);
            DrawPopups(now);
            DrawBanner(now);
        }

        private void DrawThought(float now)
        {
            BrawlerFighter m = match.Arena.Fighters[BrawlerMatch.Machine];
            thought.gameObject.SetActive(match.CurrentPhase == BrawlerMatch.Phase.Fighting);
            Place(thought, new Vector3(m.Position.x, 2.25f, m.Position.y));
            float s = 1f + Mathf.Exp(-(now - thoughtAt) * 14f) * 0.35f;
            thought.localScale = new Vector3(s, s, 1f);
        }

        private void DrawPopups(float now)
        {
            for (int i = 0; i < popups.Count; i++)
            {
                Popup p = popups[i];
                float age = now - p.Born;
                bool alive = age < p.Life;
                p.Root.gameObject.SetActive(alive);
                if (!alive) continue;
                float k = age / p.Life;
                Place(p.Root, p.World + Vector3.up * (k * 0.7f));
                float pop = 1f + Mathf.Exp(-age * 16f) * 0.8f;
                p.Root.localScale = new Vector3(pop, pop, 1f);
                float a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                if (p.Text != null) SetAlpha(p.Text, a);
                if (p.Icon != null) SetAlpha(p.Icon, a);
            }
        }

        private void Pop(Vector3 worldPoint, string text, Sprite icon, Color color, int size)
        {
            Popup p = null;
            for (int i = 0; i < popups.Count; i++)
                if (Time.unscaledTime - popups[i].Born >= popups[i].Life) { p = popups[i]; break; }
            if (p == null)
            {
                p = new Popup { Root = Rect("Popup", world, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 80f)) };
                p.Text = Label(p.Root, "", 44, Color.white, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 80f), TextAnchor.MiddleCenter);
                p.Text.fontStyle = FontStyle.BoldAndItalic;
                Outline o = p.Text.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(2.5f, -2.5f);
                p.Icon = Img(p.Root, "Icon", null, Color.white, Vector2.zero, new Vector2(56f, 56f));
                popups.Add(p);
            }
            p.World = worldPoint + new Vector3(Random.Range(-0.25f, 0.25f), 0f, 0f);
            p.Born = Time.unscaledTime;
            p.Life = 0.9f;
            p.Text.enabled = text != null;
            p.Text.text = text ?? "";
            p.Text.fontSize = size;
            p.Text.color = color;
            p.Icon.enabled = icon != null;
            p.Icon.sprite = icon;
            p.Icon.color = color;
            p.Icon.rectTransform.sizeDelta = new Vector2(size, size);
            p.Root.SetAsLastSibling();
        }

        private void DrawBanner(float now)
        {
            float age = now - bannerAt;
            float alpha = bannerStays ? 1f : bannerFor <= 0f ? 0f : Mathf.Clamp01(1f - (age - bannerFor * 0.75f) / (bannerFor * 0.25f + 0.001f));
            bannerRoot.gameObject.SetActive(alpha > 0.001f);
            if (alpha <= 0.001f) return;
            float punch = 1f + Mathf.Exp(-age * 10f) * 0.5f;
            bannerRoot.localScale = new Vector3(punch, punch, 1f);
            SetAlpha(bannerPlate, alpha * 0.95f);
            SetAlpha(bannerText, alpha);
            SetAlpha(bannerBurst, alpha * 0.45f);
            bannerBurst.rectTransform.localRotation = Quaternion.Euler(0f, 0f, now * 25f);
        }

        private void ShowBanner(string text, Color color, float seconds, bool stays)
        {
            if (bannerText == null) return;
            bannerText.text = text;
            bannerPlate.color = color;
            bannerBurst.color = Color.Lerp(color, Color.white, 0.3f);
            bannerAt = Time.unscaledTime;
            bannerFor = seconds;
            bannerStays = stays;
        }

        // ---------------------------------------------------------------- helpers

        private Color TeamColor(int fighter) => fighter == BrawlerMatch.Human ? humanColor : machineColor;

        private static string Ms(float ms) => ms > 0f ? ms.ToString("0", Inv) + " ms" : "-";

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
        }

        private void Place(RectTransform rt, Vector3 worldPoint)
        {
            if (view == null) return;
            Vector3 screen = view.WorldToScreenPoint(worldPoint);
            if (screen.z < 0f) screen = new Vector3(-9999f, -9999f, 0f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            rt.anchoredPosition = local;
        }

        private static Vector2 BarHome(int fighter) => new Vector2(fighter == BrawlerMatch.Human ? -470f : 470f, -46f);

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

            world = Rect("World", canvasRect, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            world.anchorMin = Vector2.zero;
            world.anchorMax = Vector2.one;
            world.sizeDelta = Vector2.zero;
            BuildThought();

            for (int i = 0; i < 2; i++) BuildHealth(i);
            BuildClock();
            BuildBanner();
            BuildLegend();
            BuildMatchOver();
            if (match.CurrentPhase == BrawlerMatch.Phase.Intro) OnRoundIntro(match.Round);
        }

        private void BuildHealth(int fighter)
        {
            bool left = fighter == BrawlerMatch.Human;
            RectTransform root = Rect(left ? "HumanHealth" : "MachineHealth", canvasRect, new Vector2(0.5f, 1f), BarHome(fighter), new Vector2(720f, 60f));
            barRoots[fighter] = root;
            Image back = Img(root, "Back", bar, Ink, Vector2.zero, new Vector2(720f, 60f));
            if (!left) back.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            trail[fighter] = Fill(root, new Color(1f, 0.92f, 0.8f, 0.85f), left);
            health[fighter] = Fill(root, TeamColor(fighter), left);

            Text name = Label(canvasRect, left ? "YOU" : match.MachineName, 30, TeamColor(fighter), new Vector2(0.5f, 1f),
                              new Vector2(left ? -560f : 560f, -102f), new Vector2(520f, 40f), left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            name.rectTransform.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(left ? -820f : 820f, -102f);
            Outline o = name.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.7f);
            o.effectDistance = new Vector2(2f, -2f);

            int n = Mathf.Clamp(match.Rules.roundsToWin, 1, 7);
            stars[fighter] = new Image[n];
            for (int k = 0; k < n; k++)
            {
                float x = (left ? -170f : 170f) + (left ? -k : k) * 38f;
                stars[fighter][k] = Img(canvasRect, "Round", star, Dim, Vector2.zero, new Vector2(32f, 32f));
                stars[fighter][k].rectTransform.anchorMin = stars[fighter][k].rectTransform.anchorMax = new Vector2(0.5f, 1f);
                stars[fighter][k].rectTransform.anchoredPosition = new Vector2(x, -102f);
            }
        }

        private Image Fill(RectTransform root, Color color, bool fromLeft)
        {
            Image f = Img(root, "Fill", bar, color, Vector2.zero, new Vector2(700f, 44f));
            f.type = UnityEngine.UI.Image.Type.Filled;
            f.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            // The machine's bar is mirrored, so it fills from its own outer end too.
            f.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Right;
            if (fromLeft) f.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Right;
            if (!fromLeft) f.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            f.fillAmount = 1f;
            return f;
        }

        private void BuildClock()
        {
            RectTransform clockRoot = Rect("Clock", canvasRect, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(220f, 120f));
            clockRoot.pivot = new Vector2(0.5f, 1f);
            Img(clockRoot, "Plate", plate, Ink, Vector2.zero, new Vector2(220f, 120f));
            clockDigits[0] = new SevenSegment(clockRoot, segment, new Vector2(-30f, 2f), 0.62f);
            clockDigits[1] = new SevenSegment(clockRoot, segment, new Vector2(30f, 2f), 0.62f);
        }

        private void BuildThought()
        {
            thought = Rect("MachineThought", world, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84f, 84f));
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
            Image tab = Img(thought, "Latency", key, new Color(0.05f, 0.06f, 0.09f, 0.8f), new Vector2(86f, 0f), new Vector2(92f, 28f));
            tab.type = UnityEngine.UI.Image.Type.Sliced;
            Img(tab.rectTransform, "Clock", clock, Muted, new Vector2(-30f, 0f), new Vector2(18f, 18f));
            thoughtMs = Label(tab.rectTransform, "", 18, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-16f, 1f), new Vector2(70f, 24f), TextAnchor.MiddleLeft);
            thoughtMs.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        private void BuildBanner()
        {
            bannerRoot = Rect("Banner", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(900f, 160f));
            bannerBurst = Img(bannerRoot, "Burst", burst, Color.white, Vector2.zero, new Vector2(440f, 440f));
            bannerPlate = Img(bannerRoot, "Plate", bar, Color.white, Vector2.zero, new Vector2(900f, 130f));
            bannerText = Label(bannerRoot, "", 104, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(860f, 140f), TextAnchor.MiddleCenter);
            bannerText.fontStyle = FontStyle.BoldAndItalic;
            bannerText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bannerText.verticalOverflow = VerticalWrapMode.Truncate;
            bannerText.resizeTextForBestFit = true;
            bannerText.resizeTextMinSize = 40;
            bannerText.resizeTextMaxSize = 104;
            Shadow sh = bannerText.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.65f);
            sh.effectDistance = new Vector2(5f, -5f);
            bannerRoot.gameObject.SetActive(false);
        }

        private void BuildLegend()
        {
            RectTransform legend = Rect("Controls", canvasRect, new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(1000f, 60f));
            Img(legend, "Back", key, new Color(0.05f, 0.06f, 0.09f, 0.85f), new Vector2(0f, 0f), new Vector2(1000f, 54f)).type = UnityEngine.UI.Image.Type.Sliced;
            float x = -470f;
            x = KeyItem(legend, x, "WASD", move, null);
            x = MouseItem(legend, x, mouseLeft, fist);
            x = MouseItem(legend, x, mouseRight, hook);
            x = KeyItem(legend, x, "SHIFT", shield, "hold");
            x = KeyItem(legend, x, "SPACE", roll, null);
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
            return x + 30f;
        }

        private float MouseItem(RectTransform parent, float x, Sprite mouse, Sprite icon)
        {
            Img(parent, "Mouse", mouse, new Color(0.85f, 0.87f, 0.92f), new Vector2(x + 14f, 0f), new Vector2(30f, 40f));
            x += 36f;
            Img(parent, "Icon", icon, Color.white, new Vector2(x + 15f, 0f), new Vector2(30f, 30f));
            return x + 64f;
        }

        private void BuildMatchOver()
        {
            RectTransform over = Rect("MatchOver", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(820f, 150f));
            Image back = Img(over, "Plate", plate, Ink, Vector2.zero, new Vector2(820f, 205f));
            back.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            Sprite[] icons = { clock, roll, fist, hook };
            string[] captions = { "decides", "answers a hook", "your jabs", "your hooks" };
            for (int i = 0; i < 4; i++)
            {
                float x = -300f + i * 200f;
                Img(over, "Icon", icons[i], i == 0 ? Muted : Color.white, new Vector2(x - 40f, 12f), new Vector2(36f, 36f));
                overStats[i] = Label(over, "", 32, Color.white, new Vector2(0.5f, 0.5f), new Vector2(x - 16f, 12f), new Vector2(140f, 44f), TextAnchor.MiddleLeft);
                overStats[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                Text c = Label(over, captions[i], 16, Muted, new Vector2(0.5f, 0.5f), new Vector2(x + 10f, -22f), new Vector2(190f, 22f), TextAnchor.MiddleCenter);
                c.fontStyle = FontStyle.Normal;
            }
            RectTransform replay = Rect("Replay", over, new Vector2(0.5f, 0.5f), new Vector2(0f, -78f), new Vector2(120f, 40f));
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

        /// <summary>A digit drawn with seven bars, lit in a colour and dimmed otherwise.</summary>
        private sealed class SevenSegment
        {
            private readonly Image[] bars = new Image[7];
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
}
