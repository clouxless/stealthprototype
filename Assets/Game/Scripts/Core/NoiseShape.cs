using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Builds flat meshes showing where a noise is heard: a circle of its radius on the floor,
    /// with the parts in other spaces (behind walls that seal it in) cut out. Used by the noise
    /// rings and the knock preview. Placeholder look ([PH]).
    /// </summary>
    public static class NoiseShape
    {
        static readonly List<Vector3> Verts = new List<Vector3>();
        static readonly List<Color> Colors = new List<Color>();
        static readonly List<int> Tris = new List<int>();

        public static void Begin()
        {
            Verts.Clear();
            Colors.Clear();
            Tris.Clear();
        }

        public static void End(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(Verts);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>A ring outline of this radius, skipping segments not open to the space.</summary>
        public static void Ring(Vector3 centre, float radius, float width, int space, Color color, int segments = 72)
        {
            float inner = Mathf.Max(0f, radius - width * 0.5f), outer = radius + width * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                float am = (a0 + a1) * 0.5f;
                Vector3 mid = centre + new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)) * radius;
                if (!SoundGrid.FloorOpenTo(mid, space)) continue;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad(centre + d0 * inner, centre + d0 * outer, centre + d1 * outer, centre + d1 * inner, color);
            }
        }

        /// <summary>A filled disc in grid-sized tiles, only where the floor is open to the space.</summary>
        public static void Disc(Vector3 centre, float radius, int space, Color color)
        {
            float step = SoundGrid.CellSize;
            float h = step * 0.5f;
            int n = Mathf.CeilToInt(radius / step);
            for (int z = -n; z <= n; z++)
                for (int x = -n; x <= n; x++)
                {
                    Vector3 offset = new Vector3(x * step, 0f, z * step);
                    if (offset.sqrMagnitude > radius * radius) continue;
                    Vector3 p = centre + offset;
                    if (!SoundGrid.FloorOpenTo(p, space)) continue;
                    Quad(p + new Vector3(-h, 0f, -h), p + new Vector3(-h, 0f, h), p + new Vector3(h, 0f, h), p + new Vector3(h, 0f, -h), color);
                }
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            for (int k = 0; k < 4; k++) Colors.Add(color);
            // Both windings so it shows from any camera angle (the cover swing looks low).
            Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
            Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
            Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 1);
            Tris.Add(i); Tris.Add(i + 3); Tris.Add(i + 2);
        }

        /// <summary>A world-space mesh object using the placeholder line material (vertex colours).</summary>
        public static MeshFilter CreateRenderer(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = name };
            mf.sharedMesh.MarkDynamic();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = FX.LineMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mf;
        }
    }
}
