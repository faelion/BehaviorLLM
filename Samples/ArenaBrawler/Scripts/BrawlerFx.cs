using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// Makes the fight feel like one: a freeze and a shake when a punch lands, sparks where it
    /// lands (orange on a hit, blue on a block, violet when a guard breaks), the struck fighter
    /// flashing white, dust under a roll, slow motion on a knockout, a camera that follows the
    /// fighters and closes in on the winner to circle them while they dance under a spotlight in
    /// a shower of confetti, a crowd that jumps, and a sound for each.
    ///
    /// None of it changes the fight. It reads the arena through <see cref="BrawlerMatch.Stepped"/>,
    /// and the only thing it touches outside its own objects is <see cref="Time.timeScale"/>, for
    /// moments, which slows both fighters alike.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Brawler Fx")]
    public class BrawlerFx : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The match to dress.")]
        public BrawlerMatch match;

        [Tooltip("The camera: framed on the fight, shaken on impacts, and sent round the winner.")]
        public Camera view;

        [Tooltip("Particles for sparks and dust, emitted from code.")]
        public ParticleSystem sparks;

        [Tooltip("Particles for the winner's confetti, emitted from code.")]
        public ParticleSystem confetti;

        [Tooltip("Spotlight that finds the winner.")]
        public Light spotlight;

        [Tooltip("The arena's main light, dimmed while the spotlight is on.")]
        public Light keyLight;

        [Tooltip("Parent of the spectators, who bob and jump.")]
        public Transform crowd;

        [Header("Feel")]
        [Tooltip("Seconds the fight freezes on a clean jab. A hook freezes for twice as long.")]
        [Range(0f, 0.2f)] public float hitStop = 0.05f;

        [Tooltip("How far the camera shakes on the hardest impact, in metres. 0 turns shaking off.")]
        [Range(0f, 0.6f)] public float shakeStrength = 0.25f;

        [Tooltip("Speed of time during a knockout's slow motion.")]
        [Range(0.05f, 1f)] public float knockoutTimeScale = 0.25f;

        [Tooltip("Seconds, in real time, the knockout's slow motion lasts.")]
        [Range(0f, 3f)] public float knockoutSlowMotion = 1.3f;

        [Tooltip("Volume of every sound effect. 0 mutes them.")]
        [Range(0f, 1f)] public float volume = 0.8f;

        private Vector3 cameraHome;
        private Quaternion cameraHomeRotation;
        private Vector3 cameraPos;
        private Quaternion cameraRot;
        private float orbit;
        private float trauma;
        private float hitStopUntil, slowUntil;
        private float keyIntensity;
        private float cheerAt = -10f, bigCheerAt = -10f;
        private float confettiCarry;

        private Transform[] fans;
        private Vector3[] fanHome;

        private AudioSource audioSource;
        private AudioClip sWhoosh, sJab, sHook, sBlock, sBreak, sRoll, sKO, sBell, sCrowd, sFanfare;

        private const float ChestHeight = 1.3f;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (view != null)
            {
                cameraHome = cameraPos = view.transform.position;
                cameraHomeRotation = cameraRot = view.transform.rotation;
            }
            if (keyLight != null) keyIntensity = keyLight.intensity;
            if (spotlight != null) spotlight.intensity = 0f;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            sWhoosh = BrawlerSound.Whoosh(); sJab = BrawlerSound.Jab(); sHook = BrawlerSound.Hook();
            sBlock = BrawlerSound.Block(); sBreak = BrawlerSound.GuardBreak(); sRoll = BrawlerSound.Roll();
            sKO = BrawlerSound.KO(); sBell = BrawlerSound.Bell(); sCrowd = BrawlerSound.Crowd(); sFanfare = BrawlerSound.Fanfare();

            if (crowd != null)
            {
                fans = new Transform[crowd.childCount];
                fanHome = new Vector3[crowd.childCount];
                for (int i = 0; i < fans.Length; i++) { fans[i] = crowd.GetChild(i); fanHome[i] = fans[i].localPosition; }
            }
        }

        private void OnEnable()
        {
            if (match == null) return;
            match.Stepped += OnStepped;
            match.RoundIntro += OnRoundIntro;
            match.Fight += OnFight;
            match.RoundWon += OnRoundWon;
            match.MatchWon += OnMatchWon;
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
            if (match == null) return;
            match.Stepped -= OnStepped;
            match.RoundIntro -= OnRoundIntro;
            match.Fight -= OnFight;
            match.RoundWon -= OnRoundWon;
            match.MatchWon -= OnMatchWon;
        }

        // ---------------------------------------------------------------- events

        private void OnStepped(BrawlerArena arena)
        {
            for (int i = 0; i < arena.Events.Count; i++)
            {
                ArenaEvent e = arena.Events[i];
                BrawlerFighter a = arena.Fighters[e.Fighter];
                BrawlerFighter d = arena.Opponent(e.Fighter);
                Vector3 contact = Contact(a, d);
                switch (e.Kind)
                {
                    case ArenaEventKind.AttackStarted:
                        Play(sWhoosh, e.Heavy ? 0.5f : 0.3f, e.Heavy ? 0.75f : 1.2f);
                        break;

                    case ArenaEventKind.Hit:
                    {
                        BrawlerAttack atk = e.Heavy ? arena.Rules.hook : arena.Rules.jab;
                        bool counter = e.Damage > atk.damage + 0.01f;
                        Burst(contact, new Color(1f, 0.6f, 0.2f), e.Heavy ? 40 : 18, e.Heavy ? 7f : 4.5f, e.Heavy ? 0.16f : 0.11f);
                        Burst(contact, Color.white, e.Heavy ? 14 : 6, 3f, 0.09f);
                        if (counter) Burst(contact, new Color(1f, 0.85f, 0.2f), 24, 6f, 0.13f);
                        ViewOf(d.Index)?.Flash(Color.white);
                        Freeze(hitStop * (e.Heavy ? 2.2f : 1f) * (counter ? 1.4f : 1f));
                        Shake(e.Heavy ? 0.65f : 0.3f);
                        Play(e.Heavy ? sHook : sJab, 1f, Random.Range(0.92f, 1.08f));
                        cheerAt = Time.unscaledTime;
                        break;
                    }

                    case ArenaEventKind.Blocked:
                        Burst(contact, new Color(0.4f, 0.75f, 1f), 14, 4f, 0.1f);
                        ViewOf(d.Index)?.Flash(new Color(0.5f, 0.8f, 1f));
                        Shake(0.15f);
                        Play(sBlock, 0.8f, Random.Range(0.95f, 1.1f));
                        break;

                    case ArenaEventKind.GuardBreak:
                        Burst(contact, new Color(0.75f, 0.35f, 1f), 46, 7.5f, 0.15f);
                        ViewOf(d.Index)?.Flash(new Color(0.85f, 0.6f, 1f));
                        Freeze(hitStop * 2f);
                        Shake(0.55f);
                        Play(sBreak, 1f, 1f);
                        cheerAt = Time.unscaledTime;
                        break;

                    case ArenaEventKind.Whiff:
                        Play(sWhoosh, 0.25f, e.Heavy ? 0.6f : 1f);
                        break;

                    case ArenaEventKind.DashStarted:
                        Burst(new Vector3(a.Position.x, 0.1f, a.Position.y), new Color(0.75f, 0.7f, 0.62f), 16, 2.5f, 0.2f);
                        Play(sRoll, 0.6f, Random.Range(0.9f, 1.1f));
                        break;

                    case ArenaEventKind.KO:
                        Burst(contact, Color.white, 60, 9f, 0.18f);
                        Burst(contact, new Color(1f, 0.5f, 0.15f), 40, 6f, 0.2f);
                        slowUntil = Time.unscaledTime + knockoutSlowMotion;
                        Shake(1f);
                        Play(sKO, 1f, 1f);
                        bigCheerAt = Time.unscaledTime;
                        break;
                }
            }
        }

        private void OnRoundIntro(int round) => Play(sBell, 0.5f, 1f);
        private void OnFight() => Play(sBell, 0.8f, 1.12f);

        private void OnRoundWon(int winner, bool knockout)
        {
            Play(sCrowd, 1f, 1f);
            Play(sFanfare, 0.7f, winner == BrawlerMatch.Human ? 1f : 0.94f);
            bigCheerAt = Time.unscaledTime;
            orbit = Mathf.Atan2(cameraPos.x, cameraPos.z) * Mathf.Rad2Deg;
        }

        private void OnMatchWon(int winner) => Play(sFanfare, 0.9f, 1f);

        // ---------------------------------------------------------------- per frame

        private void LateUpdate()
        {
            if (match == null || match.Arena == null) return;
            float now = Time.unscaledTime;
            float udt = Time.unscaledDeltaTime;
            Time.timeScale = now < hitStopUntil ? 0.02f : now < slowUntil ? knockoutTimeScale : 1f;

            bool celebrating = match.RoundWinner >= 0 &&
                               (match.CurrentPhase == BrawlerMatch.Phase.RoundOver || match.CurrentPhase == BrawlerMatch.Phase.MatchOver);
            UpdateCamera(celebrating, udt);
            UpdateLights(celebrating, udt);
            if (celebrating && now >= slowUntil) RainConfetti(udt);
            UpdateCrowd(celebrating, now);
        }

        private void UpdateCamera(bool celebrating, float udt)
        {
            if (view == null) return;
            BrawlerArena arena = match.Arena;
            Vector3 a = Ground(arena.Fighters[0]), b = Ground(arena.Fighters[1]);
            Vector3 targetPos;
            Quaternion targetRot;
            if (celebrating && Time.unscaledTime >= slowUntil - 0.4f)
            {
                // Round the winner, slowly, close enough to see the dance.
                Vector3 w = Ground(arena.Fighters[match.RoundWinner]);
                orbit += udt * 22f;
                Vector3 offset = Quaternion.Euler(0f, orbit, 0f) * new Vector3(0f, 0f, -4.2f);
                targetPos = w + offset + Vector3.up * 2.1f;
                targetRot = Quaternion.LookRotation(w + Vector3.up * 1.05f - targetPos);
            }
            else
            {
                // Follow the fight: drift toward the fighters and lean in as they close.
                Vector3 mid = (a + b) * 0.5f;
                float apart = Vector3.Distance(a, b);
                float zoom = Mathf.InverseLerp(6f, 1.2f, apart) * 1.4f;
                if (Time.unscaledTime < slowUntil) zoom += 1.6f;
                Vector3 toCam = (cameraHome - new Vector3(0f, 0.7f, 0.6f)).normalized;
                targetPos = cameraHome + new Vector3(mid.x, 0f, mid.z) * 0.45f - toCam * zoom;
                targetRot = Quaternion.Slerp(cameraHomeRotation,
                    Quaternion.LookRotation(mid + Vector3.up * 0.9f - targetPos), 0.35f);
            }
            float ease = 1f - Mathf.Exp(-(celebrating ? 2.2f : 4f) * udt);
            cameraPos = Vector3.Lerp(cameraPos, targetPos, ease);
            cameraRot = Quaternion.Slerp(cameraRot, targetRot, ease);

            trauma = Mathf.Max(0f, trauma - udt * 1.7f);
            float s = trauma * trauma * shakeStrength;
            float t = Time.unscaledTime * 28f;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.9f, t) - 0.5f, 0f) * 2f * s;
            view.transform.position = cameraPos + cameraRot * jitter;
            view.transform.rotation = cameraRot * Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, t) - 0.5f) * 6f * s);
        }

        private void UpdateLights(bool celebrating, float udt)
        {
            float ease = 1f - Mathf.Exp(-3f * udt);
            if (keyLight != null) keyLight.intensity = Mathf.Lerp(keyLight.intensity, keyIntensity * (celebrating ? 0.35f : 1f), ease);
            if (spotlight == null) return;
            spotlight.intensity = Mathf.Lerp(spotlight.intensity, celebrating ? 60f : 0f, ease);
            if (celebrating)
            {
                Vector3 w = Ground(match.Arena.Fighters[match.RoundWinner]);
                spotlight.color = Color.Lerp(Color.white, ViewOf(match.RoundWinner) != null ? ViewOf(match.RoundWinner).color : Color.white, 0.25f);
                spotlight.transform.position = w + new Vector3(0f, 7f, -1.5f);
                spotlight.transform.rotation = Quaternion.LookRotation(w - spotlight.transform.position);
            }
        }

        private void RainConfetti(float udt)
        {
            if (confetti == null) return;
            BrawlerFighterView winner = ViewOf(match.RoundWinner);
            Color team = winner != null ? winner.color : Color.white;
            Vector3 w = Ground(match.Arena.Fighters[match.RoundWinner]);
            confettiCarry += udt * (match.CurrentPhase == BrawlerMatch.Phase.MatchOver ? 90f : 60f);
            ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
            while (confettiCarry >= 1f)
            {
                confettiCarry -= 1f;
                float pick = Random.value;
                p.startColor = pick < 0.55f ? team : pick < 0.8f ? new Color(1f, 0.85f, 0.25f) : Color.white;
                p.position = w + new Vector3(Random.Range(-3f, 3f), Random.Range(4.5f, 6f), Random.Range(-3f, 3f));
                p.velocity = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-1.8f, -0.8f), Random.Range(-0.6f, 0.6f));
                p.startSize = Random.Range(0.06f, 0.12f);
                p.startLifetime = Random.Range(3f, 4.5f);
                p.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                p.angularVelocity3D = new Vector3(Random.Range(-400f, 400f), Random.Range(-400f, 400f), 0f);
                confetti.Emit(p, 1);
            }
        }

        private void UpdateCrowd(bool celebrating, float now)
        {
            if (fans == null) return;
            for (int i = 0; i < fans.Length; i++)
            {
                float bob = Mathf.Sin(now * 2.3f + i * 1.9f) * 0.04f;
                float small = Mathf.Max(0f, 1f - (now - cheerAt) * 2.5f) * Mathf.Abs(Mathf.Sin((now - cheerAt) * 18f + i)) * 0.15f;
                float since = now - bigCheerAt;
                float big = celebrating || since < 2f
                    ? Mathf.Abs(Mathf.Sin(now * 7f + i * 0.7f)) * 0.35f * (celebrating ? 1f : Mathf.Clamp01(2f - since))
                    : 0f;
                fans[i].localPosition = fanHome[i] + Vector3.up * (bob + small + big);
            }
        }

        // ---------------------------------------------------------------- helpers

        private BrawlerFighterView ViewOf(int fighter) => fighter == BrawlerMatch.Human ? match.humanView : match.machineView;

        private static Vector3 Ground(BrawlerFighter f) => new Vector3(f.Position.x, 0f, f.Position.y);

        /// <summary>Where a punch from <paramref name="a"/> meets <paramref name="d"/>: on the defender, at chest height, on the attacker's side.</summary>
        private static Vector3 Contact(BrawlerFighter a, BrawlerFighter d)
        {
            Vector2 dir = d.Position - a.Position;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : a.Facing;
            Vector2 at = d.Position - dir * 0.25f;
            return new Vector3(at.x, ChestHeight, at.y);
        }

        private void Freeze(float seconds) => hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);

        private void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        private void Burst(Vector3 at, Color color, int count, float speed, float size)
        {
            if (sparks == null) return;
            ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                p.position = at;
                p.velocity = Random.onUnitSphere * speed * Random.Range(0.3f, 1f) + Vector3.up * 1.2f;
                p.startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.35f));
                p.startSize = size * Random.Range(0.6f, 1.4f);
                p.startLifetime = Random.Range(0.25f, 0.6f);
                sparks.Emit(p, 1);
            }
        }

        private void Play(AudioClip clip, float vol, float pitch)
        {
            if (clip == null || audioSource == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, vol * volume);
        }
    }
}
