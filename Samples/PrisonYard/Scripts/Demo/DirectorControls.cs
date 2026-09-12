using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// You are not a character in this sample; you are the director. These keys provoke the prison
    /// and then you watch eight decision makers deal with the consequences without any of them
    /// being told what to do.
    ///
    /// Reads whichever input backend the project is set to: Unity throws if you touch the legacy
    /// <c>Input</c> class while Active Input Handling is the Input System package, so this compiles
    /// against both rather than assuming one.
    /// </summary>
    [AddComponentMenu("PrisonYard/Director Controls")]
    public class DirectorControls : MonoBehaviour
    {
        [Tooltip("How fast right-dragging swings the camera around the prison, in degrees per pixel.")]
        public float orbitSpeed = 0.2f;

        [Tooltip("How fast the scroll wheel zooms, in metres per notch.")]
        public float zoomSpeed = 4f;

        [Tooltip("Closest and furthest the camera can get from the middle of the prison, in metres.")]
        public Vector2 zoomRange = new Vector2(28f, 95f);

        [Tooltip("Damage key 3 does to the nearest guard. Two presses take a fresh guard below " +
                 "half health, where its action menu changes.")]
        public int injureAmount = 35;

        private Camera cam;
        private PrisonClock clock;
        private PrisonStatusBoard board;
        private PrisonRadio radio;

        private float yaw = 0f;
        private float pitch = 58f;
        private float distance = 67f;

        private void Awake()
        {
            cam = Camera.main;
            clock = FindFirstObjectByType<PrisonClock>();
            board = FindFirstObjectByType<PrisonStatusBoard>();
            radio = FindFirstObjectByType<PrisonRadio>();
        }

        private void Update()
        {
            HandleCamera();
            HandleProvocations();
        }

        // ---------------------------------------------------------------- camera

        private void HandleCamera()
        {
            if (cam == null) return;

            if (RightMouseHeld())
            {
                Vector2 delta = MouseDelta();
                yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, 15f, 85f);
            }

            distance = Mathf.Clamp(distance - ScrollDelta() * zoomSpeed, zoomRange.x, zoomRange.y);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = rotation * new Vector3(0f, 0f, -distance);
            cam.transform.LookAt(Vector3.zero);
        }

        // ---------------------------------------------------------------- provocations

        private void HandleProvocations()
        {
            if (KeyPressed(1)) StartFight();
            if (KeyPressed(2)) PlantContraband();
            if (KeyPressed(3)) InjureNearestGuard();
            if (KeyPressed(4)) CutTheLights();
            if (KeyPressed(5)) OpenAllGates();
        }

        /// <summary>Key 1: makes the hothead swing at whoever is nearest.</summary>
        private void StartFight()
        {
            PrisonerState hothead = FindByTemperament(PrisonerTemperament.Hothead);
            if (hothead == null) return;

            PrisonerState victim = NearestOtherPrisoner(hothead);
            if (victim == null) return;

            PrisonerExecutor executor = hothead.GetComponent<PrisonerExecutor>();
            if (executor != null) executor.OnFight(victim.name);
        }

        /// <summary>Key 2: gives the escapee something to hide, which puts Hide on its menu.</summary>
        private void PlantContraband()
        {
            PrisonerState escapee = FindByTemperament(PrisonerTemperament.Escapee);
            if (escapee == null) return;

            escapee.hasContraband = true;
            if (radio != null) radio.Post("Control", $"tip-off: {escapee.name} may be carrying something", null);
        }

        /// <summary>Key 3: hurts the guard nearest the middle of the prison.</summary>
        private void InjureNearestGuard()
        {
            GuardState[] guards = FindObjectsByType<GuardState>(FindObjectsSortMode.None);
            GuardState best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < guards.Length; i++)
            {
                float d = guards[i].transform.position.sqrMagnitude;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = guards[i];
            }
            if (best == null) return;

            best.TakeDamage(injureAmount);
            if (radio != null) radio.Post("Control", $"{best.name} is hurt", null);
        }

        /// <summary>Key 4: skips straight to lights out, when being out of place is noticed.</summary>
        private void CutTheLights()
        {
            if (clock != null) clock.SkipTo(PrisonPhase.LightsOut);
        }

        /// <summary>Key 5: reopens every gate, to undo a lockdown by hand.</summary>
        private void OpenAllGates()
        {
            if (board == null) return;
            for (int i = 0; i < PrisonSections.All.Length; i++) board.SetLocked(PrisonSections.All[i], false);
            if (radio != null) radio.Post("Control", "all gates released", null);
        }

        // ---------------------------------------------------------------- helpers

        private static PrisonerState FindByTemperament(PrisonerTemperament temperament)
        {
            PrisonerState[] all = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].temperament == temperament) return all[i];
            }
            return null;
        }

        private static PrisonerState NearestOtherPrisoner(PrisonerState from)
        {
            PrisonerState[] all = FindObjectsByType<PrisonerState>(FindObjectsSortMode.None);
            PrisonerState best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == from) continue;
                float d = Vector3.Distance(from.transform.position, all[i].transform.position);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = all[i];
            }
            return best;
        }

        // ---------------------------------------------------------------- input backends

        private static bool KeyPressed(int number)
        {
            #if ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            switch (number)
            {
                case 1: return k.digit1Key.wasPressedThisFrame;
                case 2: return k.digit2Key.wasPressedThisFrame;
                case 3: return k.digit3Key.wasPressedThisFrame;
                case 4: return k.digit4Key.wasPressedThisFrame;
                case 5: return k.digit5Key.wasPressedThisFrame;
                default: return false;
            }
            #else
            return Input.GetKeyDown(KeyCode.Alpha0 + number);
            #endif
        }

        private static bool RightMouseHeld()
        {
            #if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
            #else
            return Input.GetMouseButton(1);
            #endif
        }

        private static Vector2 MouseDelta()
        {
            #if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            #else
            return new Vector2(Input.GetAxis("Mouse X") * 10f, Input.GetAxis("Mouse Y") * 10f);
            #endif
        }

        private static float ScrollDelta()
        {
            #if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
            #else
            return Input.GetAxis("Mouse ScrollWheel") * 10f;
            #endif
        }
    }
}
