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
            if (character != null) FaceClipsForward();
            AnimatorController controller = character != null ? CreateController() : null;
            BrawlerIcons.Set icons = BrawlerIcons.Build();

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ArenaBrawler";

            Camera camera = CreateCameraAndLights(out Light keyLight, out Light spotlight);
            Material floor = FloorMaterial(rules);
            CreateArena(rules, floor);
            Transform crowd = CreateCrowd(rules);

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
            match.client = client;
            match.humanView = human;
            match.machineView = machine;
            match.view = camera;

            GameObject fxGo = new GameObject("Fx");
            BrawlerFx fx = fxGo.AddComponent<BrawlerFx>();
            fx.match = match;
            fx.view = camera;
            fx.keyLight = keyLight;
            fx.spotlight = spotlight;
            fx.crowd = crowd;
            fx.sparks = Particles("Sparks", fxGo.transform, icons.Particle, 0.9f, 0.18f, false);
            fx.confetti = Particles("Confetti", fxGo.transform, ConfettiMaterial(), 0.06f, 0.05f, true);

            GameObject hudGo = new GameObject("HUD");
            BrawlerHud hud = hudGo.AddComponent<BrawlerHud>();
            hud.match = match;
            hud.view = camera;
            hud.humanColor = HumanColor;
            hud.machineColor = MachineColor;
            hud.fist = icons.Fist;
            hud.hook = icons.Hook;
            hud.shield = icons.Shield;
            hud.roll = icons.Roll;
            hud.arrow = icons.Arrow;
            hud.stand = icons.Stand;
            hud.burst = icons.Burst;
            hud.star = icons.Star;
            hud.segment = icons.Segment;
            hud.plate = icons.Plate;
            hud.bar = icons.Bar;
            hud.bubble = icons.Bubble;
            hud.key = icons.Key;
            hud.mouseLeft = icons.MouseLeft;
            hud.mouseRight = icons.MouseRight;
            hud.restart = icons.Restart;
            hud.move = icons.Move;
            hud.clock = icons.Clock;
            hud.disc = icons.Disc;
            hud.ring = icons.Ring;

            // A vignette in front of the camera, framed with it.
            GameObject vignette = new GameObject("Vignette", typeof(SpriteRenderer));
            vignette.transform.SetParent(camera.transform, false);
            vignette.transform.localPosition = new Vector3(0f, 0f, camera.nearClipPlane + 0.05f);
            float h = 2f * (camera.nearClipPlane + 0.05f) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            vignette.transform.localScale = new Vector3(h * 16f / 9f * 1.05f, h * 1.05f, 1f);
            SpriteRenderer vr = vignette.GetComponent<SpriteRenderer>();
            vr.sprite = icons.Vignette;
            vr.sharedMaterial = icons.Particle;
            vr.sortingOrder = 100;

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
            // Against the autopilot the machine won 3 matches of 3 with Cancel Request and lost 2 of
            // 2 with Discard Answer, although Discard answers a wind-up about 160 ms sooner.
            cfg.decisionModelInterrupt = DecisionModelInterrupt.CancelRequest;
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

        /// <summary>
        /// Bakes every clip's root rotation relative to the body's orientation rather than to the
        /// orientation the clip was recorded in. Quaternius' clips were recorded facing different
        /// ways, so with "original" orientation a fighter turned away from the opponent in some
        /// moves while the code had turned the fighter toward them.
        /// </summary>
        /// <summary>
        /// Clips whose hips stay turned even relative to the body's orientation, and by how much
        /// to turn them back, in degrees. Measured in play from the hip bones against the
        /// fighter's forward: the jog ran 31 degrees turned away from where the fighter faced, and
        /// the hook's recovery settled 30 degrees off before returning to guard.
        /// </summary>
        private static readonly Dictionary<string, float> ClipYawOffsets = new Dictionary<string, float>
        {
            { "Jog_Fwd_Loop", 31f },
            { "Melee_Hook_Rec", 30f },
        };

        private static void FaceClipsForward()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { ArenaBrawlerPaths.Animations });
            for (int i = 0; i < guids.Length; i++)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (clip == null) continue;
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                float offset = ClipYawOffsets.TryGetValue(clip.name, out float o) ? o : 0f;
                if (!settings.keepOriginalOrientation && settings.loopBlendOrientation && Mathf.Approximately(settings.orientationOffsetY, offset)) continue;
                settings.keepOriginalOrientation = false;
                settings.loopBlendOrientation = true;
                settings.orientationOffsetY = offset;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
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

            // The relaxed idle and the fists-up guard are told apart on purpose: a raised guard is
            // the block, and it has to read as one from across the arena.
            State(BrawlerFighterView.Idle, "Idle_Loop", true);
            State(BrawlerFighterView.Run, "Jog_Fwd_Loop");
            State(BrawlerFighterView.Walk, "Walk_Loop");
            State(BrawlerFighterView.Jab, "Punch_Jab");
            State(BrawlerFighterView.Hook, "Melee_Hook");
            State(BrawlerFighterView.HookRecovery, "Melee_Hook_Rec");
            State(BrawlerFighterView.Block, "Punch_Enter");
            State(BrawlerFighterView.Roll, "Roll");
            State(BrawlerFighterView.Hit, "Hit_Head");
            // A heavy hit reels rather than knocks down: the stun is half a second, and a fighter
            // who hit the floor would have to spring back up from it.
            State(BrawlerFighterView.HitHeavy, "Hit_Chest");
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
                view.hitClip = Length("Hit_Head", view.hitClip);
                view.heavyHitClip = Length("Hit_Chest", view.heavyHitClip);
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

        private static Camera CreateCameraAndLights(out Light keyLight, out Light spotlight)
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 6.2f, -8.2f);
            cam.transform.LookAt(new Vector3(0f, 0.7f, 0.6f));
            Camera camera = cam.GetComponent<Camera>();
            camera.fieldOfView = 46f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.05f);

            // A dark hall: the light falls on the ring, and the stands fade into the fog.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.fogColor = new Color(0.025f, 0.03f, 0.05f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.22f);

            GameObject sun = new GameObject("Key Light", typeof(Light));
            keyLight = sun.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.25f;
            keyLight.color = new Color(1f, 0.95f, 0.86f);
            keyLight.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(62f, -24f, 0f);

            // Each corner lit in its fighter's colour from behind, so the silhouettes separate.
            Spot("Blue Rim", new Vector3(-7f, 6f, 5f), new Vector3(1.5f, 0f, -1f), HumanColor, 30f, 18f);
            Spot("Red Rim", new Vector3(7f, 6f, 5f), new Vector3(-1.5f, 0f, -1f), MachineColor, 30f, 18f);

            spotlight = Spot("Winner Spot", new Vector3(0f, 8f, -1f), Vector3.zero, Color.white, 0f, 24f);
            spotlight.shadows = LightShadows.Soft;
            return camera;
        }

        private static Light Spot(string name, Vector3 position, Vector3 target, Color color, float intensity, float angle)
        {
            GameObject go = new GameObject(name, typeof(Light));
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position);
            Light l = go.GetComponent<Light>();
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 25f;
            l.spotAngle = angle * 2f;
            l.innerSpotAngle = angle;
            l.shadows = LightShadows.None;
            return l;
        }

        private static void CreateArena(BrawlerRules rules, Material floor)
        {
            GameObject arena = new GameObject("Arena");
            float d = rules.arenaRadius * 2f;
            Shape(PrimitiveType.Cylinder, "Base", arena.transform, new Vector3(0f, -0.45f, 0f), new Vector3(d + 1.6f, 0.4f, d + 1.6f), new Color(0.09f, 0.09f, 0.11f), true);
            Shape(PrimitiveType.Cylinder, "Apron", arena.transform, new Vector3(0f, -0.14f, 0f), new Vector3(d + 0.9f, 0.1f, d + 0.9f), new Color(0.16f, 0.13f, 0.1f), true);
            Renderer mat = Shape(PrimitiveType.Cylinder, "Floor", arena.transform, new Vector3(0f, -0.05f, 0f), new Vector3(d, 0.1f, d), Color.white, true);
            mat.sharedMaterial = floor;
            Shape(PrimitiveType.Cylinder, "Glow", arena.transform, new Vector3(0f, -0.08f, 0f), new Vector3(d + 0.35f, 0.08f, d + 0.35f), new Color(1f, 0.7f, 0.3f), false)
                .sharedMaterial = Emissive(new Color(1f, 0.62f, 0.25f), 2.2f);

            // Posts round the edge with lanterns on top.
            const int Posts = 12;
            Material lantern = Emissive(new Color(1f, 0.75f, 0.4f), 4f);
            for (int i = 0; i < Posts; i++)
            {
                float a = (i + 0.5f) / Posts * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rules.arenaRadius + 0.55f);
                Shape(PrimitiveType.Cylinder, "Post", arena.transform, at + Vector3.up * 0.5f, new Vector3(0.14f, 0.6f, 0.14f), new Color(0.12f, 0.11f, 0.12f), true);
                Shape(PrimitiveType.Sphere, "Lantern", arena.transform, at + Vector3.up * 1.18f, Vector3.one * 0.2f, Color.white, false).sharedMaterial = lantern;
            }
        }

        /// <summary>Spectators on three tiers round the arena, in mixed colours, half lost in the dark.</summary>
        private static Transform CreateCrowd(BrawlerRules rules)
        {
            Transform crowd = new GameObject("Crowd").transform;
            Transform stands = new GameObject("Stands").transform;
            System.Random random = new System.Random(5);
            Material[] shirts = new Material[8];
            for (int i = 0; i < shirts.Length; i++)
            {
                Color c = i < 3 ? Color.Lerp(HumanColor, Color.black, i * 0.25f)
                        : i < 6 ? Color.Lerp(MachineColor, Color.black, (i - 3) * 0.25f)
                        : Color.Lerp(new Color(0.75f, 0.72f, 0.66f), Color.black, (i - 6) * 0.4f);
                shirts[i] = Material(c, true);
            }
            Material skin = Material(new Color(0.85f, 0.66f, 0.52f), true);
            for (int tier = 0; tier < 3; tier++)
            {
                float radius = rules.arenaRadius + 2.6f + tier * 1.1f;
                float height = tier * 0.55f;
                // A ring of bench blocks, each tier a step higher than the one in front of it.
                int benches = Mathf.RoundToInt(radius * Mathf.PI * 2f / 1.4f);
                for (int b = 0; b < benches; b++)
                {
                    float ba = (b + 0.5f) / benches * Mathf.PI * 2f;
                    if (Mathf.Sin(ba) < -0.6f) continue;
                    Vector3 at = new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * radius;
                    Renderer bench = Shape(PrimitiveType.Cube, "Bench", stands, at + Vector3.up * ((height - 0.3f) * 0.5f),
                                           new Vector3(1.45f, height + 0.3f, 1.05f), new Color(0.08f, 0.08f, 0.1f), true);
                    bench.transform.rotation = Quaternion.LookRotation(-at);
                    bench.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                int count = Mathf.RoundToInt(radius * Mathf.PI * 2f / 0.75f);
                for (int i = 0; i < count; i++)
                {
                    float a = (i + (float)random.NextDouble() * 0.4f) / count * Mathf.PI * 2f;
                    // Leave the camera's side open.
                    if (Mathf.Sin(a) < -0.55f) continue;
                    if (random.NextDouble() < 0.15) continue;
                    Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * height;
                    GameObject fan = new GameObject("Fan");
                    fan.transform.SetParent(crowd, false);
                    fan.transform.localPosition = at;
                    fan.transform.rotation = Quaternion.LookRotation(-new Vector3(at.x, 0f, at.z));
                    Renderer body = Shape(PrimitiveType.Capsule, "Body", fan.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.42f, 0.42f, 0.32f), Color.white, true);
                    body.sharedMaterial = shirts[random.Next(shirts.Length)];
                    Renderer head = Shape(PrimitiveType.Sphere, "Head", fan.transform, new Vector3(0f, 1.12f, 0f), Vector3.one * 0.26f, Color.white, true);
                    head.sharedMaterial = skin;
                    body.shadowCastingMode = head.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            return crowd;
        }

        /// <summary>
        /// The arena's mat: dark canvas with a worn grain, two rings, a star in the middle and each
        /// fighter's corner tinted in their colour, painted into a texture beside the sample's data.
        /// </summary>
        private static Material FloorMaterial(BrawlerRules rules)
        {
            const int N = 1024;
            Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            Color32[] px = new Color32[N * N];
            Color canvas = new Color(0.2f, 0.21f, 0.25f);
            for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float x = (i + 0.5f) / N * 2f - 1f, y = (j + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float grain = (Mathf.PerlinNoise(x * 40f, y * 40f) - 0.5f) * 0.06f + (Mathf.PerlinNoise(x * 6f + 3f, y * 6f) - 0.5f) * 0.08f;
                Color c = canvas * (1f + grain);
                // Corners in the fighters' colours: the human starts on the left (x < 0).
                float side = Mathf.Clamp01((Mathf.Abs(x) - 0.35f) * 3f) * Mathf.Clamp01((0.95f - r) * 8f);
                c = Color.Lerp(c, (x < 0f ? HumanColor : MachineColor) * 0.45f, side * 0.6f);
                c *= 1.15f - 0.35f * r * r;
                float line = Mathf.Max(Band(Mathf.Abs(r - 0.93f), 0.012f), Band(Mathf.Abs(r - 0.3f), 0.008f));
                line = Mathf.Max(line, Band(Mathf.Abs(x), 0.004f) * (r < 0.3f ? 0f : r < 0.93f ? 0.35f : 0f));
                float star = StarShape(x / 0.2f, y / 0.2f);
                c = Color.Lerp(c, new Color(0.95f, 0.78f, 0.4f), Mathf.Max(line * 0.85f, star * 0.8f));
                if (r > 1f) c = new Color(0.1f, 0.1f, 0.12f);
                c.a = 1f;
                px[j * N + i] = c;
            }
            t.SetPixels32(px);
            t.Apply();
            string folder = ArenaBrawlerPaths.Data + "/UI";
            string texPath = folder + "/ArenaFloor.png";
            File.WriteAllBytes(texPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string matPath = folder + "/ArenaFloor.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = Material(Color.white, true);
                AssetDatabase.CreateAsset(m, matPath);
            }
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static float Band(float d, float half) => Mathf.Clamp01((half - d) * 400f + 0.5f);

        private static float StarShape(float x, float y)
        {
            float a = Mathf.Atan2(y, x) + Mathf.PI * 0.5f;
            float k = Mathf.Cos(a * 5f) * 0.5f + 0.5f;
            float edge = Mathf.Lerp(0.45f, 1f, Mathf.Pow(k, 3f));
            return Mathf.Clamp01((edge - Mathf.Sqrt(x * x + y * y)) * 60f);
        }

        private static Material Emissive(Color color, float intensity)
        {
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            Shader shader = (urp ? Shader.Find("Universal Render Pipeline/Unlit") : null) ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            Color c = color * intensity;
            c.a = 1f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            return mat;
        }

        private static Material ConfettiMaterial()
        {
            string path = ArenaBrawlerPaths.Data + "/UI/Brawler_Confetti.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>A particle system driven from code: no emission of its own, slowing as it spreads.</summary>
        private static ParticleSystem Particles(string name, Transform parent, Material material, float gravity, float dampen, bool tumbling)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000;
            main.startRotation3D = tumbling;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0f;
            drag.dampen = dampen;
            if (!tumbling)
            {
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            }
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            if (tumbling)
            {
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                r.alignment = ParticleSystemRenderSpace.Local;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
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
