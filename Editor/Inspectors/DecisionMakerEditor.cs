using System;
using System.Collections.Generic;
using BehaviorLLM.Core.Actions;
using BehaviorLLM.Core.Decisions;
using UnityEditor;
using UnityEngine;

namespace BehaviorLLM.Editor.Inspectors
{
    /// <summary>
    /// Inspector for <see cref="DecisionMaker"/> that keeps the action bindings list a mirror of
    /// the assigned <see cref="ActionConfig"/>: same actions, same order, names read-only, and
    /// only the <c>On Execute</c> event editable. The user never types an action name twice, so
    /// the class of bug where a binding silently fails to match the config cannot happen.
    ///
    /// Reconciliation runs every time the inspector draws, which is cheap for a handful of
    /// actions and means editing the config asset is reflected the moment this inspector is
    /// looked at. It is careful with data: a binding whose action left the config is removed only
    /// when nothing is wired to it. One with listeners is kept and flagged, so a renamed action
    /// never throws away the user's wiring without them seeing it.
    ///
    /// The fallback action is drawn as a dropdown of the config's actions for the same reason.
    /// </summary>
    [CustomEditor(typeof(DecisionMaker))]
    public class DecisionMakerEditor : UnityEditor.Editor
    {
        private const string BindingsProperty = "actionBindings";
        private const string FallbackProperty = "fallbackAction";
        private const string ConfigProperty = "actionConfig";

        private static readonly GUIContent OnExecuteLabel = new GUIContent("On Execute",
            "The function to call when the AI picks this action, like a UI button's On Click. It " +
            "receives one string: the argument the AI chose, or an empty string for actions that take none.");

        private static readonly GUIContent FallbackLabel = new GUIContent("Fallback Action",
            "What to do when the AI's answer cannot be used, for example it named an action that does " +
            "not exist or left out a required argument. Pick a safe action, such as holding position. " +
            "None means do nothing on a bad answer. Recommended for anything you ship.");

        private static readonly GUIContent FallbackArgumentLabel = new GUIContent("Fallback Argument",
            "The argument to pass to the fallback action, if that action needs one. Otherwise leave empty.");

        private GUIStyle descriptionStyle;
        private GUIStyle warningStyle;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ActionConfig config = ((DecisionMaker)target).actionConfig;
            List<ActionDefinition> actions = CollectActions(config);

