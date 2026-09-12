using UnityEngine;
using UnityEngine.AI;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// The door in a section's wall. Closing it is what a lockdown physically means: the cube
    /// appears, and its carving obstacle cuts the gap out of the NavMesh so anyone pathing through
    /// has to go around, or has nowhere to go.
    ///
    /// The NavMesh is baked once with every gate open; carving handles the rest at runtime, which
    /// is why the sample never rebakes anything while playing.
    /// </summary>
    [AddComponentMenu("PrisonYard/Prison Gate")]
    public class PrisonGate : MonoBehaviour
    {
        [Tooltip("The section this gate lets people in and out of. Locking that section down closes " +
                 "this gate.")]
        public PrisonSection section = PrisonSection.Yard;

        [Tooltip("Whether the gate starts open. Every gate starts open in this sample; the warden " +
                 "closes them.")]
        public bool startOpen = true;

        [Tooltip("Read-only while playing: whether the gate is currently open.")]
        [SerializeField] private bool isOpen = true;

        private Renderer barrier;
        private Collider barrierCollider;
        private NavMeshObstacle obstacle;

        /// <summary>True while people can walk through this gate.</summary>
        public bool IsOpen => isOpen;

        private void Awake()
        {
            barrier = GetComponentInChildren<Renderer>();
            barrierCollider = GetComponentInChildren<Collider>();
            obstacle = GetComponentInChildren<NavMeshObstacle>();
            SetOpen(startOpen);
        }

        /// <summary>Opens or closes the gate. Safe to call every frame; only acts on a change.</summary>
        public void SetOpen(bool open)
        {
            isOpen = open;

            // Close the collider before enabling the obstacle: carving takes a frame to appear in
            // the NavMesh, and the collider is what stops someone slipping through in the meantime.
            if (barrierCollider != null) barrierCollider.enabled = !open;
            if (barrier != null) barrier.enabled = !open;
            if (obstacle != null) obstacle.enabled = !open;
        }
    }
}
