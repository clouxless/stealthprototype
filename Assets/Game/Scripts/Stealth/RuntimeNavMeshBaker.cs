using Unity.AI.Navigation;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Bakes the NavMesh when the scene starts, so the graybox can be rebuilt freely without
    /// managing baked NavMesh assets. Runs before everything else.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavMeshSurface))]
    public class RuntimeNavMeshBaker : MonoBehaviour
    {
        void Awake() => GetComponent<NavMeshSurface>().BuildNavMesh();
    }
}
