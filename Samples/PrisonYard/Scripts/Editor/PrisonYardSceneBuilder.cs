using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
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

namespace Project.Samples.PrisonYard.Editor
{
    /// <summary>
    /// Builds the whole PrisonYard scene from primitives, so the sample is reproducible from
    /// source and carries no art dependencies. Run it from Tools > PrisonYard > Build Scene.
    ///
    /// It also authors the sample's configuration assets, which is how a project is meant to use
    /// the package: the guards' senses, the prisoners' cadence and the warden's deliberation are
    /// reusable assets, not fields buried on eight components.
    /// </summary>
    public static class PrisonYardSceneBuilder
    {
        // Every path is derived from where this sample actually is; see PrisonYardPaths for why.
        private static string Root => PrisonYardPaths.OutputRoot;
        private static string ScenePath => PrisonYardPaths.Scenes + "/PrisonYard.unity";
        private static string Data => PrisonYardPaths.Data;
        private const string OccluderLayerName = "PrisonOccluder";

        /// <summary>
        /// Layer for scenery that must not reach the NavMesh.
        ///
        /// This scene bakes from render meshes, which is what lets a table block a route without a
        /// collider - but it also means a decorative mesh blocks one by accident. The pack's wood
        /// and grate floors are not closed surfaces, and a room paved with one bakes full of holes;
        /// the room signs are flat meshes lying on the paving outside each doorway, and baked in
        /// they wall the doorway off. Both are on this layer, and the surface is told to skip it.
        /// </summary>
        private const string DecorLayerName = "PrisonDecor";
        /// <summary>Shipped model preset this sample wires up, looked up by name rather than by
        /// path so it survives the package being installed anywhere.</summary>
        private const string ShippedModelAsset = "Model_Granite-4.1-3B-Q4_K_M";
        private static string CharacterArt => PrisonYardPaths.CharacterArt;
        private static string DungeonArt => PrisonYardPaths.DungeonArt;
        /// <summary>The KayKit dungeon pieces are modelled on a four-unit grid.</summary>
        private const float Tile = 4f;

        /// <summary>
        /// How far the camera tilts down. The room signs are turned by the same amount so they face
        /// it; this camera is fixed, so doing it once at build time is enough.
        /// </summary>
        private const float CameraPitch = 58f;

        /// <summary>
        /// Half the width of the walled compound, in metres. The furthest section reaches 28 m from
        /// the origin, so 32 leaves a one-tile margin between the buildings and the outer wall.
        /// Must stay a multiple of <see cref="Tile"/> or the wall stops meeting its own corners.
        /// </summary>
        private const float Extent = 32f;

        /// <summary>
        /// Height for the paving outside the rooms. The floor meshes are 20 cm slabs whose top face
        /// sits about 11 cm above their origin, so dropping the outer paving to -0.10 puts its
        /// surface just above zero and a comfortable five centimetres below a room's floor, which
        /// is what keeps the two from z-fighting where they overlap.
        /// </summary>
        private const float GroundFloorY = -0.10f;

        /// <summary>
        /// How one section is dressed. Kept next to the geometry table, and separate from it,
        /// because none of it changes where anyone can walk: a reader who wants to know what the
        /// prison *is* reads Layout, and a reader who wants to know why the infirmary looks like an
        /// infirmary reads this.
        /// </summary>
        private struct SectionStyle
        {
            public PrisonSection Section;
            /// <summary>The name painted above the room, so a screenshot needs no legend.</summary>
            public string Title;
            /// <summary>
            /// The mesh the room's floor is built from. Only the two solid ones are allowed here:
            /// the pack's wood and grate floors are not closed surfaces, and a NavMesh baked from
            /// them comes out full of holes. Anything else goes in <see cref="Overlay"/>.
            /// </summary>
            public string Floor;

            /// <summary>
            /// An optional second mesh laid a few centimetres over the floor, purely to look like
            /// something. Its holes fall on the solid floor underneath, so they cost nothing.
            /// Empty for a room that needs no dressing beyond its colour.
            /// </summary>
            public string Overlay;
            /// <summary>Tints the floor and the sign. Every room gets its own, so they read apart.</summary>
            public Color Accent;
            /// <summary>The colour of this room's torches, which is what carries at a distance.</summary>
            public Color Torch;
        }

        // Six rooms in one texture atlas will always look like one room repeated unless something
        // is done about it. Four floor meshes, a colour per room on the floor, the torches and the
        // sign, and props that belong to that room are what makes them tell apart from above.
        private static readonly SectionStyle[] Styles =
        {
            new SectionStyle { Section = PrisonSection.Yard,        Title = "YARD",         Floor = "floor_dirt_large",
                Accent = new Color(0.86f, 0.80f, 0.66f), Torch = new Color(1f, 0.80f, 0.45f) },

            new SectionStyle { Section = PrisonSection.CellBlock,   Title = "CELL BLOCK",   Floor = "floor_tile_large",
                Accent = new Color(0.58f, 0.66f, 0.82f), Torch = new Color(0.65f, 0.78f, 1f) },

            new SectionStyle { Section = PrisonSection.Cafeteria,   Title = "CAFETERIA",    Floor = "floor_tile_large",
                Overlay = "floor_wood_large",
                Accent = new Color(0.95f, 0.72f, 0.42f), Torch = new Color(1f, 0.74f, 0.40f) },

            new SectionStyle { Section = PrisonSection.Workshop,    Title = "WORKSHOP",     Floor = "floor_tile_large",
                Overlay = "floor_tile_big_grate",
                Accent = new Color(0.82f, 0.55f, 0.35f), Torch = new Color(1f, 0.62f, 0.32f) },

            new SectionStyle { Section = PrisonSection.Infirmary,   Title = "INFIRMARY",    Floor = "floor_tile_large",
                Accent = new Color(0.66f, 0.90f, 0.78f), Torch = new Color(0.70f, 1f, 0.85f) },

            new SectionStyle { Section = PrisonSection.ControlRoom, Title = "CONTROL ROOM", Floor = "floor_tile_large",
                Overlay = "floor_wood_large",
                Accent = new Color(0.72f, 0.62f, 0.92f), Torch = new Color(0.78f, 0.70f, 1f) }
        };

        /// <summary>The style for a section, or the yard's as a stand-in if one is ever missing.</summary>
        private static SectionStyle StyleOf(PrisonSection section)
        {
            for (int i = 0; i < Styles.Length; i++) if (Styles[i].Section == section) return Styles[i];
            return Styles[0];
        }

        // Section geometry, in one table so the walls, volumes, gates and markers agree.
        private struct SectionLayout
        {
            public PrisonSection Section;
            public Vector3 Centre;
            public Vector2 Size;
            /// <summary>Where the doorways sit, relative to the centre. One gate object per entry.</summary>
            public Vector3[] GateOffsets;
        }

        // The yard is the hub and everything else opens onto it through a five-metre corridor, so
        // any two sections are a short straight walk apart and most journeys cross the yard, which
        // is where the interesting encounters happen. An earlier layout put the sections in the
        // corners around a 22 m yard with a single gate; every route was a long detour around the
        // outside and the sample was hard to read.
        // Sizes are multiples of the four-metre tile, and every wall carrying a doorway has an odd
        // number of tiles, so the doorway lands on a whole piece rather than straddling two.
        private static readonly SectionLayout[] Layout =
        {
            new SectionLayout { Section = PrisonSection.Yard, Centre = new Vector3(0f, 0f, 0f), Size = new Vector2(20f, 20f),
                GateOffsets = new[] { new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, -10f), new Vector3(10f, 0f, 0f), new Vector3(-10f, 0f, 0f) } },

            new SectionLayout { Section = PrisonSection.CellBlock, Centre = new Vector3(0f, 0f, 22f), Size = new Vector2(20f, 12f),
                GateOffsets = new[] { new Vector3(0f, 0f, -6f) } },

            new SectionLayout { Section = PrisonSection.Cafeteria, Centre = new Vector3(22f, 0f, 0f), Size = new Vector2(12f, 20f),
                GateOffsets = new[] { new Vector3(-6f, 0f, 0f) } },

            new SectionLayout { Section = PrisonSection.Workshop, Centre = new Vector3(0f, 0f, -22f), Size = new Vector2(20f, 12f),
                GateOffsets = new[] { new Vector3(0f, 0f, 6f) } },

            new SectionLayout { Section = PrisonSection.Infirmary, Centre = new Vector3(-22f, 0f, 0f), Size = new Vector2(12f, 20f),
                GateOffsets = new[] { new Vector3(6f, 0f, 0f) } },

