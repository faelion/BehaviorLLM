using System.Collections.Generic;
using UnityEngine;

namespace BehaviorLLM.Core.Perception
{
    public interface IVisionStrategy
    {
        // `perceptionLayers` selects which colliders count as candidate targets for the
        // OverlapSphere step. `occluderLayers` is consulted by strategies that perform a
        // line-of-sight raycast (currently Cone); a strategy that does no LOS check (e.g.
        // Sphere) ignores it. Pass <c>0</c> for `occluderLayers` to disable the LOS step
        // entirely - this is the safe default because the previous implicit
        // <c>~perceptionLayers</c> mask treated every non-perception collider as an
        // occluder, which surprised users with transparent or trigger colliders.
        List<LLMContextObject> Scan(Transform eye, float range, LayerMask perceptionLayers, LayerMask occluderLayers);
    }
}
