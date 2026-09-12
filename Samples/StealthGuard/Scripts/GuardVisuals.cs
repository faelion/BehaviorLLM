using UnityEngine;
using UnityEngine.AI;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Makes the guard's decision visible: the model walks when it walks, and a plate above its
    /// head says what it is doing and how hurt it is.
    ///
    /// One-way, like the executor: nothing here is read back by the decision loop, so deleting it
    /// changes how the sample looks and not how it behaves. The sample deliberately does not share
    /// this with PrisonYard, because a sample that needs another sample to compile is not
    /// self-contained.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Visuals")]
    public class GuardVisuals : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The animator on the character model. Found in the children when left empty.")]
        public Animator animator;

        [Tooltip("The label above this character's head. Leave empty for no label.")]
        public TextMesh label;

        [Tooltip("The coloured part of the health bar, scaled horizontally by health.")]
        public Transform healthFill;

        [Tooltip("The whole health bar, hidden while at full health.")]
        public GameObject healthBar;

        [Tooltip("Reads the guard's current activity. Auto-found on this object when left empty.")]
        public GuardExecutor executor;

        [Tooltip("Reads the guard's health. Auto-found on this object when left empty.")]
        public GuardHealth health;

        [Header("Tuning")]
        [Tooltip("Speed in metres per second that counts as a full run in the animation blend.")]
        public float runSpeed = 3.5f;

        [Tooltip("How quickly the animated speed catches up with the real speed.")]
        public float speedSmoothing = 8f;

        [Tooltip("Height above the feet for the label and bar, in metres.")]
        public float overheadHeight = 2.1f;

        private NavMeshAgent nav;
        private CharacterController controller;
        private Camera cam;
        private float shownSpeed;
        private float fillBaseWidth = 1f;
        private Vector3 lastPosition;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private void Awake()
        {
            nav = GetComponent<NavMeshAgent>();
            controller = GetComponent<CharacterController>();
            lastPosition = transform.position;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (executor == null) executor = GetComponent<GuardExecutor>();
            if (health == null) health = GetComponent<GuardHealth>();
            if (healthFill != null) fillBaseWidth = healthFill.localScale.x;
            cam = Camera.main;
        }

        private void Update()
        {
            if (animator == null) return;

            float actual = CurrentSpeed();
            shownSpeed = Mathf.Lerp(shownSpeed, actual, Time.deltaTime * Mathf.Max(1f, speedSmoothing));
            animator.SetFloat(SpeedParam, Mathf.Clamp(shownSpeed, 0f, Mathf.Max(0.1f, runSpeed)));
        }

        /// <summary>
        /// The plate is placed after everything has moved, not during. Placed in Update it is put
        /// where the character was at the start of the frame, and then the character's own Update
        /// moves the parent underneath it: the label ends up a frame behind and visibly shivers
        /// whenever the character is moving.
        /// </summary>
        private void LateUpdate()
        {
            DrawOverhead();
        }

        /// <summary>
        /// How fast this character is actually moving, whatever is moving it. The guard is steered
        /// by a NavMeshAgent and the player-controlled intruder by a CharacterController, and a
        /// character that is only being teleported around is measured from its own movement.
        /// </summary>
        private float CurrentSpeed()
        {
            if (nav != null && nav.enabled && nav.isOnNavMesh) return nav.velocity.magnitude;
            if (controller != null && controller.enabled) return new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            return Time.deltaTime > 0f ? new Vector3(delta.x, 0f, delta.z).magnitude / Time.deltaTime : 0f;
        }

        private void DrawOverhead()
        {
            if (cam == null) cam = Camera.main;

            float fraction = health != null ? health.Fraction : 1f;

            if (healthBar != null)
            {
                healthBar.SetActive(fraction < 0.999f);
                if (healthFill != null)
                {
                    float f = Mathf.Clamp01(fraction);
                    Vector3 s = healthFill.localScale;
                    healthFill.localScale = new Vector3(fillBaseWidth * f, s.y, s.z);
                    healthFill.localPosition = new Vector3(-fillBaseWidth * (1f - f) * 0.5f, 0f, -0.01f);
                }
            }

            if (label != null)
            {
                string activity = executor != null ? executor.currentActivity : "";
                int space = activity != null ? activity.IndexOf(' ') : -1;
                string verb = space > 0 ? activity.Substring(0, space) : activity;
                label.text = string.IsNullOrEmpty(verb) ? name : $"{name}\n{verb}";
            }

            if (cam == null) return;
            Vector3 head = transform.position + Vector3.up * overheadHeight;
            if (label != null)
            {
                label.transform.position = head + Vector3.up * 0.28f;
                label.transform.rotation = cam.transform.rotation;
            }
            if (healthBar != null)
            {
                healthBar.transform.position = head;
                healthBar.transform.rotation = cam.transform.rotation;
            }
        }
    }
}
