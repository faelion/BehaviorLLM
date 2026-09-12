using UnityEngine;
using System;
using System.Reflection;

namespace BehaviorLLM.Core.Perception
{
    [Serializable]
    public class ContextDataBinding
    {
        [Tooltip("The component holding the value, for example a Health script. Drag it " +
                 "here.")]
        public Component sourceComponent;

        [Tooltip("The name of the public field, property or method on that component to " +
                 "read, spelled exactly. A method must take no arguments and return a " +
                 "value.")]
        public string memberName;

        // --- Cache for Runtime Performance ---
        private bool isInitialized = false;
        private MemberInfo cachedMember;
        private MethodInfo cachedMethod;
        private FieldInfo cachedField;
        private PropertyInfo cachedProperty;

        public string GetValue()
        {
            if (sourceComponent == null || string.IsNullOrEmpty(memberName)) return "N/A";

            if (!isInitialized) Initialize();

            try
            {
                object result = null;
                if (cachedField != null) result = cachedField.GetValue(sourceComponent);
                else if (cachedProperty != null) result = cachedProperty.GetValue(sourceComponent);
                else if (cachedMethod != null) result = cachedMethod.Invoke(sourceComponent, null);
                
                return result != null ? result.ToString() : "null";
            }
            catch
            {
                return "Error";
            }
        }

        private void Initialize()
        {
            if (sourceComponent == null) return;
            Type type = sourceComponent.GetType();
            
            // Try cache Field
            cachedField = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (cachedField != null) { isInitialized = true; return; }

            // Try cache Property
            cachedProperty = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (cachedProperty != null) { isInitialized = true; return; }

            // Try cache Method
            cachedMethod = type.GetMethod(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (cachedMethod != null) { isInitialized = true; return; }
            
            isInitialized = true; // Tried and failed, stop trying
        }
    }
}
