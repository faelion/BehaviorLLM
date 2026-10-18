using UnityEngine;

namespace Project.Samples.ArenaBrawler
{
    /// <summary>
    /// Draws one fighter from the state the arena holds: where it is, which way it faces, which
    /// animation plays and at what speed so the clip lasts exactly as long as the move does, and a
    /// ring on the floor that telegraphs what it is about to do. The ring is there for the human:
    /// the machine reads the same information as text.
    ///
    /// Without the character art it animates a primitive stand-in instead: a fist that shoots out
    /// on a punch and a guard that comes up on a block. The game plays the same either way.
    /// </summary>
    [AddComponentMenu("Arena Brawler/Fighter View")]
    public class BrawlerFighterView : MonoBehaviour
    {
        [Tooltip("The character's animator. Empty when the primitive stand-in is used.")]
        public Animator animator;

        [Tooltip("Ring on the floor that changes colour with the fighter's intent.")]
        public Renderer ring;

        [Tooltip("Renderers tinted with the fighter's colour.")]
        public Renderer[] tinted;

        [Tooltip("The fighter's colour.")]
        public Color color = Color.white;

        [Header("Primitive stand-in")]
        [Tooltip("The stand-in's fist, pushed forward on a punch.")]
        public Transform fist;

        [Tooltip("The stand-in's guard, shown while blocking.")]
        public GameObject guard;

        [Header("Clip lengths, filled in by the scene builder")]
        public float jabClip = 0.87f;
        public float hookClip = 1f;
        public float hookRecoveryClip = 0.7f;
        public float rollClip = 1.47f;

        // Animator state names, matching the controller the builder makes.
        public const string Idle = "Idle", Run = "Run", Walk = "Walk", Jab = "Jab", Hook = "Hook", HookRecovery = "HookRecovery",
            Block = "Block", Roll = "Roll", Hit = "Hit", HitHeavy = "HitHeavy", GuardBreak = "GuardBreak", KO = "KO", Win = "Win";

        private string current;
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (tinted == null) return;
            for (int i = 0; i < tinted.Length; i++)
            {
                if (tinted[i] == null) continue;
                tinted[i].GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                block.SetColor(ColorId, color);
                tinted[i].SetPropertyBlock(block);
            }
        }

        /// <summary>Shows <paramref name="f"/>. <paramref name="won"/> plays the victory animation.</summary>
        public void Show(BrawlerArena arena, BrawlerFighter f, bool won)
        {
            BrawlerRules r = arena.Rules;
            transform.position = new Vector3(f.Position.x, 0f, f.Position.y);

            Vector2 look = f.Facing;
            string state;
            float speed = 1f;
            switch (f.State)
            {
                case FighterState.Jab:
                    state = Jab;
                    speed = jabClip / r.jab.Total;
                    break;
                case FighterState.Hook:
                    bool recovering = arena.PhaseOf(f) == AttackPhase.Recovery;
                    state = recovering ? HookRecovery : Hook;
                    speed = recovering ? hookRecoveryClip / r.hook.recovery : hookClip / (r.hook.windup + r.hook.active);
                    break;
                case FighterState.Dash:
                    state = Roll;
                    speed = rollClip / (r.dashDuration + 0.25f);
                    look = f.DashDirection;
                    break;
                case FighterState.HitStun: state = f.StateDuration >= 0.5f ? HitHeavy : Hit; break;
                case FighterState.GuardBroken: state = GuardBreak; break;
                case FighterState.KO: state = KO; break;
                default:
                    if (won) { state = Win; break; }
                    if (f.Blocking) { state = Block; break; }
                    float moving = f.Velocity.magnitude;
                    if (moving < 0.3f) { state = Idle; break; }
                    Vector2 dir = f.Velocity / moving;
                    float forward = Vector2.Dot(dir, f.Facing);
                    if (forward < -0.5f)
                    {
                        // Backing off: walk backwards while still facing the opponent.
                        state = Walk;
                        speed = -moving / 1.4f;
                    }
                    else
                    {
                        // Turn the body toward the run, but never fully away from the opponent.
                        state = Run;
                        speed = moving / r.moveSpeed;
                        look = Vector3.Slerp(new Vector3(f.Facing.x, f.Facing.y), new Vector3(dir.x, dir.y), 0.7f);
                    }
                    break;
            }

            if (look.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(new Vector3(look.x, 0f, look.y));
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-25f * Time.deltaTime));
            }

            if (animator != null)
            {
                if (state != current)
                {
                    animator.CrossFadeInFixedTime(state, state == Jab || state == Hook ? 0.04f : 0.1f, 0, 0f);
                    current = state;
                }
                animator.SetFloat("Speed", speed);
            }
            else ShowStandIn(arena, f);

            ShowRing(arena, f);
        }

        private void ShowStandIn(BrawlerArena arena, BrawlerFighter f)
        {
            if (fist != null)
            {
                float reach = 0.35f;
                AttackPhase phase = arena.PhaseOf(f);
                if (phase == AttackPhase.Active)
                    reach = (f.State == FighterState.Hook ? arena.Rules.hook.range : arena.Rules.jab.range) - 0.4f;
                else if (phase == AttackPhase.Windup) reach = 0.15f;
                fist.localPosition = new Vector3(f.State == FighterState.Hook ? 0.25f : 0.15f, 1.2f, reach);
            }
            if (guard != null) guard.SetActive(f.Blocking);
            transform.localScale = f.State == FighterState.KO ? new Vector3(1f, 0.25f, 1f) : Vector3.one;
        }

        private void ShowRing(BrawlerArena arena, BrawlerFighter f)
        {
            if (ring == null) return;
            Color c;
            bool on = true;
            AttackPhase phase = arena.PhaseOf(f);
            if (phase == AttackPhase.Windup) c = f.State == FighterState.Hook ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.85f, 0.2f);
            else if (phase == AttackPhase.Active) c = Color.white;
            else if (f.Blocking) c = new Color(0.35f, 0.75f, 1f);
            else if (f.State == FighterState.GuardBroken || f.State == FighterState.HitStun) c = new Color(0.7f, 0.3f, 1f);
            else if (f.State == FighterState.Dash) c = new Color(0.8f, 0.8f, 0.8f);
            else { c = Color.black; on = false; }

            ring.enabled = on;
            if (!on) return;
            ring.GetPropertyBlock(block);
            block.SetColor(BaseColor, c);
            block.SetColor(ColorId, c);
            ring.SetPropertyBlock(block);
        }
    }
}
