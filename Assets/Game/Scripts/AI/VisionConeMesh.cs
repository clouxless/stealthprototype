using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StealthProto
{
    /// <summary>
    /// The guard's vision drawn on the surfaces it actually covers: a grid draped over floors,
    /// platform tops and catwalks, shown only where the guard can see (its visible height layers,
    /// line of sight from the eye). So it's clear at a glance whether a cone reaches the catwalk.
    /// Tinted by state. Toggle with F1.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VisionConeMesh : MonoBehaviour
    {
        public static bool Visible = true;

        public GuardVision vision;
        public GuardBrain brain;
        public int segments = 20;
        [Tooltip("Distance between rows of the grid, in metres.")]
        public float rowStep = 0.75f;
        public float updateInterval = 0.1f;
        [Range(0f, 1f)] public float alpha = 0.2f;
        public float heightOffset = 0.04f;
        [Tooltip("Cells whose corners differ in height by more than this are skipped (no sheets up walls).")]
        public float maxStepInCell = 0.4f;

        Mesh mesh;
        MeshRenderer meshRenderer;
        Vector3[] vertices;
        Color[] colors;
        bool[] valid;
        float[] heights;
        readonly List<int> triangles = new List<int>();
        int rows;
        float timer;

        void Awake()
        {
            mesh = new Mesh { name = "PH_VisionCone" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer.sharedMaterial == null) meshRenderer.sharedMaterial = FX.LineMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        void Allocate()
        {
            rows = Mathf.Max(2, Mathf.CeilToInt(vision.MaxRange / rowStep) + 1);
            int count = (segments + 1) * rows;
            vertices = new Vector3[count];
            colors = new Color[count];
            valid = new bool[count];
            heights = new float[count];
        }

        void LateUpdate()
        {
            meshRenderer.enabled = Visible && vision != null && vision.eye != null;
            if (!meshRenderer.enabled) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = updateInterval;

            if (vertices == null || rows != Mathf.Max(2, Mathf.CeilToInt(vision.MaxRange / rowStep) + 1)) Allocate();
            Rebuild();
        }

        void Rebuild()
        {
            Vector3 eye = vision.eye.position;
            Vector3 forward = vision.eye.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            float half = vision.EffectiveHalfAngle;
            float maxRange = vision.MaxRange;
            Transform self = vision.transform;
            Transform target = vision.target;

            Color baseColor = brain != null ? brain.CurrentColor : Color.white;
            float castTop = eye.y + 0.2f; // guards don't see above their eyes unless searching
            if (vision.LookUp) castTop += 6f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)segments);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
                // The narrow spike dead ahead is the long sight: only lit targets are seen out there.
                float range = vision.RangeAtAngle(angle);
                for (int j = 0; j < rows; j++)
                {
                    int v = i * rows + j;
                    float dist = Mathf.Min(range, 0.3f + j * rowStep);
                    Vector3 column = new Vector3(eye.x, castTop, eye.z) + dir * dist;

                    bool ok = false;
                    float y = transform.position.y;
                    if (Occlusion.Raycast(column, Vector3.down, 12f, self, target, out RaycastHit hit) && hit.normal.y > 0.6f)
                    {
                        y = hit.point.y;
                        ok = vision.CanSeeLayer(HeightLayers.LayerOf(y))
                             && !Occlusion.Blocked(eye, hit.point + Vector3.up * 0.3f, self, target);
                    }

                    heights[v] = y;
                    valid[v] = ok;
                    vertices[v] = transform.InverseTransformPoint(new Vector3(column.x, y + heightOffset, column.z));
                    Color c = baseColor;
                    float fade = dist > vision.viewDistance ? 0.5f : 1f; // the long-sight spike is fainter
                    c.a = ok ? Mathf.Lerp(alpha, alpha * 0.3f, dist / maxRange) * fade : 0f;
                    colors[v] = c;
                }
            }

            triangles.Clear();
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < rows - 1; j++)
                {
                    int a = i * rows + j, b = (i + 1) * rows + j, c = (i + 1) * rows + j + 1, d = i * rows + j + 1;
                    if (!(valid[a] || valid[b] || valid[c] || valid[d])) continue;
                    float min = Mathf.Min(Mathf.Min(heights[a], heights[b]), Mathf.Min(heights[c], heights[d]));
                    float max = Mathf.Max(Mathf.Max(heights[a], heights[b]), Mathf.Max(heights[c], heights[d]));
                    if (max - min > maxStepInCell) continue;
                    // Clockwise seen from above: near-left, far-left, far-right / near-left, far-right, near-right.
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
