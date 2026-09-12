using System.Collections.Generic;
using UnityEngine;

namespace BehaviorLLM.Core.Perception
{
    public class SphereVisionStrategy : IVisionStrategy
    {
        // Just a helper logic class, or could be a ScriptableObject/Component.
        // For simplicity, let's keep logic pure C#.
        
        // Sphere mode does not perform a line-of-sight raycast, so `occluderLayers` is
        // intentionally unused here. The interface forces the parameter through for
        // signature consistency with strategies that do (e.g. Cone).
        public List<LLMContextObject> Scan(Transform eye, float range, LayerMask perceptionLayers, LayerMask occluderLayers)
        {
            List<LLMContextObject> results = new List<LLMContextObject>();
            HashSet<int> seen = new HashSet<int>();
            Collider[] hits = Physics.OverlapSphere(eye.position, range, perceptionLayers);
            
            foreach(var hit in hits)
            {
                if(hit.transform == eye) continue; // Ignore self if checking from self root
                var ctx = hit.GetComponent<LLMContextObject>();
                if(ctx == null) continue;

                int id = ctx.GetInstanceID();
                if (seen.Add(id))
                {
                    results.Add(ctx);
                }
            }
            return results;
        }
    }
}
