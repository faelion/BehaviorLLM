using UnityEditor;
using UnityEngine;

namespace Project.Samples.StealthGuard.Editor
{
    /// <summary>
    /// Places pieces of the Kenney survival kit into the scene.
    ///
    /// Everything this puts down is decoration. Nothing here carries a collider, so nothing here
    /// changes where the guard can walk or what it can see: the sample's NavMesh is built from
    /// physics colliders, and the only colliders in the compound are the four sight-blocking walls
    /// and the ground. That separation is deliberate. Dressing a scene should not be able to break
    /// the behaviour it is dressing, and if the art folder is missing the sample still builds.
    /// </summary>
    public static class StealthGuardProps
    {
        private static string Art => StealthGuardPaths.PropArt;
        private static string SharedMaterialPath => Art + "/SurvivalColormap.mat";

        /// <summary>
        /// The pack is modelled small: a wall panel is half a unit wide and half a unit tall. Three
        /// puts a crate at about knee height next to the 1.75 m characters and a fence at chest
        /// height, which is the proportion the art was drawn for.
        /// </summary>
        public const float Scale = 3f;

        private static Material shared;

        /// <summary>True when the art is present, so the builder can skip the dressing pass.</summary>
        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/barrel.obj") != null;

        /// <summary>Forgets the cached material, so a rebuild picks up a reimported texture.</summary>
        public static void Reset() { shared = null; }

        /// <summary>
        /// One material for all eighty models. Every OBJ in the pack imports its own copy of the
        /// same colormap material, which works but gives the scene eighty materials that cannot
        /// batch. Pointing every instance at one of them costs nothing and fixes that.
        /// </summary>
        public static Material SharedMaterial()
        {
            if (shared != null) return shared;

            shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (shared != null) return shared;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/colormap.png");
            if (texture == null) return null;

            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") : Shader.Find("Standard");
            shared = new Material(shader);
            if (shared.HasProperty("_BaseMap")) shared.SetTexture("_BaseMap", texture);
            if (shared.HasProperty("_MainTex")) shared.SetTexture("_MainTex", texture);
            if (shared.HasProperty("_Smoothness")) shared.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(shared, SharedMaterialPath);
            return shared;
        }

        /// <summary>
        /// Drops one model into the scene. <paramref name="modelName"/> is a file name from the
        /// pack without its extension, for example "barrel" or "structure-metal-wall"; returns null
        /// when the pack is not in the project.
        /// </summary>
        public static GameObject Place(Transform parent, string modelName, Vector3 position, float yaw, float scale = Scale)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{modelName}.obj");
            if (source == null) return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = modelName;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * scale;

            Material material = SharedMaterial();
            if (material != null)
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = material;
            }
            return instance;
        }

        /// <summary>
        /// Lays a run of one model end to end between two points, the way a fence or a wall face is
        /// built. The pieces are spaced to divide the run exactly, so no seam falls short or
        /// overlaps whatever the distance happens to be.
        /// </summary>
        public static void Run(Transform parent, string modelName, Vector3 from, Vector3 to, float pieceWidth, float scale = Scale)
        {
            Vector3 span = to - from;
            float length = span.magnitude;
            if (length < 0.01f || pieceWidth <= 0.01f) return;

            int count = Mathf.Max(1, Mathf.RoundToInt(length / pieceWidth));
            float step = length / count;
            Vector3 direction = span / length;
            float yaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;

            for (int i = 0; i < count; i++)
            {
                Vector3 centre = from + direction * (step * (i + 0.5f));
                GameObject piece = Place(parent, modelName, centre, yaw + 90f, scale);
                // The run is measured in whole pieces, so any rounding is absorbed by stretching
                // each piece along its own length rather than leaving a gap at the end.
                if (piece != null && !Mathf.Approximately(step, pieceWidth))
                {
                    Vector3 s = piece.transform.localScale;
                    piece.transform.localScale = new Vector3(s.x * (step / pieceWidth), s.y, s.z);
                }
            }
        }
    }
}
