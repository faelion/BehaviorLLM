using System.Collections.Generic;
using System.IO;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Telemetry;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Samples.Dodgeball.Editor
{
    /// <summary>
    /// Builds the Dodgeball scene from flat shapes: a court seen from above, two discs and three
    /// balls. Run it from BehaviorLLM > Samples > Build Dodgeball Scene.
    /// </summary>
    public static class DodgeballSceneBuilder
    {
        private static string ScenePath => DodgeballPaths.Scenes + "/Dodgeball.unity";
        private static string RulesPath => DodgeballPaths.Data + "/DodgeballRules.asset";
        private static string ActionConfigPath => DodgeballPaths.Data + "/DodgeballActions.asset";
        private static string DecisionConfigPath => DodgeballPaths.Data + "/DodgeballDecisions.asset";
        private static string ServerConfigPath => DodgeballPaths.Data + "/DodgeballServer.asset";

        /// <summary>Shipped model preset this sample wires up, found by name so it survives the
        /// package being installed anywhere.</summary>
        private const string ModelConfigAsset = "Model_LiquidAI-d1-3B-Q4_K_M";

        private static readonly Color HumanColor = new Color(0.25f, 0.55f, 1f);
        private static readonly Color MachineColor = new Color(1f, 0.32f, 0.28f);

        [MenuItem("BehaviorLLM/Samples/Build Dodgeball Scene")]
        public static void BuildScene()
        {
            EnsureFolder(DodgeballPaths.Data);
            EnsureFolder(DodgeballPaths.Scenes);
            DodgeballRules rules = CreateRules();
            ActionConfig actions = CreateActionConfig();
            DecisionMakerConfig decisions = CreateDecisionConfig();
            BehaviorLLMServerConfig server = CreateServerConfig();
            BehaviorLLMModelConfig model = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(ModelConfigAsset);

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Dodgeball";

            Camera camera = CreateCameraAndLight(rules);
            CreateCourt(rules);

            Transform human = Disc(DodgeballIds.HumanName, rules, HumanColor);
            Transform machine = Disc(DodgeballIds.MachineName, rules, MachineColor);
            Transform humanRing = Ring("HumanCatch", rules);
            Transform machineRing = Ring("MachineCatch", rules);
            List<Transform> balls = new List<Transform>();
            for (int i = 0; i < rules.ballCount; i++) balls.Add(Ball(i, rules));

            GameObject llm = new GameObject("LLM");
            BehaviorLLMServer llmServer = llm.AddComponent<BehaviorLLMServer>();
            BehaviorLLMClient client = llm.AddComponent<BehaviorLLMClient>();
            llmServer.ApplyConfig(server, model);
            client.ApplyConfig(server, model);
            SetObject(client, "server", llmServer);
            llm.AddComponent<DecisionTelemetryRecorder>();

            GameObject matchGo = new GameObject("Match");
            DodgeballMatch match = matchGo.AddComponent<DodgeballMatch>();

            DodgeballMachinePad pad = machine.gameObject.AddComponent<DodgeballMachinePad>();
            pad.match = match;
            pad.playerIndex = 1;
            CourtObservationModule eyes = machine.gameObject.AddComponent<CourtObservationModule>();
            eyes.match = match;
            eyes.playerIndex = 1;
            DecisionMaker maker = CreateMachineBrain(machine.gameObject, actions, decisions, pad, client);

            match.rules = rules;
            match.machine = maker;
            match.machinePad = pad;
            match.humanView = human;
            match.machineView = machine;
            match.humanCatchRing = humanRing;
            match.machineCatchRing = machineRing;
            match.ballViews = balls.ToArray();
            match.view = camera;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Dodgeball] Built {ScenePath}. Press Play: you are blue on the left, the machine is red on the right.");
        }

        // ---------------------------------------------------------------- config assets

        private static DodgeballRules CreateRules()
        {
            // Created once and then left alone: a project that retuned its rules keeps them when
            // the scene is rebuilt.
            DodgeballRules rules = AssetDatabase.LoadAssetAtPath<DodgeballRules>(RulesPath);
            if (rules != null) return rules;
            rules = ScriptableObject.CreateInstance<DodgeballRules>();
            AssetDatabase.CreateAsset(rules, RulesPath);
            return rules;
        }

        private static ActionConfig CreateActionConfig()
        {
            ActionConfig config = AssetDatabase.LoadAssetAtPath<ActionConfig>(ActionConfigPath);
            bool isNew = config == null;
            if (isNew) config = ScriptableObject.CreateInstance<ActionConfig>();

            config.validActions = new List<ActionDefinition>
            {
                Action(DodgeballIds.Move, "Hold a direction until your next input. Forward is toward the center line, Up toward the top wall.",
                       DodgeballIds.DirectionParameter, DodgeballIds.MoveDirections),
                Action(DodgeballIds.Stand, "Let go of the stick and stand still.", null, null),
                Action(DodgeballIds.Throw, "Throw your ball: at the opponent, or ahead of them toward the top or the bottom wall.",
                       DodgeballIds.AimParameter, DodgeballIds.Aims),
                Action(DodgeballIds.Catch, "Open a catch for a moment. A ball that arrives while it is open is caught and you score.",
                       null, null),
                Action(DodgeballIds.Dodge, "A quick burst a couple of metres up, down or back.",
                       DodgeballIds.DirectionParameter, DodgeballIds.DodgeDirections)
            };

            // Tactics the game cannot enforce. What the game can enforce (no throwing without a
            // ball or in front of the throwing line, no catching while holding one, no dodging
            // while it recovers) is enforced by the pad's availability instead, because a written
            // rule is followed unreliably.
            //
            // The word "back" is kept out on purpose. Replayed on recorded states, a guide that
            // said "move back first" (to get behind the throwing line) made LiquidAI d1 answer
            // Move Back to 26 of 27 balls about to hit it; without the word it caught or dodged
            // 26 of 27, and went for the nearest free ball 83% of the time instead of 41%.
            config.modelInstructions =
                "Each turn you press one input. A direction stays held until your next input.\n" +
                "If a ball will hit you, dodge up or down out of its line, or catch it if it arrives very soon.\n" +
                "If you have no ball, run to the nearest free ball on your side.\n" +
                "Throws only count from behind your throwing line, away from the center.\n" +
                "If you can throw, throw.";

            if (isNew) AssetDatabase.CreateAsset(config, ActionConfigPath);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static ActionDefinition Action(string name, string description, string parameter, string[] values)
        {
            ActionDefinition def = new ActionDefinition { actionName = name, description = description };
            if (parameter != null)
            {
                def.parameters.Add(new ActionParameter
                {
                    name = parameter,
                    type = ActionParameterType.String,
                    exampleValue = values[0],
                    allowedValues = new List<string>(values)
                });
            }
            return def;
        }

        private static DecisionMakerConfig CreateDecisionConfig()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(DecisionConfigPath);
            // Continuous control: the next decision starts as soon as the last one has landed, so the
            // machine presses its pad as often as the model can answer (about eight times a second
            // with LiquidAI d1 on a mid-range GPU).
            cfg.decisionInterval = 0f;
            cfg.decisionIntervalJitter = 0f;
            cfg.profile = DecisionProfile.Reactive;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            // The court text says everything the machine needs about the present; a memory of its
            // own last inputs would only invite it to repeat them, which decision models do.
            cfg.maxMemoryEntries = 0;
            cfg.maxVisionEntries = 0;
            cfg.logDecisions = false;
            cfg.logPrompts = false;
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        private static BehaviorLLMServerConfig CreateServerConfig()
        {
            BehaviorLLMServerConfig cfg = LoadOrCreate<BehaviorLLMServerConfig>(ServerConfigPath);
            cfg.autoStartOnAwake = true;
            // One slot for one decision maker. A spare would take the decision that arrives while a
            // cancelled one is still finishing, and from then on hold a longer copy of the machine's
            // prompt than the slot the server keeps its persona in, so every later decision would
            // go there and reread everything. With one slot, a decision after an interrupt waits
            // for the cancelled one instead, which costs less than rereading.
            cfg.parallelSlots = 1;
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

        // ---------------------------------------------------------------- machine

        private static DecisionMaker CreateMachineBrain(GameObject go, ActionConfig actions, DecisionMakerConfig cfg,
                                                        DodgeballMachinePad pad, BehaviorLLMClient client)
        {
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionConfig = actions;
            maker.systemPersona =
                "You are the machine: the red player on the right half of a dodgeball court, playing one on one " +
                "against a human on the left half. You want to win: hit the human with a ball before they hit you, " +
                "and never stay in the path of a throw.";
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
            Bind(maker, DodgeballIds.Move, pad.OnMove);
            Bind(maker, DodgeballIds.Stand, pad.OnStand);
            Bind(maker, DodgeballIds.Throw, pad.OnThrow);
            Bind(maker, DodgeballIds.Catch, pad.OnCatch);
            Bind(maker, DodgeballIds.Dodge, pad.OnDodge);

            SerializedObject so = new SerializedObject(maker);
            so.FindProperty("config").objectReferenceValue = cfg;
            so.FindProperty("defaultBackendComponent").objectReferenceValue = client;
            so.FindProperty("actionAvailabilityProviderComponent").objectReferenceValue = pad;
            so.FindProperty("requestSlot").intValue = 0;
            so.FindProperty("fallbackAction").FindPropertyRelative("fallbackActionName").stringValue = DodgeballIds.Stand;
            so.ApplyModifiedPropertiesWithoutUndo();
            return maker;
        }

        private static void Bind(DecisionMaker maker, string actionName, UnityAction<ActionArguments> callback)
        {
            ActionEvent evt = new ActionEvent();
            UnityEventTools.AddPersistentListener(evt, callback);
            maker.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = actionName, onExecute = evt });
        }

        // ---------------------------------------------------------------- scene pieces

        private static Camera CreateCameraAndLight(DodgeballRules rules)
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 20f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Camera camera = cam.GetComponent<Camera>();
            camera.orthographic = true;
            // The court's width plus room for the score above it and the captions below.
            camera.orthographicSize = rules.HalfWidth + 1.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.1f, 0.12f);

            GameObject light = new GameObject("Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
            return camera;
        }

        private static void CreateCourt(DodgeballRules rules)
        {
            GameObject court = new GameObject("Court");
            float l = rules.courtLength, w = rules.courtWidth;
            Block("HumanHalf", court.transform, new Vector3(-l / 4f, -0.06f, 0f), new Vector3(l / 2f, 0.1f, w), new Color(0.16f, 0.2f, 0.3f));
            Block("MachineHalf", court.transform, new Vector3(l / 4f, -0.06f, 0f), new Vector3(l / 2f, 0.1f, w), new Color(0.3f, 0.17f, 0.17f));
            Block("CenterLine", court.transform, new Vector3(0f, 0f, 0f), new Vector3(0.08f, 0.02f, w), Color.white);
            // The throwing lines, dashed so they do not read as walls.
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = -w / 2f + 0.3f; z < w / 2f; z += 0.6f)
                    Block(side < 0 ? "HumanThrowingLine" : "MachineThrowingLine", court.transform,
                          new Vector3(side * rules.throwingLine, 0f, z), new Vector3(0.06f, 0.02f, 0.3f), new Color(0.85f, 0.85f, 0.85f));
            }
            Color wall = new Color(0.75f, 0.75f, 0.78f);
            Block("TopWall", court.transform, new Vector3(0f, 0f, w / 2f + 0.06f), new Vector3(l + 0.24f, 0.1f, 0.12f), wall);
            Block("BottomWall", court.transform, new Vector3(0f, 0f, -w / 2f - 0.06f), new Vector3(l + 0.24f, 0.1f, 0.12f), wall);
            Block("HumanWall", court.transform, new Vector3(-l / 2f - 0.06f, 0f, 0f), new Vector3(0.12f, 0.1f, w), wall);
            Block("MachineWall", court.transform, new Vector3(l / 2f + 0.06f, 0f, 0f), new Vector3(0.12f, 0.1f, w), wall);
        }

        private static Transform Disc(string name, DodgeballRules rules, Color color)
        {
            GameObject go = Shape(PrimitiveType.Cylinder, name, null, Vector3.zero,
                                  new Vector3(rules.playerRadius * 2f, 0.05f, rules.playerRadius * 2f), color);
            // A notch on the side that faces the center line, so each disc reads as a player.
            float forward = name == DodgeballIds.HumanName ? 1f : -1f;
            Shape(PrimitiveType.Cube, "Front", go.transform, new Vector3(forward * 0.32f, 1.2f, 0f),
                  new Vector3(0.25f, 0.6f, 0.4f), Color.Lerp(color, Color.white, 0.6f));
            return go.transform;
        }

        private static Transform Ring(string name, DodgeballRules rules)
        {
            float d = (rules.playerRadius + rules.ballRadius) * 2.4f;
            GameObject go = Shape(PrimitiveType.Cylinder, name, null, Vector3.zero, new Vector3(d, 0.01f, d), new Color(1f, 0.85f, 0.2f));
            go.SetActive(false);
            return go.transform;
        }

        private static Transform Ball(int index, DodgeballRules rules)
        {
            float d = rules.ballRadius * 2f;
            return Shape(PrimitiveType.Sphere, "Ball_" + (char)('A' + index), null, Vector3.up * 0.25f,
                         new Vector3(d, d, d), new Color(1f, 0.95f, 0.75f)).transform;
        }

        private static void Block(string name, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            Shape(PrimitiveType.Cube, name, parent, position, scale, color);
        }

        private static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Flat(color);
            return go;
        }

        // Unlit so the court reads as flat colour from above, whichever render pipeline the project uses.
        private static Material Flat(Color color)
        {
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")
                : Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static void SetObject(Object target, string property, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