            // Draw every field in declaration order so the [Header] groups keep their places, and
            // take over only the two fields whose values must come from the config.
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                switch (iterator.name)
                {
                    case "m_Script":
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(iterator);
                        break;
                    case BindingsProperty:
                        DrawBindings(iterator, config, actions);
                        break;
                    case FallbackProperty:
                        DrawFallback(iterator, actions);
                        break;
                    default:
                        EditorGUILayout.PropertyField(iterator, true);
                        break;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------ bindings

        private void DrawBindings(SerializedProperty list, ActionConfig config, List<ActionDefinition> actions)
        {
            if (config == null)
            {
                EditorGUILayout.HelpBox("Assign an Action Config above. Its actions will appear here, " +
                                        "each with an On Execute event to wire up.", MessageType.Info);
                return;
            }

            if (targets.Length > 1)
            {
                // Reconciling several objects against possibly different configs is not worth the
                // complexity; the default list is still correct, just not synchronised.
                EditorGUILayout.PropertyField(list, true);
                return;
            }

            int orphanStart = Reconcile(list, actions);

            if (actions.Count == 0)
            {
                EditorGUILayout.HelpBox("The Action Config has no actions yet. Add some to it and they " +
                                        "will appear here.", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            bool sectionOpen = SessionState.GetBool(SectionKey(), true);
            bool nowOpen = EditorGUILayout.Foldout(sectionOpen, $"Action Bindings ({actions.Count})", true, EditorStyles.foldoutHeader);
            if (nowOpen != sectionOpen) SessionState.SetBool(SectionKey(), nowOpen);
            if (nowOpen && actions.Count > 0)
            {
                if (GUILayout.Button("Expand all", EditorStyles.miniButtonLeft, GUILayout.Width(78))) SetAll(actions, true);
                if (GUILayout.Button("Collapse all", EditorStyles.miniButtonRight, GUILayout.Width(84))) SetAll(actions, false);
            }
            EditorGUILayout.EndHorizontal();
            if (!nowOpen) return;

            EditorGUILayout.HelpBox("One entry per action in the Action Config, kept in sync automatically. " +
                                    "Open an action and wire up what the game should do for it.", MessageType.None);

            for (int i = 0; i < actions.Count && i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                DrawBoundAction(element, actions[i]);
            }

            if (orphanStart < list.arraySize)
            {
                DrawOrphans(list, orphanStart, actions);
            }
        }

        private void DrawBoundAction(SerializedProperty element, ActionDefinition def)
        {
            int listeners = PersistentCallCount(element);
            string signature = def.TakesArgument ? $"{def.actionName} ({def.parameterType})" : def.actionName;

            // An action nothing is wired to is the one that needs attention, so it starts open;
            // a wired one starts collapsed to keep a long action list compact.
            string key = ActionKey(def.actionName);
            bool open = SessionState.GetBool(key, listeners == 0);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            bool nowOpen = EditorGUILayout.Foldout(open, signature, true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            string summary = listeners == 0 ? "nothing wired" : listeners == 1 ? "1 listener" : $"{listeners} listeners";
            EditorGUILayout.LabelField(summary, listeners == 0 ? WarningStyle : EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();
            if (nowOpen != open) SessionState.SetBool(key, nowOpen);

            if (nowOpen)
            {
                if (!string.IsNullOrWhiteSpace(def.description))
                {
                    EditorGUILayout.LabelField(def.description, DescriptionStyle);
                }
                EditorGUILayout.PropertyField(element.FindPropertyRelative("onExecute"), OnExecuteLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void SetAll(List<ActionDefinition> actions, bool open)
        {
            for (int i = 0; i < actions.Count; i++) SessionState.SetBool(ActionKey(actions[i].actionName), open);
        }

        // Foldout state is per component and per action, and lives only for the Editor session:
        // it is UI state, not data, so it must never dirty the scene.
        private string SectionKey() => $"BehaviorLLM.DecisionMaker.{target.GetInstanceID()}.bindings";
        private string ActionKey(string actionName) => $"BehaviorLLM.DecisionMaker.{target.GetInstanceID()}.binding.{actionName}";

        private void DrawOrphans(SerializedProperty list, int orphanStart, List<ActionDefinition> orphanActions)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("These bindings have something wired up but their action is no longer in " +
                                    "the Action Config, so they will never run. If the action was renamed, " +
                                    "copy the wiring to its new entry above, then remove these.",
                                    MessageType.Warning);

            for (int i = orphanStart; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                string name = element.FindPropertyRelative("actionName").stringValue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool duplicate = IndexOfAction(orphanActions, name) >= 0;
                EditorGUILayout.LabelField(duplicate ? $"{name}  (duplicate of the entry above)" : $"{name}  (not in Action Config)", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    list.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break; // The list changed under us; redraw next frame.
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.PropertyField(element.FindPropertyRelative("onExecute"), OnExecuteLabel);
                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>
        /// Makes the first <c>actions.Count</c> entries of the list match the config in order,
        /// carrying existing wiring across by name, adding empty entries for new actions, and
        /// dropping entries for removed actions that have nothing wired. Entries for removed
        /// actions that do have wiring are moved to the end and kept. Returns the index where
        /// those kept orphans start.
        /// </summary>
        public static int Reconcile(SerializedProperty list, List<ActionDefinition> actions)
        {
            // Pass 1: drop orphans nothing is wired to. Walk backwards so indices stay valid.
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                string name = element.FindPropertyRelative("actionName").stringValue;
                if (IndexOfAction(actions, name) >= 0) continue;
                if (PersistentCallCount(element) == 0) list.DeleteArrayElementAtIndex(i);
            }

            // Pass 2: put one entry per config action at the front, in config order.
            for (int i = 0; i < actions.Count; i++)
            {
                string wanted = actions[i].actionName;
                int found = -1;
                for (int j = i; j < list.arraySize; j++)
                {
                    string name = list.GetArrayElementAtIndex(j).FindPropertyRelative("actionName").stringValue;
                    if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) { found = j; break; }
                }

                if (found >= 0)
                {
                    if (found != i) list.MoveArrayElement(found, i);
                    // Normalise the case so the serialized name is exactly the config's.
                    list.GetArrayElementAtIndex(i).FindPropertyRelative("actionName").stringValue = wanted;
                }
                else
                {
                    list.InsertArrayElementAtIndex(i);
                    SerializedProperty fresh = list.GetArrayElementAtIndex(i);
                    fresh.FindPropertyRelative("actionName").stringValue = wanted;
                    // InsertArrayElementAtIndex duplicates a neighbour; start from an empty event.
                    SerializedProperty calls = fresh.FindPropertyRelative("onExecute.m_PersistentCalls.m_Calls");
                    if (calls != null) calls.ClearArray();
                }
            }

            return actions.Count;
        }

        public static int PersistentCallCount(SerializedProperty bindingElement)
        {
            SerializedProperty calls = bindingElement.FindPropertyRelative("onExecute.m_PersistentCalls.m_Calls");
            return calls != null ? calls.arraySize : 0;
        }

        // ------------------------------------------------------------------ fallback

        private void DrawFallback(SerializedProperty fallback, List<ActionDefinition> actions)
        {
            SerializedProperty nameProp = fallback.FindPropertyRelative("fallbackActionName");
            SerializedProperty argProp = fallback.FindPropertyRelative("fallbackArgument");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Fallback", EditorStyles.boldLabel);

            if (actions.Count == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(nameProp, FallbackLabel);
            }
            else
            {
                string[] options = new string[actions.Count + 1];
                options[0] = "None";
                for (int i = 0; i < actions.Count; i++) options[i + 1] = actions[i].actionName;

                int current = IndexOfAction(actions, nameProp.stringValue);
                int selected = current >= 0 ? current + 1 : 0;

                if (!string.IsNullOrEmpty(nameProp.stringValue) && current < 0)
                {
                    EditorGUILayout.HelpBox($"Fallback action '{nameProp.stringValue}' is not in the Action " +
                                            "Config, so it can never run. Pick another.", MessageType.Warning);
                }

                int picked = EditorGUILayout.Popup(FallbackLabel, selected, options);
                if (picked != selected)
                {
                    nameProp.stringValue = picked == 0 ? string.Empty : actions[picked - 1].actionName;
                }
            }

            int chosen = IndexOfAction(actions, nameProp.stringValue);
            bool takesArgument = chosen >= 0 && actions[chosen].TakesArgument;
            using (new EditorGUI.DisabledScope(!takesArgument))
            {
                EditorGUILayout.PropertyField(argProp, FallbackArgumentLabel);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Config actions with a usable name, first occurrence wins on duplicates.</summary>
        public static List<ActionDefinition> CollectActions(ActionConfig config)
        {
            List<ActionDefinition> result = new List<ActionDefinition>();
            if (config == null || config.validActions == null) return result;
            for (int i = 0; i < config.validActions.Count; i++)
            {
                ActionDefinition def = config.validActions[i];
                if (def == null || string.IsNullOrWhiteSpace(def.actionName)) continue;
                if (IndexOfAction(result, def.actionName) >= 0) continue;
                result.Add(def);
            }
            return result;
        }

        private static int IndexOfAction(List<ActionDefinition> actions, string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < actions.Count; i++)
            {
                if (string.Equals(actions[i].actionName, name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private GUIStyle WarningStyle
        {
            get
            {
                if (warningStyle == null)
                {
                    warningStyle = new GUIStyle(EditorStyles.miniLabel);
                    warningStyle.normal.textColor = new Color(0.95f, 0.65f, 0.2f);
                }
                return warningStyle;
            }
        }

        private GUIStyle DescriptionStyle
        {
            get
            {
                if (descriptionStyle == null)
                {
                    descriptionStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, fontStyle = FontStyle.Italic };
                }
                return descriptionStyle;
            }
        }
    }
}
