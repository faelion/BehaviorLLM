using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// The player. Walk into the guard's cone to be seen, and attack to push its health below
    /// half so the action menu changes while you watch.
    ///
    /// Reads whichever input backend the project is set to: Unity throws if you touch the legacy
    /// <c>Input</c> class while Active Input Handling is set to the Input System package, so the
    /// sample compiles against both rather than assuming one.
    ///
    /// The on-screen panel lives on <see cref="StealthObjective"/>, so there is one of them rather
    /// than one per component that has something to say.
    /// </summary>
    [AddComponentMenu("StealthGuard/Intruder Controller")]
    public class IntruderController : MonoBehaviour
    {
        [Tooltip("Movement speed in metres per second. WASD or the arrow keys.")]
        public float moveSpeed = 5f;

        [Tooltip("Guard this intruder attacks. Left empty - which is how the scene ships - it hits " +
                 "whichever guard is nearest and in range, so a compound with three guards does not " +
                 "need three different keys.")]
        public GuardHealth targetGuard;

        [Tooltip("How close you must be to land an attack, in metres.")]
        public float attackRange = 2.5f;

        [Tooltip("Damage per attack. With the default 100 health, four hits cross the halfway line " +
                 "where Chase leaves the guard's action menu and Retreat appears.")]
        public int attackDamage = 15;

        [Tooltip("Seconds between attacks.")]
        public float attackCooldown = 0.4f;

        private CharacterController controller;
        private float nextAttackTime;

        private GuardHealth[] guards;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            guards = FindObjectsByType<GuardHealth>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            Move(ReadMoveInput());
            if (ReadAttackHeld()) TryAttack();
        }

        // ---------------------------------------------------------------- input

        private Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private bool ReadAttackHeld()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && k.spaceKey.isPressed;
#else
            return Input.GetKey(KeyCode.Space);
#endif
        }

        // ---------------------------------------------------------------- movement

        private void Move(Vector2 input)
        {
            Vector3 dir = new Vector3(input.x, 0f, input.y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            Vector3 motion = dir * moveSpeed;
            motion.y = -9.81f; // keep the controller grounded on the plane

            if (controller != null) controller.Move(motion * Time.deltaTime);
            else transform.position += new Vector3(motion.x, 0f, motion.z) * Time.deltaTime;

            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        }

        private void TryAttack()
        {
            if (Time.time < nextAttackTime) return;

            GuardHealth victim = targetGuard != null ? targetGuard : NearestGuard();
            if (victim == null) return;
            if (Vector3.Distance(transform.position, victim.transform.position) > attackRange) return;

            nextAttackTime = Time.time + attackCooldown;
            victim.TakeDamage(attackDamage);
        }

        /// <summary>The guard closest to the intruder, or null when the scene has none.</summary>
        private GuardHealth NearestGuard()
        {
            GuardHealth best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; guards != null && i < guards.Length; i++)
            {
                if (guards[i] == null) continue;
                float distance = Vector3.Distance(transform.position, guards[i].transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = guards[i];
            }
            return best;
        }

    }
}
