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
    /// Builds the dock sandbox: an original night-time quay for testing the player and guard AI.
    /// You start at the end of a dark pier, cross a grid of cover blocks split by long sightline
    /// lanes, and leave through a lit gate at the north end. The layout comes from the text map
    /// below, so it's easy to tweak: edit the map and run the menu item again.
    /// Everything it makes is a placeholder (Assets/_Placeholder, PH_*).
    /// </summary>
    public static class DockSandboxBuilder
    {
        const string ScenePath = G.Root + "/Scenes/PH_DockSandbox.unity";

        // Each cell is 3 x 3 m. Top row is north. Columns run west to east.
        // W water    . deck    P pier (deck over water)    # gate wall (4.5 m, too tall to climb)
        // L low cover (1.1 m: hides legs, not head)    T tall cover (2.6 m: full cover, climbable)
        // S stack (3.9 m vantage, climbable)    H hide shed (dark, roofed, one open side)
        // G exit goal
        static readonly string[] Map =
        {
            "WW####GG####", // 13  exit gate
            "WW..H.......", // 12  gate apron, shed beside the gate
            "WW........S.", // 11  stack overlooks the apron
            "WW.L.....TT.", // 10
            "WW.TT.TT....", //  9  cover grid: lanes on rows 4, 6, 8 and columns 2, 5, 8
            "WW........L.", //  8
            "WW.TT.TL.TT.", //  7
            "WW..........", //  6
            "WW.TL.TT.TT.", //  5
            "WW.........H", //  4  shed at the end of a long lane
            "WW.L..L.....", //  3
            "PP.......L..", //  2  start at the west end of the pier
            "WW..T.......", //  1
            "WWWWWWWWWWWW", //  0
        };

        /// <summary>Which side of each hide shed is open, by cell.</summary>
        static readonly Dictionary<Vector2Int, Vector3> ShedOpenings = new Dictionary<Vector2Int, Vector3>
        {
            { new Vector2Int(11, 4), Vector3.left },  // faces back down the row-4 lane
            { new Vector2Int(4, 12), Vector3.back },  // faces south, away from the gate guard
        };

        const float Cell = 3f;
        const float DeckThickness = 1.5f;
        const float PierThickness = 0.4f;
        const float WaterLevel = -1f;
        const float CoverInset = 0.2f;

        static int Rows => Map.Length;
        static int Cols => Map[0].Length;

        static char At(int c, int r) => Map[Rows - 1 - r][c];

        /// <summary>World position of a cell centre (fractions allowed).</summary>
        static Vector3 CellPos(float c, float r, float y = 0f) =>
            new Vector3((c - (Cols - 1) * 0.5f) * Cell, y, (r - (Rows - 1) * 0.5f) * Cell);

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
            var waterMat = G.MakeLitMaterial("PH_Water", null, new Color(0.05f, 0.12f, 0.2f));
            waterMat.SetFloat("_Smoothness", 0.85f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            G.SetupLighting();

            var level = new GameObject("PH_Level");
            BuildDeck(level.transform);
            BuildCover(level.transform);
            BuildSheds(level.transform);
            BuildPerimeter(level.transform);
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            level.AddComponent<RuntimeNavMeshBaker>();

            BuildLamps(level.transform);
            BuildWater(waterMat);   // outside PH_Level so the NavMesh never sees it
            BuildGoal();

            var player = G.BuildPlayer(CellPos(0.2f, 2f));
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
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

        static void BuildDeck(Transform root)
        {
            var deck = G.Group(root, "PH_Deck");
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    char ch = At(c, r);
                    if (ch == 'W' || ch == '#') continue; // gate walls reach down to the waterline themselves
                    // One block per cell keeps the number of lights touching each block low.
                    float thickness = ch == 'P' ? PierThickness : DeckThickness;
                    G.Block(deck, ch == 'P' ? "PH_Pier" : "PH_DeckTile",
                        CellPos(c, r, -thickness * 0.5f), new Vector3(Cell, thickness, Cell));
                }

            // Mooring posts along the pier, so it reads as a pier. Low enough to step around, not over.
            for (int c = 0; c < Cols && At(c, 2) == 'P'; c++)
            {
                G.Block(deck, "PH_Bollard", CellPos(c, 2, 0.35f) + new Vector3(0f, 0f, Cell * 0.5f - 0.3f), new Vector3(0.4f, 0.7f, 0.4f));
                G.Block(deck, "PH_Bollard", CellPos(c, 2, 0.35f) - new Vector3(0f, 0f, Cell * 0.5f - 0.3f), new Vector3(0.4f, 0.7f, 0.4f));
            }
        }

        static void BuildCover(Transform root)
        {
            var cover = G.Group(root, "PH_Cover");
            for (int r = 0; r < Rows; r++)
            {
                int c = 0;
                while (c < Cols)
                {
                    char ch = At(c, r);
                    float height = HeightOf(ch);
                    if (height <= 0f) { c++; continue; }

                    // Merge a run of the same block along the row into one box, so tops are walkable.
                    int start = c;
                    while (c < Cols && At(c, r) == ch) c++;
                    int count = c - start;

                    bool wall = ch == '#';
                    float inset = wall ? 0f : CoverInset;
                    float bottom = wall ? -DeckThickness : 0f;
                    var size = new Vector3(count * Cell - inset * 2f, height - bottom, Cell - inset * 2f);
                    var center = CellPos(start + (count - 1) * 0.5f, r, (height + bottom) * 0.5f);
                    G.Block(cover, NameOf(ch), center, size);
                }
            }
        }

        static float HeightOf(char ch) => ch switch
        {
            'L' => 1.1f,
            'T' => 2.6f,
            'S' => 3.9f,
            '#' => 4.5f,
            _ => 0f,
        };

        static string NameOf(char ch) => ch switch
        {
            'L' => "PH_CoverLow",
            'T' => "PH_CoverTall",
            'S' => "PH_Stack",
            _ => "PH_GateWall",
        };

        /// <summary>Three walls and a roof: a pocket of darkness with one way in and one way to be seen.</summary>
        static void BuildSheds(Transform root)
        {
            var sheds = G.Group(root, "PH_HideSheds");
            const float height = 2.4f, wall = 0.3f, roof = 0.2f;
            float half = Cell * 0.5f - 0.15f;

            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    if (At(c, r) != 'H') continue;
                    var shed = G.Group(sheds, "PH_HideShed");
                    Vector3 basePos = CellPos(c, r);
                    Vector3 open = ShedOpenings.TryGetValue(new Vector2Int(c, r), out var o) ? o : Vector3.back;

                    foreach (var side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        if (side == open) continue;
                        bool alongX = side.z != 0f;
                        var size = alongX ? new Vector3(half * 2f, height, wall) : new Vector3(wall, height, half * 2f);
                        G.Block(shed, "PH_ShedWall", basePos + side * (half - wall * 0.5f) + Vector3.up * height * 0.5f, size);
                    }
                    G.Block(shed, "PH_ShedRoof", basePos + Vector3.up * (height + roof * 0.5f), new Vector3(half * 2f, roof, half * 2f));
                }
        }

        static void BuildPerimeter(Transform root)
        {
            var walls = G.Group(root, "PH_Perimeter");
            Vector3 east = CellPos(Cols - 1, 0f) + Vector3.right * (Cell * 0.5f + 0.5f);
            float zMin = CellPos(0, 1).z - Cell * 0.5f, zMax = CellPos(0, Rows - 1).z + Cell * 0.5f;
            G.Block(walls, "PH_Wall_E", new Vector3(east.x, 1.5f, (zMin + zMax) * 0.5f), new Vector3(1f, 6f, zMax - zMin));

            // Close the back of the goal cells.
            for (int c = 0; c < Cols; c++)
                if (At(c, Rows - 1) == 'G')
                    G.Block(walls, "PH_Wall_N", CellPos(c, Rows - 1, 1.5f) + Vector3.forward * (Cell * 0.5f + 0.5f), new Vector3(Cell, 6f, 1f));
        }

        /// <summary>
        /// Light pools at lane crossings, dark runs between them. The pier and the sheds stay dark.
        /// The gate is lit, so the last approach always means crossing light.
        /// </summary>
        static void BuildLamps(Transform parent)
        {
            var lamps = new GameObject("PH_Lamps").transform;
            lamps.SetParent(parent, false);
            G.Lamp(lamps, CellPos(5f, 4f, 3.4f), 6f);     // centre lane crossing
            G.Lamp(lamps, CellPos(8f, 8f, 3.4f), 6f);     // east lane crossing
            G.Lamp(lamps, CellPos(2f, 10f, 3.4f), 6f);    // west lane, upper
            G.Lamp(lamps, CellPos(11f, 7f, 3.4f), 5f);    // east service lane
            G.Lamp(lamps, CellPos(8f, 1.5f, 3.2f), 5f);   // south quay
            G.Lamp(lamps, CellPos(6.5f, 12.2f, 4f), 7f);  // gate floodlight

            // Alarm floodlights: dark until the first alert, then on for good. Getting spotted
            // lights up the dark west lane and the shed lane, so the route you used is gone.
            G.SpotLamp(lamps, "PH_AlarmLamp", CellPos(2f, 6f, 4.5f), CellPos(2f, 7f), 60f, 9f)
                .gameObject.AddComponent<AlarmLight>();
            G.SpotLamp(lamps, "PH_AlarmLamp", CellPos(11f, 2.5f, 4.5f), CellPos(11f, 4f), 60f, 9f)
                .gameObject.AddComponent<AlarmLight>();
        }

        static void BuildWater(Material waterMat)
        {
            var water = new GameObject("PH_Water").transform;
            float width = Cols * Cell + 40f, length = Rows * Cell + 40f;

            var surface = G.Visual(PrimitiveType.Cube, water, "PH_WaterSurface",
                new Vector3(0f, WaterLevel - 0.05f, 0f), new Vector3(width, 0.1f, length), waterMat);
            surface.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Falling off the deck lands you in this trigger, which downs you and restarts.
            var hazard = new GameObject("PH_WaterHazard");
            hazard.transform.SetParent(water, false);
            hazard.transform.position = new Vector3(0f, WaterLevel - 1f, 0f);
            var box = hazard.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, 1.6f, length);
            hazard.AddComponent<WaterHazard>();
        }

        static void BuildGoal()
        {
            float cMin = float.MaxValue, cMax = float.MinValue;
            for (int c = 0; c < Cols; c++)
                if (At(c, Rows - 1) == 'G') { cMin = Mathf.Min(cMin, c); cMax = Mathf.Max(cMax, c); }
            if (cMin > cMax) return;

            var goal = new GameObject("PH_ExitGoal");
            goal.transform.position = CellPos((cMin + cMax) * 0.5f, Rows - 1, 1.5f);
            var box = goal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3((cMax - cMin + 1f) * Cell, 3f, Cell);
            goal.AddComponent<SandboxGoal>();

            // Flat strip on the floor so the goal is visible from a distance.
            var strip = G.Visual(PrimitiveType.Cube, goal.transform, "PH_ExitMarker",
                new Vector3(0f, -1.48f, 0f), new Vector3(box.size.x - 0.4f, 0.02f, Cell - 0.4f), G.noseMat);
            strip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------------------------------------------------------- guards

        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;

            // Lanes guard: a loop around the lower half of the grid. At each corner it stops and
            // looks down a long lane, including straight at the row-4 shed's open side.
            var lanes = G.Route(routes, "PH_Route_Lanes", true, 2f,
                (CellPos(2, 4), 180f),
                (CellPos(8, 4), 90f),
                (CellPos(8, 8), 0f),
                (CellPos(2, 8), 180f));
            G.BuildGuard("PH_Guard_Lanes", CellPos(2, 4), 90f, lanes, player);

            // Spine guard: walks the full centre lane north and south, checking the side lanes.
            var spine = G.Route(routes, "PH_Route_Spine", false, 2.5f,
                (CellPos(5, 2), 270f),
                (CellPos(5, 6), 90f),
                (CellPos(5, 11), 270f));
            G.BuildGuard("PH_Guard_Spine", CellPos(5, 11), 180f, spine, player);

            // Gate guard: a post under the floodlight, alternating between watching the apron
            // and watching the east side. Its back is to the shed when it looks east.
            var gate = G.Route(routes, "PH_Route_Gate", false, 3.5f,
                (CellPos(6.2f, 12f), 200f),
                (CellPos(6.8f, 12f), 100f));
            G.BuildGuard("PH_Guard_Gate", CellPos(6.2f, 12f), 200f, gate, player);
        }
    }
}
