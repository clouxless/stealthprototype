using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StealthProto
{
    /// <summary>
    /// Draws a spot light's pool as a crisp, flat overlay on the surfaces it reaches, in the same
    /// draped style as the vision cones, instead of a soft rendered beam. Each cell shows exactly
    /// the tier the light gives there (StealthLight.TierAt): bright where Lit, faint where Dim,
    /// nothing where Dark or shadowed. So what you see is what the guards get. Tinted with the
    /// light's colour (guard state for flashlights).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LightFootprintMesh : MonoBehaviour
    {
        public StealthLight source;
        public int segments = 36;
        public float rowStep = 0.4f;
        public float updateInterval = 0.05f;
        [Range(0f, 1f)] public float litAlpha = 0.42f;
        [Range(0f, 1f)] public float dimAlpha = 0.14f;
        public float heightOffset = 0.05f;
        public float maxStepInCell = 0.4f;

        Mesh mesh;
        Light lightComponent;
        Vector3[] points;
        LightTier[] tiers;
        bool[] valid;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        int rows;
        float timer;

        void Awake()
        {
            mesh = new Mesh { name = "PH_LightFootprint" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (mr.sharedMaterial == null) mr.sharedMaterial = FX.LineMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (source != null) lightComponent = source.GetComponent<Light>();
        }

        void LateUpdate()
        {
            // World-space mesh: keep this object at the origin with no rotation.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = updateInterval;
            Rebuild();
        }

        void Rebuild()
        {
            mesh.Clear();
            if (source == null || lightComponent == null || !lightComponent.enabled || lightComponent.intensity <= 0f) return;

            float range = lightComponent.range;
            int r = Mathf.Max(2, Mathf.CeilToInt(range / rowStep) + 1);
            if (points == null || r != rows)
            {
                rows = r;
                int n = (segments + 1) * rows;
                points = new Vector3[n];
                tiers = new LightTier[n];
                valid = new bool[n];
            }

            Vector3 origin = source.transform.position;
            Vector3 forward = source.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();
            float half = lightComponent.spotAngle * 0.5f + 2f;
            float castTop = origin.y + 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(-half, half, i / (float)segments), 0f) * forward;
                for (int j = 0; j < rows; j++)
                {
                    int v = i * rows + j;
                    float dist = 0.2f + j * rowStep;
                    Vector3 column = new Vector3(origin.x, castTop, origin.z) + dir * dist;
                    valid[v] = false;
                    tiers[v] = LightTier.Dark;
                    if (Occlusion.Raycast(column, Vector3.down, 12f, source.ignoreRoot, null, out RaycastHit hit) && hit.normal.y > 0.6f)
                    {
                        points[v] = hit.point + Vector3.up * heightOffset;
                        tiers[v] = source.TierAt(hit.point + Vector3.up * 0.05f, null);
                        valid[v] = true;
                    }
                }
            }

            verts.Clear();
            colors.Clear();
            tris.Clear();
            Color tint = lightComponent.color;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < rows - 1; j++)
                {
                    int a = i * rows + j, b = (i + 1) * rows + j, c = (i + 1) * rows + j + 1, d = i * rows + j + 1;
                    if (!(valid[a] && valid[b] && valid[c] && valid[d])) continue;
                    float min = Mathf.Min(Mathf.Min(points[a].y, points[b].y), Mathf.Min(points[c].y, points[d].y));
                    float max = Mathf.Max(Mathf.Max(points[a].y, points[b].y), Mathf.Max(points[c].y, points[d].y));
                    if (max - min > maxStepInCell) continue;

                    // Binary per cell: the weakest corner decides, so edges stay crisp.
                    LightTier t = tiers[a];
                    if (tiers[b] < t) t = tiers[b];
                    if (tiers[c] < t) t = tiers[c];
                    if (tiers[d] < t) t = tiers[d];
                    if (t == LightTier.Dark) continue;

                    Color col = tint;
                    col.a = t == LightTier.Lit ? litAlpha : dimAlpha;
                    int k = verts.Count;
                    verts.Add(points[a]); verts.Add(points[d]); verts.Add(points[c]); verts.Add(points[b]);
                    colors.Add(col); colors.Add(col); colors.Add(col); colors.Add(col);
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                    tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
                }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