            new SectionLayout { Section = PrisonSection.ControlRoom, Centre = new Vector3(22f, 0f, 22f), Size = new Vector2(12f, 12f),
                GateOffsets = new[] { new Vector3(-6f, 0f, 0f) } }
        };

        [MenuItem("Tools/PrisonYard/Build Scene")]
        public static void BuildScene()
        {
            int occluderLayer = EnsureLayer(OccluderLayerName);
            PrisonYardAnimatorBuilder.Reset();
            EnsureFolder(Data);
            EnsureFolder(Root + "/Scenes");

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "PrisonYard";

            CreateEssentials();
            NavMeshSurface surface = CreateEnvironment(occluderLayer);
            CreateSectionVolumes();
            List<PrisonMarker> markers = CreateMarkers();
            GameObject systems = CreateSystems();

            // Bake before the gates exist at all. A gate barrier is a solid cube standing in a
            // doorway, and baking with them present sealed every doorway: no section connected to
            // any other, and everyone milled about the room they spawned in. Creating them after
            // the bake means only their carving obstacles ever affect the NavMesh, and only while
            // a gate is actually shut.
            surface.BuildNavMesh();
            CreateGates();

            PerceptionConfig guardPerception = CreateGuardPerception(occluderLayer);
            PerceptionConfig prisonerPerception = CreatePrisonerPerception();
            PerceptionConfig wardenPerception = CreateWardenPerception();
            BehaviorLLMServerConfig serverConfig = CreateServerConfig();
            BehaviorLLMModelConfig modelConfig = CreateModelConfig();

            CreateGuards(guardPerception, serverConfig, modelConfig);
            CreatePrisoners(prisonerPerception, serverConfig, modelConfig);
            CreateWarden(wardenPerception, serverConfig, modelConfig);
            CreateLlmManager(serverConfig, modelConfig);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PrisonYard] Built {ScenePath} with {markers.Count} markers and 8 decision makers. " +
                      "Press Play, then use keys 1-5 to provoke the prison.");
        }

        // ================================================================ world

        private static void CreateEssentials()
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            // High and steep, so all six sections and the corridor ring between them fit in frame.
            // Pulled back far enough to hold the whole walled compound, sign boards included: the
            // southernmost room's name board sat under the bottom edge from closer in.
            cam.transform.position = new Vector3(0f, 64f, -42f);
            cam.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
            Camera camera = cam.GetComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.backgroundColor = new Color(0.13f, 0.14f, 0.17f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.farClipPlane = 300f;

            GameObject lightGo = new GameObject("Directional Light", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static NavMeshSurface CreateEnvironment(int occluderLayer)
        {
            GameObject root = new GameObject("_Environment");
            int decorLayer = EnsureLayer(DecorLayerName);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(Extent * 0.2f, 1f, Extent * 0.2f);
            Paint(ground, new Color(0.22f, 0.20f, 0.17f));
            // The flagstones cover every square metre of the compound, so the plane is never seen.
            // It stays for its collider and is left out of the picture rather than z-fighting with
            // the tiles laid a centimetre above it.
            MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
            if (groundRenderer != null) groundRenderer.enabled = false;

            TileGrounds(root.transform);
            CreatePerimeter(root.transform, occluderLayer);
            CreateSectionSigns(root.transform, decorLayer);

            for (int i = 0; i < Layout.Length; i++)
            {
                CreateSectionWalls(root.transform, Layout[i], occluderLayer);
                DecorateSection(root.transform, Layout[i]);
                TileFloor(root.transform, Layout[i]);
            }
            ScatterProps(root.transform);

            NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = ~(1 << decorLayer);
            return surface;
        }

        /// <summary>
        /// Four walls with a three-metre doorway in one of them. The doorway is where the gate
        /// goes, so locking a section down has somewhere to physically happen.
        /// </summary>
        private static void CreateSectionWalls(Transform parent, SectionLayout s, int occluderLayer)
        {
            GameObject sectionRoot = new GameObject($"Walls_{s.Section.Id()}");
            sectionRoot.transform.SetParent(parent);

            float hx = s.Size.x * 0.5f;
            float hz = s.Size.y * 0.5f;
            const float t = 0.6f;   // wall thickness
            const float h = 3f;     // wall height
            const float doorway = 3f;

            // North and south walls.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 centre = s.Centre + new Vector3(0f, h * 0.5f, sign * hz);
                CreateWallRun(sectionRoot.transform, $"Wall_Z{sign}", centre, new Vector3(s.Size.x, h, t),
                    HasGateOn(s, alongX: true, sign: sign), true, doorway, occluderLayer);
            }
            // East and west walls.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 centre = s.Centre + new Vector3(sign * hx, h * 0.5f, 0f);
                CreateWallRun(sectionRoot.transform, $"Wall_X{sign}", centre, new Vector3(t, h, s.Size.y),
                    HasGateOn(s, alongX: false, sign: sign), false, doorway, occluderLayer);
            }
        }

        /// <summary>True when one of this section's doorways sits on the given wall.</summary>
        private static bool HasGateOn(SectionLayout s, bool alongX, int sign)
        {
            for (int i = 0; i < s.GateOffsets.Length; i++)
            {
                Vector3 g = s.GateOffsets[i];
                bool onZ = Mathf.Abs(g.z) > Mathf.Abs(g.x);
                if (onZ != alongX) continue;
                float coordinate = onZ ? g.z : g.x;
                if (Mathf.Sign(coordinate) == sign) return true;
            }
            return false;
        }

        /// <summary>One wall, split into two pieces when it needs a doorway in the middle.</summary>
        private static void CreateWallRun(Transform parent, string name, Vector3 centre, Vector3 size,
                                          bool hasDoor, bool alongX, float doorway, int occluderLayer)
        {
            if (!hasDoor)
            {
                CreateWall(parent, name, centre, size, occluderLayer);
                return;
            }

            float span = alongX ? size.x : size.z;
            float halfPiece = (span - doorway) * 0.5f;
            if (halfPiece <= 0.1f)
            {
                CreateWall(parent, name, centre, size, occluderLayer);
                return;
            }

            float offset = (doorway + halfPiece) * 0.5f;
            Vector3 dir = alongX ? Vector3.right : Vector3.forward;
            Vector3 pieceSize = alongX ? new Vector3(halfPiece, size.y, size.z) : new Vector3(size.x, size.y, halfPiece);

            CreateWall(parent, name + "_A", centre - dir * offset, pieceSize, occluderLayer);
            CreateWall(parent, name + "_B", centre + dir * offset, pieceSize, occluderLayer);
        }

        /// <summary>
        /// The physical wall: a box that blocks navigation and line of sight. Its renderer is off,
        /// because the visible wall is a row of dungeon pieces placed on the same line. Keeping the
        /// two apart means the art can change without touching what the NavMesh or the guards' cone
        /// vision do.
        /// </summary>
        private static void CreateWall(Transform parent, string name, Vector3 pos, Vector3 size, int layer)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent);
            wall.transform.position = pos;
            wall.transform.localScale = size;
            wall.layer = layer;

            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        // ================================================================ dungeon dressing

        /// <summary>
        /// Lays the visible stonework: one wall piece per four metres, an arched doorway where the
        /// gate is, and a mounted torch on every second piece.
        /// </summary>
        private static void DecorateSection(Transform parent, SectionLayout s)
        {
            GameObject root = new GameObject($"Stonework_{s.Section.Id()}");
            root.transform.SetParent(parent);

            float hx = s.Size.x * 0.5f;
            float hz = s.Size.y * 0.5f;

            // North and south runs, tiled along x.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int tiles = Mathf.RoundToInt(s.Size.x / Tile);
                for (int i = 0; i < tiles; i++)
                {
                    float x = s.Centre.x - hx + Tile * 0.5f + i * Tile;
                    Vector3 pos = new Vector3(x, 0f, s.Centre.z + sign * hz);
                    PlaceWallPiece(root.transform, pos, sign > 0 ? 180f : 0f, IsDoorway(s, pos), i, s);
                }
            }
            // East and west runs, tiled along z.
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int tiles = Mathf.RoundToInt(s.Size.y / Tile);
                for (int i = 0; i < tiles; i++)
                {
                    float z = s.Centre.z - hz + Tile * 0.5f + i * Tile;
                    Vector3 pos = new Vector3(s.Centre.x + sign * hx, 0f, z);
                    PlaceWallPiece(root.transform, pos, sign > 0 ? 270f : 90f, IsDoorway(s, pos), i, s);
                }
            }
        }

        /// <summary>True when one of this section's doorways sits on this wall piece.</summary>
        private static bool IsDoorway(SectionLayout s, Vector3 piecePosition)
        {
            for (int i = 0; i < s.GateOffsets.Length; i++)
            {
                Vector3 gate = s.Centre + s.GateOffsets[i];
                if (Vector3.Distance(new Vector3(gate.x, 0f, gate.z), new Vector3(piecePosition.x, 0f, piecePosition.z)) < Tile * 0.5f)
                    return true;
            }
            return false;
        }

        private static void PlaceWallPiece(Transform parent, Vector3 position, float yaw, bool doorway, int index, SectionLayout section)
        {
            Vector3 sectionCentre = section.Centre;
            string piece = doorway ? "wall_doorway" : (index % 3 == 1 ? "wall_cracked" : "wall");
            GameObject go = PlaceDungeonPiece(parent, piece, position, yaw);
            if (go == null || doorway) return;

            // A torch every third piece: enough to read as a lit room without a forest of them.
            if (index % 3 != 0) return;
            GameObject torch = PlaceDungeonPiece(parent, "torch_mounted", position, yaw);
            if (torch == null) return;

            // Hang it on the face that looks into the room, worked out from the section's centre
            // rather than from the wall's rotation, which differs per side.
            Vector3 inward = sectionCentre - position;
            inward.y = 0f;
            inward = inward.sqrMagnitude > 0.001f ? inward.normalized : Vector3.forward;
            torch.transform.position = position + Vector3.up * 2.4f + inward * 0.55f;
            torch.transform.rotation = Quaternion.LookRotation(-inward, Vector3.up);

            GameObject lightGo = new GameObject("TorchLight");
            lightGo.transform.SetParent(torch.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            // Each room burns its own colour. Seen from the sample's overhead camera this is what
            // separates the rooms at a glance, more than the floor tint does.
            light.color = StyleOf(section.Section).Torch;
            light.range = 10f;
            light.intensity = 2.6f;
        }

        /// <summary>
        /// Lays flagstones across a section. Tiled rather than one stretched quad, because the
        /// dungeon texture is an atlas and stretching one tile over twenty metres smears it.
        /// The floor sits a hair above the ground plane so the two do not fight for depth.
        /// </summary>
        private static void TileFloor(Transform parent, SectionLayout s)
        {
            GameObject root = new GameObject($"Floor_{s.Section.Id()}");
            root.transform.SetParent(parent);

            SectionStyle style = StyleOf(s.Section);
            Material tint = TintedMaterial(s.Section.Id(), style.Accent);
            int decor = LayerMask.NameToLayer(DecorLayerName);

            int across = Mathf.RoundToInt(s.Size.x / Tile);
            int deep = Mathf.RoundToInt(s.Size.y / Tile);

            for (int x = 0; x < across; x++)
            {
                for (int z = 0; z < deep; z++)
                {
                    Vector3 pos = new Vector3(
                        s.Centre.x - s.Size.x * 0.5f + Tile * 0.5f + x * Tile,
                        0.01f,
                        s.Centre.z - s.Size.y * 0.5f + Tile * 0.5f + z * Tile);
                    // A quarter turn every other tile breaks up the repeat.
                    PlaceDungeonPiece(root.transform, style.Floor, pos, ((x + z) % 2) * 90f, tint);
                    if (string.IsNullOrEmpty(style.Overlay)) continue;
                    GameObject overlay = PlaceDungeonPiece(root.transform, style.Overlay, pos + Vector3.up * 0.04f, ((x + z) % 2) * 90f, tint);
                    if (overlay != null) SetLayer(overlay, decor);
                }
            }
        }

        /// <summary>
        /// Paves everything outside the rooms: the yard's approaches, the corridors between the
        /// sections and the margin along the wall. Each section lays its own floor on top.
        ///
        /// A tile is skipped only when the *whole* tile falls inside a room. Skipping every tile
        /// whose centre was inside left a two-metre unpaved ring around each section, because a
        /// room's edge does not have to land on a tile boundary: this grid is offset half a tile
        /// from the yard's, and the yard is offset half a tile from the cell block's, so no single
        /// phase can line up with all of them. Overlapping is the cheap way out, and the section
        /// floors are laid a few centimetres higher so the overlap is never seen.
        /// </summary>
        private static void TileGrounds(Transform parent)
        {
            GameObject root = new GameObject("Floor_Grounds");
            root.transform.SetParent(parent);

            int half = Mathf.RoundToInt(Extent / Tile);
            for (int x = -half; x < half; x++)
            {
                for (int z = -half; z < half; z++)
                {
                    Vector3 pos = new Vector3(x * Tile + Tile * 0.5f, GroundFloorY, z * Tile + Tile * 0.5f);
                    if (BuriedByASection(pos)) continue;
                    PlaceDungeonPiece(root.transform, "floor_dirt_large", pos, ((x + z) % 2) * 90f);
                }
            }
        }

        /// <summary>True when a whole four-metre tile centred here would sit inside a room.</summary>
        private static bool BuriedByASection(Vector3 point)
        {
            const float margin = 0.01f; // absorbs the float error in the half-sizes
            for (int i = 0; i < Layout.Length; i++)
            {
                SectionLayout s = Layout[i];
                if (Mathf.Abs(point.x - s.Centre.x) + Tile * 0.5f <= s.Size.x * 0.5f + margin &&
                    Mathf.Abs(point.z - s.Centre.z) + Tile * 0.5f <= s.Size.y * 0.5f + margin) return true;
            }
            return false;
        }

        /// <summary>
        /// The outer wall. It is what stops the prison from being a set of buildings on an infinite
        /// plane, and unlike the decorative fence in StealthGuard it is real: it carries render
        /// geometry, the NavMesh is baked from render meshes here, so nobody walks out of the map.
        /// </summary>
        private static void CreatePerimeter(Transform parent, int occluderLayer)
        {
            GameObject root = new GameObject("Walls_Perimeter");
            root.transform.SetParent(parent);

            int half = Mathf.RoundToInt(Extent / Tile);
            for (int i = -half; i < half; i++)
            {
                float along = i * Tile + Tile * 0.5f;
                // Each side faces inward, so the decorated face of the piece is the one seen.
                PlaceDungeonPiece(root.transform, "wall", new Vector3(along, 0f, Extent), 180f);
                PlaceDungeonPiece(root.transform, "wall", new Vector3(along, 0f, -Extent), 0f);
                PlaceDungeonPiece(root.transform, "wall", new Vector3(Extent, 0f, along), 270f);
                PlaceDungeonPiece(root.transform, "wall", new Vector3(-Extent, 0f, along), 90f);
            }

            // No corner pieces. Each run is centred on its tiles, so the north wall already reaches
            // x = +/-Extent and the west wall already reaches z = +/-Extent: the four corners are
            // covered twice over, and a corner piece on top of that only pokes out of the silhouette.

            // A collider per side, on the occluder layer, so the wall blocks sight the same way a
            // section wall does. The art itself never carries one; see PlaceDungeonPiece.
            CreatePerimeterBlocker(root.transform, "Blocker_North", new Vector3(0f, 2f, Extent), new Vector3(Extent * 2f, 4f, 1f), occluderLayer);
            CreatePerimeterBlocker(root.transform, "Blocker_South", new Vector3(0f, 2f, -Extent), new Vector3(Extent * 2f, 4f, 1f), occluderLayer);
            CreatePerimeterBlocker(root.transform, "Blocker_East", new Vector3(Extent, 2f, 0f), new Vector3(1f, 4f, Extent * 2f), occluderLayer);
            CreatePerimeterBlocker(root.transform, "Blocker_West", new Vector3(-Extent, 2f, 0f), new Vector3(1f, 4f, Extent * 2f), occluderLayer);
        }

        private static void CreatePerimeterBlocker(Transform parent, string name, Vector3 position, Vector3 size, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = size;
            go.layer = layer;

            // Invisible: the wall pieces in front of it are what the player sees.
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        /// <summary>
        /// The room's name, in its own colour, hanging over the wall the camera looks at.
        ///
        /// It is the cheapest way to make a screenshot of eight decision makers legible: without it
        /// a reader has to count rooms against the layout table in the README to work out which one
        /// a prisoner just walked into.
        /// </summary>
        private static void CreateSectionSigns(Transform parent, int decorLayer)
        {
            GameObject root = new GameObject("_Signs");
            root.transform.SetParent(parent);

            for (int i = 0; i < Layout.Length; i++)
            {
                SectionLayout s = Layout[i];
                SectionStyle style = StyleOf(s.Section);

                GameObject go = new GameObject($"Sign_{s.Section.Id()}");
                go.transform.SetParent(root.transform);
                // Over the near wall, hanging down towards the camera. Two things had to be got
                // right here: a sign lying on the paving in front of the room falls off the bottom
                // of the frame for the southernmost room, and one anchored by its bottom edge
                // projects away from a camera that looks down at 58 degrees, so it lands on top of
                // whoever is standing in the room. Anchored by its top edge above the wall, it
                // hangs over the stonework and clears both.
                go.transform.position = new Vector3(s.Centre.x, 4.9f, s.Centre.z - s.Size.y * 0.5f - 0.4f);
                go.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);

                TextMesh mesh = go.AddComponent<TextMesh>();
                mesh.text = style.Title;
                mesh.characterSize = 0.34f;
                mesh.fontSize = 64;
                mesh.anchor = TextAnchor.UpperCenter;
                mesh.alignment = TextAlignment.Center;
                mesh.color = style.Accent;
                mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null && mesh.font != null) renderer.sharedMaterial = mesh.font.material;
                go.layer = decorLayer;
            }
        }

        /// <summary>Furniture and clutter, so each section reads as the room it is meant to be.</summary>
        private static void ScatterProps(Transform parent)
        {
            GameObject root = new GameObject("_Props");
            root.transform.SetParent(parent);
            Transform t = root.transform;

            Vector3 cells = CentreOf(PrisonSection.CellBlock);
            Vector3 hall = CentreOf(PrisonSection.Cafeteria);
            Vector3 shop = CentreOf(PrisonSection.Workshop);
            Vector3 ward = CentreOf(PrisonSection.Infirmary);
            Vector3 office = CentreOf(PrisonSection.ControlRoom);

            // The cell block: four bunks against the back wall, with shelves between them, and the
            // keys the guards carry on a hook by the door.
            for (int i = 0; i < 4; i++)
            {
                PlaceDungeonPiece(t, "table_medium", cells + new Vector3(-7.5f + i * 5f, 0f, 4f), 0f);
                PlaceDungeonPiece(t, "wall_shelves", cells + new Vector3(-7.5f + i * 5f, 0f, 5.6f), 180f);
                PlaceDungeonPiece(t, "table_medium", cells + new Vector3(-7.5f + i * 5f, 0f, -3.6f), 0f);
            }
            PlaceDungeonPiece(t, "keyring", cells + new Vector3(8.4f, 1.1f, -3.6f), 0f);

            // The refectory: two long benches down the room, and the stores along one wall.
            for (int i = -1; i <= 1; i++)
            {
                PlaceDungeonPiece(t, "table_medium", hall + new Vector3(-2.2f, 0f, i * 4f), 90f);
                PlaceDungeonPiece(t, "table_medium", hall + new Vector3(2.2f, 0f, i * 4f), 90f);
            }
            PlaceDungeonPiece(t, "barrel_large", hall + new Vector3(3.9f, 0f, 7.5f), 0f);
            PlaceDungeonPiece(t, "keg", hall + new Vector3(-3.9f, 0f, 7f), 0f);
            PlaceDungeonPiece(t, "keg", hall + new Vector3(-3.9f, 0f, -7f), 0f);
            PlaceDungeonPiece(t, "barrel_small", hall + new Vector3(3.9f, 0f, -7.4f), 0f);

            // The workshop: benches down one side and stacked stock down the other, which is what
            // makes it the room with somewhere to hide.
            PlaceDungeonPiece(t, "box_large", shop + new Vector3(-8f, 0f, -3.5f), 25f);
            PlaceDungeonPiece(t, "box_small", shop + new Vector3(-6.5f, 0f, -4.5f), -15f);
            PlaceDungeonPiece(t, "box_large", shop + new Vector3(-8.2f, 0f, 3.6f), -10f);
            PlaceDungeonPiece(t, "box_small", shop + new Vector3(-6.6f, 0f, 4.2f), 30f);
            PlaceDungeonPiece(t, "barrel_large", shop + new Vector3(8f, 0f, -3.5f), 0f);
            PlaceDungeonPiece(t, "barrel_small", shop + new Vector3(6.8f, 0f, -4.4f), 0f);
            PlaceDungeonPiece(t, "table_medium", shop + new Vector3(4f, 0f, 4f), 0f);
            PlaceDungeonPiece(t, "table_medium", shop + new Vector3(-1.5f, 0f, 4f), 0f);
            PlaceDungeonPiece(t, "chest", shop + new Vector3(0f, 0f, -4f), 0f);

            // The infirmary: four beds in a row against each wall, with a candle on the near one.
            for (int i = -1; i <= 1; i += 2)
            {
                PlaceDungeonPiece(t, "table_medium", ward + new Vector3(i * 3.4f, 0f, -4f), 90f);
                PlaceDungeonPiece(t, "table_medium", ward + new Vector3(i * 3.4f, 0f, 4f), 90f);
            }
            PlaceDungeonPiece(t, "candle_lit", ward + new Vector3(-3.4f, 1f, 4f), 0f);
            PlaceDungeonPiece(t, "candle_lit", ward + new Vector3(3.4f, 1f, -4f), 0f);
            PlaceDungeonPiece(t, "chest", ward + new Vector3(0f, 0f, 7.5f), 0f);

            // The warden's office. Nothing sits on a section centre: that point is where Respond
            // and Wander send people, and the NavMesh is baked from render meshes, so a table there
            // would carve the room's own destination out of the floor.
            PlaceDungeonPiece(t, "table_medium", office + new Vector3(0f, 0f, 3f), 0f);
            PlaceDungeonPiece(t, "chest", office + new Vector3(3f, 0f, 3f), 45f);
            PlaceDungeonPiece(t, "banner_patternA_red", office + new Vector3(0f, 0f, 5.4f), 0f);
            PlaceDungeonPiece(t, "banner_shield_red", office + new Vector3(-3f, 0f, 5.4f), 0f);
            PlaceDungeonPiece(t, "banner_patternA_blue", office + new Vector3(3f, 0f, 5.4f), 0f);
            PlaceDungeonPiece(t, "wall_shelves", office + new Vector3(-4.2f, 0f, 3f), 90f);
            PlaceDungeonPiece(t, "keyring", office + new Vector3(-1.2f, 1.1f, 3f), 0f);

            // The yard: something to sit on, and a stash behind it. Kept to the corners, because
            // the middle is where Wander and Respond send everybody.
            Vector3 yard = CentreOf(PrisonSection.Yard);
            PlaceDungeonPiece(t, "box_large", yard + new Vector3(-6f, 0f, -6f), 0f);
            PlaceDungeonPiece(t, "barrel_small", yard + new Vector3(-7.2f, 0f, -7f), 0f);
            PlaceDungeonPiece(t, "box_large", yard + new Vector3(6.4f, 0f, -6.6f), 20f);
            PlaceDungeonPiece(t, "barrel_large", yard + new Vector3(7.4f, 0f, 6.4f), 0f);
            PlaceDungeonPiece(t, "box_small", yard + new Vector3(-6.8f, 0f, 6.8f), -25f);
        }

        /// <summary>
        /// Drops one dungeon mesh into the scene. Pure decoration: no colliders, because the
        /// walking and the seeing are handled by the invisible boxes and the section volumes.
        ///
        /// Pass a <paramref name="tint"/> to swap the pack's shared material for one of this
        /// sample's tinted copies. Every piece in the pack samples the same atlas, so a tint is the
        /// only lever there is for telling one stone room from another.
        /// </summary>
        private static GameObject PlaceDungeonPiece(Transform parent, string pieceName, Vector3 position, float yaw, Material tint = null)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{DungeonArt}/{pieceName}.obj");
            if (source == null) return null;

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.name = pieceName;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Collider[] colliders = go.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++) Object.DestroyImmediate(colliders[i]);

            if (tint != null)
            {
                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = tint;
            }
            return go;
        }

        /// <summary>Puts a whole instance on one layer, so a NavMesh bake can be told to skip it.</summary>
        private static void SetLayer(GameObject go, int layer)
        {
            if (layer < 0) return;
            Transform[] all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = layer;
        }

        /// <summary>
        /// A material asset for one section's floor: the pack's atlas, tinted. Created once and
        /// reused, so rebuilding the scene does not leave a trail of materials behind.
        /// </summary>
        private static Material TintedMaterial(string id, Color tint)
        {
            string path = $"{DungeonArt}/Tint_{id}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{DungeonArt}/dungeon_texture.png");
            if (texture == null) return existing;

            Material material = existing;
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateSectionVolumes()
        {
            GameObject root = new GameObject("_Sections");
            for (int i = 0; i < Layout.Length; i++)
            {
                GameObject go = new GameObject($"Volume_{Layout[i].Section.Id()}");
                go.transform.SetParent(root.transform);
                go.transform.position = Layout[i].Centre;

                SectionVolume volume = go.AddComponent<SectionVolume>();
                volume.section = Layout[i].Section;
                volume.size = Layout[i].Size;
            }
        }

        // ================================================================ markers and gates

        private static List<PrisonMarker> CreateMarkers()
        {
            GameObject root = new GameObject("_Markers");
            List<PrisonMarker> markers = new List<PrisonMarker>();

            Color sectionColour = new Color(0.35f, 0.55f, 0.95f);
            Color patrolColour = new Color(0.3f, 0.75f, 1f);
            Color cellColour = new Color(0.75f, 0.75f, 0.8f);
            Color hideColour = new Color(0.9f, 0.5f, 0.9f);
            Color bedColour = new Color(0.35f, 0.9f, 0.5f);

            for (int i = 0; i < Layout.Length; i++)
            {
                SectionLayout s = Layout[i];

                markers.Add(CreateMarker(root.transform, s.Section.Id(), PrisonMarkerKind.SectionCentre, s.Section, s.Centre, sectionColour));

                float px = s.Size.x * 0.28f;
                float pz = s.Size.y * 0.28f;
                markers.Add(CreateMarker(root.transform, $"{s.Section.Id()}_Patrol_N", PrisonMarkerKind.PatrolPoint, s.Section,
                    s.Centre + new Vector3(-px, 0f, pz), patrolColour));
                markers.Add(CreateMarker(root.transform, $"{s.Section.Id()}_Patrol_S", PrisonMarkerKind.PatrolPoint, s.Section,
                    s.Centre + new Vector3(px, 0f, -pz), patrolColour));
            }

            Vector3 cellBlock = CentreOf(PrisonSection.CellBlock);
            for (int i = 0; i < 4; i++)
            {
                markers.Add(CreateMarker(root.transform, $"Cell_{i + 1:00}", PrisonMarkerKind.Cell, PrisonSection.CellBlock,
                    cellBlock + new Vector3(-7.5f + i * 5f, 0f, 3.5f), cellColour));
            }

            // Hiding spots are tucked against walls, away from the patrol points, so they are
            // genuinely out of a guard's cone rather than nominally so.
            markers.Add(CreateMarker(root.transform, "Workshop_Corner", PrisonMarkerKind.HidingSpot, PrisonSection.Workshop,
                CentreOf(PrisonSection.Workshop) + new Vector3(-8f, 0f, -3.5f), hideColour));
            markers.Add(CreateMarker(root.transform, "Workshop_Racks", PrisonMarkerKind.HidingSpot, PrisonSection.Workshop,
                CentreOf(PrisonSection.Workshop) + new Vector3(8f, 0f, -3.5f), hideColour));
            markers.Add(CreateMarker(root.transform, "Yard_Bleachers", PrisonMarkerKind.HidingSpot, PrisonSection.Yard,
                CentreOf(PrisonSection.Yard) + new Vector3(-6f, 0f, -6f), hideColour));

            markers.Add(CreateMarker(root.transform, "Bed_01", PrisonMarkerKind.Bed, PrisonSection.Infirmary,
                CentreOf(PrisonSection.Infirmary) + new Vector3(0f, 0f, -4f), bedColour));
            markers.Add(CreateMarker(root.transform, "Bed_02", PrisonMarkerKind.Bed, PrisonSection.Infirmary,
                CentreOf(PrisonSection.Infirmary) + new Vector3(0f, 0f, 4f), bedColour));

            return markers;
        }

        /// <summary>The middle of a section, from the one layout table everything else agrees with.</summary>
        private static Vector3 CentreOf(PrisonSection section)
        {
            for (int i = 0; i < Layout.Length; i++)
            {
                if (Layout[i].Section == section) return Layout[i].Centre;
            }
            return Vector3.zero;
        }

        private static PrisonMarker CreateMarker(Transform parent, string id, PrisonMarkerKind kind,
                                                 PrisonSection section, Vector3 pos, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Marker_" + id;
            go.transform.SetParent(parent);
            go.transform.position = pos + Vector3.up * 0.05f;
            go.transform.localScale = new Vector3(kind == PrisonMarkerKind.SectionCentre ? 1.6f : 1.1f, 0.05f,
                                                  kind == PrisonMarkerKind.SectionCentre ? 1.6f : 1.1f);
            Object.DestroyImmediate(go.GetComponent<Collider>()); // never block navigation or sight
            Paint(go, colour);

            PrisonMarker marker = go.AddComponent<PrisonMarker>();
            marker.id = id;
            marker.kind = kind;
            marker.section = section;
            return marker;
        }

        /// <summary>
        /// One gate per section, sitting in its doorway. A gate is an empty parent with a cube
        /// child; closing it enables the cube's renderer and collider and its carving obstacle,
        /// which cuts the doorway out of the NavMesh.
        /// </summary>
        private static void CreateGates()
        {
            GameObject root = new GameObject("_Gates");

            for (int i = 0; i < Layout.Length; i++)
            {
                SectionLayout s = Layout[i];
                for (int g = 0; g < s.GateOffsets.Length; g++)
                {
                    Vector3 offset = s.GateOffsets[g];
                    string suffix = s.GateOffsets.Length > 1 ? $"_{g}" : "";

                    GameObject gateRoot = new GameObject($"Gate_{s.Section.Id()}{suffix}");
                    gateRoot.transform.SetParent(root.transform);
                    gateRoot.transform.position = s.Centre + offset;

                    bool alongX = Mathf.Abs(offset.z) > Mathf.Abs(offset.x);
                    Vector3 size = alongX ? new Vector3(3f, 3f, 0.6f) : new Vector3(0.6f, 3f, 3f);

                    GameObject barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    barrier.name = "Barrier";
                    barrier.transform.SetParent(gateRoot.transform);
                    barrier.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                    barrier.transform.localScale = size;

                    // The box is the physics; the portcullis is what you see. PrisonGate toggles
                    // the renderer it finds in its children, so dropping the mesh in here is all
                    // that is needed for a lockdown to look like one.
                    MeshRenderer boxRenderer = barrier.GetComponent<MeshRenderer>();
                    GameObject bars = PlaceDungeonPiece(gateRoot.transform, "wall_gated", gateRoot.transform.position, alongX ? 0f : 90f);
                    if (bars != null)
                    {
                        bars.name = "Bars";
                        if (boxRenderer != null) Object.DestroyImmediate(boxRenderer);
                    }
                    else if (boxRenderer != null)
                    {
                        Paint(barrier, new Color(0.85f, 0.35f, 0.25f));
                    }

                    // Keep the barrier out of the bake entirely. Without this it is a solid cube
                    // standing in the doorway when the NavMesh is built, so every doorway bakes
                    // shut and no section connects to any other: the whole prison becomes six
                    // sealed rooms. Only the carving obstacle should ever affect the NavMesh, and
                    // only while the gate is closed.
                    NavMeshModifier modifier = barrier.AddComponent<NavMeshModifier>();
                    modifier.ignoreFromBuild = true;

                    NavMeshObstacle obstacle = barrier.AddComponent<NavMeshObstacle>();
                    obstacle.carving = true;
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = Vector3.one;

                    PrisonGate gate = gateRoot.AddComponent<PrisonGate>();
                    gate.section = s.Section;
                    gate.startOpen = true;

                    // Save the gate in the state it will be in the moment the scene runs. PrisonGate
                    // does this in Awake, which never happens in the Editor, so a scene saved with
                    // the obstacle switched on has every doorway carved shut on disk: the prison
                    // looks like six sealed rooms to anything that inspects it without pressing
                    // Play, including any check written to prove the sections connect.
                    obstacle.enabled = false;
                    Collider barrierCollider = barrier.GetComponent<Collider>();
                    if (barrierCollider != null) barrierCollider.enabled = false;
                    MeshRenderer barrierRenderer = barrier.GetComponent<MeshRenderer>();
                    if (barrierRenderer != null) barrierRenderer.enabled = false;
                    if (bars != null)
                    {
                        Renderer[] barRenderers = bars.GetComponentsInChildren<Renderer>(true);
                        for (int r = 0; r < barRenderers.Length; r++) barRenderers[r].enabled = false;
                    }
                }
            }
        }

        // ================================================================ systems

        private static GameObject CreateSystems()
        {
            GameObject go = new GameObject("_PrisonSystems");
            go.AddComponent<PrisonRadio>();
            go.AddComponent<PrisonClock>();
            go.AddComponent<PrisonStatusBoard>();
            go.AddComponent<PrisonHud>();
            go.AddComponent<DirectorControls>();
            return go;
        }

        private static void CreateLlmManager(BehaviorLLMServerConfig serverConfig, BehaviorLLMModelConfig modelConfig)
        {
            GameObject go = new GameObject("LLMManager");

            BehaviorLLMServer server = go.AddComponent<BehaviorLLMServer>();
            SerializedObject serverSo = new SerializedObject(server);
            serverSo.FindProperty("serverConfig").objectReferenceValue = serverConfig;
            serverSo.FindProperty("modelConfig").objectReferenceValue = modelConfig;
            serverSo.ApplyModifiedPropertiesWithoutUndo();

            DecisionTelemetryRecorder recorder = go.AddComponent<DecisionTelemetryRecorder>();
            SerializedObject recorderSo = new SerializedObject(recorder);
            recorderSo.FindProperty("runLabel").stringValue = "prisonyard";
            recorderSo.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<PrisonRunReport>();
        }

        // ================================================================ the cast

        private static void CreateGuards(PerceptionConfig perception, BehaviorLLMServerConfig serverConfig, BehaviorLLMModelConfig modelConfig)
        {
            ActionConfig actions = CreateGuardActions();
            DecisionMakerConfig decisions = CreateGuardDecisions();
            Color colour = new Color(0.25f, 0.45f, 0.85f);

            CreateGuard(PrisonYardIds.Guard01, PrisonSection.Yard, CentreOf(PrisonSection.Yard) + new Vector3(0f, 0f, -5f), colour, 0,
                "You are Guard_01, the senior officer on this shift, responsible for the Yard. " +
                "You are calm and you go by the book: deal with what is in front of you, then get back to your patrol.",
                actions, decisions, perception, serverConfig, modelConfig);

            CreateGuard(PrisonYardIds.Guard02, PrisonSection.CellBlock, CentreOf(PrisonSection.CellBlock) + new Vector3(-8f, 0f, 0f), colour, 1,
                "You are Guard_02, responsible for the Cell Block. You are new and jumpy, and you would " +
                "rather call something in over the radio than let it go unreported.",
                actions, decisions, perception, serverConfig, modelConfig);

            CreateGuard(PrisonYardIds.Guard03, PrisonSection.Workshop, CentreOf(PrisonSection.Workshop) + new Vector3(-8f, 0f, 0f), colour, 2,
                "You are Guard_03, responsible for the Workshop. You are slow to react but thorough: " +
                "you search prisoners properly and you finish what you start.",
                actions, decisions, perception, serverConfig, modelConfig);
        }

        private static void CreateGuard(string guardName, PrisonSection section, Vector3 position, Color colour, int slot,
                                        string persona, ActionConfig actions, DecisionMakerConfig decisions,
                                        PerceptionConfig perception, BehaviorLLMServerConfig serverConfig,
                                        BehaviorLLMModelConfig modelConfig)
        {
            // Guards are knights: plate armour reads as "authority" from across the yard, which is
            // the whole reason for using real characters rather than coloured capsules.
            GameObject go = CreateBody(guardName, position, colour, "Knight");

            NavMeshAgent nav = AddNav(go, 3.6f);
            GuardState state = go.AddComponent<GuardState>();
            state.assignedSection = section;

            go.AddComponent<GuardAvailability>();
            GuardArgumentOptions options = go.AddComponent<GuardArgumentOptions>();
            GuardExecutor executor = go.AddComponent<GuardExecutor>();

            go.AddComponent<ModularVisionModule>().ApplyConfig(perception);
            go.AddComponent<BasicMemory>().ApplyConfig(perception);
            AddRadio(go, guardName);

            LLMContextObject ctx = go.AddComponent<LLMContextObject>();
            ctx.objectName = guardName;
            ctx.objectType = PrisonYardIds.TypeGuard;
            ctx.staticDescription = $"A prison officer covering {section.Id()}";
            ctx.dataBindings = new List<ContextDataBinding>
            {
                new ContextDataBinding { sourceComponent = state, memberName = nameof(GuardState.HealthReport) },
                new ContextDataBinding { sourceComponent = state, memberName = nameof(GuardState.activity) }
            };
            go.AddComponent<SelfObservationModule>().topicName = "Your status";

            BehaviorLLMClient client = AddClient(go, serverConfig, modelConfig);
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionConfig = actions;
            maker.systemPersona = persona;
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();

            Bind(maker, PrisonYardIds.HoldPosition, executor.OnHoldPosition);
            Bind(maker, PrisonYardIds.Patrol, executor.OnPatrol);
            Bind(maker, PrisonYardIds.Respond, executor.OnRespond);
            Bind(maker, PrisonYardIds.Escort, executor.OnEscort);
            Bind(maker, PrisonYardIds.Search, executor.OnSearch);
            Bind(maker, PrisonYardIds.Report, executor.OnReport);
            Bind(maker, PrisonYardIds.Retreat, executor.OnRetreat);

            WireDecisionMaker(maker, client, decisions, options, go.GetComponent<GuardAvailability>(), slot, PrisonYardIds.HoldPosition);
        }

        private static void CreatePrisoners(PerceptionConfig perception, BehaviorLLMServerConfig serverConfig, BehaviorLLMModelConfig modelConfig)
        {
            ActionConfig actions = CreatePrisonerActions();
            DecisionMakerConfig decisions = CreatePrisonerDecisions();
            Color calm = new Color(0.90f, 0.55f, 0.20f);
            Color trouble = new Color(0.88f, 0.34f, 0.22f);
            Vector3 cellBlock = CentreOf(PrisonSection.CellBlock);

            CreatePrisoner(PrisonYardIds.Prisoner01, PrisonerTemperament.Compliant, cellBlock + new Vector3(-6f, 0f, 0f), calm, 3,
                "You are Prisoner_01. You want to serve your time quietly and get out. You follow the " +
                "schedule and you keep out of trouble.",
                actions, decisions, perception, serverConfig, modelConfig, "Mage");

            CreatePrisoner(PrisonYardIds.Prisoner02, PrisonerTemperament.Compliant, cellBlock + new Vector3(-2f, 0f, 0f), calm, 4,
                "You are Prisoner_02. You follow the schedule, but you are sociable and you would " +
                "rather be talking to someone than standing on your own.",
                actions, decisions, perception, serverConfig, modelConfig, "Rogue");

            CreatePrisoner(PrisonYardIds.Prisoner03, PrisonerTemperament.Hothead, cellBlock + new Vector3(2f, 0f, 0f), trouble, 5,
                "You are Prisoner_03. You have a temper and a reputation to keep. You settle things " +
                "yourself, and you are careful about who is watching when you do.",
                actions, decisions, perception, serverConfig, modelConfig, "Barbarian");

            CreatePrisoner(PrisonYardIds.Prisoner04, PrisonerTemperament.Escapee, cellBlock + new Vector3(6f, 0f, 0f), trouble, 6,
                "You are Prisoner_04. You are working on a way out. You test doors, you learn the " +
                "guards' rounds, and you keep what you are carrying out of sight.",
                actions, decisions, perception, serverConfig, modelConfig, "Rogue_Hooded");
        }

        private static void CreatePrisoner(string prisonerName, PrisonerTemperament temperament, Vector3 position, Color colour, int slot,
                                           string persona, ActionConfig actions, DecisionMakerConfig decisions,
                                           PerceptionConfig perception, BehaviorLLMServerConfig serverConfig,
                                           BehaviorLLMModelConfig modelConfig, string modelName)
        {
            GameObject go = CreateBody(prisonerName, position, colour, modelName);

            AddNav(go, 3.2f);
            PrisonerState state = go.AddComponent<PrisonerState>();
            state.temperament = temperament;

            go.AddComponent<PrisonerAvailability>();
            PrisonerArgumentOptions options = go.AddComponent<PrisonerArgumentOptions>();
            PrisonerExecutor executor = go.AddComponent<PrisonerExecutor>();

            go.AddComponent<ModularVisionModule>().ApplyConfig(perception);
            go.AddComponent<BasicMemory>().ApplyConfig(perception);
            AddRadio(go, prisonerName);

            LLMContextObject ctx = go.AddComponent<LLMContextObject>();
            ctx.objectName = prisonerName;
            ctx.objectType = PrisonYardIds.TypePrisoner;
            ctx.staticDescription = "An inmate";
            // Contraband and temperament are on this object's own bindings, so a prisoner knows
            // what it is carrying. Other people's vision reads the same object, which is why the
            // description stays vague: what a guard sees is the activity, not the intent.
            ctx.dataBindings = new List<ContextDataBinding>
            {
                new ContextDataBinding { sourceComponent = state, memberName = nameof(PrisonerState.HealthReport) },
                new ContextDataBinding { sourceComponent = state, memberName = nameof(PrisonerState.activity) }
            };
            go.AddComponent<SelfObservationModule>().topicName = "Your status";

            BehaviorLLMClient client = AddClient(go, serverConfig, modelConfig);
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionConfig = actions;
            maker.systemPersona = persona;
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();

            Bind(maker, PrisonYardIds.FollowSchedule, executor.OnFollowSchedule);
            Bind(maker, PrisonYardIds.Wander, executor.OnWander);
            Bind(maker, PrisonYardIds.Talk, executor.OnTalk);
            Bind(maker, PrisonYardIds.Fight, executor.OnFight);
            Bind(maker, PrisonYardIds.Hide, executor.OnHide);
            Bind(maker, PrisonYardIds.Sneak, executor.OnSneak);
            Bind(maker, PrisonYardIds.Comply, executor.OnComply);

            WireDecisionMaker(maker, client, decisions, options, go.GetComponent<PrisonerAvailability>(), slot, PrisonYardIds.FollowSchedule);
        }

        private static void CreateWarden(PerceptionConfig perception, BehaviorLLMServerConfig serverConfig, BehaviorLLMModelConfig modelConfig)
        {
            // No body, no NavMesh agent, no vision: the warden is a game system that happens to
            // decide with a language model.
            GameObject go = new GameObject(PrisonYardIds.Warden);
            go.transform.position = CentreOf(PrisonSection.ControlRoom);

            go.AddComponent<WardenAvailability>();
            WardenArgumentOptions options = go.AddComponent<WardenArgumentOptions>();
            WardenExecutor executor = go.AddComponent<WardenExecutor>();

            go.AddComponent<StatusBoardObservationModule>();
            go.AddComponent<BasicMemory>().ApplyConfig(perception);
            AddRadio(go, PrisonYardIds.Warden);

            BehaviorLLMClient client = AddClient(go, serverConfig, modelConfig);
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionConfig = CreateWardenActions();
            maker.systemPersona =
                "You are the warden of this prison. You watch the whole facility from the control room " +
                "and you decide how it is run. You prefer the least disruptive response that actually " +
                "resolves a problem, and you reopen sections as soon as they are quiet.";
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();

            Bind(maker, PrisonYardIds.Observe, executor.OnObserve);
            Bind(maker, PrisonYardIds.Lockdown, executor.OnLockdown);
            Bind(maker, PrisonYardIds.LiftLockdown, executor.OnLiftLockdown);
            Bind(maker, PrisonYardIds.Reinforce, executor.OnReinforce);
            Bind(maker, PrisonYardIds.Announce, executor.OnAnnounce);

            WireDecisionMaker(maker, client, CreateWardenDecisions(), options, go.GetComponent<WardenAvailability>(), 7, PrisonYardIds.Observe);
        }

        // ================================================================ shared assembly

        /// <summary>
        /// One person: an empty root carrying all the logic, with the character model as a child.
        /// The root is what everything else addresses, so swapping the art never touches the
        /// components that make decisions.
        /// </summary>
        private static GameObject CreateBody(string objectName, Vector3 position, Color colour, string modelName)
        {
            GameObject go = new GameObject(objectName);
            go.transform.position = new Vector3(position.x, 0f, position.z);

            // A trigger collider so the vision modules' OverlapSphere can find this person. It
            // lives on the root, next to the context object, because that is what vision reports.
            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.5f;
            trigger.center = new Vector3(0f, 1f, 0f);

            AttachCharacterModel(go, modelName, colour);
            AttachOverhead(go);
            return go;
        }

        /// <summary>
        /// Puts the KayKit model under the root and gives it a controller built from its own clips.
        /// Falls back to a coloured capsule when the art is missing, so the sample still builds and
        /// runs in a checkout without the asset packs.
        /// </summary>
        private static void AttachCharacterModel(GameObject root, string modelName, Color colour)
        {
            string modelPath = $"{CharacterArt}/{modelName}.glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            if (source == null)
            {
                GameObject stand_in = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                stand_in.name = "Model (art missing)";
                stand_in.transform.SetParent(root.transform, false);
                stand_in.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                Object.DestroyImmediate(stand_in.GetComponent<Collider>());
                Paint(stand_in, colour);
                Debug.LogWarning($"[PrisonYard] {modelPath} not found; using a capsule for {root.name}.");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController =
                    PrisonYardAnimatorBuilder.Build(modelPath, $"{CharacterArt}/{modelName}.controller");
                // The NavMeshAgent drives movement; root motion would fight it.
                animator.applyRootMotion = false;
            }
        }

        /// <summary>
        /// A name plate and a health bar that face the camera. Plain quads and a TextMesh rather
        /// than a canvas, so the sample stays buildable from one menu item with no UI prefabs.
        /// </summary>
        private static void AttachOverhead(GameObject root)
        {
            GameObject overhead = new GameObject("Overhead");
            overhead.transform.SetParent(root.transform, false);
            overhead.transform.localPosition = new Vector3(0f, 2.3f, 0f);

            GameObject bar = new GameObject("HealthBar");
            bar.transform.SetParent(overhead.transform, false);

            GameObject back = CreateQuad(bar.transform, "Back", new Vector3(1.1f, 0.16f, 1f),
                new Color(0.08f, 0.08f, 0.09f), 0f);
            GameObject fill = CreateQuad(bar.transform, "Fill", new Vector3(1.04f, 0.1f, 1f),
                new Color(0.35f, 0.85f, 0.4f), -0.01f);

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

            PrisonCharacterVisuals visuals = root.AddComponent<PrisonCharacterVisuals>();
            visuals.healthBar = bar;
            visuals.healthFill = fill.transform;
            visuals.label = text;

            _ = back;
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

        private static NavMeshAgent AddNav(GameObject go, float speed)
        {
            NavMeshAgent nav = go.AddComponent<NavMeshAgent>();
            nav.speed = speed;
            nav.angularSpeed = 260f;
            nav.acceleration = 14f;
            nav.radius = 0.4f;
            nav.height = 2f;
            nav.stoppingDistance = 0.6f;
            return nav;
        }

        private static void AddRadio(GameObject go, string listenerName)
        {
            RadioObservationModule radio = go.AddComponent<RadioObservationModule>();
            radio.listenerName = listenerName;
        }

        private static BehaviorLLMClient AddClient(GameObject go, BehaviorLLMServerConfig serverConfig, BehaviorLLMModelConfig modelConfig)
        {
            BehaviorLLMClient client = go.AddComponent<BehaviorLLMClient>();
            SerializedObject so = new SerializedObject(client);
            so.FindProperty("serverConfig").objectReferenceValue = serverConfig;
            so.FindProperty("modelConfig").objectReferenceValue = modelConfig;
            so.ApplyModifiedPropertiesWithoutUndo();
            return client;
        }

        /// <summary>
        /// The wiring every decision maker shares: which backend, which config, its providers, its
        /// own server slot and its fallback action.
        /// </summary>
        private static void WireDecisionMaker(DecisionMaker maker, BehaviorLLMClient client, DecisionMakerConfig config,
                                              MonoBehaviour options, MonoBehaviour availability, int slot, string fallbackAction)
        {
            SerializedObject so = new SerializedObject(maker);
            so.FindProperty("defaultBackendComponent").objectReferenceValue = client;
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("argumentOptionsProviderComponent").objectReferenceValue = options;
            so.FindProperty("actionAvailabilityProviderComponent").objectReferenceValue = availability;

            // Its own slot, so the server keeps this decision maker's prompt in cache instead of
            // evicting it every time somebody else decides.
            so.FindProperty("requestSlot").intValue = slot;

            SerializedProperty fallback = so.FindProperty("fallbackAction");
            fallback.FindPropertyRelative("fallbackActionName").stringValue = fallbackAction;
            fallback.FindPropertyRelative("fallbackArgument").stringValue = "";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Bind(DecisionMaker maker, string actionName, UnityAction<ActionArguments> callback)
        {
            ActionEvent evt = new ActionEvent();
            UnityEventTools.AddPersistentListener(evt, callback);
            maker.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = actionName, onExecute = evt });
        }

        // ================================================================ config assets

        private static ActionConfig CreateGuardActions()
        {
            ActionConfig config = LoadOrCreate<ActionConfig>(Data + "/Actions_Guard.asset");
            config.validActions = new List<ActionDefinition>
            {
                Action(PrisonYardIds.HoldPosition, "Stand still and keep watch where you are.", ActionParameterType.None, ""),
                Action(PrisonYardIds.Patrol, "Walk to a patrol point in your section and watch from there.", ActionParameterType.String, "Yard_Patrol_N"),
                Action(PrisonYardIds.Respond, "Go to a section where something is happening.", ActionParameterType.String, "Yard"),
                Action(PrisonYardIds.Escort, "Take a prisoner where they are supposed to be.", ActionParameterType.String, PrisonYardIds.Prisoner01),
                Action(PrisonYardIds.Search, "Search a prisoner for contraband.", ActionParameterType.String, PrisonYardIds.Prisoner04),
                Action(PrisonYardIds.Report, "Call in what you can see in a section over the radio.", ActionParameterType.String, "Yard"),
                Action(PrisonYardIds.Retreat, "Fall back to the infirmary to recover.", ActionParameterType.String, "Infirmary")
            };
            config.modelInstructions =
                "Choose exactly one action per turn.\n" +
                "Deal with what you can see before going back to your routine.\n" +
                "If the radio reports trouble, respond to it.\n" +
                "Otherwise keep patrolling. Prefer patrolling over standing still.";
            EditorUtility.SetDirty(config);
            return config;
        }

        private static ActionConfig CreatePrisonerActions()
        {
            ActionConfig config = LoadOrCreate<ActionConfig>(Data + "/Actions_Prisoner.asset");
            config.validActions = new List<ActionDefinition>
            {
                Action(PrisonYardIds.FollowSchedule, "Go where the schedule says you should be.", ActionParameterType.None, ""),
                Action(PrisonYardIds.Wander, "Walk to another part of the prison you are allowed into.", ActionParameterType.String, "Yard"),
                Action(PrisonYardIds.Talk, "Stand and talk with another prisoner.", ActionParameterType.String, PrisonYardIds.Prisoner02),
                Action(PrisonYardIds.Fight, "Attack another prisoner.", ActionParameterType.String, PrisonYardIds.Prisoner01),
                Action(PrisonYardIds.Hide, "Stash what you are carrying somewhere out of sight.", ActionParameterType.String, "Workshop_Corner"),
                Action(PrisonYardIds.Sneak, "Slip into a part of the prison you are not supposed to be in.", ActionParameterType.String, "Workshop"),
                Action(PrisonYardIds.Comply, "Do as the officer says and stay still.", ActionParameterType.None, "")
            };
            config.modelInstructions =
                "Choose exactly one action per turn.\n" +
                "Follow the schedule unless something better is on the menu.\n" +
                "Prefer doing something over standing still.";
            EditorUtility.SetDirty(config);
            return config;
        }

        private static ActionConfig CreateWardenActions()
        {
            ActionConfig config = LoadOrCreate<ActionConfig>(Data + "/Actions_Warden.asset");

            ActionDefinition announce = Action(PrisonYardIds.Announce, "Say something to the whole prison over the radio.",
                ActionParameterType.String, PrisonYardIds.StayCalm);
            // The one argument list in this sample that is a fixed vocabulary rather than a set of
            // scene objects, which is exactly what Allowed Arguments is for.
            announce.allowedArguments = new List<string>
            {
                PrisonYardIds.ReturnToCells,
                PrisonYardIds.StayCalm,
                PrisonYardIds.MealTime,
                PrisonYardIds.YardTime
            };

            config.validActions = new List<ActionDefinition>
            {
                Action(PrisonYardIds.Observe, "Change nothing and keep watching.", ActionParameterType.None, ""),
                Action(PrisonYardIds.Lockdown, "Shut a section so nobody can go in or out.", ActionParameterType.String, "Yard"),
                Action(PrisonYardIds.LiftLockdown, "Reopen a section that is shut.", ActionParameterType.String, "Yard"),
                Action(PrisonYardIds.Reinforce, "Send the nearest free officer to a section.", ActionParameterType.String, "Yard"),
                announce
            };
            config.modelInstructions =
                "Choose exactly one action per turn.\n" +
                "Prefer the least disruptive response that resolves the incident.\n" +
                "Lift a lockdown once that section is quiet again.";
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

        private static DecisionMakerConfig CreateGuardDecisions()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(Data + "/Decisions_Guard.asset");
            cfg.decisionInterval = 2.5f;
            cfg.profile = DecisionProfile.Reactive;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            cfg.maxVisionEntries = 4;
            cfg.maxMemoryEntries = 3;
            // The cap covers the WHOLE prompt, system half included, and this sample's system
            // half is about 1,500 characters. A budget below that silently trimmed every
            // observation away and the model decided from an empty state block. Keep it well
            // above the system prompt; a slot holds 2,048 tokens (roughly 8,000 characters), so
            // there is plenty of room.
            cfg.maxPromptChars = 2800;
            cfg.logDecisions = true;
            cfg.logPrompts = false;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static DecisionMakerConfig CreatePrisonerDecisions()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(Data + "/Decisions_Prisoner.asset");
            cfg.decisionInterval = 4f;
            cfg.profile = DecisionProfile.Reactive;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            cfg.maxVisionEntries = 3;
            cfg.maxMemoryEntries = 2;
            cfg.maxPromptChars = 2600;
            cfg.logDecisions = true;
            cfg.logPrompts = false;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static DecisionMakerConfig CreateWardenDecisions()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(Data + "/Decisions_Warden.asset");
            cfg.decisionInterval = 12f;
            cfg.profile = DecisionProfile.Deliberative;
            cfg.reasonMaxChars = 120;
            cfg.thinkingBudgetTokens = 0;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            cfg.maxVisionEntries = 0;
            cfg.maxMemoryEntries = 5;
            cfg.maxPromptChars = 3000;
            cfg.logDecisions = true;
            cfg.logPrompts = false;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static PerceptionConfig CreateGuardPerception(int occluderLayer)
        {
            PerceptionConfig cfg = LoadOrCreate<PerceptionConfig>(Data + "/Perception_Guard.asset");
            cfg.mode = VisionMode.Cone;
            cfg.range = 14f;
            cfg.fovAngle = 110f;
            cfg.perceptionLayers = ~0;
            cfg.occluderLayers = 1 << occluderLayer;
            cfg.scanInterval = 0.2f;
            cfg.triggerInterruptOnNewObject = true;
            cfg.visionTopicName = "What you can see";
            cfg.memoryTopicName = "What you just did";
            cfg.memoryCapacity = 3;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static PerceptionConfig CreatePrisonerPerception()
        {
            PerceptionConfig cfg = LoadOrCreate<PerceptionConfig>(Data + "/Perception_Prisoner.asset");
            cfg.mode = VisionMode.Sphere;
            cfg.range = 8f;
            cfg.fovAngle = 360f;
            cfg.perceptionLayers = ~0;
            cfg.occluderLayers = 0;
            cfg.scanInterval = 0.3f;
            cfg.triggerInterruptOnNewObject = true;
            cfg.visionTopicName = "What you can see";
            cfg.memoryTopicName = "What you just did";
            cfg.memoryCapacity = 2;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static PerceptionConfig CreateWardenPerception()
        {
            PerceptionConfig cfg = LoadOrCreate<PerceptionConfig>(Data + "/Perception_Warden.asset");
            cfg.memoryTopicName = "Your recent orders";
            cfg.memoryCapacity = 5;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static BehaviorLLMServerConfig CreateServerConfig()
        {
            BehaviorLLMServerConfig cfg = LoadOrCreate<BehaviorLLMServerConfig>(Data + "/Server_PrisonYard.asset");
            cfg.autoStartOnAwake = true;
            // Eight lanes, one per decision maker, so nobody's cached prompt is evicted by anyone
            // else's request.
            cfg.parallelSlots = 8;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static BehaviorLLMModelConfig CreateModelConfig()
        {
            BehaviorLLMModelConfig cfg = LoadOrCreate<BehaviorLLMModelConfig>(Data + "/Model_PrisonYard.asset");
            BehaviorLLMModelConfig shipped = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(ShippedModelAsset);

            if (shipped != null)
            {
                EditorUtility.CopySerialized(shipped, cfg);
            }
            cfg.displayName = "Granite 4.1 3B (PrisonYard, 8 slots)";
            // The context window is divided across the lanes, so eight of them need eight times
            // what one decision maker uses.
            cfg.contextSize = 16384;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        // ================================================================ helpers

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Paint(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            renderer.sharedMaterial = mat;
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

            Debug.LogWarning($"[PrisonYard] No free layer for '{name}'; line-of-sight blocking is disabled.");
            return 0;
        }
    }
}
