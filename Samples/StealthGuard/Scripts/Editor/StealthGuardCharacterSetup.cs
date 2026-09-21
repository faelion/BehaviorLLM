using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Project.Samples.StealthGuard.Editor
{
    /// <summary>
    /// Prepares the Kenney character art for use: one shared rig, three animation files that have
    /// to borrow that rig's avatar, a material per skin, and a small animator controller.
    ///
    /// Kenney ships the model and each animation as separate FBX files. Unity will not play a clip
    /// from one file on a rig from another unless the clip's importer is told to copy the model's
    /// avatar, which is what this does. Doing it in script keeps the sample reproducible from the
    /// one menu item rather than from a page of import settings somebody has to remember.
    /// </summary>
    public static class StealthGuardCharacterSetup
    {
        private static string Art => StealthGuardPaths.CharacterArt;
        private static string ModelPath => Art + "/characterMedium.fbx";
        private static string ControllerPath => Art + "/KenneyCharacter.controller";

        private static readonly string[] AnimationFiles = { "idle", "run", "jump" };

        /// <summary>The skeleton root inside the model file. See <see cref="Retarget"/>.</summary>
        public const string RigRootName = "Root";

        // Preparing means reimporting three files, so the result is cached: the scene builder asks
        // for it once per character and there is no reason to pay for it eight times.
        private static AnimatorController cachedController;
        private static Avatar cachedAvatar;

        /// <summary>The rig every animation file borrows. Null until <see cref="Prepare"/> has run.</summary>
        public static Avatar ModelAvatar => cachedAvatar;

        /// <summary>True when the art is present, so the builder can fall back to capsules.</summary>
        public static bool ArtAvailable => AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null;

        /// <summary>
        /// Configures the importers, builds the controller and returns it. Safe to call repeatedly;
        /// it only reimports when a setting actually changes.
        /// </summary>
        public static AnimatorController Prepare()
        {
            if (!ArtAvailable) return null;
            if (cachedController != null) return cachedController;

            cachedAvatar = ConfigureModel();
            for (int i = 0; i < AnimationFiles.Length; i++) ConfigureAnimation(AnimationFiles[i], cachedAvatar);

            cachedController = BuildController();
            return cachedController;
        }

        /// <summary>Forgets the cache, so a rebuild reconfigures the importers from scratch.</summary>
        public static void Reset()
        {
            cachedController = null;
            cachedAvatar = null;
        }

        /// <summary>The model owns the avatar every animation file copies.</summary>
        private static Avatar ConfigureModel()
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            bool changed = false;

            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
            // The FBX is authored in centimetres and says so. Honouring that is what makes the
            // character arrive at roughly human size instead of a hundred times too big.
            if (!importer.useFileScale)
            {
                importer.useFileScale = true;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
            }

            Object[] all = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is Avatar avatar) return avatar;
            }
            return null;
        }

        /// <summary>
        /// An animation file carries a clip but no usable rig of its own, so it is pointed at the
        /// model's avatar. The clip is also set to loop, which the source files do not specify.
        /// </summary>
        private static void ConfigureAnimation(string fileName, Avatar avatar)
        {
            string path = $"{Art}/{fileName}.fbx";
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            if (!importer.useFileScale)
            {
                importer.useFileScale = true;
                changed = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != avatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                changed = true;
            }

            // Idle and run have to loop; a jump plays once.
            bool shouldLoop = fileName != "jump";
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0 && clips[0].loopTime != shouldLoop)
            {
                clips[0].loopTime = shouldLoop;
                importer.clipAnimations = clips;
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }

        /// <summary>
        /// Idle and run blended on the character's real speed. Kenney's pack has no walk clip, so
        /// the run is blended in from a low speed and reads as a walk while the guard patrols.
        /// </summary>
        private static AnimatorController BuildController()
        {
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            for (int i = 0; i < AnimationFiles.Length; i++)
            {
                AnimationClip found = FindClip(AnimationFiles[i]);
                if (found != null) clips[AnimationFiles[i]] = found;
            }
            if (!clips.ContainsKey("idle") || !clips.ContainsKey("run")) return null;

            // See Retarget for why the clips out of the FBX files cannot be played as they are.
            Dictionary<string, AnimationClip> playable = new Dictionary<string, AnimationClip>();
            foreach (KeyValuePair<string, AnimationClip> entry in clips)
            {
                AnimationClip fixedUp = Retarget(entry.Value, $"{Art}/{entry.Key}_retargeted.anim");
                if (fixedUp != null) playable[entry.Key] = fixedUp;
            }
            if (!playable.ContainsKey("idle") || !playable.ContainsKey("run")) return null;
            clips = playable;

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            BlendTree tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["idle"], 0f);
            tree.AddChild(clips["run"], 3.5f);

            AnimatorState state = controller.layers[0].stateMachine.AddState("Locomotion");
            state.motion = tree;
            controller.layers[0].stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// Picks the motion out of one of the animation files.
        ///
        /// Each file holds two clips: the motion itself, named after the file ("Root|Run"), and a
        /// one-frame "Targeting Pose" the exporter adds. Taking whichever came first meant taking
        /// the pose, which is why every character stood still in its bind pose no matter what the
        /// blend tree said.
        /// </summary>
        private static AnimationClip FindClip(string fileName)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath($"{Art}/{fileName}.fbx");
            AnimationClip longest = null;

            for (int i = 0; i < all.Length; i++)
            {
                if (!(all[i] is AnimationClip candidate) || candidate.name.StartsWith("__")) continue;
                if (candidate.name.ToLowerInvariant().Contains("targeting pose")) continue;
                // Fall back on the longest clip if the naming ever changes; a one-frame pose loses.
                if (longest == null || candidate.length > longest.length) longest = candidate;
            }
            return longest;
        }

        /// <summary>
        /// Writes a playable copy of one of the pack's clips, and returns it.
        ///
        /// Kenney ships the model and the animations as separate FBX files whose hierarchies do not
        /// line up. In the model the skeleton hangs off a child called "Root", next to the skinned
        /// mesh; in an animation file the skeleton root *is* the file root, so every curve in the
        /// clip is addressed one level too high ("HipsCtrl/Hips" instead of "Root/HipsCtrl/Hips").
        /// Played as they are, the curves find nothing and the character stands in its T-pose.
        ///
        /// Rather than move the Animator down onto the rig, which then hands the clip's root curves
        /// the node that carries the file's x100 unit conversion and shrinks the character to a
        /// speck, the copy re-addresses every curve from the model root down. Two kinds of curve are
        /// dropped on the way: scale, which is authored in the animation file's units and would
        /// undo that same conversion, and the root's own position, which is the NavMeshAgent's job
        /// here. The bones keep every bit of their motion.
        /// </summary>
        private static AnimationClip Retarget(AnimationClip source, string path)
        {
            if (source == null) return null;

            AnimationClip clean = new AnimationClip { frameRate = source.frameRate };
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
            int kept = 0;

            for (int i = 0; i < bindings.Length; i++)
            {
                bool isRigRoot = string.IsNullOrEmpty(bindings[i].path);
                if (bindings[i].propertyName.Contains("Scale")) continue;
                if (isRigRoot && bindings[i].propertyName.Contains("Position")) continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, bindings[i]);
                if (curve == null) continue;

                string retargeted = isRigRoot ? RigRootName : RigRootName + "/" + bindings[i].path;
                clean.SetCurve(retargeted, bindings[i].type, bindings[i].propertyName, curve);
                kept++;
            }
            if (kept == 0) return null;

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = !source.name.ToLowerInvariant().Contains("jump");
            AnimationUtility.SetAnimationClipSettings(clean, settings);

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clean, path);
            return clean;
        }

        /// <summary>
        /// A material for one of the pack's skins. The FBX has no material of its own, so the skin
        /// texture is what turns the same model into a guard or an intruder.
        /// </summary>
        public static Material SkinMaterial(string skinName)
        {
            string materialPath = $"{Art}/{skinName}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/{skinName}.png");
            if (texture == null) return null;

            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") : Shader.Find("Standard");
            Material material = existing != null ? existing : new Material(shader);
            material.shader = shader;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            // Flat shading suits the art; a metallic sheen on a lunchbox character looks wrong.
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (existing == null) AssetDatabase.CreateAsset(material, materialPath);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
