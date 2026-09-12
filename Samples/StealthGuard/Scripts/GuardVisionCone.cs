using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Perception;
using UnityEngine;

namespace Project.Samples.StealthGuard
{
    /// <summary>
    /// Draws what the guard can actually see, on the ground, in front of it.
    ///
    /// Not a cone: a cone is a lie the moment a wall gets in the way. This casts the same rays the
    /// vision module casts, against the same occluder layers, and builds a fan whose edge stops
    /// where each ray stopped. The shape you see wrapping around a wall and cutting off behind it
    /// *is* the guard's line of sight, so "hide behind a wall and it loses you" stops being
    /// something the README claims and becomes something you can watch.
    ///
    /// It reads the guard's <see cref="PerceptionConfig"/> rather than carrying its own range and
    /// angle, so the drawing cannot drift from the sensing. One-way like the rest of the sample's
    /// visuals: nothing here is read by the decision loop, and deleting the component changes only
    /// what you can see.
    /// </summary>
    [AddComponentMenu("StealthGuard/Guard Vision Cone")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GuardVisionCone : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The vision module whose range, angle and occluder layers this draws. Found on the " +
                 "parent when left empty; without one the component does nothing.")]
        public ModularVisionModule vision;

        [Tooltip("Reads what the guard is doing, to colour the fan. Found on the parent when left empty.")]
        public GuardExecutor executor;

        [Header("Shape")]
        [Tooltip("How many rays the fan is built from. More is smoother and slightly dearer; 64 is " +
                 "enough that a wall edge does not visibly stair-step.")]
        [Range(8, 256)] public int rayCount = 64;

        [Tooltip("Height the rays are cast from, in metres. Should match roughly where the guard's " +
                 "eyes are, because that is the height at which a wall does or does not block it.")]
        public float eyeHeight = 1.6f;

        [Tooltip("How far above the floor the fan is drawn, in metres. Just enough to sit on top of " +
                 "the ground without flickering against it.")]
        public float groundHeight = 0.06f;

        [Tooltip("How far short of a wall the fan stops, in metres. Without a small gap the mesh " +
                 "pokes through the wall it is supposed to be stopped by.")]
        public float wallInset = 0.05f;

        [Tooltip("Seconds between rebuilds. The fan only has to keep up with the eye, not with the " +
                 "renderer, so a few rebuilds a second is plenty and costs almost nothing.")]
        public float refreshInterval = 0.05f;

        [Header("Colour")]
        [Tooltip("While the guard is patrolling or holding position: it has not noticed anything.")]
        public Color calmColour = new Color(1f, 0.92f, 0.45f, 0.13f);

        [Tooltip("While the guard is investigating: it is looking for something it has been told about.")]
        public Color alertColour = new Color(1f, 0.66f, 0.2f, 0.20f);

        [Tooltip("While the guard is chasing: it can see you right now.")]
        public Color chaseColour = new Color(1f, 0.25f, 0.22f, 0.26f);

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private float nextRebuild;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            if (vision == null) vision = GetComponentInParent<ModularVisionModule>();
            if (executor == null) executor = GetComponentInParent<GuardExecutor>();

            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // A mesh per guard, owned by this component: three guards drawing three different
            // shapes cannot share one.
            mesh = new Mesh { name = "VisionFan" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            block = new MaterialPropertyBlock();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            if (vision == null || meshRenderer == null) return;
            if (Time.time < nextRebuild) return;
            nextRebuild = Time.time + Mathf.Max(0f, refreshInterval);

            Rebuild();
            Paint();
        }

        /// <summary>
        /// Casts the fan and writes it into the mesh. Vertices are in this object's local space,
        /// and this object shares the guard's rotation, so the fan turns with the guard for free.
        /// </summary>
        private void Rebuild()
        {
            PerceptionConfig cfg = vision.Config;
            if (cfg == null) return;

            // A sphere sees in every direction; a cone sees its field of view. Global vision has no
            // shape worth drawing, so the fan is simply hidden.
            bool global = cfg.mode == VisionMode.Global;
            meshRenderer.enabled = !global;
            if (global) return;

            float sweep = cfg.mode == VisionMode.Cone ? Mathf.Clamp(cfg.fovAngle, 1f, 360f) : 360f;
            float range = Mathf.Max(0.1f, cfg.range);
            int rays = Mathf.Max(8, rayCount);

            Vector3 eye = transform.parent != null
                ? transform.parent.position + Vector3.up * eyeHeight
                : transform.position + Vector3.up * eyeHeight;

            var vertices = new Vector3[rays + 2];
            var triangles = new int[rays * 3];
            vertices[0] = Vector3.zero;

            float step = sweep / rays;
            for (int i = 0; i <= rays; i++)
            {
                float angle = -sweep * 0.5f + step * i;
                Quaternion turn = Quaternion.Euler(0f, angle, 0f);
                Vector3 direction = transform.rotation * turn * Vector3.forward;

                float distance = range;
                // occluderLayers of Nothing means the module does no line-of-sight test at all, so
                // neither does the drawing: the fan is then a plain wedge, which is the truth.
                if (cfg.occluderLayers.value != 0 &&
                    Physics.Raycast(eye, direction, out RaycastHit hit, range, cfg.occluderLayers))
                {
                    distance = Mathf.Max(0f, hit.distance - wallInset);
                }

                vertices[i + 1] = turn * Vector3.forward * distance;
            }

            for (int i = 0; i < rays; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Colours the fan by what the guard is doing. Through a property block rather than by
        /// touching the material, so all three guards keep sharing one material asset.
        /// </summary>
        private void Paint()
        {
            string activity = executor != null ? executor.currentActivity : "";
            Color colour = calmColour;
            if (!string.IsNullOrEmpty(activity))
            {
                if (activity.StartsWith("Chasing")) colour = chaseColour;
                else if (activity.StartsWith("Investigating")) colour = alertColour;
            }

            meshRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetColor(ColorId, colour);
            meshRenderer.SetPropertyBlock(block);
        }
    }
}
