using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Samples.Dodgeball
{
    /// <summary>
    /// Draws the court and everything on it, and makes what happens feel like it happened: players
    /// that face where they aim, hands that swing, throw and reach for a catch, balls that lift and
    /// leave a trail, a freeze and a shake on a hit, a slow beat on a catch, ghosts behind a dodge,
    /// sparks, a crowd that jumps for its side, and a sound for each of them.
    ///
    /// None of it changes the game. It reads the court after the match steps it, through
    /// <see cref="DodgeballMatch.Stepped"/>, and the only thing it touches outside its own objects is
    /// <see cref="Time.timeScale"/>, for a few hundredths of a second at a time, which slows both
    /// players alike.
    /// </summary>
    [AddComponentMenu("Dodgeball/Dodgeball View")]
    public class DodgeballView : MonoBehaviour
    {
        /// <summary>One player's pieces. The root turns to face; the ground layer stays upright.</summary>
        [Serializable]
        public class PlayerRig
        {
            [Tooltip("Moves with the player and turns to face where they look.")]
            public Transform root;

            [Tooltip("Child of the root that squashes and stretches. Holds the torso and head.")]
            public Transform body;

            [Tooltip("The torso, tinted with the player's colour and flashed white on a hit.")]
            public SpriteRenderer bodySprite;

            [Tooltip("Hands, children of the root, so they swing with the facing but not the squash.")]
            public Transform handLeft;

            [Tooltip("The throwing hand. A held ball sits in it.")]
            public Transform handRight;

            [Tooltip("Moves with the player without turning: the shadow and the charge arc.")]
            public Transform ground;

            [Tooltip("Ring shown while a catch is open.")]
            public SpriteRenderer catchRing;

            [Tooltip("Arc around the player that fills while a throw charges.")]
            public LineRenderer chargeArc;

            [Tooltip("The player's colour.")]
            public Color color = Color.white;
        }

        /// <summary>One ball's pieces.</summary>
        [Serializable]
        public class BallRig
        {
            [Tooltip("Moves with the ball. Its scale is the ball's diameter.")]
            public Transform root;

            [Tooltip("The ball itself, raised and enlarged while it flies.")]
            public Transform sprite;

            [Tooltip("The ball's shadow, which falls behind it as it rises.")]
            public Transform shadow;

            [Tooltip("Streak left behind a thrown ball.")]
            public TrailRenderer trail;

            [Tooltip("Ring that fills around a dropped ball until it can be picked up again.")]
            public LineRenderer lockArc;
        }

        [Header("Wiring")]
        [Tooltip("The match to draw.")]
        public DodgeballMatch match;

        [Tooltip("The camera, shaken on impacts.")]
        public Camera view;

        [Tooltip("The human's rig, then the machine's.")]
        public PlayerRig[] players = new PlayerRig[2];

        [Tooltip("One rig per ball, in the court's order.")]
        public BallRig[] balls;

        [Tooltip("Dashed path a throw from the human's hand would take, bounces included, as far as " +
                 "the charge would carry it. It grows while the throw charges.")]
        public LineRenderer aimLine;

        [Tooltip("The same path for the machine, shown while it charges a throw.")]
        public LineRenderer machineAimLine;

        [Tooltip("The human's no-throw zone, then the machine's: lit while its player holds a ball in it.")]
        public SpriteRenderer[] noThrowZones = new SpriteRenderer[2];

        [Tooltip("Marker under the pointer.")]
        public SpriteRenderer reticle;

        [Tooltip("The human's throwing line, then the machine's. They pulse while their player holds a ball too close to throw.")]
        public LineRenderer[] throwingLines = new LineRenderer[2];

        [Tooltip("Parent of the spectators. A child whose name ends in 0 supports the human, in 1 the machine.")]
        public Transform crowd;

        [Tooltip("Particle system the bursts are emitted from.")]
        public ParticleSystem sparks;

        [Tooltip("Sprite for the afterimages a dodge leaves.")]
        public Sprite ghostSprite;

        [Tooltip("Material for the afterimages.")]
        public Material ghostMaterial;

        [Header("Feel")]
        [Tooltip("How far the camera shakes on the hardest impact, in metres. 0.35 is a firm jolt; 0 turns shaking off.")]
        [Range(0f, 1f)] public float shakeStrength = 0.35f;

        [Tooltip("Seconds everything freezes when a ball hits a player. A few hundredths sell the impact; much more feels like a stall.")]
        [Range(0f, 0.2f)] public float hitStop = 0.08f;

        [Tooltip("Seconds of slow motion after a catch.")]
        [Range(0f, 0.6f)] public float catchSlowMotion = 0.3f;

        [Tooltip("Volume of every sound effect. 0 mutes them.")]
        [Range(0f, 1f)] public float volume = 0.8f;

        // ---------------------------------------------------------------- state

        private readonly float[] facing = { 0f, 180f };
        private readonly float[] throwAnim = new float[2];
        private readonly float[] squash = new float[2];
        private readonly float[] flash = new float[2];
        private readonly float[] stride = new float[2];
        private readonly float[] lineFlash = new float[2];
        private readonly float[] ghostTimer = new float[2];
        private readonly float[] wasCharge = new float[2];
        private readonly List<Vector2> path = new List<Vector2>();
        private float[] ballAge;
        private BallState[] ballWas;
        private Vector2[] ballVelocity;

        private int deflectBall = -1;
        private Vector2 deflectFrom, deflectVelocity;
        private float deflectStart;

        private float trauma;
        private float hitStopUntil, slowUntil;
        private Vector3 cameraHome;
        private Quaternion cameraRotation = Quaternion.identity;

        private float cheerAt = -10f;
        private int cheerSide = -1;
        private Transform[] fans;
        private Vector3[] fanHome;
        private Vector3[] fanScale;
        private int[] fanTeam;

        private readonly List<Ghost> ghosts = new List<Ghost>();

        private AudioSource audioSource;
        private AudioClip sThrow, sHit, sCatch, sPickup, sDodge, sWall, sBuzzer, sBeep, sGo, sPoint, sCrowd, sCharge, sFull, sClack;

        private struct Ghost
        {
            public SpriteRenderer Sprite;
            public float Born;
        }

        private const float GhostLife = 0.28f;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (view != null)
            {
                cameraHome = view.transform.position;
                cameraRotation = view.transform.rotation;
            }
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            sThrow = DodgeballSound.Throw(); sHit = DodgeballSound.Hit(); sCatch = DodgeballSound.Catch();
            sPickup = DodgeballSound.Pickup(); sDodge = DodgeballSound.Dodge(); sWall = DodgeballSound.Wall();
            sBuzzer = DodgeballSound.Buzzer(); sBeep = DodgeballSound.Beep(); sGo = DodgeballSound.GoBeep();
            sPoint = DodgeballSound.Point(); sCrowd = DodgeballSound.Crowd();
            sCharge = DodgeballSound.Charge(); sFull = DodgeballSound.FullCharge(); sClack = DodgeballSound.Clack();

            if (crowd != null)
            {
                int n = crowd.childCount;
                fans = new Transform[n];
                fanHome = new Vector3[n];
                fanScale = new Vector3[n];
                fanTeam = new int[n];
                for (int i = 0; i < n; i++)
                {
                    fans[i] = crowd.GetChild(i);
                    fanHome[i] = fans[i].localPosition;
                    fanScale[i] = fans[i].localScale;
                    fanTeam[i] = fans[i].name.EndsWith("1") ? DodgeballMatch.Machine : DodgeballMatch.Human;
                }
            }
        }

        private void OnEnable()
        {
            if (match == null) return;
            match.Stepped += OnStepped;
            match.PointWon += OnPoint;
            match.ServeStarted += OnServe;
            match.Go += OnGo;
            match.ThrowRefused += OnThrowRefused;
            match.MatchOver += OnMatchOver;
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            if (match == null) return;
            match.Stepped -= OnStepped;
            match.PointWon -= OnPoint;
            match.ServeStarted -= OnServe;
            match.Go -= OnGo;
            match.ThrowRefused -= OnThrowRefused;
            match.MatchOver -= OnMatchOver;
        }

        private void LateUpdate()
        {
            if (match == null || match.Court == null) return;
            DodgeballCourt court = match.Court;
            EnsureBallState(court);

            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;
            for (int i = 0; i < 2; i++) DrawPlayer(court, i, dt, udt);
            DrawBalls(court, dt);
            DrawAim(court);
            DrawMachineAim(court);
            DrawZones(court);
            DrawThrowingLines(court, udt);
            DrawCrowd();
            UpdateGhosts();
            UpdateTimeAndCamera(udt);
        }

        private void EnsureBallState(DodgeballCourt court)
        {
            if (ballAge != null && ballAge.Length == court.Balls.Count) return;
            ballAge = new float[court.Balls.Count];
            ballWas = new BallState[court.Balls.Count];
            ballVelocity = new Vector2[court.Balls.Count];
        }

        // ---------------------------------------------------------------- court events

        private void OnStepped(DodgeballCourt court)
        {
            EnsureBallState(court);
            for (int i = 0; i < court.Events.Count; i++)
            {
                CourtEvent e = court.Events[i];
                switch (e.Kind)
                {
                    case CourtEventKind.Throw:
                    {
                        throwAnim[e.Player] = 1f;
                        squash[e.Player] = 0.18f;
                        ballAge[e.Ball] = 0f;
                        if (balls != null && e.Ball < balls.Length && balls[e.Ball].trail != null)
                        {
                            balls[e.Ball].trail.Clear();
                            Color c = players[e.Player].color;
                            balls[e.Ball].trail.startColor = new Color(c.r, c.g, c.b, 0.8f);
                            balls[e.Ball].trail.endColor = new Color(c.r, c.g, c.b, 0f);
                        }
                        Vector2 v = court.Balls[e.Ball].Velocity;
                        facing[e.Player] = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                        Play(sThrow, 0.7f, UnityEngine.Random.Range(0.9f, 1.15f));
                        Shake(0.12f);
                        break;
                    }

                    case CourtEventKind.Pickup:
                        squash[e.Player] = -0.15f;
                        Play(sPickup, 0.6f, UnityEngine.Random.Range(0.95f, 1.1f));
                        Burst(court.Players[e.Player].Position, new Color(1f, 0.85f, 0.4f), 8, 2f, 0.12f);
                        break;

                    case CourtEventKind.Hit:
                    {
                        int target = 1 - e.Player;
                        Vector2 at = court.Players[target].Position;
                        flash[target] = 1f;
                        squash[target] = 0.35f;
                        Burst(at, players[e.Player].color, 34, 7f, 0.22f);
                        Burst(at, Color.white, 14, 4f, 0.16f);
                        Shake(0.8f);
                        hitStopUntil = Time.unscaledTime + hitStop;
                        Play(sHit, 1f, 1f);

                        // The ball glances off the player instead of dropping dead at their feet.
                        Vector2 incoming = ballVelocity[e.Ball];
                        Vector2 away = incoming.sqrMagnitude > 0.01f ? incoming.normalized : Vector2.right;
                        Vector2 sideways = new Vector2(-away.y, away.x) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
                        deflectBall = e.Ball;
                        deflectFrom = at;
                        deflectVelocity = (-away * 0.4f + sideways) * 5f;
                        deflectStart = Time.time;
                        break;
                    }

                    case CourtEventKind.Catch:
                        squash[e.Player] = -0.25f;
                        Burst(court.Players[e.Player].Position, new Color(1f, 0.85f, 0.2f), 30, 6f, 0.2f);
                        Shake(0.35f);
                        slowUntil = Time.unscaledTime + catchSlowMotion;
                        Play(sCatch, 0.9f, 1f);
                        break;

                    case CourtEventKind.CatchOpened:
                        squash[e.Player] = -0.08f;
                        break;

                    case CourtEventKind.ChargeStarted:
                        squash[e.Player] = -0.1f;
                        Play(sCharge, 0.45f, 1f);
                        break;

                    case CourtEventKind.Drop:
                        Burst(court.Balls[e.Ball].Position, new Color(1f, 0.8f, 0.35f), 6, 2f, 0.1f);
                        Play(sWall, 0.4f, 1.4f);
                        break;

                    case CourtEventKind.WallBounce:
                        Burst(court.Balls[e.Ball].Position, Color.white, 8, 3f, 0.11f);
                        Play(sWall, 0.55f, UnityEngine.Random.Range(1.05f, 1.25f));
                        Shake(0.05f);
                        break;

                    case CourtEventKind.BallBounce:
                        Burst(court.Balls[e.Ball].Position, new Color(1f, 0.9f, 0.5f), 10, 3.5f, 0.12f);
                        Play(sClack, 0.7f, UnityEngine.Random.Range(0.9f, 1.15f));
                        Shake(0.08f);
                        break;

                    case CourtEventKind.BallDied:
                        Burst(court.Balls[e.Ball].Position, new Color(0.8f, 0.75f, 0.7f), 6, 1.5f, 0.1f);
                        break;

                    case CourtEventKind.Dodge:
                        squash[e.Player] = 0.22f;
                        ghostTimer[e.Player] = 0.2f;
                        Burst(court.Players[e.Player].Position, new Color(0.85f, 0.85f, 0.9f), 10, 3f, 0.14f);
                        Play(sDodge, 0.7f, UnityEngine.Random.Range(0.9f, 1.1f));
                        break;
                }
            }

            // A ball that was flying and is now free, on a step that scored nothing, stopped
            // against a wall.
            for (int i = 0; i < court.Balls.Count; i++)
            {
                CourtBall b = court.Balls[i];
                ballWas[i] = b.State;
                if (b.State == BallState.Flying) ballVelocity[i] = b.Velocity;
            }
        }

        private void OnPoint(int winner, bool byCatch)
        {
            cheerAt = Time.unscaledTime;
            cheerSide = winner;
            Play(sPoint, 0.7f, 1f);
            Play(sCrowd, 0.8f, 1f);
        }

        private void OnServe()
        {
            deflectBall = -1;
            if (ballWas != null) for (int i = 0; i < ballWas.Length; i++) ballWas[i] = BallState.Free;
            if (balls != null) for (int i = 0; i < balls.Length; i++) if (balls[i].trail != null) balls[i].trail.Clear();
            Play(sBeep, 0.5f, 1f);
        }

        private void OnGo() => Play(sGo, 0.6f, 1f);

        private void OnThrowRefused(int player)
        {
            lineFlash[player] = 1f;
            Play(sBuzzer, 0.6f, 1f);
            Shake(0.06f);
        }

        private void OnMatchOver(int winner)
        {
            cheerAt = Time.unscaledTime;
            cheerSide = -1;
            Play(sCrowd, 1f, 0.9f);
            DodgeballRules r = match.Rules;
            for (int i = 0; i < 8; i++)
            {
                Vector2 at = new Vector2(UnityEngine.Random.Range(-r.HalfLength, r.HalfLength) * 0.8f,
                                         UnityEngine.Random.Range(-r.HalfWidth, r.HalfWidth) * 0.8f);
                Burst(at, Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 1f), 26, 6f, 0.2f);
            }
        }

        // ---------------------------------------------------------------- players

        private void DrawPlayer(DodgeballCourt court, int i, float dt, float udt)
        {
            PlayerRig rig = players[i];
            if (rig == null || rig.root == null) return;
            CourtPlayer p = court.Players[i];
            Vector3 at = new Vector3(p.Position.x, p.Position.y, 0f);
            rig.root.position = at;
            if (rig.ground != null) rig.ground.position = at;

            // Where to face: the human looks at the pointer. Otherwise a player faces the opponent
            // while holding a ball, catching or standing, and faces the way they run.
            Vector2 look;
            if (i == DodgeballMatch.Human && match.HumanAimsWithPointer) look = match.HumanAim - p.Position;
            else if (p.Holding || p.Catching || p.Velocity.sqrMagnitude < 0.2f) look = court.Opponent(i).Position - p.Position;
            else look = p.Velocity;
            if (throwAnim[i] <= 0.6f && look.sqrMagnitude > 0.0001f)
            {
                float target = Mathf.Atan2(look.y, look.x) * Mathf.Rad2Deg;
                facing[i] = Mathf.LerpAngle(facing[i], target, 1f - Mathf.Exp(-18f * udt));
            }
            rig.root.rotation = Quaternion.Euler(0f, 0f, facing[i] - 90f);

            // Running swings the hands; holding, catching and throwing override them.
            float speed = match.Playing ? p.Velocity.magnitude : 0f;
            stride[i] += speed * dt * 3.2f;
            float swing = Mathf.Sin(stride[i]) * Mathf.Clamp01(speed / 3f) * 0.12f;
            Vector3 left = new Vector3(-0.4f, 0.08f + swing, 0f);
            Vector3 right = new Vector3(0.4f, 0.08f - swing, 0f);
            if (p.Holding) right = new Vector3(0.36f, 0.36f, 0f);
            if (p.HeldCount > 1) left = new Vector3(-0.38f, 0.3f, 0f);
            if (p.Charging)
            {
                // Wound back further the longer it charges, trembling once it is full.
                float charge = court.Charge(i);
                float tremble = charge >= 1f ? Mathf.Sin(Time.time * 70f) * 0.025f : 0f;
                right = new Vector3(0.48f + charge * 0.1f + tremble, 0.1f - charge * 0.35f, 0f);
                if (charge >= 1f && wasCharge[i] < 1f) { Play(sFull, 0.5f, i == 0 ? 1f : 0.9f); squash[i] = 0.12f; }
                wasCharge[i] = charge;
            }
            else wasCharge[i] = 0f;
            if (p.Catching)
            {
                left = new Vector3(-0.24f, 0.5f, 0f);
                right = new Vector3(0.24f, 0.5f, 0f);
            }
            if (throwAnim[i] > 0f)
            {
                float k = Mathf.Sin(throwAnim[i] * Mathf.PI);
                right = Vector3.Lerp(right, new Vector3(0.12f, 0.74f, 0f), k);
                throwAnim[i] = Mathf.Max(0f, throwAnim[i] - dt / 0.22f);
            }
            float handEase = 1f - Mathf.Exp(-30f * dt);
            if (rig.handLeft != null) rig.handLeft.localPosition = Vector3.Lerp(rig.handLeft.localPosition, left, handEase);
            if (rig.handRight != null) rig.handRight.localPosition = Vector3.Lerp(rig.handRight.localPosition, right, handEase);

            // Squash and stretch: a kick that settles back to round, over a slow breath.
            squash[i] = Mathf.Lerp(squash[i], 0f, 1f - Mathf.Exp(-10f * udt));
            float breathe = Mathf.Sin(Time.time * 3f + i * 1.3f) * 0.015f;
            if (rig.body != null) rig.body.localScale = new Vector3(1f - squash[i] * 0.6f + breathe, 1f + squash[i] + breathe, 1f);

            if (rig.bodySprite != null)
            {
                flash[i] = Mathf.Max(0f, flash[i] - udt * 4f);
                rig.bodySprite.color = Color.Lerp(rig.color, Color.white, flash[i]);
            }

            if (rig.catchRing != null)
            {
                rig.catchRing.enabled = p.Catching;
                if (p.Catching)
                {
                    float f = p.CatchTimeLeft / court.Rules.catchWindow;
                    rig.catchRing.transform.localScale = Vector3.one * (1.2f + (1f - f) * 0.4f);
                    rig.catchRing.color = new Color(1f, 0.85f, 0.25f, 0.3f + 0.7f * f);
                }
            }

            if (rig.chargeArc != null)
            {
                float charge = court.Charge(i);
                Arc(rig.chargeArc, 0.7f, p.Charging ? Mathf.Max(0.02f, charge) : 0f);
                Color cc = Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.15f), charge);
                if (charge >= 1f) cc = Color.Lerp(cc, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 30f));
                rig.chargeArc.startColor = rig.chargeArc.endColor = cc;
            }

            if (ghostTimer[i] > 0f)
            {
                float before = ghostTimer[i];
                ghostTimer[i] -= dt;
                // One afterimage every 40 ms of the dodge.
                if (Mathf.Floor(before / 0.04f) != Mathf.Floor(ghostTimer[i] / 0.04f)) SpawnGhost(p.Position, rig.color);
            }
        }

        /// <summary>Draws the remaining <paramref name="fraction"/> of a cooldown as an arc from twelve o'clock; hidden at 0.</summary>
        private static void Arc(LineRenderer line, float radius, float fraction)
        {
            if (line == null) return;
            line.enabled = fraction > 0.001f;
            if (!line.enabled) return;
            const int Segments = 40;
            int n = Mathf.Max(2, Mathf.CeilToInt(Segments * fraction) + 1);
            line.positionCount = n;
            for (int k = 0; k < n; k++)
            {
                float a = Mathf.PI * 0.5f - (k / (float)(n - 1)) * fraction * Mathf.PI * 2f;
                line.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        // ---------------------------------------------------------------- balls

        private void DrawBalls(DodgeballCourt court, float dt)
        {
            if (balls == null) return;
            for (int i = 0; i < balls.Length && i < court.Balls.Count; i++)
            {
                BallRig rig = balls[i];
                CourtBall b = court.Balls[i];
                if (rig == null || rig.root == null) continue;

                Vector3 pos = new Vector3(b.Position.x, b.Position.y, 0f);
                float height = 0f;
                switch (b.State)
                {
                    case BallState.Held:
                        CourtPlayer holder = court.Players[b.Holder];
                        Transform hand = holder.Held.IndexOf(i) == 0 ? players[b.Holder].handRight : players[b.Holder].handLeft;
                        if (hand != null) pos = new Vector3(hand.position.x, hand.position.y, 0f);
                        height = 0.12f;
                        break;
                    case BallState.Flying:
                        // A flat throw seen from above: it lifts a little early in its flight, which
                        // reads as the ball growing and its shadow falling behind.
                        ballAge[i] += dt;
                        height = 0.3f * Mathf.Sin(Mathf.Clamp01(ballAge[i] / 0.8f) * Mathf.PI) + 0.1f;
                        if (rig.sprite != null) rig.sprite.Rotate(0f, 0f, -900f * dt);
                        break;
                    default:
                        if (i == deflectBall && match.CurrentPhase == DodgeballMatch.Phase.Point)
                        {
                            // Decelerating glance off the player, with two small bounces.
                            float t = Time.time - deflectStart;
                            const float Drag = 4f;
                            Vector2 off = deflectVelocity * (1f - Mathf.Exp(-Drag * t)) / Drag;
                            pos = new Vector3(deflectFrom.x + off.x, deflectFrom.y + off.y, 0f);
                            height = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.35f * Mathf.Exp(-t * 4f);
                            if (rig.sprite != null) rig.sprite.Rotate(0f, 0f, -400f * Mathf.Exp(-t * 3f) * dt);
                        }
                        break;
                }

                rig.root.position = pos;
                if (rig.sprite != null)
                {
                    rig.sprite.localScale = Vector3.one * (1f + height);
                    rig.sprite.localPosition = new Vector3(0f, height * 0.5f, 0f);
                }
                if (rig.shadow != null)
                {
                    rig.shadow.localPosition = new Vector3(0.15f + height * 0.6f, -0.15f - height * 0.6f, 0f);
                    rig.shadow.localScale = Vector3.one * (1.1f - height * 0.3f);
                }
                if (rig.trail != null) rig.trail.emitting = b.State == BallState.Flying;
                if (rig.lockArc != null)
                {
                    float locked = b.State == BallState.Free && b.Locked ? 1f - b.LockTime / Mathf.Max(0.01f, court.Rules.pickupLock) : 0f;
                    Arc(rig.lockArc, 1.0f, b.State == BallState.Free && b.Locked ? Mathf.Max(0.02f, locked) : 0f);
                }
            }
        }

        // ---------------------------------------------------------------- aim and lines

        private void DrawAim(DodgeballCourt court)
        {
            CourtPlayer me = court.Players[DodgeballMatch.Human];
            bool show = match.HumanAimsWithPointer && match.CurrentPhase != DodgeballMatch.Phase.Over;
            if (reticle != null) reticle.enabled = show;
            if (aimLine != null) aimLine.enabled = show && me.Holding;
            if (!show) return;

            bool ready = court.CanThrow(DodgeballMatch.Human);
            Vector3 to = new Vector3(match.HumanAim.x, match.HumanAim.y, 0f);
            // White when a release would throw, red when holding a ball in the no-throw zone.
            Color c = ready ? new Color(1f, 1f, 1f, 0.95f)
                    : me.Holding ? new Color(1f, 0.3f, 0.2f, 0.9f)
                    : new Color(1f, 1f, 1f, 0.3f);
            if (aimLine != null && me.Holding)
            {
                // The path as far as the throw would go if released now: short before charging,
                // growing with the charge.
                float charge = court.Charge(DodgeballMatch.Human);
                float range = me.Charging ? court.Rules.RangeFor(charge) : court.Rules.minRange * 0.5f;
                Vector2 dir = DodgeballCourt.ThrowDirection(me, match.HumanAim);
                DrawPath(aimLine, court, me.Position + dir * (court.Rules.playerRadius + court.Rules.ballRadius), dir, range,
                         new Color(c.r, c.g, c.b, me.Charging ? c.a : c.a * 0.45f));
                aimLine.widthMultiplier = me.Charging ? 0.07f + charge * 0.04f : 0.05f;
            }
            if (reticle != null)
            {
                reticle.transform.position = to;
                reticle.transform.rotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * (ready ? 90f : 25f));
                reticle.color = c;
                float size = ready ? 0.62f + Mathf.Sin(Time.unscaledTime * 8f) * 0.05f : 0.5f;
                reticle.transform.localScale = Vector3.one * size;
            }
        }

        private void DrawMachineAim(DodgeballCourt court)
        {
            if (machineAimLine == null) return;
            CourtPlayer m = court.Players[DodgeballMatch.Machine];
            DodgeballMachinePad pad = match.machinePad;
            bool show = m.Charging && pad != null && pad.TryGetChargeAim(out Vector2 aim) && match.Playing;
            machineAimLine.enabled = show;
            if (!show) return;
            pad.TryGetChargeAim(out Vector2 target);
            float charge = court.Charge(DodgeballMatch.Machine);
            Vector2 dir = DodgeballCourt.ThrowDirection(m, target);
            Color c = players[DodgeballMatch.Machine].color;
            DrawPath(machineAimLine, court, m.Position + dir * (court.Rules.playerRadius + court.Rules.ballRadius), dir,
                     court.Rules.RangeFor(charge), new Color(c.r, c.g, c.b, 0.9f));
            machineAimLine.widthMultiplier = 0.06f + charge * 0.04f;
        }

        /// <summary>Lays <paramref name="line"/> along the path a ball would take, fading toward its end.</summary>
        private void DrawPath(LineRenderer line, DodgeballCourt court, Vector2 from, Vector2 dir, float range, Color c)
        {
            court.PredictPath(from, dir, range, path);
            line.positionCount = path.Count;
            for (int k = 0; k < path.Count; k++) line.SetPosition(k, new Vector3(path[k].x, path[k].y, 0f));
            line.startColor = c;
            line.endColor = new Color(c.r, c.g, c.b, c.a * 0.25f);
        }

        private void DrawZones(DodgeballCourt court)
        {
            for (int i = 0; i < 2; i++)
            {
                SpriteRenderer zone = noThrowZones[i];
                if (zone == null) continue;
                CourtPlayer p = court.Players[i];
                bool warn = p.Holding && court.InNoThrowZone(i) && match.Playing;
                float target = warn ? 0.16f + 0.1f * Mathf.Sin(Time.unscaledTime * 8f) : 0f;
                target = Mathf.Max(target, lineFlash[i] * 0.35f);
                Color c = zone.color;
                c.a = Mathf.Lerp(c.a, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                zone.color = c;
            }
        }

        private void DrawThrowingLines(DodgeballCourt court, float udt)
        {
            for (int i = 0; i < 2; i++)
            {
                LineRenderer line = throwingLines[i];
                if (line == null) continue;
                lineFlash[i] = Mathf.Max(0f, lineFlash[i] - udt * 2.5f);
                CourtPlayer p = court.Players[i];
                // Holding a ball in front of the line makes it pulse: that is where to go.
                float warn = p.Holding && !court.CanThrow(i) && match.Playing
                    ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f) : 0f;
                Color c = Color.Lerp(new Color(1f, 1f, 1f, 0.55f), new Color(1f, 0.65f, 0.2f, 0.95f), warn);
                c = Color.Lerp(c, new Color(1f, 0.2f, 0.12f, 1f), lineFlash[i]);
                line.startColor = line.endColor = c;
                float w = 0.07f + lineFlash[i] * 0.08f + warn * 0.03f;
                line.startWidth = line.endWidth = w;
            }
        }

        // ---------------------------------------------------------------- crowd

        private void DrawCrowd()
        {
            if (fans == null) return;
            float since = Time.unscaledTime - cheerAt;
            for (int i = 0; i < fans.Length; i++)
            {
                Vector3 home = fanHome[i];
                float bob = Mathf.Sin(Time.unscaledTime * 2.1f + i * 1.7f) * 0.025f;
                // The scorer's supporters leap; the cheer starts at the scorer's end and runs along
                // the stands. A side of -1 is the end of the match, when everyone stands up.
                bool cheering = cheerSide < 0 || fanTeam[i] == cheerSide;
                float fromEnd = cheerSide == DodgeballMatch.Machine ? -home.x : home.x;
                float t = since - (fromEnd + 12f) * 0.025f;
                float jump = 0f;
                if (cheering && t > 0f && t < 1.6f)
                    jump = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * Mathf.Exp(-t * 1.8f) * 0.3f;
                fans[i].localPosition = home + new Vector3(0f, bob, 0f);
                fans[i].localScale = fanScale[i] * (1f + jump);
            }
        }

        // ---------------------------------------------------------------- ghosts and sparks

        private void SpawnGhost(Vector2 at, Color color)
        {
            if (ghostSprite == null) return;
            GameObject go = new GameObject("Afterimage");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(at.x, at.y, 0f);
            go.transform.localScale = Vector3.one * 0.9f;
            SpriteRenderer s = go.AddComponent<SpriteRenderer>();
            s.sprite = ghostSprite;
            if (ghostMaterial != null) s.sharedMaterial = ghostMaterial;
            s.color = new Color(color.r, color.g, color.b, 0.45f);
            s.sortingOrder = 4;
            ghosts.Add(new Ghost { Sprite = s, Born = Time.time });
        }

        private void UpdateGhosts()
        {
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = ghosts[i];
                float age = Time.time - g.Born;
                if (age >= GhostLife || g.Sprite == null)
                {
                    if (g.Sprite != null) Destroy(g.Sprite.gameObject);
                    ghosts.RemoveAt(i);
                    continue;
                }
                Color c = g.Sprite.color;
                c.a = 0.45f * (1f - age / GhostLife);
                g.Sprite.color = c;
            }
        }

        private void Burst(Vector2 at, Color color, int count, float speed, float size)
        {
            if (sparks == null) return;
            ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = new Vector3(at.x, at.y, 0f);
                p.velocity = (Vector3)(UnityEngine.Random.insideUnitCircle.normalized * speed * UnityEngine.Random.Range(0.3f, 1f));
                p.startColor = Color.Lerp(color, Color.white, UnityEngine.Random.Range(0f, 0.3f));
                p.startSize = size * UnityEngine.Random.Range(0.6f, 1.3f);
                p.startLifetime = UnityEngine.Random.Range(0.25f, 0.55f);
                sparks.Emit(p, 1);
            }
        }

        // ---------------------------------------------------------------- time and camera

        private void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        private void UpdateTimeAndCamera(float udt)
        {
            float now = Time.unscaledTime;
            Time.timeScale = now < hitStopUntil ? 0.02f : now < slowUntil ? 0.35f : 1f;

            trauma = Mathf.Max(0f, trauma - udt * 1.8f);
            if (view == null) return;
            // Squared, so small knocks barely move the camera and a hit jolts it.
            float s = trauma * trauma * shakeStrength;
            float t = now * 30f;
            view.transform.position = cameraHome + new Vector3(
                (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * s,
                (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * s, 0f);
            view.transform.rotation = cameraRotation * Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, t) - 0.5f) * 4f * s);
        }

        private void Play(AudioClip clip, float vol, float pitch)
        {
            if (clip == null || audioSource == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, vol * volume);
        }
    }
}
