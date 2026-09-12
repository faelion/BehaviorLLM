using System.Collections.Generic;
using UnityEngine;

namespace BehaviorLLM.Core.Perception
{
    public class ConeVisionStrategy : IVisionStrategy
    {
        public float fieldOfView = 90f;

        public ConeVisionStrategy(float fov)
        {
            this.fieldOfView = fov;
        }

        public List<LLMContextObject> Scan(Transform eye, float range, LayerMask perceptionLayers, LayerMask occluderLayers)
        {
            // When occluderLayers is 0, the caller has opted out of the LOS step. We treat
            // it as "every candidate is visible if it's in the FoV", which matches the
            // behavior of Sphere mode plus an angle gate.
            bool checkLineOfSight = occluderLayers.value != 0;

            List<LLMContextObject> results = new List<LLMContextObject>();
            HashSet<int> seen = new HashSet<int>();
            Collider[] hits = Physics.OverlapSphere(eye.position, range, perceptionLayers);

            foreach(var hit in hits)
            {
                if(hit.transform == eye) continue;

                Vector3 dir = (hit.transform.position - eye.position).normalized;
                if(Vector3.Angle(eye.forward, dir) >= fieldOfView * 0.5f) continue;

                if (checkLineOfSight)
                {
                    // Raycast only against the explicit occluder mask. Previously this used
                    // ~perceptionLayers, which made every collider outside the perception
                    // mask block sight - including triggers and other non-visual colliders.
                    // Separating the masks makes the intended occlusion model explicit.
                    float dist = Vector3.Distance(eye.position, hit.transform.position);
                    if (Physics.Raycast(eye.position, dir, dist, occluderLayers)) continue;
                }

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
