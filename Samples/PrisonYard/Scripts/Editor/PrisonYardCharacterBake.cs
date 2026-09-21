using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Project.Samples.PrisonYard.Editor
{
    /// <summary>
    /// Maintainer utility: bake imported KayKit GLBs into native Unity prefabs and subassets.
    /// Only regeneration needs a glTF importer; consuming or building the sample does not.
    /// </summary>
    public static class PrisonYardCharacterBake
    {
        private static readonly string[] Models = { "Knight", "Barbarian", "Mage", "Rogue", "Rogue_Hooded" };
        private static readonly HashSet<string> Clips = new HashSet<string>
        {
            "Idle", "Walking_A", "Running_A", "Unarmed_Melee_Attack_Punch_A",
            "Interact", "PickUp", "Hit_A", "Sit_Floor_Idle"
        };

        /// <summary>Regenerates the shipped native art in a writable Assets checkout with imported GLBs.</summary>
        public static void Bake()
        {
            string art = PrisonYardPaths.Root + "/Art/Characters";
            if (!art.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Bake from a writable Assets checkout, not an installed package.");
            string output = art + "/Native";
            if (!AssetDatabase.IsValidFolder(output)) AssetDatabase.CreateFolder(art, "Native");
            foreach (string name in Models) BakeModel(art, output, name);
            AssetDatabase.SaveAssets();
        }

        private static void BakeModel(string art, string output, string name)
        {
            string sourcePath = art + "/" + name + ".glb";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("Import " + sourcePath + " with glTFast before baking.");
            Object[] imported = AssetDatabase.LoadAllAssetsAtPath(sourcePath);
            foreach (string clip in Clips)
                if (!imported.OfType<AnimationClip>().Any(c => c.name == clip))
                    throw new InvalidOperationException(name + " is missing " + clip);

            string assetPath = output + "/" + name + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
                AssetDatabase.CreateAsset(new Mesh { name = name + "Assets" }, assetPath);
            Object[] existing = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var copies = new Dictionary<Object, Object>();
            foreach (Object original in imported)
            {
                if (!(original is Mesh) && !(original is Avatar) &&
                    !(original is AnimationClip animation && Clips.Contains(animation.name))) continue;
                Object copy = Array.Find(existing, item => item.GetType() == original.GetType() && item.name == original.name);
                if (copy == null)
                {
                    copy = Object.Instantiate(original);
                    copy.name = original.name;
                    copy.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(copy, assetPath);
                }
                else EditorUtility.CopySerialized(original, copy);
                if (copy is AnimationClip clip)
                {
                    clip.legacy = false;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = clip.name == "Idle" || clip.name == "Walking_A" ||
                                        clip.name == "Running_A" || clip.name == "Sit_Floor_Idle";
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                }
                EditorUtility.SetDirty(copy);
                copies.Add(original, copy);
            }

            string materialPath = output + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            string texture = name.StartsWith("Rogue") ? "rogue" : name.ToLowerInvariant();
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(art + "/" + texture + "_texture.png");
            EditorUtility.SetDirty(material);

            GameObject instance = Object.Instantiate(source);
            try
            {
                instance.name = name;
                foreach (Component component in instance.GetComponentsInChildren<Component>(true))
                {
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue != null && copies.TryGetValue(property.objectReferenceValue, out Object copy))
                            property.objectReferenceValue = copy;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                {
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                }
                string prefabPath = output + "/" + name + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                AssetDatabase.SaveAssets();
                if (AssetDatabase.GetDependencies(prefabPath, true).Any(path => path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(prefabPath + " still references a GLB.");
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
