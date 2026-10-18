using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// The match's interface: the score with the machine called by its model's name, a card for
    /// each player showing whether they hold a ball and whether they can throw it, how far their
    /// dodge and catch have recovered, the machine's last input and how long it takes to decide,
    /// banners for the serve, each point and the result, and a reminder when a throw is refused.
    ///
    /// Built in code when the scene starts, from three sprites the scene builder generates, so
    /// there is no prefab or font asset to keep in step with it.
    /// </summary>
    [AddComponentMenu("Dodgeball/Dodgeball HUD")]
    public class DodgeballHud : MonoBehaviour
    {
        [Tooltip("The match to show.")]
        public DodgeballMatch match;

        [Tooltip("Rounded rectangle, sliced, for panels and bars.")]
        public Sprite panel;

        [Tooltip("The ball, for the held-ball indicator.")]
        public Sprite ballIcon;

        [Tooltip("The human's colour.")]
        public Color humanColor = new Color(0.3f, 0.6f, 1f);

        [Tooltip("The machine's colour.")]
        public Color machineColor = new Color(1f, 0.36f, 0.3f);

        private static readonly Color Ink = new Color(0.07f, 0.08f, 0.11f, 0.88f);
        private static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f);
        private static readonly Color Warn = new Color(1f, 0.66f, 0.22f);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private Font font;
        private Text[] scoreText = new Text[2];
        private float[] scorePunch = new float[2];
        private int[] shownScore = { -1, -1 };
        private Card[] cards = new Card[2];
        private Text autopilotTag;
        private Text machineCaption, machineSpeed;
        private Text banner, bannerSub;
        private float bannerAt = -10f, bannerFor;
        private Text toast;
        private float toastAt = -10f;
        private GameObject overPanel;
        private Text overTitle, overStats;

        private sealed class Card
        {
            public Image Ball;
            public Text BallText;
            public RectTransform DodgeFill, CatchFill;
            public Text DodgeLabel, CatchLabel;
            public Image DodgeImage, CatchImage;
        }

        // ---------------------------------------------------------------- lifecycle

        private void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
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

        private void OnServe()
        {
            if (overPanel != null) overPanel.SetActive(false);
            bool first = match.Score[0] + match.Score[1] == 0;
            ShowBanner(first ? "READY" : "SERVE", first ? "first to " + match.Rules.pointsToWin : Score(), Color.white, match.Rules.pointPause);
        }

        private void OnGo() => ShowBanner("GO!", "", new Color(0.55f, 1f, 0.6f), 0.6f);

        private void OnPoint(int winner, bool byCatch)
        {
            string who = winner == DodgeballMatch.Human ? "Point to you" : "Point to " + match.MachineName;
            ShowBanner(byCatch ? "CAUGHT!" : "HIT!", who, winner == DodgeballMatch.Human ? humanColor : machineColor,
                       DodgeballMatch.PointReplaySeconds);
        }

        private void OnMatchOver(int winner)
        {
            bannerFor = 0f;
            overPanel.SetActive(true);
            overTitle.text = winner == DodgeballMatch.Human ? "YOU WIN" : match.MachineName.ToUpperInvariant() + " WINS";
            overTitle.color = winner == DodgeballMatch.Human ? humanColor : machineColor;
            match.HumanThrowOutcomes(out int onTarget, out int caught, out int dodged, out int hit);
            overStats.text =
                $"{match.Score[DodgeballMatch.Human]} - {match.Score[DodgeballMatch.Machine]}\n\n" +
                $"{match.MachineName} decided in {Ms(match.MachineDecisionMedianMs)} (median) and answered your throws in {Ms(match.MachineReactionMedianMs)}.\n" +
                $"Of your {onTarget} throws on target it caught {caught}, got out of the way of {dodged} and took {hit}.\n\n" +
                "<color=#9AA3B5>R  play again</color>";
        }

        private void OnThrowRefused(int player)
        {
            if (player != DodgeballMatch.Human) return;
            toast.text = "Back up behind your line to throw";
            toastAt = Time.unscaledTime;
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (match == null || match.Court == null || font == null) return;
            DodgeballCourt court = match.Court;
            float now = Time.unscaledTime;

            for (int i = 0; i < 2; i++)
            {
                if (shownScore[i] != match.Score[i])
                {
                    if (shownScore[i] >= 0) scorePunch[i] = 1f;
                    shownScore[i] = match.Score[i];
                    scoreText[i].text = match.Score[i].ToString(Inv);
                }
                scorePunch[i] = Mathf.Max(0f, scorePunch[i] - Time.unscaledDeltaTime * 2.5f);
                float s = 1f + Mathf.Sin(scorePunch[i] * Mathf.PI) * 0.45f;
                scoreText[i].rectTransform.localScale = new Vector3(s, s, 1f);
                DrawCard(court, i);
            }

            autopilotTag.enabled = match.humanAutopilot;
            machineCaption.text = string.IsNullOrEmpty(match.MachineCaption) ? "waiting for its first decision" : "last input  " + match.MachineCaption;
            float decide = match.MachineDecisionMedianMs;
            machineSpeed.text = decide > 0f ? "decides in " + Ms(decide) : "";

            // Banner: punches in, holds, fades.
            float age = now - bannerAt;
            float alpha = bannerFor <= 0f ? 0f : Mathf.Clamp01(1f - (age - bannerFor * 0.7f) / (bannerFor * 0.3f + 0.001f));
            float punch = 1f + Mathf.Exp(-age * 12f) * 0.6f;
            SetAlpha(banner, alpha);
            SetAlpha(bannerSub, alpha);
            banner.rectTransform.localScale = new Vector3(punch, punch, 1f);

            float toastAge = now - toastAt;
            SetAlpha(toast, toastAge < 1.6f ? Mathf.Clamp01((1.6f - toastAge) * 3f) : 0f);
            toast.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(Mathf.Clamp01(toastAge * 4f) * Mathf.PI * 4f) * 10f * (1f - Mathf.Clamp01(toastAge * 4f)), 230f);
        }

        private void DrawCard(DodgeballCourt court, int i)
        {
            Card c = cards[i];
            CourtPlayer p = court.Players[i];
            Color team = i == DodgeballMatch.Human ? humanColor : machineColor;
            DodgeballRules r = court.Rules;

            if (p.Holding)
            {
                bool ready = court.CanThrow(i);
                c.Ball.color = Color.white;
                c.BallText.text = ready ? "READY TO THROW" : "BACK UP TO THROW";
                c.BallText.color = ready ? Color.white : Warn;
            }
            else
            {
                c.Ball.color = new Color(1f, 1f, 1f, 0.15f);
                c.BallText.text = "NO BALL";
                c.BallText.color = Muted;
            }
            float pulse = p.Holding && court.CanThrow(i) ? 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.06f : 1f;
            c.Ball.rectTransform.localScale = new Vector3(pulse, pulse, 1f);

            float dodge = p.CanDodge ? 1f : 1f - p.DodgeCooldown / (r.dodgeDuration + r.dodgeCooldown);
            SetBar(c.DodgeFill, c.DodgeImage, c.DodgeLabel, "DODGE", dodge, team);

            float catchReady = p.Holding ? 0f : p.Catching ? 1f : p.CanCatch ? 1f : 1f - p.CatchCooldown / (r.catchWindow + r.catchCooldown);
            string catchName = p.Catching ? "CATCHING" : p.Holding ? "CATCH  (hands full)" : "CATCH";
            SetBar(c.CatchFill, c.CatchImage, c.CatchLabel, catchName, catchReady, p.Catching ? new Color(1f, 0.85f, 0.25f) : team);
        }

        private static void SetBar(RectTransform fill, Image image, Text label, string name, float fraction, Color team)
        {
            fraction = Mathf.Clamp01(fraction);
            fill.anchorMax = new Vector2(fraction, 1f);
            bool full = fraction >= 0.999f;
            image.color = full ? team : Color.Lerp(team, new Color(0.35f, 0.38f, 0.45f), 0.6f);
            label.text = name;
            label.color = full ? Color.white : Muted;
        }

        private void ShowBanner(string text, string sub, Color color, float seconds)
        {
            if (banner == null) return;
            banner.text = text;
            banner.color = color;
            bannerSub.text = sub;
            bannerAt = Time.unscaledTime;
            bannerFor = seconds;
        }

        private string Score() => $"{match.Score[0]} - {match.Score[1]}";

        private static string Ms(float ms) => ms > 0f ? ms.ToString("0", Inv) + " ms" : "-";

        private static void SetAlpha(Graphic g, float a)
        {
            Color c = g.color;
            c.a = a;
            g.color = c;
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
            Transform root = canvasGo.transform;

            // Scoreboard.
            RectTransform board = Box("Scoreboard", root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(820f, 104f), Ink, new Vector2(0.5f, 1f));
            Box("HumanAccent", board, new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(12f, 104f), humanColor, new Vector2(0f, 0.5f));
            Box("MachineAccent", board, new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(12f, 104f), machineColor, new Vector2(1f, 0.5f));
            Label("HumanName", board, "YOU", 34, humanColor, new Vector2(0f, 0.5f), new Vector2(36f, 6f), new Vector2(240f, 44f), TextAnchor.MiddleLeft, FontStyle.Bold);
            autopilotTag = Label("Autopilot", board, "AUTOPILOT  (T)", 16, Muted, new Vector2(0f, 0.5f), new Vector2(36f, -26f), new Vector2(240f, 24f), TextAnchor.MiddleLeft, FontStyle.Normal);
            scoreText[0] = Label("HumanScore", board, "0", 66, Color.white, new Vector2(0.5f, 0.5f), new Vector2(-120f, 0f), new Vector2(110f, 90f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label("FirstTo", board, "FIRST TO " + match.Rules.pointsToWin, 17, Muted, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(140f, 24f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label("Versus", board, "VS", 26, Muted, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(140f, 34f), TextAnchor.MiddleCenter, FontStyle.Bold);
            scoreText[1] = Label("MachineScore", board, "0", 66, Color.white, new Vector2(0.5f, 0.5f), new Vector2(120f, 0f), new Vector2(110f, 90f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Text machineName = Label("MachineName", board, match.MachineName, 30, machineColor, new Vector2(1f, 0.5f), new Vector2(-36f, 6f), new Vector2(250f, 44f), TextAnchor.MiddleRight, FontStyle.Bold);
            machineName.resizeTextForBestFit = true;
            machineName.resizeTextMinSize = 16;
            machineName.resizeTextMaxSize = 30;
            Label("MachineKind", board, "LOCAL MODEL", 16, Muted, new Vector2(1f, 0.5f), new Vector2(-36f, -26f), new Vector2(250f, 24f), TextAnchor.MiddleRight, FontStyle.Normal);

            // Player cards.
            cards[0] = BuildCard(root, DodgeballMatch.Human, "YOU", humanColor);
            cards[1] = BuildCard(root, DodgeballMatch.Machine, match.MachineName, machineColor);

            // The machine's last input and decision time, above its card.
            machineCaption = Label("MachineCaption", root, "", 20, Color.white, new Vector2(1f, 0f), new Vector2(-28f, 222f), new Vector2(520f, 28f), TextAnchor.MiddleRight, FontStyle.Normal);
            machineSpeed = Label("MachineSpeed", root, "", 18, Muted, new Vector2(1f, 0f), new Vector2(-28f, 196f), new Vector2(520f, 26f), TextAnchor.MiddleRight, FontStyle.Normal);

            // Banner and toast.
            banner = Label("Banner", root, "", 130, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1400f, 170f), TextAnchor.MiddleCenter, FontStyle.Bold);
            DropShadow(banner, 5f);
            bannerSub = Label("BannerSub", root, "", 36, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1200f, 50f), TextAnchor.MiddleCenter, FontStyle.Bold);
            DropShadow(bannerSub, 3f);
            toast = Label("Toast", root, "", 30, Warn, new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(900f, 44f), TextAnchor.MiddleCenter, FontStyle.Bold);
            DropShadow(toast, 3f);
            SetAlpha(banner, 0f);
            SetAlpha(bannerSub, 0f);
            SetAlpha(toast, 0f);

            // Controls.
            Box("HelpBack", root, new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1180f, 34f), Ink, new Vector2(0.5f, 0f));
            Label("Help", root,
                  "WASD move     mouse aim     left click throw     right click catch     space dodge     T autopilot     R restart",
                  17, Muted, new Vector2(0.5f, 0f), new Vector2(0f, 35f), new Vector2(1160f, 26f), TextAnchor.MiddleCenter, FontStyle.Normal);

            // End of match.
            RectTransform over = Box("MatchOver", root, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1000f, 380f), new Color(0.05f, 0.06f, 0.09f, 0.94f));
            overTitle = Label("Title", over, "", 84, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(960f, 110f), TextAnchor.MiddleCenter, FontStyle.Bold);
            overTitle.resizeTextForBestFit = true;
            overTitle.resizeTextMinSize = 30;
            overTitle.resizeTextMaxSize = 84;
            DropShadow(overTitle, 4f);
            overStats = Label("Stats", over, "", 24, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(920f, 230f), TextAnchor.MiddleCenter, FontStyle.Normal);
            overPanel = over.gameObject;
            overPanel.SetActive(false);

            // The match may have served before this was built.
            if (match.CurrentPhase == DodgeballMatch.Phase.Serve) OnServe();
        }

        private Card BuildCard(Transform root, int player, string title, Color team)
        {
            bool left = player == DodgeballMatch.Human;
            Vector2 anchor = new Vector2(left ? 0f : 1f, 0f);
            RectTransform card = Box(left ? "HumanCard" : "MachineCard", root, anchor, new Vector2(left ? 28f : -28f, 54f), new Vector2(430f, 132f), Ink, anchor);
            Box("Accent", card, new Vector2(0f, 1f), Vector2.zero, new Vector2(430f, 6f), team, new Vector2(0f, 1f));

            Card c = new Card();
            Text name = Label("Name", card, title, 24, team, new Vector2(0f, 1f), new Vector2(20f, -26f), new Vector2(390f, 32f), TextAnchor.MiddleLeft, FontStyle.Bold);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 14;
            name.resizeTextMaxSize = 24;

            GameObject ballGo = new GameObject("Ball", typeof(RectTransform), typeof(Image));
            ballGo.transform.SetParent(card, false);
            c.Ball = ballGo.GetComponent<Image>();
            c.Ball.sprite = ballIcon;
            c.Ball.preserveAspect = true;
            Place(c.Ball.rectTransform, new Vector2(0f, 1f), new Vector2(46f, -76f), new Vector2(54f, 54f));
            c.BallText = Label("BallState", card, "NO BALL", 19, Muted, new Vector2(0f, 1f), new Vector2(86f, -62f), new Vector2(330f, 26f), TextAnchor.MiddleLeft, FontStyle.Bold);
            c.BallText.rectTransform.pivot = new Vector2(0f, 0.5f);

            c.DodgeFill = Bar(card, "Dodge", new Vector2(86f, -92f), out c.DodgeImage, out c.DodgeLabel);
            c.CatchFill = Bar(card, "Catch", new Vector2(86f, -114f), out c.CatchImage, out c.CatchLabel);
            return c;
        }

        private RectTransform Bar(RectTransform card, string name, Vector2 at, out Image fillImage, out Text label)
        {
            label = Label(name + "Label", card, name.ToUpperInvariant(), 15, Muted, new Vector2(0f, 1f), at, new Vector2(140f, 20f), TextAnchor.MiddleLeft, FontStyle.Bold);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            RectTransform back = Box(name + "Bar", card, new Vector2(0f, 1f), at + new Vector2(150f, 0f), new Vector2(180f, 12f), new Color(1f, 1f, 1f, 0.08f), new Vector2(0f, 0.5f));
            RectTransform fill = Box("Fill", back, Vector2.zero, Vector2.zero, Vector2.zero, Color.white, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fillImage = fill.GetComponent<Image>();
            return fill;
        }

        private RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, Vector2? pivot = null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = panel;
            image.type = panel != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            RectTransform rt = image.rectTransform;
            Place(rt, anchor, position, size);
            if (pivot.HasValue) rt.pivot = pivot.Value;
            return rt;
        }

        private Text Label(string name, Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 position,
                           Vector2 box, TextAnchor alignment, FontStyle style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = alignment;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Place(t.rectTransform, anchor, position, box);
            if (alignment == TextAnchor.MiddleLeft) t.rectTransform.pivot = new Vector2(0f, 0.5f);
            else if (alignment == TextAnchor.MiddleRight) t.rectTransform.pivot = new Vector2(1f, 0.5f);
            return t;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void DropShadow(Text t, float distance)
        {
            Shadow s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(distance, -distance);
        }
    }
}
