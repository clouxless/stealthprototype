using UnityEngine;
using UnityEngine.Rendering;

namespace StealthProto
{
    /// <summary>
    /// Faint floor fan showing a guard's vision, clipped by walls and tinted by state.
    /// Secondary to the flashlight; toggle with F1.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VisionConeMesh : MonoBehaviour
    {
        public static bool Visible = true;

        public GuardVision vision;
        public GuardBrain brain;
        public int segments = 24;
        [Range(0f, 1f)] public float alpha = 0.16f;
        public float heightOffset = 0.04f;

        Mesh mesh;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Color[] colors;
        int[] triangles;

        void Awake()
        {
            mesh = new Mesh { name = "PH_VisionCone" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial == null) meshRenderer.sharedMaterial = FX.LineMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            vertices = new Vector3[segments + 2];
            colors = new Color[segments + 2];
            triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        void LateUpdate()
        {
            meshRenderer.enabled = Visible && vision != null;
            if (!meshRenderer.enabled) return;

            Vector3 origin = vision.eye.position;
            Vector3 forward = vision.eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            float half = vision.EffectiveHalfAngle;
            float range = vision.viewDistance;
            float floorY = transform.position.y + heightOffset;

            Color near = brain != null ? brain.CurrentColor : Color.white;
            near.a = alpha;
            Color far = near;
            far.a = alpha * 0.25f;

            vertices[0] = transform.InverseTransformPoint(new Vector3(origin.x, floorY, origin.z));
            colors[0] = near;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)segments);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
                float d = range;
                if (Occlusion.Raycast(origin, dir, range, vision.transform, vision.target, out RaycastHit hit)) d = hit.distance;
                Vector3 p = origin + dir * d;
                p.y = floorY;
                vertices[i + 1] = transform.InverseTransformPoint(p);
                colors[i + 1] = Color.Lerp(near, far, d / range);
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
