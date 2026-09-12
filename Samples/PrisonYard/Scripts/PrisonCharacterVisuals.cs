using UnityEngine;
using UnityEngine.AI;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// Makes a decision visible. It reads the same state the prompt reads and turns it into
    /// animation, a health bar and a label, so what the model chose can be recognised across the
    /// room without opening the console.
    ///
    /// It is deliberately one-way: nothing here is read back by the decision loop, and removing
    /// this component changes how the prison looks but not how it behaves. That is the same
    /// separation the executors keep, for the same reason.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Character Visuals")]
    public class PrisonCharacterVisuals : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The animator on the character model. Found in the children when left empty.")]
        public Animator animator;

        [Tooltip("The label shown above this character's head. Left empty, no label is drawn.")]
        public TextMesh label;

        [Tooltip("The green part of the health bar. Its horizontal scale is driven by health.")]
        public Transform healthFill;

        [Tooltip("The whole health bar, hidden while the character is at full health so a healthy " +
                 "prison is not covered in bars.")]
        public GameObject healthBar;

        [Header("Tuning")]
        [Tooltip("Speed in metres per second that counts as a full run in the animation blend. " +
                 "Lower it if characters look like they are sprinting while walking.")]
        public float runSpeed = 4f;

        [Tooltip("How quickly the animated speed catches up with the real speed. Higher is snappier " +
                 "and jerkier.")]
        public float speedSmoothing = 8f;

        [Tooltip("Height above the character's feet for the label and health bar, in metres.")]
        public float overheadHeight = 2.3f;

        private NavMeshAgent nav;
        private GuardState guard;
        private PrisonerState prisoner;
        private Camera cam;

        private float shownSpeed;
        private string lastActivity = "";

        // The bar is built at whatever width looks right in the scene; health scales that width
        // rather than replacing it, so the bar does not jump to one unit wide on the first frame.
        private float fillBaseWidth = 1f;

        // Animator parameter and state names, matching what PrisonYardAnimatorBuilder creates.
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int PunchParam = Animator.StringToHash("Punch");
        private static readonly int InteractParam = Animator.StringToHash("Interact");
        private static readonly int PickUpParam = Animator.StringToHash("PickUp");
        private static readonly int HitParam = Animator.StringToHash("Hit");
        private static readonly int SitParam = Animator.StringToHash("Sit");

        private void Awake()
        {
            nav = GetComponent<NavMeshAgent>();
            guard = GetComponent<GuardState>();
            prisoner = GetComponent<PrisonerState>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            cam = Camera.main;
            if (healthFill != null) fillBaseWidth = healthFill.localScale.x;
        }

        private void Update()
        {
            DriveLocomotion();
            ReactToActivityChange();
        }

        /// <summary>
        /// The plate is placed after everything has moved, not during. Placed in Update it is put
        /// where the character was at the start of the frame, and then the NavMeshAgent moves the
        /// parent underneath it: the label ends up a frame behind and visibly shivers whenever the
        /// character is walking.
        /// </summary>
        private void LateUpdate()
        {
            DrawOverhead();
        }

        /// <summary>Real movement speed drives the idle/walk/run blend, so nobody moon-walks.</summary>
        private void DriveLocomotion()
        {
            if (animator == null) return;

            float actual = nav != null && nav.enabled && nav.isOnNavMesh ? nav.velocity.magnitude : 0f;
            shownSpeed = Mathf.Lerp(shownSpeed, actual, Time.deltaTime * Mathf.Max(1f, speedSmoothing));
            animator.SetFloat(SpeedParam, Mathf.Clamp(shownSpeed, 0f, Mathf.Max(0.1f, runSpeed)));

            // Sitting is a held pose rather than a one-shot, so it is a bool.
            bool resting = Activity() == PrisonActivities.Recovering;
            animator.SetBool(SitParam, resting);
        }

        /// <summary>
        /// Fires a one-shot animation when the activity changes. The activity string is the same
        /// one the model sees in the prompt, so what you watch and what it reads cannot drift.
        /// </summary>
        private void ReactToActivityChange()
        {
            string now = Activity();
            if (now == lastActivity) return;
            lastActivity = now;

            if (animator == null) return;

            if (now == PrisonActivities.Fighting) animator.SetTrigger(PunchParam);
            else if (now == PrisonActivities.Hiding) animator.SetTrigger(PickUpParam);
            else if (now == PrisonActivities.Talking) animator.SetTrigger(InteractParam);
            else if (now == PrisonActivities.Complying) animator.SetTrigger(HitParam);
            else if (now.StartsWith("Searching")) animator.SetTrigger(InteractParam);
            else if (now.StartsWith("Reporting")) animator.SetTrigger(InteractParam);
        }

        private void DrawOverhead()
        {
            if (label == null && healthBar == null) return;
            if (cam == null) cam = Camera.main;

            float fraction = HealthFraction();

            if (healthBar != null)
            {
                healthBar.SetActive(fraction < 0.999f);
                if (healthFill != null)
                {
                    float f = Mathf.Clamp01(fraction);
                    Vector3 s = healthFill.localScale;
                    healthFill.localScale = new Vector3(fillBaseWidth * f, s.y, s.z);
                    // Drain from the right rather than shrinking towards the centre.
                    healthFill.localPosition = new Vector3(-fillBaseWidth * (1f - f) * 0.5f, 0f, -0.01f);
                }
            }

            if (label != null) label.text = ShortActivity();

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

        private string Activity()
        {
            if (guard != null) return guard.activity ?? "";
            if (prisoner != null) return prisoner.activity ?? "";
            return "";
        }

        /// <summary>The label is read at a glance, so it shows the verb rather than the sentence.</summary>
        private string ShortActivity()
        {
            string a = Activity();
            if (string.IsNullOrEmpty(a)) return name;
            int space = a.IndexOf(' ');
            string verb = space > 0 ? a.Substring(0, space) : a;
            return $"{name}\n{verb}";
        }

        private float HealthFraction()
        {
            if (guard != null && guard.maxHealth > 0) return (float)guard.currentHealth / guard.maxHealth;
            if (prisoner != null && prisoner.maxHealth > 0) return (float)prisoner.currentHealth / prisoner.maxHealth;
            return 1f;
        }
    }
}
