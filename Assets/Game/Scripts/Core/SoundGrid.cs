using UnityEngine;
using UnityEngine.SceneManagement;

namespace StealthProto
{
    /// <summary>
    /// Which spaces sound can reach. The level is cut into cells of open air; cells that touch
    /// through open air (doorways, gaps, open sky, the air between a catwalk and the floor) belong
    /// to the same space. Closed doors and walls are solid, and vents and grates (SoundDuct) are
    /// left out, so they never connect two spaces.
    ///
    /// The noise rule built on this (NoiseSystem): a noise is a straight-line circle, and a
    /// listener inside the circle hears it if they're in the same space as the noise, however long
    /// the way round. A fully sealed space doesn't hear it.
    ///
    /// Built once per scene from the colliders (characters and triggers ignored). Geometry that
    /// moves at runtime isn't tracked: call Invalidate() after changing it.
    /// </summary>
    public static class SoundGrid
    {
        public const float CellSize = 0.5f;
        /// <summary>Above this many cells the grid isn't built and every space counts as connected.</summary>
        public const int MaxCells = 4000000;

        /// <summary>No grid, or a point outside it: treated as connected to everything.</summary>
        public const int Unknown = int.MinValue;
        public const int Solid = -1;
        public const int InDuct = -2;

        static bool built;
        static Vector3 origin;
        static int nx, ny, nz;
        static int[] space; // per cell: space id (0+), Solid or InDuct
        static readonly Collider[] Overlap = new Collider[32];

        public static int SpaceCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            built = false;
            space = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BuildForFirstScene() => Build();

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            built = false;
            Build();
        }

        /// <summary>Rebuild on next use (geometry or ducts changed).</summary>
        public static void Invalidate() => built = false;

        static void EnsureBuilt()
        {
            if (!built) Build();
        }

        static bool IsCharacter(Collider c) =>
            c is CharacterController || c.GetComponentInParent<Health>() != null
            || c.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null;

        public static void Build()
        {
            built = true;
            space = null;
            nx = ny = nz = 0;
            SpaceCount = 0;
            Physics.SyncTransforms();

            bool any = false;
            Bounds bounds = default;
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!c.enabled || c.isTrigger || IsCharacter(c)) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }
            if (!any) return;

            // A margin all round, and open sky above the tallest thing, so outdoor areas join up over walls.
            bounds.min -= new Vector3(CellSize * 2f, CellSize, CellSize * 2f);
            bounds.max += new Vector3(CellSize * 2f, CellSize * 2f, CellSize * 2f);
            origin = bounds.min;
            nx = Mathf.CeilToInt(bounds.size.x / CellSize);
            ny = Mathf.CeilToInt(bounds.size.y / CellSize);
            nz = Mathf.CeilToInt(bounds.size.z / CellSize);
            long total = (long)nx * ny * nz;
            if (total > MaxCells)
            {
                Debug.LogWarning($"[PH] SoundGrid: level too big ({total} cells). Noise ignores walls.");
                nx = ny = nz = 0;
                return;
            }

            space = new int[total];
            // Slightly smaller than a cell, so a wall that only touches a cell's face doesn't fill it.
            Vector3 half = Vector3.one * (CellSize * 0.49f);
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        Vector3 centre = CellCentre(x, y, z);
                        int state = 0;
                        if (Physics.CheckBox(centre, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        {
                            int n = Physics.OverlapBoxNonAlloc(centre, half, Overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                            for (int i = 0; i < n; i++)
                                if (!IsCharacter(Overlap[i])) { state = Solid; break; }
                        }
                        if (state == 0 && InsideDuct(centre)) state = InDuct;
                        space[Index(x, y, z)] = state == 0 ? Unknown : state; // Unknown = open air, not yet flooded
                    }

            // Flood fill the open air into spaces.
            var queue = new int[total];
            int nextId = 0;
            for (int start = 0; start < total; start++)
            {
                if (space[start] != Unknown) continue;
                int id = nextId++;
                int head = 0, tail = 0;
                queue[tail++] = start;
                space[start] = id;
                while (head < tail)
                {
                    int i = queue[head++];
                    int x = i % nx, rest = i / nx, y = rest % ny, z = rest / ny;
                    TryVisit(x - 1, y, z); TryVisit(x + 1, y, z);
                    TryVisit(x, y - 1, z); TryVisit(x, y + 1, z);
                    TryVisit(x, y, z - 1); TryVisit(x, y, z + 1);
                }

                void TryVisit(int vx, int vy, int vz)
                {
                    if (vx < 0 || vy < 0 || vz < 0 || vx >= nx || vy >= ny || vz >= nz) return;
                    int j = Index(vx, vy, vz);
                    if (space[j] != Unknown) return;
                    space[j] = id;
                    queue[tail++] = j;
                }
            }
            SpaceCount = nextId;
        }

        static bool InsideDuct(Vector3 p)
        {
            foreach (var d in SoundDuct.All)
                if (d.Box.Contains(p)) return true;
            return false;
        }

        static int Index(int x, int y, int z) => x + nx * (y + ny * z);

        static Vector3 CellCentre(int x, int y, int z) =>
            origin + new Vector3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, (z + 0.5f) * CellSize);

        static int CellAt(Vector3 p)
        {
            Vector3 local = (p - origin) / CellSize;
            int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(local.y), z = Mathf.FloorToInt(local.z);
            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return Unknown;
            return space[Index(x, y, z)];
        }

        static readonly Vector3[] Nudges = BuildNudges();

        static Vector3[] BuildNudges()
        {
            // Straight up first (a point on a floor), then the nearest neighbours.
            var list = new System.Collections.Generic.List<Vector3>
            {
                Vector3.zero, Vector3.up * CellSize, Vector3.up * CellSize * 2f
            };
            var ring = new System.Collections.Generic.List<Vector3>();
            for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        if (x != 0 || y != 0 || z != 0) ring.Add(new Vector3(x, y, z) * CellSize);
            ring.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
            list.AddRange(ring);
            list.Add(Vector3.up * CellSize * 3f);
            return list.ToArray();
        }

        /// <summary>
        /// The space a point is in. Points inside a wall or floor (feet on the ground, a noise on a
        /// wall) take the nearest open cell, looking up first. InDuct inside a vent or grate.
        /// </summary>
        public static int SpaceAt(Vector3 p)
        {
            EnsureBuilt();
            if (space == null) return Unknown;
            foreach (var nudge in Nudges)
            {
                int s = CellAt(p + nudge);
                if (s >= 0 || s == Unknown || s == InDuct) return s;
            }
            return Unknown; // buried in geometry: don't block anything
        }

        /// <summary>
        /// For drawing: is this floor point open to the given space? Only looks straight up a
        /// little (the floor cell and the two above), never sideways, so outlines stop at walls.
        /// </summary>
        public static bool FloorOpenTo(Vector3 p, int target)
        {
            EnsureBuilt();
            if (space == null || target == Unknown) return true;
            for (int i = 0; i < 3; i++)
            {
                int s = CellAt(p + Vector3.up * (CellSize * (0.5f + i)));
                if (s == Unknown) return true;
                if (s >= 0) return s == target;
            }
            return false;
        }

        /// <summary>True if sound in space a reaches space b.</summary>
        public static bool Connected(int a, int b)
        {
            if (a == Unknown || b == Unknown) return true;
            if (a < 0 || b < 0) return false;
            return a == b;
        }

        public static bool Connected(Vector3 a, Vector3 b) => Connected(SpaceAt(a), SpaceAt(b));
    }
}
