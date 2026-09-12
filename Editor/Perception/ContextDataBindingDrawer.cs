using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using BehaviorLLM.Core.Perception;

namespace BehaviorLLM.Editor.Perception
{
    [CustomPropertyDrawer(typeof(ContextDataBinding))]
    public class ContextDataBindingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2 + 4; // 2 lines
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Calculate rects
            Rect componentRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            Rect memberRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2, position.width, EditorGUIUtility.singleLineHeight);

            // Draw Component Field
            SerializedProperty componentProp = property.FindPropertyRelative("sourceComponent");
            SerializedProperty memberNameProp = property.FindPropertyRelative("memberName");
            
            EditorGUI.PropertyField(componentRect, componentProp, new GUIContent("Source Component"));

            // Draw Dropdown if Component is selected
            if (componentProp.objectReferenceValue != null)
            {
                Component comp = componentProp.objectReferenceValue as Component;
                if (comp != null)
                {
                    string[] options = GetBindableMembers(comp.GetType());
                    int index = Array.IndexOf(options, memberNameProp.stringValue);
                    
                    if (index == -1) index = 0; // Default or "None"

                    int newIndex = EditorGUI.Popup(memberRect, "Member To Bind", index, options);
                    
                    if (newIndex >= 0 && newIndex < options.Length)
                    {
                        memberNameProp.stringValue = options[newIndex];
                    }
                }
            }
            else
            {
                EditorGUI.LabelField(memberRect, "Select a Component first.");
            }

            EditorGUI.EndProperty();
        }

        private string[] GetBindableMembers(Type type)
        {
            List<string> members = new List<string>();
            members.Add(""); // Empty option

            // Fields
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (IsBindableType(f.FieldType))
                    members.Add(f.Name);
            }

            // Properties
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.CanRead && IsBindableType(p.PropertyType))
                    members.Add(p.Name);
            }

            // Methods (Parameterless, returning value)
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!m.IsSpecialName && m.GetParameters().Length == 0 && IsBindableType(m.ReturnType))
                    members.Add(m.Name);
            }

            return members.ToArray();
        }

        private bool IsBindableType(Type t)
        {
            // Allow primitives, strings, enums, vectors
            return t.IsPrimitive || t == typeof(string) || t.IsEnum || t == typeof(Vector3) || t == typeof(Vector2);
        }
    }
}
