using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace StealthProto
{
    /// <summary>
    /// Bakes the NavMesh when the scene starts, so the graybox can be rebuilt freely without
    /// managing baked NavMesh assets. Runs before everything else.
    ///
    /// overrideAgent bakes with a slimmer agent than the project's default (Humanoid: 0.5 m radius,
    /// 2 m tall). The default eats 0.5 m off each side of a passage, which leaves almost nothing of
    /// a 1.3 m railed stair lane, so guards couldn't path up or down the stacked stairs. Guards are
    /// 0.35 m radius, 1.8 m tall; the bake uses those (a touch slimmer) under the same agent type.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavMeshSurface))]
    public class RuntimeNavMeshBaker : MonoBehaviour
    {
        public bool overrideAgent = true;
        public float agentRadius = 0.3f;
        public float agentHeight = 1.8f;

        void Awake()
        {
            var surface = GetComponent<NavMeshSurface>();
            if (!overrideAgent)
            {
                surface.BuildNavMesh();
                return;
            }

            NavMeshBuildSettings settings = surface.GetBuildSettings(); // agent type + voxel override
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(transform, surface.layerMask, surface.useGeometry, surface.defaultArea,
                new List<NavMeshBuildMarkup>(), sources);
            var bounds = new Bounds(Vector3.zero, new Vector3(800f, 300f, 800f)); // local to this transform
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, transform.position, transform.rotation);
            if (data == null)
            {
                Debug.LogWarning("[PH] Custom NavMesh bake failed; falling back to the surface's default bake.");
                surface.BuildNavMesh();
                return;
            }
            NavMesh.AddNavMeshData(data, transform.position, transform.rotation);
        }
    }
}
