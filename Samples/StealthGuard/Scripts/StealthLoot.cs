using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// One crate of supplies the player is here to steal.
    ///
    /// It knows nothing about the objective beyond its own two states, taken and not taken, so the
    /// scene can hold any number of these and <see cref="StealthObjective"/> simply counts them.
    /// Getting caught puts a taken crate back where it started, which is why it remembers its home.
    /// </summary>
    [AddComponentMenu("StealthGuard/Stealth Loot")]
    public class StealthLoot : MonoBehaviour
    {
        [Tooltip("How close the intruder must get to pick this up, in metres. Walking over it is " +
                 "enough; there is no button to press.")]
        public float pickupRange = 1.8f;

        [Tooltip("The model and the marker above it. Hidden while the crate is being carried, and " +
                 "shown again if the guard catches the thief.")]
        public GameObject visuals;

        [Tooltip("The glow on the crate. Turned off with the visuals so a taken crate leaves no " +
                 "light hanging in mid air.")]
        public Light glow;

        [Tooltip("The caption over the crate. It hangs outside the part that spins, or it would be " +
                 "read backwards for half of every turn.")]
        public GameObject label;

        /// <summary>True once the intruder has walked over it and not yet been caught.</summary>
        public bool Taken { get; private set; }

        [Tooltip("Degrees per second the crate turns on the spot. A prop that moves reads as " +
                 "something to pick up rather than as scenery.")]
        public float spinSpeed = 45f;

        [Tooltip("How far the crate bobs up and down, in metres.")]
        public float bobHeight = 0.12f;

        private Vector3 home;
        private Vector3 visualsHome;

        private void Update()
        {
            if (Taken || visuals == null) return;
            visuals.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
            visuals.transform.localPosition = visualsHome + Vector3.up * (Mathf.Sin(Time.time * 2f) * bobHeight);
        }

        private void Awake()
        {
            home = transform.position;
            if (visuals == null && transform.childCount > 0) visuals = transform.GetChild(0).gameObject;
            if (glow == null) glow = GetComponentInChildren<Light>();
            if (visuals != null) visualsHome = visuals.transform.localPosition;
        }

        /// <summary>Marks it stolen and takes it off the ground. Ignored if it is already taken.</summary>
        public void Take()
        {
            if (Taken) return;
            Taken = true;
            Show(false);
        }

        /// <summary>Puts it back where it started, which is what being caught costs.</summary>
        public void ReturnHome()
        {
            if (!Taken) return;
            Taken = false;
            transform.position = home;
            Show(true);
        }

        private void Show(bool visible)
        {
            if (visuals != null) visuals.SetActive(visible);
            if (glow != null) glow.enabled = visible;
            if (label != null) label.SetActive(visible);
        }

        /// <summary>True when this point is close enough to pick the crate up.</summary>
        public bool InReach(Vector3 point)
        {
            if (Taken) return false;
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude <= pickupRange * pickupRange;
        }
    }
}
