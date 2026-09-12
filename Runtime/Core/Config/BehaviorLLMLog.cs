using UnityEngine;

namespace BehaviorLLM.Core.Config
{
    /// <summary>
    /// Every console line the package writes goes through here, so one project-wide setting can
    /// decide how loud it is - and so a shipped build can be silent without a single
    /// <c>#if UNITY_EDITOR</c> scattered through the runtime.
    ///
    /// The message arguments are <see cref="System.Func{TResult}"/> rather than strings on purpose.
    /// Most of the package's log lines interpolate a prompt, a response body or a list of action
    /// names; passing a string builds it whether or not anyone will read it, and the verbose lines
    /// are exactly the expensive ones. A closure that is never invoked costs nothing beyond the
    /// allocation of the closure itself, and the level check happens first.
    ///
    /// This is the one file in the runtime that calls <c>Debug.Log</c> directly. Anything else
    /// doing so is a line that cannot be turned off, which is the bug this type exists to prevent.
    /// </summary>
    public static class BehaviorLLMLog
    {
        /// <summary>True when a message at this level would be printed. Check it before doing work
        /// only needed for a log line that is more than one interpolation.</summary>
        public static bool Allows(BehaviorLLMLogLevel level) =>
            BehaviorLLMSettings.Current.ActiveLogLevel >= level;

        /// <summary>
        /// A line the author explicitly asked for by ticking a switch on a config asset, such as
        /// `logPrompts`. It prints at the normal level, because ticking the box *is* the opt-in and
        /// having to raise a second, project-wide switch before the first one does anything is a
        /// trap. Setting the project quieter than Warnings still silences it, which is what makes a
        /// build quiet without visiting every config asset.
        /// </summary>
        public static void Requested(System.Func<string> message, Object context = null)
        {
            if (!Allows(BehaviorLLMLogLevel.Warnings)) return;
            Debug.Log(message(), context);
        }

        /// <summary>Detail nobody asked for. Off unless the level is Verbose.</summary>
        public static void Info(System.Func<string> message, Object context = null)
        {
            if (!Allows(BehaviorLLMLogLevel.Verbose)) return;
            Debug.Log(message(), context);
        }

        /// <summary>Misconfiguration and fallbacks: things that still work but probably should not.</summary>
        public static void Warn(System.Func<string> message, Object context = null)
        {
            if (!Allows(BehaviorLLMLogLevel.Warnings)) return;
            Debug.LogWarning(message(), context);
        }

        /// <summary>Something is broken. Printed unless logging is turned off entirely.</summary>
        public static void Error(System.Func<string> message, Object context = null)
        {
            if (!Allows(BehaviorLLMLogLevel.ErrorsOnly)) return;
            Debug.LogError(message(), context);
        }
    }
}
