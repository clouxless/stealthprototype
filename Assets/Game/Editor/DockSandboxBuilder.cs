using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using G = StealthProto.EditorTools.GrayboxBuilder;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// Builds the dock sandbox from Cloux's layout: a yard of six sight-blocking blocks between
    /// a bottomless pit on the west and an elevator exit on the east. A ladder by the start leads
    /// up to a catwalk that runs the west wall from south to north, over the pit.
    /// The floor plan is the text map below, so tweaks are an edit plus a rebuild.
    /// Everything it makes is a placeholder (Assets/_Placeholder, PH_*).
    /// </summary>
    public static class DockSandboxBuilder
    {
        const string ScenePath = G.Root + "/Scenes/PH_DockSandbox.unity";

        // Each cell is 2 x 2 m. Row 0 is north. Columns run west to east.
        // X nothing (walled off)   . floor   O bottomless pit   G elevator (exit)
        // L low block (1.1 m: hides legs, not head)   T tall block (2.6 m: blocks sight, climbable)
        static readonly string[] Map =
        {
            "........XXXXXXXXXXXXXXXXX", //  0
            "....TT..XXXXXXXXXXXXXXXXX", //  1
            "....TT...............TTXX", //  2
            ".......................XX", //  3  north lane
            "OOOOOOO.TTT..TTT.TTT...XX", //  4
            "OOOOOOO.TTT..TTT.TTT...GG", //  5
            "OOOOOOO................GG", //  6  middle lane
            "OOOOOOO................GG", //  7
            "OOOOOOO.TTT..TTT.TTT...XX", //  8
            "OOOOOOO.TTT..TTT.TTT.XXXX", //  9
            "........TTT..TTT.TTT.XXXX", // 10
            "..TT...............L.XXXX", // 11  south lane
            "..TT....XXXXXXXXXXXXXXXXX", // 12
            "........XXXXXXXXXXXXXXXXX", // 13
        };

        const float Cell = 2f;
        const float DeckThickness = 1.5f;
        const float WallHeight = 4.5f;      // above the 4.2 m climb limit
        const float WallThickness = 0.5f;
        const float CoverInset = 0.1f;

        // West catwalk: column 0, from the north wall to the ladder at its south end.
        const int CatwalkColumn = 0, CatwalkNorthRow = 0, CatwalkSouthRow = 12;
        const float CatwalkTop = 3.2f, CatwalkThickness = 0.3f;

        static int Rows => Map.Length;
        static int Cols => Map[0].Length;

        static char At(int c, int r) => c < 0 || r < 0 || c >= Cols || r >= Rows ? 'X' : Map[r][c];
        static bool IsFloor(char ch) => ch != 'X' && ch != 'O';

        /// <summary>World position of a cell centre (fractions allowed). Row 0 is north (+Z).</summary>
        static Vector3 CellPos(float c, float r, float y = 0f) =>
            new Vector3((c - (Cols - 1) * 0.5f) * Cell, y, ((Rows - 1) * 0.5f - r) * Cell);

        [MenuItem("Stealth Prototype/Build Dock Sandbox Scene", priority = 1)]
        public static void Build()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
            {
                EditorUtility.DisplayDialog("URP not found",
                    "This builder expects a URP project (Unity Hub 'Universal 3D' template). See README.md.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            G.EnsureFolder(G.Root + "/Materials");
            G.EnsureFolder(G.Root + "/Textures");
            G.EnsureFolder(G.Root + "/Scenes");
            G.MeshCache.Clear();
            G.CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            G.SetupLighting();

            var level = new GameObject("PH_Level");
            BuildFloor(level.transform);
            BuildWalls(level.transform);
            BuildBlocks(level.transform);
            BuildCatwalk(level.transform);
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            level.AddComponent<RuntimeNavMeshBaker>();

            BuildLamps(level.transform);
            BuildPitHazard();   // outside PH_Level so the NavMesh never sees it
            BuildGoal();

            var player = G.BuildPlayer(CellPos(1f, 13f));
            player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            G.BuildCamera(player);
            BuildGuards(player.transform);

            new GameObject("PH_DebugHUD").AddComponent<DebugHUD>();
            new GameObject("PH_LastKnownMarker").AddComponent<LastKnownMarker>();
            new GameObject("HeightLayers").AddComponent<HeightLayers>(); // Floor < 0.75 m <= Platform < 2.75 m <= Catwalk

            EditorSceneManager.SaveScene(scene, ScenePath);
            G.AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PH] Dock sandbox built at {ScenePath}. Press Play.");
        }

        // ---------------------------------------------------------------- level

        static void BuildFloor(Transform root)
        {
            var floor = G.Group(root, "PH_Floor");
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (IsFloor(At(c, r)))
                        // One block per cell keeps the number of lights touching each block low.
                        G.Block(floor, "PH_FloorTile", CellPos(c, r, -DeckThickness * 0.5f), new Vector3(Cell, DeckThickness, Cell));
        }

        /// <summary>A wall on every cell edge between the playable area (floor or pit) and nothing.</summary>
        static void BuildWalls(Transform root)
        {
            var walls = G.Group(root, "PH_Walls");
            var sides = new[] { (dc: 0, dr: -1), (dc: 0, dr: 1), (dc: 1, dr: 0), (dc: -1, dr: 0) };
            float height = WallHeight + DeckThickness;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    if (At(c, r) == 'X') continue;
                    foreach (var (dc, dr) in sides)
                    {
                        if (At(c + dc, r + dr) != 'X') continue;
                        // Grid +r is world -Z.
                        Vector3 outward = new Vector3(dc, 0f, -dr);
                        Vector3 center = CellPos(c, r, height * 0.5f - DeckThickness) + outward * (Cell + WallThickness) * 0.5f;
                        // Overlap the corners so there are no gaps where two walls meet.
                        float length = Cell + WallThickness * 2f;
                        var size = dc != 0 ? new Vector3(WallThickness, height, length) : new Vector3(length, height, WallThickness);
                        G.Block(walls, "PH_Wall", center, size);
                    }
                }
        }

        /// <summary>Sight blocks. Touching cells of the same type merge into one rectangle.</summary>
        static void BuildBlocks(Transform root)
        {
            var blocks = G.Group(root, "PH_Blocks");
            var used = new bool[Cols, Rows];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    char ch = At(c, r);
                    float height = ch == 'T' ? 2.6f : ch == 'L' ? 1.1f : 0f;
                    if (height <= 0f || used[c, r]) continue;

                    int w = 1;
                    while (At(c + w, r) == ch && !used[c + w, r]) w++;
                    int h = 1;
                    while (RowMatches(c, r + h, w, ch, used)) h++;
                    for (int y = r; y < r + h; y++)
                        for (int x = c; x < c + w; x++)
                            used[x, y] = true;

                    var center = CellPos(c + (w - 1) * 0.5f, r + (h - 1) * 0.5f, height * 0.5f);
                    var size = new Vector3(w * Cell - CoverInset * 2f, height, h * Cell - CoverInset * 2f);
                    G.Block(blocks, ch == 'T' ? "PH_BlockTall" : "PH_BlockLow", center, size);
                }
        }

        static bool RowMatches(int c, int r, int w, char ch, bool[,] used)
        {
            if (r >= Rows) return false;
            for (int x = c; x < c + w; x++)
                if (At(x, r) != ch || used[x, r]) return false;
            return true;
        }

        /// <summary>
        /// High ground along the west wall, from the ladder by the start to the north end, passing
        /// over the pit. Guards can't climb, so it's theirs to watch but never to walk. Its edges
        /// are high enough to hang from, including over the pit.
        /// </summary>
        static void BuildCatwalk(Transform root)
        {
            var catwalk = G.Group(root, "PH_Catwalk");
            Vector3 north = CellPos(CatwalkColumn, CatwalkNorthRow), south = CellPos(CatwalkColumn, CatwalkSouthRow);
            float length = north.z - south.z + Cell;
            float y = CatwalkTop - CatwalkThickness * 0.5f;
            G.Block(catwalk, "PH_CatwalkDeck", new Vector3(north.x, y, (north.z + south.z) * 0.5f), new Vector3(Cell, CatwalkThickness, length));

            // Pillars against the wall wherever there's floor under the catwalk, so guards can walk beneath.
            float pillarX = north.x - Cell * 0.5f + 0.2f;
            float pillarHeight = CatwalkTop - CatwalkThickness;
            for (int r = CatwalkNorthRow; r <= CatwalkSouthRow; r += 3)
                if (IsFloor(At(CatwalkColumn, r)))
                    G.Block(catwalk, "PH_CatwalkPillar", new Vector3(pillarX, pillarHeight * 0.5f, CellPos(CatwalkColumn, r).z), new Vector3(0.3f, pillarHeight, 0.3f));

            // Ladder: a climbable plate under the catwalk's east edge at its south end. Face it
            // and press E to climb (it's within the 4.2 m climb limit).
            Vector3 foot = CellPos(CatwalkColumn, CatwalkSouthRow) + Vector3.right * (Cell * 0.5f - 0.075f);
            var ladder = G.Block(catwalk, "PH_Ladder", foot + Vector3.up * CatwalkTop * 0.5f, new Vector3(0.15f, CatwalkTop, 1.2f));
            for (float h = 0.35f; h < CatwalkTop; h += 0.35f)
                G.Visual(PrimitiveType.Cube, ladder.transform, "PH_LadderRung",
                    new Vector3(0.1f, h - CatwalkTop * 0.5f, 0f), new Vector3(0.05f, 0.05f, 1f), G.noseMat);
        }

        /// <summary>
        /// Downward lamps at lane crossings leave dark runs between them. The start and the pit side
        /// stay dark. The elevator is lit, so the exit always means crossing light.
        /// </summary>
        static void BuildLamps(Transform parent)
        {
            var lamps = new GameObject("PH_Lamps").transform;
            lamps.SetParent(parent, false);
            G.Lamp(lamps, CellPos(11.5f, 3f, 3.4f), 6f);   // north lane, west crossing
            G.Lamp(lamps, CellPos(16f, 6.5f, 3.4f), 6f);   // middle lane, east crossing
            G.Lamp(lamps, CellPos(7f, 11f, 3.4f), 6f);     // south lane by the pit corner
            G.Lamp(lamps, CellPos(3f, 2.5f, 3.4f), 6f);    // north-west floor
            G.Lamp(lamps, CellPos(20f, 10f, 3.4f), 5f);    // south-east pocket
            G.Lamp(lamps, CellPos(22f, 6.5f, 4f), 7f);     // elevator floodlight

            // Alarm floodlights: dark until the first alert, then on for good. Getting spotted
            // lights the catwalk over the pit and the south lane, so the high route is no longer free.
            G.SpotLamp(lamps, "PH_AlarmLamp", CellPos(4f, 6.5f, 6f), CellPos(0f, 6.5f, CatwalkTop), 60f, 10f)
                .gameObject.AddComponent<AlarmLight>();
            G.SpotLamp(lamps, "PH_AlarmLamp", CellPos(14.5f, 11f, 4.5f), CellPos(14.5f, 11f), 60f, 8f)
                .gameObject.AddComponent<AlarmLight>();
        }

        /// <summary>A trigger far below the pit. Falling in downs you, which restarts the scene.</summary>
        static void BuildPitHazard()
        {
            var hazard = new GameObject("PH_PitHazard");
            hazard.transform.position = new Vector3(0f, -12f, 0f);
            var box = hazard.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(Cols * Cell + 20f, 4f, Rows * Cell + 20f);
            hazard.AddComponent<FallHazard>();
        }

        static void BuildGoal()
        {
            int cMin = int.MaxValue, cMax = int.MinValue, rMin = int.MaxValue, rMax = int.MinValue;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (At(c, r) == 'G')
                    {
                        cMin = Mathf.Min(cMin, c); cMax = Mathf.Max(cMax, c);
                        rMin = Mathf.Min(rMin, r); rMax = Mathf.Max(rMax, r);
                    }
            if (cMin > cMax) return;

            var goal = new GameObject("PH_ExitGoal");
            goal.transform.position = CellPos((cMin + cMax) * 0.5f, (rMin + rMax) * 0.5f, 1.5f);
            var box = goal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3((cMax - cMin + 1) * Cell, 3f, (rMax - rMin + 1) * Cell);
            var sandboxGoal = goal.AddComponent<SandboxGoal>();
            sandboxGoal.objective = "Reach the elevator at the east end";

            // Flat strip on the floor so the exit is visible from a distance.
            var strip = G.Visual(PrimitiveType.Cube, goal.transform, "PH_ExitMarker",
                new Vector3(0f, -1.48f, 0f), new Vector3(box.size.x - 0.4f, 0.02f, box.size.z - 0.4f), G.noseMat);
            strip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- guards

        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;

            // North guard: circles the north-west floor, walks the north lane, then loops the
            // middle-top block.
            var north = G.Route(routes, "PH_Route_North", true, 1.5f,
                (CellPos(1.5f, 1f), 270f),
                (CellPos(4.5f, 3f), 90f),
                (CellPos(11.5f, 3f), 90f),
                (CellPos(16f, 3f), 180f),
                (CellPos(16f, 6.5f), 270f),
                (CellPos(11.5f, 6.5f), 0f),
                (CellPos(11.5f, 3f), 270f),
                (CellPos(4.5f, 3f), 270f));
            G.BuildGuard("PH_Guard_North", CellPos(11.5f, 6.5f), 0f, north, player);

            // East guard: loops the east-top block and swings past the elevator.
            var east = G.Route(routes, "PH_Route_East", true, 2f,
                (CellPos(16f, 3f), 90f),
                (CellPos(19.5f, 3f), 90f),
                (CellPos(21.5f, 7f), 180f),
                (CellPos(19.5f, 6.5f), 270f),
                (CellPos(16f, 6.5f), 0f));
            G.BuildGuard("PH_Guard_East", CellPos(19.5f, 3f), 90f, east, player);

            // South guard: loops the west-bottom block, checking the pit edge and the south lane.
            var south = G.Route(routes, "PH_Route_South", true, 2f,
                (CellPos(11.5f, 6.5f), 270f),
                (CellPos(7f, 6.5f), 270f),
                (CellPos(7f, 11f), 180f),
                (CellPos(11.5f, 11f), 90f));
            G.BuildGuard("PH_Guard_South", CellPos(7f, 11f), 90f, south, player);
        }
    }
}
