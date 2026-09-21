using System.Linq;
using NUnit.Framework;
using Project.Samples.PrisonYard.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Project.Samples.PrisonYard.Tests
{
    public class PrisonYardArtTests
    {
        [TestCase("Knight")]
        [TestCase("Barbarian")]
        [TestCase("Mage")]
        [TestCase("Rogue")]
        [TestCase("Rogue_Hooded")]
        public void ShippedCharacter_HasNativeMeshesPlayableClipsAndCompleteController(string name)
        {
            string script = AssetDatabase.FindAssets("PrisonYardSceneBuilder t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).First(p => p.EndsWith("/PrisonYardSceneBuilder.cs"));
            string art = script.Replace("/Scripts/Editor/PrisonYardSceneBuilder.cs", "/Art/Characters");
            if (!AssetDatabase.IsValidFolder(art)) Assert.Ignore("Optional sample art has been removed.");
            string path = art + "/Native/" + name;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
            Assert.IsNotNull(prefab, "Shipped art must work without a GLB importer.");
            Assert.IsFalse(AssetDatabase.GetDependencies(path + ".prefab", true).Any(p => p.EndsWith(".glb")));
            if (name == "Knight")
            {
                string scene = script.Replace("/Scripts/Editor/PrisonYardSceneBuilder.cs", "/Scenes/PrisonYard.unity");
                Assert.IsFalse(AssetDatabase.GetDependencies(scene, true).Any(p => p.EndsWith(".glb")),
                    "The shipped scene must also be usable without the authoring importer.");
            }
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Assert.IsNotNull(renderer.sharedMesh);
                Assert.Greater(renderer.sharedMesh.vertexCount, 0);
                Assert.IsTrue(renderer.bones.All(b => b != null));
                Assert.IsTrue(renderer.sharedMaterials.All(m => m != null && m.mainTexture != null));
            }
            Assert.Greater(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length, 0);

            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path + ".asset").OfType<AnimationClip>().ToArray();
            Assert.AreEqual(8, clips.Length, "Export all locomotion, rest and gesture clips used by the visuals.");
            GameObject instance = Object.Instantiate(prefab);
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/PrisonYardArtTest");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                Assert.IsNotNull(animator);
                AnimationClip walk = clips.Single(c => c.name == "Walking_A");
                Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
                walk.SampleAnimation(animator.gameObject, 0.1f);
                Quaternion[] before = bones.Select(b => b.localRotation).ToArray();
                walk.SampleAnimation(animator.gameObject, 0.4f);
                Assert.IsTrue(bones.Where((bone, i) => Quaternion.Angle(before[i], bone.localRotation) > 0.1f).Any(),
                    "Walking must move the baked skeleton, not just exist as an asset.");
                PrisonYardAnimatorBuilder.Reset();
                AnimatorController controller = PrisonYardAnimatorBuilder.Build(path + ".asset", folder + "/Character.controller");
                Assert.IsNotNull(controller);
                var states = controller.layers[0].stateMachine.states;
                Assert.AreEqual(6, states.Length, "Locomotion, four gestures and resting.");
                Assert.IsTrue(states.All(s => s.state.motion != null));
                var tree = states.Select(s => s.state.motion).OfType<BlendTree>().Single();
                Assert.AreEqual(3, tree.children.Length);
                Assert.IsTrue(tree.children.All(c => c.motion != null));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                PrisonYardAnimatorBuilder.Reset();
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
