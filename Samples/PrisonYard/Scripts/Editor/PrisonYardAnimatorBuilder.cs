using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Project.Samples.PrisonYard.Editor
{
    /// <summary>
    /// Builds an animator controller for a KayKit character from the clips inside its own .glb.
    ///
    /// The KayKit characters ship 76 clips each. All this needs is a walk/run blend driven by how
    /// fast the character is actually moving, plus a handful of one-shots the visuals component
    /// triggers when a decision changes what someone is doing. Building it from script rather than
    /// by hand means the whole sample still regenerates from one menu item.
    /// </summary>
    public static class PrisonYardAnimatorBuilder
    {
        // Characters that share a model share a controller. Building is destructive - it deletes
        // the asset at the path first - so calling it once per character meant the second guard's
        // build deleted the controller the first guard was already pointing at, leaving everyone
        // but the last one of a kind with a null controller and stuck in their bind pose.
        private static readonly Dictionary<string, AnimatorController> built =
            new Dictionary<string, AnimatorController>();

        /// <summary>Forgets what has been built, so a scene rebuild regenerates the controllers.</summary>
        public static void Reset() { built.Clear(); }

        /// <summary>Clip names inside the KayKit character files. Verified against Knight.glb.</summary>
        private const string ClipIdle = "Idle";
        private const string ClipWalk = "Walking_A";
        private const string ClipRun = "Running_A";
        private const string ClipPunch = "Unarmed_Melee_Attack_Punch_A";
        private const string ClipInteract = "Interact";
        private const string ClipPickUp = "PickUp";
        private const string ClipHit = "Hit_A";
        private const string ClipSit = "Sit_Floor_Idle";

        /// <summary>
        /// Creates (or rebuilds) a controller beside the model, wired to that model's own clips.
        /// One per character, because a generic rig binds its clips by transform path and there is
        /// no reason to gamble on two characters having identical hierarchies.
        /// </summary>
        public static AnimatorController Build(string modelPath, string controllerPath)
        {
            if (built.TryGetValue(controllerPath, out AnimatorController cached) && cached != null) return cached;

            Dictionary<string, AnimationClip> clips = LoadClips(modelPath);
            if (clips.Count == 0)
            {
                Debug.LogWarning($"[PrisonYard] No animation clips found in {modelPath}; characters will not animate.");
                return null;
            }

            AssetDatabase.DeleteAsset(controllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("PickUp", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Sit", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // Locomotion: one blend tree driven by the character's real speed in metres per second,
            // so the feet match the movement instead of guessing from the chosen action.
            BlendTree tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            AddChild(tree, clips, ClipIdle, 0f);
            AddChild(tree, clips, ClipWalk, 1.6f);
            AddChild(tree, clips, ClipRun, 4f);

            AnimatorState locomotion = machine.AddState("Locomotion");
            locomotion.motion = tree;
            machine.defaultState = locomotion;

            AddOneShot(machine, locomotion, clips, "Punch", ClipPunch, "Punch");
            AddOneShot(machine, locomotion, clips, "Interact", ClipInteract, "Interact");
            AddOneShot(machine, locomotion, clips, "PickUp", ClipPickUp, "PickUp");
            AddOneShot(machine, locomotion, clips, "Hit", ClipHit, "Hit");

            // Resting in the infirmary is a held pose rather than a one-shot, so it is a bool with
            // a transition back when it clears.
            if (clips.ContainsKey(ClipSit))
            {
                AnimatorState sit = machine.AddState("Resting");
                sit.motion = clips[ClipSit];

                AnimatorStateTransition into = machine.AddAnyStateTransition(sit);
                into.AddCondition(AnimatorConditionMode.If, 0f, "Sit");
                into.hasExitTime = false;
                into.duration = 0.25f;
                into.canTransitionToSelf = false;

                AnimatorStateTransition outOf = sit.AddTransition(locomotion);
                outOf.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sit");
                outOf.hasExitTime = false;
                outOf.duration = 0.25f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            built[controllerPath] = controller;
            return controller;
        }

        private static void AddChild(BlendTree tree, Dictionary<string, AnimationClip> clips, string clipName, float threshold)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip)) return;
            tree.AddChild(clip, threshold);
        }

        /// <summary>
        /// A state anything can jump into when its trigger fires, which plays once and falls back
        /// to locomotion. Interruptible, so a character that starts walking mid-gesture does not
        /// slide along in a fixed pose.
        /// </summary>
        private static void AddOneShot(AnimatorStateMachine machine, AnimatorState fallback,
                                       Dictionary<string, AnimationClip> clips,
                                       string stateName, string clipName, string trigger)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip)) return;

            AnimatorState state = machine.AddState(stateName);
            state.motion = clip;

            AnimatorStateTransition into = machine.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.hasExitTime = false;
            into.duration = 0.08f;
            into.canTransitionToSelf = false;

            AnimatorStateTransition back = state.AddTransition(fallback);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;
        }

        private static Dictionary<string, AnimationClip> LoadClips(string modelPath)
        {
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is AnimationClip clip && !clips.ContainsKey(clip.name)) clips.Add(clip.name, clip);
            }
            return clips;
        }
    }
}
