using BehaviorLLM.Core.Perception;
using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Everything that tunes <i>what a decision maker can notice</i>, in one reusable asset: the
    /// shape and reach of its senses, what counts as a target, what blocks the view, how often
    /// the scene is scanned and how much of the recent past it remembers.
    ///
    /// Every guard in a level usually senses the world the same way, so this is the asset you
    /// tune once and assign to all of them. Give the boss a second asset with a longer reach and
    /// nothing else has to change.
    ///
    /// Create with <c>Create &gt; BehaviorLLM &gt; Perception Config</c>, or start from the preset
    /// the package ships at <c>Runtime/Defaults/Perception_Default.asset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPerceptionConfig", menuName = "BehaviorLLM/Perception Config", order = 3)]
    public class PerceptionConfig : ScriptableObject
    {
        [Header("Vision Shape")]
        [Tooltip("Sphere: notice everything within Range, in all directions, like " +
                 "hearing. Cone: notice only what is in front, within Fov Angle, and " +
                 "walls can block it, which is what makes hiding possible. Global: " +
                 "notice everything in the scene regardless of distance, for a manager " +
                 "or director.")]
        public VisionMode mode = VisionMode.Sphere;

        [Tooltip("How far it can see, in metres. Keep it as small as the game allows: " +
                 "everything seen becomes text sent to the AI, and more text means " +
                 "slower decisions.")]
        public float range = 10f;

        [Range(0, 360)]
        [Tooltip("Cone mode only. Width of the cone in degrees, centred on where the " +
                 "object faces. 90 is a normal field of view; 360 behaves like Sphere.")]
        public float fovAngle = 90f;

        [Tooltip("Which Unity layers it can notice at all. Objects on other layers are " +
                 "invisible to it no matter how close. Noticed objects still need an LLM " +
                 "Context Object component to be described to the AI.")]
        public LayerMask perceptionLayers = ~0;

        [Tooltip("Cone mode only. Which layers block its line of sight, typically walls " +
                 "and world geometry. Leave at Nothing and it sees through walls.")]
        public LayerMask occluderLayers = 0;

        [Header("Timing")]
        [Tooltip("Seconds between looks at the surroundings. 0 looks every frame, which " +
                 "is wasteful. 0.2 (five looks per second) is plenty for something that " +
                 "decides every couple of seconds.")]
        public float scanInterval = 0.2f;

        [Tooltip("Decide immediately when something new comes into view, instead of " +
                 "waiting for the next scheduled decision. This is what lets a guard " +
                 "react the moment it spots you. Turn off in crowded scenes, where new " +
                 "sightings would trigger decisions constantly.")]
        public bool triggerInterruptOnNewObject = true;

        [Header("Prompt")]
        [Tooltip("The heading the seen objects appear under in the text sent to the AI. " +
                 "Anything the AI reads naturally works, such as Vision or What you can " +
                 "see.")]
        public string visionTopicName = "Vision";

        [Tooltip("The heading remembered events appear under in the text sent to the AI.")]
        public string memoryTopicName = "Short-Term Memory";

        [Tooltip("How many recent events it remembers and tells the AI about. This is " +
                 "its whole short-term memory. Small numbers keep decisions fast; 5 is a " +
                 "good start.")]
        public int memoryCapacity = 5;
    }
}
