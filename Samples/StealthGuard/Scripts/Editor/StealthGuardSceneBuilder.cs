using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Modules;
using BehaviorLLM.Core.Perception;
using BehaviorLLM.Core.Telemetry;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace Project.Samples.StealthGuard.Editor
{
    /// <summary>
    /// Builds the StealthGuard scene from primitives, so the sample is reproducible from source
    /// and carries no art dependencies. Run it from BehaviorLLM > Samples > Build StealthGuard Scene.
    /// </summary>
    public static class StealthGuardSceneBuilder
    {
        // Every path is derived from where this sample actually is; see StealthGuardPaths for why.
        private static string Root => StealthGuardPaths.OutputRoot;
        private static string ScenePath => StealthGuardPaths.Scenes + "/StealthGuard.unity";
        private static string ActionConfigPath => StealthGuardPaths.Data + "/StealthGuardActions.asset";
        private static string PerceptionConfigPath => StealthGuardPaths.Data + "/StealthGuardPerception.asset";
        private static string DecisionConfigPath => StealthGuardPaths.Data + "/StealthGuardDecisions.asset";
        private static string ServerConfigPath => StealthGuardPaths.Data + "/StealthGuardServer.asset";
        private static string VisionConeMaterialPath => StealthGuardPaths.Data + "/VisionCone.mat";

        /// <summary>Shipped model preset this sample wires up, looked up by name rather than by
        /// path so it survives the package being installed anywhere.</summary>
        private const string ModelConfigAsset = "Model_Granite-4.1-3B-Q4_K_M";
        private const string OccluderLayerName = "StealthOccluder";
        private static string CharacterArt => StealthGuardPaths.CharacterArt;

        /// <summary>
        /// How far the camera tilts down. World-space captions are turned by the same amount so
        /// they face it; the camera in this sample is fixed, so once at build time is enough.
        /// </summary>
        private const float CameraPitch = 42f;

        // Kenney's character is a big-headed cartoon and imports about 2.7 m to the top of the
        // head. Shrinking it lands the eyes near the 1.7 m the vision cone and the camera assume.
        private const float CharacterScale = 0.62f;

        [MenuItem("BehaviorLLM/Samples/Build StealthGuard Scene")]
        public static void BuildScene()
        {
            BehaviorLLM.Editor.SampleAssetPaths.PrepareArt(StealthGuardPaths.Root, "StealthGuard");
            int occluderLayer = EnsureLayer(OccluderLayerName);
            ActionConfig actions = CreateActionConfig();

            // Configure the character art before anything is instanced. Turning the model into a
            // Generic rig is what puts an Animator on it, and instancing it first would hand back
            // a model with no Animator to drive, which is exactly what left everyone in a T-pose.
            StealthGuardCharacterSetup.Reset();
            StealthGuardCharacterSetup.Prepare();

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "StealthGuard";

            CreateEssentials();
            NavMeshSurface surface = CreateEnvironment(occluderLayer);
            List<StealthGuardMarker> markers = CreateMarkers();

            GameObject intruder = CreateIntruder();
            List<GameObject> guards = CreateGuards(actions, intruder, occluderLayer);
            CreateObjective();
            CreateManager();

            // Bake after everything is placed, so the guard and intruder land on a valid NavMesh.
            surface.BuildNavMesh();
            for (int i = 0; i < guards.Count; i++) SnapToNavMesh(guards[i].transform);

            EnsureFolder(Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StealthGuard] Built {ScenePath} with {markers.Count} markers. " +
                      "Assign a downloaded model to the LLMManager's Model Config, press Play, and walk into the guard's cone.");
        }

        // ---------------------------------------------------------------- action config

        // The sample authors its own config assets rather than editing fields on the components,
        // which is how a project is meant to use the package: the guard's senses and its decision
        // cadence are reusable assets that another scene can pick up unchanged.
        private static PerceptionConfig CreatePerceptionConfig(int occluderLayer)
        {
            PerceptionConfig cfg = LoadOrCreate<PerceptionConfig>(PerceptionConfigPath);
            cfg.mode = VisionMode.Cone;
            cfg.range = 16f;
            cfg.fovAngle = 100f;
            cfg.perceptionLayers = ~0;
            cfg.occluderLayers = 1 << occluderLayer;
            cfg.triggerInterruptOnNewObject = true;
            cfg.scanInterval = 0.2f;
            cfg.memoryCapacity = 5;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static BehaviorLLMServerConfig CreateServerConfig()
        {
            BehaviorLLMServerConfig cfg = LoadOrCreate<BehaviorLLMServerConfig>(ServerConfigPath);
            cfg.autoStartOnAwake = true;
            // A slot per guard plus a spare, so no guard's cached prompt prefix is evicted by
            // another's request. Fewer slots than decision makers is the classic way to lose the
            // cache hit rate the whole prompt layout was designed around.
            cfg.parallelSlots = StealthGuardIds.GuardCount + 1;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static DecisionMakerConfig CreateDecisionConfig()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(DecisionConfigPath);
            cfg.decisionInterval = 2.0f;
            cfg.profile = DecisionProfile.Reactive;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            cfg.logDecisions = true;
            cfg.logPrompts = false;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static ActionConfig CreateActionConfig()
        {
            EnsureFolder(Root + "/Data");
            ActionConfig config = AssetDatabase.LoadAssetAtPath<ActionConfig>(ActionConfigPath);
            bool isNew = config == null;
            if (isNew) config = ScriptableObject.CreateInstance<ActionConfig>();

            config.validActions = new List<ActionDefinition>
            {
                Action(StealthGuardIds.HoldPosition, "Stand still and keep watching the area.", ActionParameterType.None, ""),
                Action(StealthGuardIds.Patrol, "Walk to a named patrol route point and keep watch there.", ActionParameterType.String, "Route_North"),
                Action(StealthGuardIds.Investigate, "Go to a named location to check on something suspicious.", ActionParameterType.String, "Warehouse"),
                Action(StealthGuardIds.Chase, "Pursue a visible intruder to catch them.", ActionParameterType.String, StealthGuardIds.IntruderName),
                Action(StealthGuardIds.Retreat, "Fall back to a named safe zone to recover.", ActionParameterType.String, "SafeZone_A")
            };

            // Deliberately free of conditional rules: the health condition is enforced by
            // GuardAvailability, because a written rule of that shape is not reliably followed.
            config.modelInstructions =
                "Choose exactly one action per turn.\n" +
                "If you can see an intruder, deal with it rather than continuing your routine.\n" +
                "If you cannot see anyone but something was reported, go and look.\n" +
                "Otherwise keep patrolling. Prefer patrolling over standing still.";

            if (isNew) AssetDatabase.CreateAsset(config, ActionConfigPath);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static ActionDefinition Action(string name, string description, ActionParameterType type, string example)
        {
            return new ActionDefinition
            {
                actionName = name,
                description = description,
                parameterType = type,
                exampleArgument = example
            };
        }

        // ---------------------------------------------------------------- scene pieces

        private static void CreateEssentials()
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            // Framed so the guard, the intruder and the markers are all legible in a screenshot:
            // these images end up in documentation.
            cam.transform.position = new Vector3(0f, 17f, -24f);
            cam.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
            Camera camera = cam.GetComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camera.clearFlags = CameraClearFlags.SolidColor;

            GameObject lightGo = new GameObject("Directional Light", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static NavMeshSurface CreateEnvironment(int occluderLayer)
        {
            GameObject root = new GameObject("_Environment");
            StealthGuardProps.Reset();

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40 x 40 m
            Paint(ground, new Color(0.30f, 0.33f, 0.28f));

            // Walls double as line-of-sight blockers, which is what makes the cone vision
            // interesting: the guard genuinely loses sight of the intruder behind them.
            CreateWall(root.transform, "Wall_Center", new Vector3(0f, 0f, 4f), 14f, 0f, occluderLayer);
            CreateWall(root.transform, "Wall_West", new Vector3(-9f, 0f, -4f), 12f, 90f, occluderLayer);
            CreateWall(root.transform, "Wall_East", new Vector3(9f, 0f, -3f), 10f, 90f, occluderLayer);
            CreateWall(root.transform, "Wall_South", new Vector3(2f, 0f, -11f), 12f, 0f, occluderLayer);

            Dress(root.transform);

            NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            // Colliders, not render meshes. The compound is dressed with dozens of crates, tents
            // and trees that carry no collider on purpose, and building from render meshes would
            // let every one of them punch a hole in the guard's walkable area.
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            return surface;
        }

        /// <summary>
        /// A wall: an invisible box that blocks sight and navigation, faced with metal panelling.
        ///
        /// The two are kept separate because they answer to different masters. The box is gameplay
        /// and has to stay exactly where the sample was tuned for it; the panelling is art and can
        /// be swapped for anything. <paramref name="yaw"/> turns the wall about its centre, so the
        /// length always runs along the wall's own local X.
        /// </summary>
        private static void CreateWall(Transform parent, string name, Vector3 groundCentre, float length, float yaw, int layer)
        {
            const float thickness = 0.4f;
            const float height = 3f;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent);
            wall.transform.position = groundCentre + Vector3.up * (height * 0.5f);
            wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            wall.transform.localScale = new Vector3(length, height, thickness);
            wall.layer = layer;
            Paint(wall, new Color(0.45f, 0.44f, 0.42f));

            if (!StealthGuardProps.Available) return;

            // The box itself is never drawn once there is panelling to draw instead. It stays in
            // the scene as the collider the vision raycast and the NavMesh bake both rely on.
            MeshRenderer boxRenderer = wall.GetComponent<MeshRenderer>();
            if (boxRenderer != null) boxRenderer.enabled = false;

            Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 end = groundCentre + along * (length * 0.5f);
            Vector3 start = groundCentre - along * (length * 0.5f);

            // The panelling hangs off its own unscaled object rather than off the box. The box is
            // stretched to the wall's dimensions, and anything parented to it inherits that stretch:
            // panels put under it came out as one wall-sized sheet.
            GameObject facing = new GameObject(name + "_Facing");
            facing.transform.SetParent(parent, false);

            // A panel is half a unit square before scaling, so a row of them is 1.5 m tall and two
            // rows reach the 3 m the sample's sight lines were built around.
            float panel = 0.5f * StealthGuardProps.Scale;
            StealthGuardProps.Run(facing.transform, "metal-panel", start, end, panel);
            StealthGuardProps.Run(facing.transform, "metal-panel-screws", start + Vector3.up * panel, end + Vector3.up * panel, panel);
        }

        /// <summary>
        /// Everything in the compound that is only there to be looked at: the perimeter fence, the
        /// stores the guard patrols past, the camp in the corner, and scrub outside the wire.
        ///
        /// Placement is worked out from a fixed seed, so two builds of this scene are identical and
        /// a screenshot taken today still matches the sample tomorrow.
        /// </summary>
        private static void Dress(Transform parent)
        {
            if (!StealthGuardProps.Available) return;

            GameObject dressing = new GameObject("Dressing");
            dressing.transform.SetParent(parent, false);
            Transform d = dressing.transform;

            float piece = 0.5f * StealthGuardProps.Scale;

            // Perimeter wire. Decoration only: it never blocks sight, so the guard's cone behaves
            // exactly as it did before there was a fence to look at.
            Vector3 nw = new Vector3(-18f, 0f, 18f), ne = new Vector3(18f, 0f, 18f);
            Vector3 se = new Vector3(18f, 0f, -18f), sw = new Vector3(-18f, 0f, -18f);
            StealthGuardProps.Run(d, "fence-fortified", nw, ne, piece);
            StealthGuardProps.Run(d, "fence-fortified", ne, se, piece);
            // The south run is laid in two halves, because the way out goes between them. The gap
            // is west of centre, clear of the Route_South patrol marker: two discs on top of each
            // other read as one thing, and these two mean quite different things.
            StealthGuardProps.Run(d, "fence-fortified", se, new Vector3(-6.5f, 0f, -18f), piece);
            StealthGuardProps.Run(d, "fence-fortified", new Vector3(-9.5f, 0f, -18f), sw, piece);
            StealthGuardProps.Run(d, "fence-fortified", sw, nw, piece);

            // The gate the intruder came in through and has to leave by.
            StealthGuardProps.Place(d, "fence-doorway", new Vector3(-8f, 0f, -18f), 0f);
            StealthGuardProps.Place(d, "signpost", new Vector3(-5.4f, 0f, -17.4f), 160f);

            // Stores, next to the Warehouse marker the guard can be told to investigate.
            StealthGuardProps.Place(d, "structure-metal", new Vector3(-15.5f, 0f, -10.5f), 15f);
            StealthGuardProps.Place(d, "box-large", new Vector3(-12.4f, 0f, -8.6f), 20f);
            StealthGuardProps.Place(d, "box-large", new Vector3(-12.4f, 0.75f, -8.6f), 65f);
            StealthGuardProps.Place(d, "box", new Vector3(-11.6f, 0f, -7.1f), 340f);
            StealthGuardProps.Place(d, "barrel", new Vector3(-15.6f, 0f, -6.4f), 0f);
            StealthGuardProps.Place(d, "barrel", new Vector3(-14.7f, 0f, -6.0f), 40f);
            StealthGuardProps.Place(d, "resource-planks", new Vector3(-13.2f, 0f, -5.2f), 105f);

            // The courtyard: a fire the guard walks past, and something to hide behind.
            StealthGuardProps.Place(d, "campfire-pit", new Vector3(1.9f, 0f, -6.4f), 0f);
            CreateFireLight(d, new Vector3(1.9f, 0.9f, -6.4f));
            StealthGuardProps.Place(d, "box", new Vector3(-2.1f, 0f, -6.7f), 25f);
            StealthGuardProps.Place(d, "barrel", new Vector3(-2.3f, 0f, -5.4f), 0f);
            StealthGuardProps.Place(d, "barrel", new Vector3(3.4f, 0f, -4.6f), 0f);
            StealthGuardProps.Place(d, "bucket", new Vector3(2.7f, 0f, -7.6f), 70f);

            // The camp in the north-west corner, where SafeZone_A sits.
            StealthGuardProps.Place(d, "tent", new Vector3(-16.4f, 0f, 12.2f), 200f);
            StealthGuardProps.Place(d, "tent-canvas", new Vector3(-13.4f, 0f, 15.6f), 145f);
            StealthGuardProps.Place(d, "campfire-stand", new Vector3(-15.1f, 0f, 15.6f), 0f);
            CreateFireLight(d, new Vector3(-15.1f, 0.9f, 15.6f));
            StealthGuardProps.Place(d, "bedroll", new Vector3(-17.2f, 0f, 15.4f), 80f);

            // The work corner by SafeZone_B.
            StealthGuardProps.Place(d, "workbench", new Vector3(16.6f, 0f, 13.4f), 190f);
            StealthGuardProps.Place(d, "workbench-anvil", new Vector3(14.2f, 0f, 16.4f), 250f);
            StealthGuardProps.Place(d, "chest", new Vector3(16.9f, 0f, 16.6f), 210f);
            StealthGuardProps.Place(d, "box", new Vector3(13.1f, 0f, 13.2f), 15f);

            // Scrub outside the wire, so the compound reads as a place rather than a floating slab.
            Scatter(d);
        }

        /// <summary>
        /// Trees, rocks and grass in the strip between the walls and the fence. Kept out of the
        /// middle of the compound, which is where the guard and the player need clear ground.
        /// </summary>
        private static void Scatter(Transform parent)
        {
            Random.State previous = Random.state;
            Random.InitState(20260908); // fixed, so the scene is the same every time it is built

            string[] trees = { "tree", "tree-tall", "tree-autumn" };
            string[] stones = { "rock-a", "rock-b", "rock-c", "rock-flat" };

            for (int i = 0; i < 26; i++)
            {
                StealthGuardProps.Place(parent, trees[Random.Range(0, trees.Length)], EdgeSpot(),
                                        Random.Range(0f, 360f), StealthGuardProps.Scale * Random.Range(0.85f, 1.25f));
            }
            for (int i = 0; i < 22; i++)
            {
                StealthGuardProps.Place(parent, stones[Random.Range(0, stones.Length)], EdgeSpot(),
                                        Random.Range(0f, 360f), StealthGuardProps.Scale * Random.Range(0.6f, 1.1f));
            }
            for (int i = 0; i < 30; i++)
            {
                StealthGuardProps.Place(parent, Random.value > 0.5f ? "patch-grass" : "grass", EdgeSpot(),
                                        Random.Range(0f, 360f), StealthGuardProps.Scale * Random.Range(0.7f, 1.3f));
            }

            Random.state = previous;
        }

        /// <summary>A point in the ring between the playable compound and the perimeter fence.</summary>
        private static Vector3 EdgeSpot()
        {
            float x = Random.Range(-17.5f, 17.5f);
            float z = Random.Range(-17.5f, 17.5f);
            // Push it out of the compound along whichever axis it is already closest to leaving.
            if (Mathf.Abs(x) > Mathf.Abs(z)) x = Mathf.Sign(x) * Random.Range(18.5f, 21f);
            else z = Mathf.Sign(z) * Random.Range(18.5f, 21f);

            // Nothing directly under the camera, which looks north from behind the south fence: a
            // tree there fills a third of the frame and hides the sample it is meant to decorate.
            if (z < -16f && Mathf.Abs(x) < 13f) z = -z;
            return new Vector3(x, 0f, z);
        }

        /// <summary>A warm point light for a fire, so the corners of the compound are not flat.</summary>
        private static void CreateFireLight(Transform parent, Vector3 position)
        {
            GameObject go = new GameObject("FireLight");
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.4f);
            light.intensity = 3.5f;
            light.range = 9f;
            light.shadows = LightShadows.None;
        }

        private static List<StealthGuardMarker> CreateMarkers()
        {
            GameObject root = new GameObject("_Markers");
            List<StealthGuardMarker> markers = new List<StealthGuardMarker>
            {
                CreateMarker(root.transform, "Route_North", MarkerKind.PatrolRoute, new Vector3(-6f, 0f, 10f), new Color(0.2f, 0.6f, 1f)),
                CreateMarker(root.transform, "Route_South", MarkerKind.PatrolRoute, new Vector3(6f, 0f, -14f), new Color(0.2f, 0.6f, 1f)),
                CreateMarker(root.transform, "Route_Perimeter", MarkerKind.PatrolRoute, new Vector3(14f, 0f, 8f), new Color(0.2f, 0.6f, 1f)),
                // Five routes for three guards. With three of each they converge on the same point
                // often enough that the compound looks like it has one guard in it.
                CreateMarker(root.transform, "Route_West", MarkerKind.PatrolRoute, new Vector3(-14f, 0f, 2f), new Color(0.2f, 0.6f, 1f)),
                CreateMarker(root.transform, "Route_Gatehouse", MarkerKind.PatrolRoute, new Vector3(-8f, 0f, -14f), new Color(0.2f, 0.6f, 1f)),
                CreateMarker(root.transform, "Warehouse", MarkerKind.InvestigateLocation, new Vector3(-14f, 0f, -8f), new Color(1f, 0.75f, 0.2f)),
                // One named place near each crate, so a radioed theft can name somewhere the guard
                // is actually able to be sent to. A report that names a spot eighteen metres from
                // where the crate went is worse than no report.
                CreateMarker(root.transform, "NorthStore", MarkerKind.InvestigateLocation, new Vector3(-11f, 0f, 10.5f), new Color(1f, 0.75f, 0.2f)),
                CreateMarker(root.transform, "EastFence", MarkerKind.InvestigateLocation, new Vector3(14.5f, 0f, 7f), new Color(1f, 0.75f, 0.2f)),
                CreateMarker(root.transform, "Courtyard", MarkerKind.InvestigateLocation, new Vector3(0f, 0f, -6f), new Color(1f, 0.75f, 0.2f)),
                CreateMarker(root.transform, "Gate", MarkerKind.InvestigateLocation, new Vector3(15f, 0f, -12f), new Color(1f, 0.75f, 0.2f)),
                CreateMarker(root.transform, "SafeZone_A", MarkerKind.SafeZone, new Vector3(-15f, 0f, 14f), new Color(0.3f, 0.9f, 0.4f)),
                CreateMarker(root.transform, "SafeZone_B", MarkerKind.SafeZone, new Vector3(15f, 0f, 15f), new Color(0.3f, 0.9f, 0.4f))
            };
            return markers;
        }

        private static StealthGuardMarker CreateMarker(Transform parent, string id, MarkerKind kind, Vector3 pos, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Marker_" + id;
            go.transform.SetParent(parent);
            go.transform.position = pos + Vector3.up * 0.05f;
            go.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // never block navigation or sight
            Paint(go, color);

            StealthGuardMarker marker = go.AddComponent<StealthGuardMarker>();
            marker.id = id;
            marker.kind = kind;
            return marker;
        }

        /// <summary>
        /// Puts the Kenney model under a root, skinned with one of the pack's textures. The whole
        /// pack is one rig plus swappable skins, so the guard and the intruder are the same mesh
        /// wearing different clothes. Falls back to a coloured capsule when the art is absent, so
        /// the sample still builds in a checkout without the asset packs.
        /// </summary>
        private static void AttachCharacterModel(GameObject root, string skinName, Color fallbackColour, float yaw)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterArt + "/characterMedium.fbx");
            if (source == null)
            {
                GameObject standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                standIn.name = "Model (art missing)";
                standIn.transform.SetParent(root.transform, false);
                standIn.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Object.DestroyImmediate(standIn.GetComponent<Collider>());
                Paint(standIn, fallbackColour);
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one * CharacterScale;

            Material skin = StealthGuardCharacterSetup.SkinMaterial(skinName);
            if (skin != null)
            {
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = skin;
            }

            // The Animator belongs on the file root, where the importer puts it, and the clips are
            // re-addressed to match rather than the other way round. StealthGuardCharacterSetup.Retarget
            // explains why moving the Animator down onto the rig instead does not work.
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            if (animator.avatar == null) animator.avatar = StealthGuardCharacterSetup.ModelAvatar;
            animator.runtimeAnimatorController = StealthGuardCharacterSetup.Prepare();
            animator.applyRootMotion = false;
        }

        /// <summary>
        /// Hangs the visibility fan under the guard. It is a child rather than a component on the
        /// guard itself because it needs its own MeshFilter and MeshRenderer, and because sitting
        /// at ankle height keeps it lying on the floor instead of cutting through the character.
        /// </summary>
        private static void AttachVisionCone(GameObject guard, ModularVisionModule vision)
        {
            GameObject go = new GameObject("VisionFan");
            go.transform.SetParent(guard.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.transform.localRotation = Quaternion.identity;

            go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = VisionConeMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GuardVisionCone cone = go.AddComponent<GuardVisionCone>();
            cone.vision = vision;
            cone.executor = guard.GetComponent<GuardExecutor>();
        }

        /// <summary>
        /// One unlit transparent material for every guard's fan. The colour is not on the material:
        /// each fan pushes its own through a MaterialPropertyBlock, so three guards showing three
        /// different states still share one asset.
        /// </summary>
        private static Material VisionConeMaterial()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(VisionConeMaterialPath);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new Material(shader) { name = "VisionCone" };

            // URP's Unlit is opaque until it is told otherwise, and telling it means all of this:
            // the surface mode, the blend factors, no depth write, no back-face culling, the
            // transparent queue, and the keyword the shader actually branches on.
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(1f, 0.92f, 0.45f, 0.13f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(1f, 0.92f, 0.45f, 0.13f));

            EnsureFolder(Root + "/Data");
            AssetDatabase.CreateAsset(material, VisionConeMaterialPath);
            return material;
        }

        /// <summary>A name plate and health bar that face the camera, built from quads and a TextMesh.</summary>
        private static GuardVisuals AttachOverhead(GameObject root)
        {
            GameObject overhead = new GameObject("Overhead");
            overhead.transform.SetParent(root.transform, false);
            overhead.transform.localPosition = new Vector3(0f, 2.1f, 0f);

            GameObject bar = new GameObject("HealthBar");
            bar.transform.SetParent(overhead.transform, false);
            CreateQuad(bar.transform, "Back", new Vector3(1.1f, 0.16f, 1f), new Color(0.08f, 0.08f, 0.09f), 0f);
            GameObject fill = CreateQuad(bar.transform, "Fill", new Vector3(1.04f, 0.1f, 1f), new Color(0.35f, 0.85f, 0.4f), -0.01f);

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(overhead.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            TextMesh text = labelGo.AddComponent<TextMesh>();
            text.text = root.name;
            text.characterSize = 0.12f;
            text.fontSize = 64;
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MeshRenderer textRenderer = labelGo.GetComponent<MeshRenderer>();
            if (textRenderer != null && text.font != null) textRenderer.sharedMaterial = text.font.material;

            GuardVisuals visuals = root.AddComponent<GuardVisuals>();
            visuals.healthBar = bar;
            visuals.healthFill = fill.transform;
            visuals.label = text;
            return visuals;
        }

        private static GameObject CreateQuad(Transform parent, string quadName, Vector3 scale, Color colour, float z)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = quadName;
            quad.transform.SetParent(parent, false);
            quad.transform.localScale = scale;
            quad.transform.localPosition = new Vector3(0f, 0f, z);
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            Renderer renderer = quad.GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            renderer.sharedMaterial = mat;
            return quad;
        }

        private static GameObject CreateIntruder()
        {
            GameObject go = new GameObject(StealthGuardIds.IntruderName);
            go.transform.position = new Vector3(0f, 0f, -14f);
            AttachCharacterModel(go, "criminalMaleA", new Color(0.85f, 0.25f, 0.25f), 0f);

            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);

            go.AddComponent<IntruderController>();

            // Without this the guard's vision has nothing to report: the vision module finds
            // colliders, but only objects carrying a context object become prompt text.
            AttachOverhead(go);

            LLMContextObject ctx = go.AddComponent<LLMContextObject>();
            ctx.objectName = StealthGuardIds.IntruderName;
            ctx.objectType = "Hostile";
            ctx.staticDescription = "An unidentified person inside the compound";

            return go;
        }

        /// <summary>
        /// Where each guard starts and who it thinks it is.
        ///
        /// Three guards rather than one, because a single guard makes the compound a puzzle about
        /// one patrol route; three make it a question of which of them is looking your way, and of
        /// which one gets radioed when a crate goes. Three personas rather than one, because the
        /// persona is the only part of the prompt that differs between them: run the scene and the
        /// three read the same STATE block and reliably do different things with it, which is worth
        /// more as a demonstration than a paragraph claiming it.
        /// </summary>
        private struct GuardSpec
        {
            public Vector3 Spawn;
            public float Yaw;
            public string Persona;
            /// <summary>The routes this guard may patrol. See GuardArgumentOptions.patrolBeat.</summary>
            public string[] Beat;
        }

        private static readonly GuardSpec[] Guards =
        {
            new GuardSpec { Spawn = new Vector3(-6f, 0f, 8f), Yaw = 180f,
                Beat = new[] { "Route_North", "Route_West" }, Persona =
                "a methodical veteran. You work the routes in order, you finish what you start, and " +
                "you do not leave a post on a hunch." },

            new GuardSpec { Spawn = new Vector3(13f, 0f, -6f), Yaw = 300f,
                Beat = new[] { "Route_Perimeter", "Route_South" }, Persona =
                "new and eager to prove yourself. You would rather check a noise and find nothing " +
                "than ignore one, and you go straight for anything you can see." },

            new GuardSpec { Spawn = new Vector3(-2f, 0f, -8f), Yaw = 0f,
                Beat = new[] { "Route_Gatehouse", "Route_South", "Route_West" }, Persona =
                "at the end of a long shift. You keep to the middle of the compound where you can " +
                "see the most for the least walking, and you want a good reason before you run." }
        };

        private static List<GameObject> CreateGuards(ActionConfig actions, GameObject intruder, int occluderLayer)
        {
            List<GameObject> made = new List<GameObject>();
            for (int i = 0; i < Mathf.Min(StealthGuardIds.GuardCount, Guards.Length); i++)
                made.Add(CreateGuard(actions, intruder, occluderLayer, i));
            return made;
        }

        private static GameObject CreateGuard(ActionConfig actions, GameObject intruder, int occluderLayer, int index)
        {
            GuardSpec spec = Guards[index];
            string guardName = StealthGuardIds.GuardName(index);

            GameObject go = new GameObject(guardName);
            go.transform.position = spec.Spawn;
            AttachCharacterModel(go, "survivorMaleB", new Color(0.25f, 0.45f, 0.85f), spec.Yaw);
            go.transform.rotation = Quaternion.Euler(0f, spec.Yaw, 0f);

            // A trigger so the intruder's presence can be found by the vision sweep, and so the
            // guard itself is a target the intruder can walk up to.
            SphereCollider guardTrigger = go.AddComponent<SphereCollider>();
            guardTrigger.isTrigger = true;
            guardTrigger.radius = 0.5f;
            guardTrigger.center = new Vector3(0f, 1f, 0f);

            NavMeshAgent nav = go.AddComponent<NavMeshAgent>();
            nav.speed = 3.5f;
            nav.angularSpeed = 240f;
            nav.acceleration = 12f;
            nav.radius = 0.4f;
            nav.height = 2f;

            GuardHealth health = go.AddComponent<GuardHealth>();
            GuardAvailability availability = go.AddComponent<GuardAvailability>();

            GuardArgumentOptions options = go.AddComponent<GuardArgumentOptions>();
            options.intruder = new GuardArgumentOptions.LLMContextObjectReference
            {
                target = intruder.transform,
                id = StealthGuardIds.IntruderName
            };
            // Its own beat. Three guards choosing freely from five routes converge on one of them
            // often enough that the compound ends up with three guards in one corner and nobody
            // anywhere else; a beat each covers the ground and costs the model nothing, because
            // argument options are built per decision maker.
            options.patrolBeat = new List<string>(spec.Beat ?? new string[0]);

            GuardExecutor executor = go.AddComponent<GuardExecutor>();
            executor.options = options;
            // A different side of the marker each, so two guards sent to the same route stand
            // beside one another rather than in the same square metre.
            executor.markerOffsetAngle = index * (360f / Mathf.Max(1, StealthGuardIds.GuardCount));

            // Perception: a cone that walls can block, so losing sight is possible. The settings
            // live in an asset the guard and the memory module share, not in fields here.
            PerceptionConfig perception = CreatePerceptionConfig(occluderLayer);
            ModularVisionModule vision = go.AddComponent<ModularVisionModule>();
            vision.ApplyConfig(perception);
            go.AddComponent<BasicMemory>().ApplyConfig(perception);

            // The radio. An observation module like any other, so a theft reaches the model the
            // same way sight does: as a line in the STATE block it may act on or ignore.
            go.AddComponent<TheftReportModule>();

            AttachVisionCone(go, vision);

            // Self-status: the guard's own health and current activity go into the prompt.
            LLMContextObject self = go.AddComponent<LLMContextObject>();
            self.objectName = guardName;
            self.objectType = "Self";
            self.staticDescription = "A security guard on night shift";
            self.dataBindings = new List<ContextDataBinding>
            {
                new ContextDataBinding { sourceComponent = health, memberName = nameof(GuardHealth.HealthReport) },
                new ContextDataBinding { sourceComponent = executor, memberName = nameof(GuardExecutor.currentActivity) }
            };
            go.AddComponent<SelfObservationModule>().topicName = "Self-Status";

            BehaviorLLMClient client = go.AddComponent<BehaviorLLMClient>();
            SerializedObject clientSo = new SerializedObject(client);
            clientSo.FindProperty("modelConfig").objectReferenceValue = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(ModelConfigAsset);
            clientSo.FindProperty("serverConfig").objectReferenceValue = CreateServerConfig();
            clientSo.ApplyModifiedPropertiesWithoutUndo();

            DecisionMaker agent = go.AddComponent<DecisionMaker>();
            agent.actionConfig = actions;
            agent.systemPersona =
                $"You are {guardName}, one of three guards patrolling a supply compound at night. " +
                $"You are {spec.Persona}";
            agent.actionBindings = new List<DecisionMaker.ActionBinding>();

            Bind(agent, StealthGuardIds.HoldPosition, executor.OnHoldPosition);
            Bind(agent, StealthGuardIds.Patrol, executor.OnPatrol);
            Bind(agent, StealthGuardIds.Investigate, executor.OnInvestigate);
            Bind(agent, StealthGuardIds.Chase, executor.OnChase);
            Bind(agent, StealthGuardIds.Retreat, executor.OnRetreat);

            SerializedObject agentSo = new SerializedObject(agent);
            agentSo.FindProperty("defaultBackendComponent").objectReferenceValue = client;
            agentSo.FindProperty("argumentOptionsProviderComponent").objectReferenceValue = options;
            agentSo.FindProperty("actionAvailabilityProviderComponent").objectReferenceValue = availability;
            agentSo.FindProperty("config").objectReferenceValue = CreateDecisionConfig();
            // A slot each, so three guards do not evict one another's cached prompt prefix.
            agentSo.FindProperty("requestSlot").intValue = index;
            SerializedProperty fallback = agentSo.FindProperty("fallbackAction");
            fallback.FindPropertyRelative("fallbackActionName").stringValue = StealthGuardIds.HoldPosition;
            fallback.FindPropertyRelative("fallbackArgument").stringValue = "";
            agentSo.ApplyModifiedPropertiesWithoutUndo();

            // A plate above the head, so the guard's current action is readable in the scene view
            // as well as in the console.
            GuardVisuals visuals = AttachOverhead(go);
            visuals.executor = executor;
            visuals.health = health;

            return go;
        }

        // Where the crates sit. Each one is out near a different patrol route, so taking all three
        // means crossing the guard's path three times rather than looting one safe corner.
        private static readonly Vector3[] LootSpots =
        {
            new Vector3(-11f, 0f, 9f),
            new Vector3(13f, 0f, 7f),
            new Vector3(-13f, 0f, -6f)
        };

        /// <summary>
        /// What the player is here to do: three crates to steal and a gate to carry them out of.
        ///
        /// The sample is about the guard's decisions, and this is what makes them matter to whoever
        /// is holding the keyboard. It is entirely separate from the decision loop - see
        /// StealthObjective - so a reader can delete the whole thing and still have the sample.
        /// </summary>
        private static void CreateObjective()
        {
            GameObject root = new GameObject("_Objective");

            for (int i = 0; i < LootSpots.Length; i++) CreateLoot(root.transform, i, LootSpots[i]);
            Light exitGlow = CreateExit(root.transform, new Vector3(-8f, 0f, -16.2f));

            StealthObjective objective = root.AddComponent<StealthObjective>();
            objective.exitGlow = exitGlow;
        }

        private static void CreateLoot(Transform parent, int index, Vector3 position)
        {
            GameObject go = new GameObject($"Crate_{index + 1:00}");
            go.transform.SetParent(parent);
            go.transform.position = position;

            // The model hangs off a child so the crate can spin and bob without moving the point
            // the pickup test measures from.
            GameObject visuals = new GameObject("Visuals");
            visuals.transform.SetParent(go.transform, false);
            visuals.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            if (StealthGuardProps.Available)
            {
                StealthGuardProps.Place(visuals.transform, "chest", visuals.transform.position, 0f);
            }
            else
            {
                GameObject standIn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                standIn.transform.SetParent(visuals.transform, false);
                standIn.transform.localScale = Vector3.one * 0.6f;
                Object.DestroyImmediate(standIn.GetComponent<Collider>());
                Paint(standIn, new Color(1f, 0.8f, 0.25f));
            }

            // On the crate's root, not on the part that turns: a caption parented to a spinning
            // object spends half of every rotation being read from behind.
            GameObject label = CreateLabel(go.transform, "STEAL", new Vector3(0f, 1.45f, 0f), new Color(1f, 0.85f, 0.35f), 0.075f);

            GameObject lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(visuals.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Light glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.82f, 0.35f);
            glow.range = 7f;
            glow.intensity = 5f;
            glow.shadows = LightShadows.None;

            StealthLoot loot = go.AddComponent<StealthLoot>();
            loot.visuals = visuals;
            loot.glow = glow;
            loot.label = label;
        }

        /// <summary>The way out, at the gap in the perimeter fence. Returns its light.</summary>
        private static Light CreateExit(Transform parent, Vector3 position)
        {
            GameObject go = new GameObject(StealthGuardIds.ExitName);
            go.transform.SetParent(parent);
            go.transform.position = position;

            // A flat disc on the ground, the same language the patrol and safe-zone markers use.
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            pad.transform.SetParent(go.transform, false);
            pad.transform.localScale = new Vector3(2.4f, 0.05f, 2.4f);
            pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            Paint(pad, new Color(0.35f, 0.9f, 0.45f));

            CreateLabel(go.transform, "EXIT", new Vector3(0f, 2.4f, 0f), new Color(0.55f, 1f, 0.6f), 0.16f);

            GameObject lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Light glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 12f;
            glow.intensity = 2f;
            glow.shadows = LightShadows.None;
            return glow;
        }

        /// <summary>
        /// A world-space caption. The camera in this sample never moves, so the text is turned to
        /// face it once at build time rather than being chased every frame by a component.
        /// </summary>
        private static GameObject CreateLabel(Transform parent, string text, Vector3 localPosition, Color colour, float size)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);

            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = size;
            mesh.fontSize = 64;
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = colour;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && mesh.font != null) renderer.sharedMaterial = mesh.font.material;
            return go;
        }

        private static void CreateManager()
        {
            GameObject go = new GameObject("LLMManager");

            BehaviorLLMServer server = go.AddComponent<BehaviorLLMServer>();
            SerializedObject serverSo = new SerializedObject(server);
            serverSo.FindProperty("modelConfig").objectReferenceValue = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(ModelConfigAsset);
            serverSo.FindProperty("serverConfig").objectReferenceValue = CreateServerConfig();
            serverSo.ApplyModifiedPropertiesWithoutUndo();

            DecisionTelemetryRecorder recorder = go.AddComponent<DecisionTelemetryRecorder>();
            SerializedObject recorderSo = new SerializedObject(recorder);
            recorderSo.FindProperty("runLabel").stringValue = "stealthguard";
            recorderSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- helpers

        private static void Bind(DecisionMaker agent, string actionName, UnityAction<ActionArguments> callback)
        {
            ActionEvent evt = new ActionEvent();
            UnityEventTools.AddPersistentListener(evt, callback);
            agent.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = actionName, onExecute = evt });
        }

        private static void Paint(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") : Shader.Find("Standard");
            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            renderer.sharedMaterial = mat;
        }

        private static void SnapToNavMesh(Transform t)
        {
            // Straight onto the surface: the character model's origin is between its feet. The
            // half-a-capsule the stand-in used to need would leave it hovering.
            if (NavMesh.SamplePosition(t.position, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                t.position = hit.position;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Adds a project layer if it is missing, so the occluder mask means something.</summary>
        private static int EnsureLayer(string name)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
            }
            for (int i = 8; i < layers.arraySize; i++) // 0-7 are reserved by Unity
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }

            Debug.LogWarning($"[StealthGuard] No free layer for '{name}'; line-of-sight blocking is disabled.");
            return 0;
        }
    }
}
