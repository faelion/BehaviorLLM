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
    /// Builds the Dodgeball scene: a wooden court seen from above, in stands full of supporters,
    /// two players, three balls, and the view and interface that make a match read. Every sprite is
    /// painted by <see cref="DodgeballArt"/>. Run it from BehaviorLLM > Samples > Build Dodgeball Scene.
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

        private static Color HumanColor => DodgeballArt.HumanColor;
        private static Color MachineColor => DodgeballArt.MachineColor;

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
            DodgeballArt.Set art = DodgeballArt.Build(rules);

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Dodgeball";

            Camera camera = CreateCamera(rules, art);
            CreateArena(rules, art);
            Transform crowd = CreateCrowd(rules, art);

            DodgeballView.PlayerRig humanRig = CreatePlayer(DodgeballIds.HumanName, rules, art, HumanColor, 0);
            DodgeballView.PlayerRig machineRig = CreatePlayer(DodgeballIds.MachineName, rules, art, MachineColor, 1);
            DodgeballView.BallRig[] balls = new DodgeballView.BallRig[rules.ballCount];
            for (int i = 0; i < rules.ballCount; i++) balls[i] = CreateBall(i, rules, art);

            GameObject llm = new GameObject("LLM");
            BehaviorLLMServer llmServer = llm.AddComponent<BehaviorLLMServer>();
            BehaviorLLMClient client = llm.AddComponent<BehaviorLLMClient>();
            // ApplyConfig rather than writing the serialized fields: written through a
            // SerializedObject, the model reference was saved empty.
            llmServer.ApplyConfig(server, model);
            client.ApplyConfig(server, model);
            SetObject(client, "server", llmServer);
            llm.AddComponent<DecisionTelemetryRecorder>();

            GameObject matchGo = new GameObject("Match");
            DodgeballMatch match = matchGo.AddComponent<DodgeballMatch>();

            GameObject machine = machineRig.root.gameObject;
            DodgeballMachinePad pad = machine.AddComponent<DodgeballMachinePad>();
            pad.match = match;
            pad.playerIndex = 1;
            CourtObservationModule eyes = machine.AddComponent<CourtObservationModule>();
            eyes.match = match;
            eyes.playerIndex = 1;
            DecisionMaker maker = CreateMachineBrain(machine, actions, decisions, pad, client);

            match.rules = rules;
            match.machine = maker;
            match.machinePad = pad;
            match.client = client;
            match.view = camera;

            GameObject viewGo = new GameObject("View");
            DodgeballView view = viewGo.AddComponent<DodgeballView>();
            view.match = match;
            view.view = camera;
            view.players = new[] { humanRig, machineRig };
            view.balls = balls;
            view.throwingLines = new[]
            {
                ThrowingLine("HumanThrowingLine", -rules.throwingLine, rules, art),
                ThrowingLine("MachineThrowingLine", rules.throwingLine, rules, art)
            };
            view.aimLine = Line("AimLine", viewGo.transform, art.Dash, 0.06f, 8, true);
            view.aimLine.textureMode = LineTextureMode.Tile;
            view.aimLine.textureScale = new Vector2(3.5f, 1f);
            view.reticle = SpriteObject("Reticle", viewGo.transform, art.Reticle, Vector3.zero, 0.5f, Color.white, 9, art);
            view.machineAimLine = Line("MachineAimLine", viewGo.transform, art.Dash, 0.06f, 8, true);
            view.machineAimLine.textureMode = LineTextureMode.Tile;
            view.machineAimLine.textureScale = new Vector2(3.5f, 1f);
            view.noThrowZones = new[]
            {
                NoThrowZone("HumanNoThrowZone", -1f, rules, art),
                NoThrowZone("MachineNoThrowZone", 1f, rules, art)
            };
            view.crowd = crowd;
            view.sparks = CreateSparks(viewGo.transform, art);
            view.ghostSprite = art.Torso;
            view.ghostMaterial = art.Sprite;

            GameObject hudGo = new GameObject("HUD");
            DodgeballHud hud = hudGo.AddComponent<DodgeballHud>();
            hud.match = match;
            hud.view = camera;
            hud.humanColor = HumanColor;
            hud.machineColor = MachineColor;
            DodgeballIcons.Set icons = art.Icons;
            hud.ball = art.Ball;
            hud.ring = art.Ring;
            hud.disc = art.Disc;
            hud.glove = icons.Glove;
            hud.dash = icons.Dash;
            hud.clock = icons.Clock;
            hud.noThrow = icons.NoThrow;
            hud.arrow = icons.Arrow;
            hud.stand = icons.Stand;
            hud.burst = icons.Burst;
            hud.pip = icons.Pip;
            hud.segment = icons.Segment;
            hud.plate = icons.Plate;
            hud.banner = icons.Banner;
            hud.bubble = icons.Bubble;
            hud.key = icons.Key;
            hud.mouseLeft = icons.MouseLeft;
            hud.mouseRight = icons.MouseRight;
            hud.restart = icons.Restart;
            hud.move = icons.Move;

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
                Action(DodgeballIds.Throw, "Charge and throw the ball in your throwing hand: at the opponent, ahead of where they run, a metre or two above or below them, or off the top or bottom wall.",
                       DodgeballIds.AimParameter, DodgeballIds.Aims),
                Action(DodgeballIds.Catch, "Open a catch for a moment, dropping any balls you hold. A ball that arrives while it is open is caught and you score.",
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

        // Sorting orders, back to front.
        private const int OrderBackdrop = -60, OrderStands = -50, OrderFans = -40, OrderFrame = -22, OrderFloor = -20,
                          OrderLines = -15, OrderGround = -10, OrderPlayer = 2, OrderBall = 5, OrderOverlay = 50;

        private static Camera CreateCamera(DodgeballRules rules, DodgeballArt.Set art)
        {
            GameObject cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            // Looking down +z at the court, which lies in the xy plane: court x is world x, court y
            // is world y. Raised a little so the scoreboard sits over the top stand, not the court.
            cam.transform.position = new Vector3(0f, 0.45f, -10f);
            Camera camera = cam.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = rules.HalfWidth + 2.6f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);

            // The vignette rides on the camera so it stays framed when the camera shakes.
            float h = camera.orthographicSize * 2f;
            SpriteRenderer vignette = SpriteObject("Vignette", cam.transform, art.Vignette, new Vector3(0f, 0f, 10f), 1f,
                                                   Color.white, OrderOverlay, art);
            vignette.transform.localScale = new Vector3(h * 16f / 9f * 1.05f, h * 1.05f, 1f);
            return camera;
        }

        private static void CreateArena(DodgeballRules rules, DodgeballArt.Set art)
        {
            Transform arena = new GameObject("Arena").transform;
            float l = rules.courtLength, w = rules.courtWidth;

            Sliced("Backdrop", arena, art, new Vector3(0f, 0f, 0f), new Vector2(l + 6f, w + 3.2f), new Color(0.12f, 0.14f, 0.19f), OrderBackdrop);

            // Stands above and below the court, three tiers each, darker toward the back.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = side * (w / 2f + 1.15f + tier * 0.72f);
                    float shade = 0.2f - tier * 0.035f;
                    Sliced("Stand" + (side < 0 ? "Bottom" : "Top") + tier, arena, art, new Vector3(0f, y, 0f),
                           new Vector2(l + 5f - tier * 0.4f, 0.66f), new Color(shade, shade + 0.02f, shade + 0.06f), OrderStands + tier);
                }
            }

            // Each team's end: a coloured banner behind its back wall.
            Sliced("HumanEnd", arena, art, new Vector3(-l / 2f - 1.15f, 0f, 0f), new Vector2(1.3f, w * 0.8f), HumanColor * 0.55f, OrderStands);
            Sliced("MachineEnd", arena, art, new Vector3(l / 2f + 1.15f, 0f, 0f), new Vector2(1.3f, w * 0.8f), MachineColor * 0.55f, OrderStands);
            SpriteObject("HumanEndBall", arena, art.Ball, new Vector3(-l / 2f - 1.15f, 0f, 0f), 0.8f, new Color(0.6f, 0.75f, 1f), OrderStands + 1, art);
            SpriteObject("MachineEndBall", arena, art.Ball, new Vector3(l / 2f + 1.15f, 0f, 0f), 0.8f, new Color(1f, 0.7f, 0.65f), OrderStands + 1, art);

            // Padded wall around the court, with a soft shadow inside it.
            Sliced("Wall", arena, art, Vector3.zero, new Vector2(l + 0.56f, w + 0.56f), new Color(0.2f, 0.24f, 0.34f), OrderFrame);
            Sliced("WallTrim", arena, art, Vector3.zero, new Vector2(l + 0.2f, w + 0.2f), new Color(0.09f, 0.1f, 0.14f), OrderFrame + 1);
            SpriteObject("Floor", arena, art.Floor, Vector3.zero, 1f, Color.white, OrderFloor, art);
        }

        /// <summary>
        /// Spectators on the stands, facing the court. Those at the human's end mostly wear blue,
        /// those at the machine's end mostly red; a name ending in 0 or 1 tells the view whom each
        /// one cheers for.
        /// </summary>
        private static Transform CreateCrowd(DodgeballRules rules, DodgeballArt.Set art)
        {
            Transform crowd = new GameObject("Crowd").transform;
            System.Random random = new System.Random(11);
            float l = rules.courtLength, w = rules.courtWidth;
            int count = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int tier = 0; tier < 3; tier++)
                {
                    float y = side * (w / 2f + 1.15f + tier * 0.72f);
                    float span = (l + 4.4f - tier * 0.4f) / 2f;
                    for (float x = -span + 0.3f; x <= span - 0.3f; x += 0.62f)
                    {
                        if (random.NextDouble() < 0.12) continue;
                        float jitter = (float)(random.NextDouble() - 0.5) * 0.18f;
                        int team = x < 0f ? (random.NextDouble() < 0.85 ? 0 : 1) : (random.NextDouble() < 0.85 ? 1 : 0);
                        Color shirt = team == 0 ? HumanColor : MachineColor;
                        if (random.NextDouble() < 0.2) shirt = Color.Lerp(new Color(0.85f, 0.85f, 0.85f), new Color(0.3f, 0.3f, 0.32f), (float)random.NextDouble());
                        shirt = Color.Lerp(shirt, Color.black, (float)random.NextDouble() * 0.3f);

                        GameObject fan = new GameObject($"Fan_{count++}_{team}");
                        fan.transform.SetParent(crowd, false);
                        fan.transform.localPosition = new Vector3(x + jitter, y + jitter * 0.4f, 0f);
                        // Faces the court: the top stand looks down, the bottom stand up.
                        fan.transform.localRotation = Quaternion.Euler(0f, 0f, side > 0 ? 180f : 0f);
                        fan.transform.localScale = Vector3.one * 0.58f;
                        SpriteObject("Shirt", fan.transform, art.Torso, Vector3.zero, 0.95f, shirt, OrderFans + tier * 2, art);
                        float hair = (float)random.NextDouble();
                        SpriteObject("Head", fan.transform, art.Head, new Vector3(0f, 0.02f, 0f), 0.46f,
                                     Color.Lerp(Color.white, new Color(1f, 0.85f, 0.6f), hair), OrderFans + tier * 2 + 1, art);
                    }
                }
            }
            return crowd;
        }

        private static DodgeballView.PlayerRig CreatePlayer(string name, DodgeballRules rules, DodgeballArt.Set art, Color color, int index)
        {
            float d = rules.playerRadius * 2f;
            Vector3 start = new Vector3((index == 0 ? -1f : 1f) * rules.HalfLength * 0.6f, 0f, 0f);
            DodgeballView.PlayerRig rig = new DodgeballView.PlayerRig { color = color };

            GameObject root = new GameObject(name);
            root.transform.position = start;
            rig.root = root.transform;

            Transform ground = new GameObject(name + "Ground").transform;
            ground.position = start;
            rig.ground = ground;
            SpriteObject("Shadow", ground, art.Soft, new Vector3(0.14f, -0.16f, 0f), d * 1.45f, new Color(0f, 0f, 0f, 0.45f), OrderGround, art);
            SpriteObject("Base", ground, art.Ring, Vector3.zero, d * 1.25f, new Color(color.r, color.g, color.b, 0.55f), OrderGround + 1, art);
            rig.chargeArc = Line("Charge", ground, art.Sprite, 0.1f, OrderPlayer + 4, false);

            rig.catchRing = SpriteObject("CatchRing", root.transform, art.Ring, Vector3.zero, d * 1.2f, new Color(1f, 0.85f, 0.25f), OrderPlayer - 1, art);
            rig.catchRing.enabled = false;

            Transform body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            rig.body = body;
            rig.bodySprite = SpriteObject("Torso", body, art.Torso, Vector3.zero, d * 1.2f, color, OrderPlayer, art);
            SpriteObject("Head", body, art.Head, new Vector3(0f, 0.03f, 0f), d * 0.56f, Color.white, OrderPlayer + 2, art);

            rig.handLeft = SpriteObject("HandLeft", root.transform, art.Hand, new Vector3(-0.4f, 0.08f, 0f), 0.25f, Color.white, OrderPlayer + 1, art).transform;
            rig.handRight = SpriteObject("HandRight", root.transform, art.Hand, new Vector3(0.4f, 0.08f, 0f), 0.25f, Color.white, OrderPlayer + 1, art).transform;
            // Facing the other half at the start.
            root.transform.rotation = Quaternion.Euler(0f, 0f, index == 0 ? -90f : 90f);
            return rig;
        }

        private static DodgeballView.BallRig CreateBall(int index, DodgeballRules rules, DodgeballArt.Set art)
        {
            DodgeballView.BallRig rig = new DodgeballView.BallRig();
            GameObject root = new GameObject("Ball_" + (char)('A' + index));
            root.transform.localScale = Vector3.one * rules.ballRadius * 2f;
            rig.root = root.transform;
            rig.shadow = SpriteObject("Shadow", root.transform, art.Soft, new Vector3(0.15f, -0.15f, 0f), 1.1f, new Color(0f, 0f, 0f, 0.5f), OrderBall - 2, art).transform;
            rig.sprite = SpriteObject("Ball", root.transform, art.Ball, Vector3.zero, 1f, Color.white, OrderBall, art).transform;

            TrailRenderer trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = art.Sprite;
            trail.time = 0.16f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = rules.ballRadius * 1.7f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.emitting = false;
            trail.sortingOrder = OrderBall - 1;
            trail.numCapVertices = 4;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            rig.trail = trail;

            rig.lockArc = Line("PickupLock", root.transform, art.Sprite, 0.05f, OrderBall + 1, false);
            rig.lockArc.startColor = rig.lockArc.endColor = new Color(1f, 0.85f, 0.3f, 0.95f);
            return rig;
        }

        private static LineRenderer ThrowingLine(string name, float x, DodgeballRules rules, DodgeballArt.Set art)
        {
            LineRenderer line = Line(name, null, art.Dash, 0.07f, OrderLines, true);
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(x, -rules.HalfWidth, 0f));
            line.SetPosition(1, new Vector3(x, rules.HalfWidth, 0f));
            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(2.5f, 1f);
            line.startColor = line.endColor = new Color(1f, 1f, 1f, 0.55f);
            return line;
        }

        /// <summary>The strip between the center line and a throwing line, lit red by the view as a warning.</summary>
        private static SpriteRenderer NoThrowZone(string name, float side, DodgeballRules rules, DodgeballArt.Set art)
        {
            SpriteRenderer zone = Sliced(name, null, art, new Vector3(side * rules.throwingLine * 0.5f, 0f, 0f),
                                         new Vector2(rules.throwingLine, rules.courtWidth), new Color(1f, 0.15f, 0.1f, 0f), OrderLines - 1);
            return zone;
        }

        private static ParticleSystem CreateSparks(Transform parent, DodgeballArt.Set art)
        {
            GameObject go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.startLifetime = 0.4f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            // Bursts slow down as they spread, and shrink away.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0f;
            drag.dampen = 0.12f;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = art.SoftParticle;
            r.sortingOrder = 7;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        private static SpriteRenderer SpriteObject(string name, Transform parent, Sprite sprite, Vector3 localPosition, float scale,
                                                   Color color, int order, DodgeballArt.Set art)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * scale;
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sharedMaterial = art.Sprite;
            r.color = color;
            r.sortingOrder = order;
            return r;
        }

        private static SpriteRenderer Sliced(string name, Transform parent, DodgeballArt.Set art, Vector3 position, Vector2 size, Color color, int order)
        {
            SpriteRenderer r = SpriteObject(name, parent, art.Panel, position, 1f, color, order, art);
            r.drawMode = SpriteDrawMode.Sliced;
            r.size = size;
            return r;
        }

        private static LineRenderer Line(string name, Transform parent, Material material, float width, int order, bool worldSpace)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.widthMultiplier = width;
            line.positionCount = 0;
            line.numCapVertices = 3;
            line.sortingOrder = order;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            return line;
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
