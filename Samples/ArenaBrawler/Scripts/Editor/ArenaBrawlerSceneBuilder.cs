using System.Collections.Generic;
using System.IO;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Backend;
using BehaviorLLM.Core.Config;
using BehaviorLLM.Core.Decisions;
using BehaviorLLM.Core.Telemetry;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace Project.Samples.ArenaBrawler.Editor
{
    /// <summary>
    /// Builds the Arena Brawler scene: a round arena, two fighters and the machine's brain. Run it
    /// from BehaviorLLM > Samples > Build Arena Brawler Scene. With the sample's art present the
    /// fighters are Quaternius' mannequin with motion-captured animations; without it they are
    /// primitive stand-ins and the fight plays the same.
    /// </summary>
    public static class ArenaBrawlerSceneBuilder
    {
        private static string ScenePath => ArenaBrawlerPaths.Scenes + "/ArenaBrawler.unity";
        private static string RulesPath => ArenaBrawlerPaths.Data + "/BrawlerRules.asset";
        private static string ActionConfigPath => ArenaBrawlerPaths.Data + "/BrawlerActions.asset";
        private static string DecisionConfigPath => ArenaBrawlerPaths.Data + "/BrawlerDecisions.asset";
        private static string ServerConfigPath => ArenaBrawlerPaths.Data + "/BrawlerServer.asset";
        private static string ControllerPath => ArenaBrawlerPaths.Data + "/Fighter.controller";
        private static string CharacterPath => ArenaBrawlerPaths.Characters + "/Mannequin_F.fbx";

        private const string ModelConfigAsset = "Model_LiquidAI-d1-3B-Q4_K_M";

        private static readonly Color HumanColor = new Color(0.3f, 0.55f, 1f);
        private static readonly Color MachineColor = new Color(1f, 0.32f, 0.26f);

        [MenuItem("BehaviorLLM/Samples/Build Arena Brawler Scene")]
        public static void BuildScene()
        {
            BehaviorLLM.Editor.SampleAssetPaths.PrepareArt(ArenaBrawlerPaths.Root, "ArenaBrawler");
            EnsureFolder(ArenaBrawlerPaths.Data);
            EnsureFolder(ArenaBrawlerPaths.Scenes);
            BrawlerRules rules = CreateRules();
            ActionConfig actions = CreateActionConfig();
            DecisionMakerConfig decisions = CreateDecisionConfig();
            BehaviorLLMServerConfig server = CreateServerConfig();
            BehaviorLLMModelConfig model = BehaviorLLMDefaults.FindShipped<BehaviorLLMModelConfig>(ModelConfigAsset);

            GameObject character = PrepareCharacter();
            AnimatorController controller = character != null ? CreateController() : null;

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ArenaBrawler";

            Camera camera = CreateCameraAndLights();
            CreateArena(rules);

            BrawlerFighterView human = Fighter(BrawlerIds.HumanName, HumanColor, character, controller, new Vector3(-2.5f, 0f, 0f));
            BrawlerFighterView machine = Fighter(BrawlerIds.MachineName, MachineColor, character, controller, new Vector3(2.5f, 0f, 0f));

            // Assigned through ApplyConfig rather than through serialized properties: the
            // components' own setter is the one path guaranteed to store the reference.
            GameObject llm = new GameObject("LLM");
            BehaviorLLMServer llmServer = llm.AddComponent<BehaviorLLMServer>();
            BehaviorLLMClient client = llm.AddComponent<BehaviorLLMClient>();
            llmServer.ApplyConfig(server, model);
            client.ApplyConfig(server, model);
            SetObject(client, "server", llmServer);
            llm.AddComponent<DecisionTelemetryRecorder>();

            GameObject matchGo = new GameObject("Match");
            BrawlerMatch match = matchGo.AddComponent<BrawlerMatch>();

            BrawlerMachinePad pad = machine.gameObject.AddComponent<BrawlerMachinePad>();
            pad.match = match;
            pad.fighterIndex = 1;
            FightObservationModule eyes = machine.gameObject.AddComponent<FightObservationModule>();
            eyes.match = match;
            eyes.fighterIndex = 1;
            DecisionMaker maker = CreateMachineBrain(machine.gameObject, actions, decisions, pad, client);

            match.rules = rules;
            match.machine = maker;
            match.machinePad = pad;
            match.humanView = human;
            match.machineView = machine;
            match.view = camera;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArenaBrawler] Built {ScenePath}" + (character == null ? " with primitive stand-ins (the character art is missing)." : ".") +
                      " Press Play: you are blue, the machine is red.");
        }

        // ---------------------------------------------------------------- config assets

        private static BrawlerRules CreateRules()
        {
            // Created once and then left alone, so a retuned fight survives a rebuild.
            BrawlerRules rules = AssetDatabase.LoadAssetAtPath<BrawlerRules>(RulesPath);
            if (rules != null) return rules;
            rules = ScriptableObject.CreateInstance<BrawlerRules>();
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
                Action(BrawlerIds.Move, "Hold a direction, relative to the opponent, until your next input.", BrawlerIds.Directions),
                Action(BrawlerIds.Stand, "Let go of everything.", null),
                Action(BrawlerIds.Jab, "A fast, short punch.", null),
                Action(BrawlerIds.Hook, "A slow, long punch that breaks a guard.", null),
                Action(BrawlerIds.Block, "Hold a guard until your next input.", null),
                Action(BrawlerIds.Evade, "Roll out of the line of a punch, untouchable for most of the roll.", null)
            };

            // Fighting sense the game cannot enforce, one line per button, each button named once.
            // A decision model leans toward the options the text names most and toward options
            // that share a word with the situation, so the guide names every button exactly once
            // and calls the opponent's punches by other names than the fighter's own (a "slow
            // swing" is a hook, a "quick strike" a jab). Replayed on recorded fight states this
            // took LiquidAI d1 from 0.55 to 0.70 of choices that fit the situation.
            config.modelInstructions =
                "Evade when a slow swing is coming and you are inside its reach.\n" +
                "Block when a quick strike is coming and you are inside its reach.\n" +
                "Hook when the opponent is recovering or blocking.\n" +
                "Jab when your jab reaches and the opponent is standing.\n" +
                "Move Toward when nothing reaches.";

            if (isNew) AssetDatabase.CreateAsset(config, ActionConfigPath);
            EditorUtility.SetDirty(config);
            return config;
        }

        private static ActionDefinition Action(string name, string description, string[] directions)
        {
            ActionDefinition def = new ActionDefinition { actionName = name, description = description };
            if (directions != null)
            {
                def.parameters.Add(new ActionParameter
                {
                    name = BrawlerIds.DirectionParameter,
                    type = ActionParameterType.String,
                    exampleValue = directions[0],
                    allowedValues = new List<string>(directions)
                });
            }
            return def;
        }

        private static DecisionMakerConfig CreateDecisionConfig()
        {
            DecisionMakerConfig cfg = LoadOrCreate<DecisionMakerConfig>(DecisionConfigPath);
            // Continuous control: the next decision starts as soon as the last one has landed.
            cfg.decisionInterval = 0f;
            cfg.decisionIntervalJitter = 0f;
            cfg.profile = DecisionProfile.Reactive;
            cfg.useStructuredOutput = true;
            cfg.includeExamplesInPrompt = true;
            // The fight text says everything about the present; a memory of its own last inputs
            // would only invite the model to repeat them.
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
            // One slot for one decision maker, so the server always resumes the machine's prompt
            // from the point it keeps for it; a spare slot ends up holding a copy that wins every
            // later decision and has none (see DodgeballSceneBuilder, where it was measured).
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

        // ---------------------------------------------------------------- art

        /// <summary>
        /// Makes the mannequin a Humanoid, so Quaternius' clips (cut for the same rig) play on it,
        /// and returns it; null when the art is not there.
        /// </summary>
        private static GameObject PrepareCharacter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(CharacterPath) as ModelImporter;
            if (importer == null) return null;
            if (importer.animationType != ModelImporterAnimationType.Human || importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        }

        private static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAssetAtPath<AnimationClip>(ArenaBrawlerPaths.Animations + "/" + name + ".anim");

        /// <summary>
        /// One state per thing a fighter can be doing, each playing at the speed the view sets in
        /// "Speed", so a clip lasts exactly as long as the move it shows. There are no transitions:
        /// the view cross-fades to whichever state the fight says, which keeps the controller a
        /// list rather than a graph to maintain.
        /// </summary>
        private static AnimatorController CreateController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            c.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            AnimatorStateMachine sm = c.layers[0].stateMachine;

            void State(string name, string clip, bool isDefault = false)
            {
                AnimatorState s = sm.AddState(name);
                s.motion = Clip(clip);
                s.speedParameterActive = true;
                s.speedParameter = "Speed";
                if (isDefault) sm.defaultState = s;
            }

            State(BrawlerFighterView.Idle, "Punch_Enter", true);
            State(BrawlerFighterView.Run, "Jog_Fwd_Loop");
            State(BrawlerFighterView.Walk, "Walk_Loop");
            State(BrawlerFighterView.Jab, "Punch_Jab");
            State(BrawlerFighterView.Hook, "Melee_Hook");
            State(BrawlerFighterView.HookRecovery, "Melee_Hook_Rec");
            State(BrawlerFighterView.Block, "Idle_Shield_Loop");
            State(BrawlerFighterView.Roll, "Roll");
            State(BrawlerFighterView.Hit, "Hit_Head");
            State(BrawlerFighterView.HitHeavy, "Hit_Knockback");
            State(BrawlerFighterView.GuardBreak, "Idle_Shield_Break");
            State(BrawlerFighterView.KO, "Death01");
            State(BrawlerFighterView.Win, "Dance_Loop");
            EditorUtility.SetDirty(c);
            return c;
        }

        // ---------------------------------------------------------------- scene pieces

        private static BrawlerFighterView Fighter(string name, Color color, GameObject character, AnimatorController controller, Vector3 position)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(position.x < 0f ? Vector3.right : Vector3.left);
            BrawlerFighterView view = root.AddComponent<BrawlerFighterView>();
            view.color = color;

            if (character != null)
            {
                GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(character, root.transform);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                Animator animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.animator = animator;
                view.tinted = body.GetComponentsInChildren<Renderer>();
                view.jabClip = Length("Punch_Jab", view.jabClip);
                view.hookClip = Length("Melee_Hook", view.hookClip);
                view.hookRecoveryClip = Length("Melee_Hook_Rec", view.hookRecoveryClip);
                view.rollClip = Length("Roll", view.rollClip);
            }
            else
            {
                List<Renderer> parts = new List<Renderer>();
                parts.Add(Shape(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), color, true));
                parts.Add(Shape(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.95f, 0f), Vector3.one * 0.45f, color, true));
                Renderer fist = Shape(PrimitiveType.Cube, "Fist", root.transform, new Vector3(0.15f, 1.2f, 0.35f), Vector3.one * 0.22f, Color.Lerp(color, Color.white, 0.5f), true);
                Renderer guard = Shape(PrimitiveType.Cube, "Guard", root.transform, new Vector3(0f, 1.35f, 0.4f), new Vector3(0.6f, 0.6f, 0.08f), new Color(0.35f, 0.75f, 1f), true);
                guard.gameObject.SetActive(false);
                view.fist = fist.transform;
                view.guard = guard.gameObject;
                view.tinted = parts.ToArray();
            }

            Renderer ring = Shape(PrimitiveType.Cylinder, "Ring", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(1.3f, 0.005f, 1.3f), Color.white, false);
            ring.enabled = false;
            view.ring = ring;
            return view;
        }

        private static float Length(string clip, float fallback)
        {
            AnimationClip c = Clip(clip);
            return c != null ? c.length : fallback;
        }

        private static Camera CreateCameraAndLights()
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 6.2f, -8.2f);
            cam.transform.LookAt(new Vector3(0f, 0.7f, 0.6f));
            Camera camera = cam.GetComponent<Camera>();
            camera.fieldOfView = 46f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f);

            GameObject sun = new GameObject("Key Light", typeof(Light));
            Light key = sun.GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.2f;
            key.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            GameObject fill = new GameObject("Fill Light", typeof(Light));
            Light f = fill.GetComponent<Light>();
            f.type = LightType.Directional;
            f.intensity = 0.35f;
            f.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            return camera;
        }

        private static void CreateArena(BrawlerRules rules)
        {
            GameObject arena = new GameObject("Arena");
            float d = rules.arenaRadius * 2f;
            Shape(PrimitiveType.Cylinder, "Rim", arena.transform, new Vector3(0f, -0.12f, 0f), new Vector3(d + 0.5f, 0.1f, d + 0.5f), new Color(0.75f, 0.55f, 0.2f), true);
            Shape(PrimitiveType.Cylinder, "Floor", arena.transform, new Vector3(0f, -0.05f, 0f), new Vector3(d, 0.1f, d), new Color(0.2f, 0.22f, 0.27f), true);
            Shape(PrimitiveType.Cylinder, "CenterMark", arena.transform, new Vector3(0f, 0.005f, 0f), new Vector3(1.6f, 0.002f, 1.6f), new Color(0.28f, 0.3f, 0.36f), true);
        }

        private static Renderer Shape(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Color color, bool lit)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = Material(color, lit);
            if (!lit) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        private static Material Material(Color color, bool lit)
        {
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader shader = lit
                ? (urp ? Shader.Find("Universal Render Pipeline/Lit") : null) ?? Shader.Find("Standard")
                : (urp ? Shader.Find("Universal Render Pipeline/Unlit") : null) ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        // ---------------------------------------------------------------- machine

        private static DecisionMaker CreateMachineBrain(GameObject go, ActionConfig actions, DecisionMakerConfig cfg,
                                                        BrawlerMachinePad pad, BehaviorLLMClient client)
        {
            DecisionMaker maker = go.AddComponent<DecisionMaker>();
            maker.actionConfig = actions;
            maker.systemPersona =
                "You are the machine: the red fighter in a one-on-one bare-knuckle bout against a human, in a small " +
                "round arena. You fight to win: land your punches, avoid theirs, and never stand still inside their reach.";
            maker.actionBindings = new List<DecisionMaker.ActionBinding>();
            Bind(maker, BrawlerIds.Move, pad.OnMove);
            Bind(maker, BrawlerIds.Stand, pad.OnStand);
            Bind(maker, BrawlerIds.Jab, pad.OnJab);
            Bind(maker, BrawlerIds.Hook, pad.OnHook);
            Bind(maker, BrawlerIds.Block, pad.OnBlock);
            Bind(maker, BrawlerIds.Evade, pad.OnEvade);

            SerializedObject so = new SerializedObject(maker);
            so.FindProperty("config").objectReferenceValue = cfg;
            so.FindProperty("defaultBackendComponent").objectReferenceValue = client;
            so.FindProperty("actionAvailabilityProviderComponent").objectReferenceValue = pad;
            so.FindProperty("requestSlot").intValue = 0;
            so.FindProperty("fallbackAction").FindPropertyRelative("fallbackActionName").stringValue = BrawlerIds.Block;
            so.ApplyModifiedPropertiesWithoutUndo();
            return maker;
        }

        private static void Bind(DecisionMaker maker, string actionName, UnityAction<ActionArguments> callback)
        {
            ActionEvent evt = new ActionEvent();
            UnityEventTools.AddPersistentListener(evt, callback);
            maker.actionBindings.Add(new DecisionMaker.ActionBinding { actionName = actionName, onExecute = evt });
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
