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
    /// Builds the tutorial's first stretch from map A (design/tutorial-gatehouse/versions/map-A-labeled):
    /// the guide wall, the two zig-zag stairs up the lowest lock head, the H6 bridge and the
    /// gatehouse on the pier deck. Geometry, lamps, the road layout from the planner, and the guards.
    ///
    /// Coordinates are the map's, in metres: x = east, z = north (upriver). y is height.
    /// Heights follow the real lowest head, measured from the guide wall top: ledge +6.4, bridge and
    /// deck +16, about 15 m of real climb from wall top to bridge. See
    /// design/tutorial-gatehouse/real-heights.md in the project files for how that was estimated.
    /// Flight counts follow photo 29: 4 short flights on the lower stair, 7 on the upper one.
    /// Everything it makes is a placeholder (Assets/_Placeholder, PH_*).
    /// </summary>
    public static class TutorialGatehouseBuilder
    {
        const string ScenePath = G.Root + "/Scenes/PH_TutorialGatehouse.unity";

        // Elevations (top surfaces).
        const float WallTop = 0f;
        const float LedgeTop = WallTop + 6.4f;
        const float DeckTop = LedgeTop + 9.6f;      // 16
        const int LowerFlights = 4;
        const int UpperFlights = 7;
        const float LowerRise = (LedgeTop - WallTop) / LowerFlights;   // 1.6 m
        const float UpperRise = (DeckTop - LedgeTop) / UpperFlights;   // 1.37 m
        const float Base = -8f;           // where the concrete meets the water (well under the surface)
        const float HideStepTop = WallTop - 1.4f, HideStepWidth = 1.6f;
        const float HideStepZ = -19f;     // the last buttress: z -19..-16
        const float WaterY = -5f;         // visual only
        const float HazardY = -12f;       // falling in the water kills you

        // Stairs (photo 29): short flights zig-zagging up a wall face in two 1.3 m lanes, 2.6 m deep.
        // Flights alternate lanes (outer, inner, outer...), so each one runs directly over the flight
        // two below it, with a turn landing across both lanes at each end. The lowest flight in each
        // lane is solid down to the floor; everything above is a 0.3 m slab, leaving 2.4-2.9 m of
        // headroom. Rails on the outer edge and between the lanes, so you never step off a flight.
        const float Lane = 1.3f;          // flight width
        const float LandingDepth = 1.5f;  // landing at each end: room to turn round
        const float SlabThick = 0.3f;
        const float LowerRun = 4f, UpperRun = 3.4f;                        // about 22 degrees
        const float LowerLength = LandingDepth * 2f + LowerRun;           // 7 m along the wall
        const float UpperLength = LandingDepth * 2f + UpperRun;           // 6.4 m

        // The ledge (top of the lower block) is 22 x 11 m, from the block front up to the pier face
        // under the bridge. Lower stairs on the
        // block's front face, from where the guide wall lands; upper stairs against the pier face at
        // the ledge's east end, topping out by the bridge lamp.
        const float PierFace = 7f;
        const float LedgeDepth = 11f;
        const float BlockFront = PierFace - LedgeDepth;          // -4
        const float LowerX0 = 3f;
        const float UpperX0 = 20f - UpperLength;                 // 13.6 (1.9 m clear east of it, to the ledge's east rail)
        const float LowerZ0 = BlockFront - 2f * Lane;            // -6.6..-4
        const float UpperZ0 = PierFace - 2f * Lane;              // 4.4..7

        // The road: four 3.9 m lanes (z 7..22.6). The pier deck north of it is laid out from its
        // original 20 m north edge, shifted by DeckShift.
        const float RoadLane = 3.9f;
        const float RoadZ0 = PierFace, RoadZ1 = RoadZ0 + 4f * RoadLane;
        const float DeckShift = RoadZ1 - 20f;                    // 2.6

        const float RailHeight = 1.1f;
        const float RailThick = 0.1f;
        const float ParapetThick = 0.3f;

        static Transform root;

        [MenuItem("Stealth Prototype/Build Tutorial Gatehouse Scene", priority = 2)]
        public static void Build()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
            {
                EditorUtility.DisplayDialog("URP not found",
                    "This builder expects a URP project (Unity Hub 'Universal 3D' template). See README.md.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            UrpSetup.EnsureActive();

            G.EnsureFolder(G.Root + "/Materials");
            G.EnsureFolder(G.Root + "/Textures");
            G.EnsureFolder(G.Root + "/Scenes");
            G.MeshCache.Clear();
            G.CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            G.SetupLighting();

            var level = new GameObject("PH_Level");
            root = level.transform;
            BuildGuideWall(G.Group(root, "PH_GuideWall"));
            BuildLowerBlock(G.Group(root, "PH_LowerBlock"));
            BuildLowerStairs(G.Group(root, "PH_LowerStairs"));
            BuildUpperStairs(G.Group(root, "PH_UpperStairs"));
            BuildBridge(G.Group(root, "PH_Bridge"));
            BuildPierDeck(G.Group(root, "PH_PierDeck"));
            BuildRoadLayout(G.Group(root, "PH_RoadLayout"));
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            // Finer voxels so the 1.4-1.5 m stair lanes and the 1.5 m parapet gap stay walkable for guards.
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.08f;
            level.AddComponent<RuntimeNavMeshBaker>();

            BuildLamps(G.Group(root, "PH_Lamps"));
            BuildWater();        // outside PH_Level: visual only
            BuildFallHazard();   // outside PH_Level so the NavMesh never sees it
            BuildGoal();
            BuildCameraZones();

            // Start on the first buttress (z -118..-115), below the walkway: clamber up to begin.
            var player = G.BuildPlayer(new Vector3(3f + HideStepWidth * 0.5f, HideStepTop, -116.5f));
            player.transform.rotation = Quaternion.identity; // facing north, up the wall
            player.GetComponent<Health>().maxHealth = 1;     // tutorial rule: one shot kills
            player.GetComponent<PlayerMotor>().dashNoiseRadius = 0f; // tutorial rule: silent dash (for now)
            G.BuildCamera(player);
            LookSetup.Apply(Camera.main);
            BuildGuards(player.transform);

            StarkLights();

            new GameObject("PH_DebugHUD").AddComponent<DebugHUD>();
            new GameObject("PH_LastKnownMarker").AddComponent<LastKnownMarker>();
            var layers = new GameObject("HeightLayers").AddComponent<HeightLayers>();
            layers.boundaries = new[] { 3f, 11f };
            layers.names = new[] { "Guide wall", "Ledge", "Bridge" };

            EditorSceneManager.SaveScene(scene, ScenePath);
            G.AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PH] Tutorial gatehouse built at {ScenePath}. Press Play.");
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A solid box from corner to corner (map x, height y, map z).</summary>
        static GameObject Box(Transform parent, string name, float x0, float x1, float y0, float y1, float z0, float z1)
            => G.Block(parent, name,
                new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, y1 - y0, z1 - z0));

        /// <summary>A landing whose top is at y, solid down to floorY.</summary>
        static void Landing(Transform parent, string name, float x0, float x1, float z0, float z1, float y, float floorY)
            => Box(parent, name, x0, x1, floorY, y, z0, z1);

        /// <summary>A straight railing (or parapet) standing on a surface at height y.</summary>
        static void RailX(Transform parent, float x0, float x1, float z, float y, float thick = RailThick, float height = RailHeight)
            => Box(parent, "PH_Rail", x0, x1, y, y + height, z - thick * 0.5f, z + thick * 0.5f);

        static void RailZ(Transform parent, float z0, float z1, float x, float y, float thick = RailThick, float height = RailHeight)
            => Box(parent, "PH_Rail", x - thick * 0.5f, x + thick * 0.5f, y, y + height, z0, z1);

        /// <summary>A railing that follows a flight: a and b are points on the walking surface.</summary>
        static void SlopedRail(Transform parent, Vector3 a, Vector3 b)
        {
            Vector3 dir = b - a;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 centre = (a + b) * 0.5f + rot * Vector3.up * (RailHeight * 0.5f + 0.05f);
            G.Block(parent, "PH_Rail", centre, new Vector3(RailThick, RailHeight, dir.magnitude), rot);
        }

        /// <summary>
        /// One flight along x in a lane (z0..z1), climbing from (xFrom, yFrom) to (xTo, yTo),
        /// solid underneath down to floorY. railZ is the side that gets a railing (NaN for none).
        /// </summary>
        static void Flight(Transform parent, string name, float xFrom, float xTo, float yFrom, float yTo,
            float floorY, float z0, float z1, float railZ)
        {
            Wedge(parent, name, xFrom, xTo, yFrom, yTo, floorY, z0, z1);
            if (!float.IsNaN(railZ))
                SlopedRail(parent, new Vector3(xFrom, yFrom, railZ), new Vector3(xTo, yTo, railZ));
        }

        /// <summary>
        /// A solid block with a sloped top: flat bottom at floorY, top rising from yFrom at xFrom to yTo
        /// at xTo, across z0..z1. Convex mesh collider, grid material, UVs in metres.
        /// </summary>
        static GameObject Wedge(Transform parent, string name, float xFrom, float xTo, float yFrom, float yTo,
            float floorY, float z0, float z1)
        {
            float xa = Mathf.Min(xFrom, xTo), xb = Mathf.Max(xFrom, xTo);
            float ya = xFrom < xTo ? yFrom : yTo, yb = xFrom < xTo ? yTo : yFrom;
            var centre = new Vector3((xa + xb) * 0.5f, floorY, (z0 + z1) * 0.5f);
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z) - centre;

            // Corners: bottom (b) and top (t) at the low-x (a) and high-x (b) ends, near (z0) and far (z1).
            Vector3 ba0 = P(xa, floorY, z0), bb0 = P(xb, floorY, z0), ba1 = P(xa, floorY, z1), bb1 = P(xb, floorY, z1);
            Vector3 ta0 = P(xa, ya, z0), tb0 = P(xb, yb, z0), ta1 = P(xa, ya, z1), tb1 = P(xb, yb, z1);

            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            Vector3 mid = P((xa + xb) * 0.5f, (floorY + (ya + yb) * 0.5f) * 0.5f, (z0 + z1) * 0.5f);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-6f) return; // degenerate (a zero-height end)
                if (Vector3.Dot(n, (a + c) * 0.5f - mid) < 0f) { var t = b; b = d; d = t; n = -n; }
                n.Normalize();
                // Planar UVs on the face's dominant axis, in metres, so the grid tiles at 1 m.
                Vector2 UV(Vector3 v)
                {
                    Vector3 w = v + centre;
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    if (ay >= ax && ay >= az) return new Vector2(w.x, w.z);
                    if (ax >= az) return new Vector2(w.z, w.y);
                    return new Vector2(w.x, w.y);
                }
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(UV(a)); uvs.Add(UV(b)); uvs.Add(UV(c)); uvs.Add(UV(d));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            Quad(ta0, tb0, tb1, ta1); // sloped top
            Quad(ba0, ba1, bb1, bb0); // bottom
            Quad(ba0, bb0, tb0, ta0); // z0 side
            Quad(ba1, ta1, tb1, bb1); // z1 side
            Quad(ba0, ta0, ta1, ba1); // low-x end
            Quad(bb0, bb1, tb1, tb0); // high-x end

            var mesh = new Mesh { name = $"PH_Wedge_{name}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = G.gridMat;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;
            return go;
        }

        // ---------------------------------------------------------------- level

        /// <summary>
        /// The right-side guide wall: a 3 m walkway, 120 m long, with the shipping channel to the west.
        /// Box buttresses under the walkway on the east side (away from the channel, as in photo 29),
        /// and the mooring dolphins carrying on downstream in the water.
        /// </summary>
        static void BuildGuideWall(Transform parent)
        {
            Box(parent, "PH_GuideWall", 0f, 3f, Base, WallTop, -120f, BlockFront);
            for (float z = -118f; z + 3f <= LowerZ0 - 1f; z += 9f)
            {
                // Buttresses, every 9 m: steps 1.6 m wide and 1.4 m below the walkway. You start on
                // the first one, so you learn straight away that they're walkable and that you climb
                // back up (clamber). They're hiding steps: lie prone against the wall face and the
                // walkway's edge hides you from a guard walking past above; lie out at the outer edge
                // and he can see you. The last one (HideStepZ), 12 m short of the stairs, is where
                // guard W meets a player who walks straight up the wall.
                Box(parent, "PH_Buttress", 3f, 3f + HideStepWidth, Base, HideStepTop, z, z + 3f);
            }

            // No railings: the edge rules stop you walking off (you hang instead), and the open tip
            // lets you dash-jump out to the dolphins. A low concrete lip marks each edge from above.
            var lips = G.Group(parent, "PH_EdgeLips");
            G.Visual(PrimitiveType.Cube, lips, "PH_EdgeLip", new Vector3(0.075f, WallTop + 0.075f, (-120f + BlockFront) * 0.5f),
                new Vector3(0.15f, 0.15f, BlockFront + 120f), G.gridMat);
            float eastLipEnd = LowerZ0;
            G.Visual(PrimitiveType.Cube, lips, "PH_EdgeLip", new Vector3(2.925f, WallTop + 0.075f, (-120f + eastLipEnd) * 0.5f),
                new Vector3(0.15f, 0.15f, eastLipEnd + 120f), G.gridMat);

            // Mooring dolphins: round concrete posts downstream of the tip, 0.5 m below the wall top.
            // Gaps: 3.6 m from the tip, then 3.2, 3.2, and 4.2 to the last one, where a short jump ends
            // in a ledge catch. The last one will hold a hidden item.
            var dolphins = G.Group(parent, "PH_Dolphins");
            float h = WallTop - 0.5f - WaterY;
            float[] centres = { -125f, -131f, -137f, -144f };
            foreach (float z in centres)
            {
                var d = G.Visual(PrimitiveType.Cylinder, dolphins, "PH_Dolphin",
                    new Vector3(1.5f, WaterY + h * 0.5f, z), new Vector3(DolphinDiameter, h * 0.5f, DolphinDiameter), G.gridMat);
                var col = d.AddComponent<MeshCollider>(); // flat top, unlike the primitive's capsule
                col.sharedMesh = d.GetComponent<MeshFilter>().sharedMesh;
                col.convex = true;
            }
            var item = G.Visual(PrimitiveType.Cube, dolphins, "PH_ItemMarker",
                new Vector3(1.5f, WallTop - 0.5f + 0.25f, centres[centres.Length - 1]), new Vector3(0.4f, 0.4f, 0.4f), G.noseMat);
            item.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        }

        const float DolphinDiameter = 2.8f;

        /// <summary>The lower block of the lock head: its top is the ledge (22 x 11 m), with a small hut.</summary>
        static void BuildLowerBlock(Transform parent)
        {
            Box(parent, "PH_LowerBlock", 0f, 22f, Base, LedgeTop, BlockFront, PierFace);

            // Ledge railings: the front edge (gap at the top of the lower stairs), the west and east ends.
            float gap0 = LowerTopX0, gap1 = LowerTopX0 + LandingDepth;
            RailX(parent, 0f, gap0, BlockFront + 0.05f, LedgeTop);
            RailX(parent, gap1, 22f, BlockFront + 0.05f, LedgeTop);
            RailZ(parent, BlockFront, PierFace, 0.05f, LedgeTop);
            RailZ(parent, BlockFront, PierFace, 21.95f, LedgeTop);

            // Hut near the top of the lower stairs: natural cover.
            Box(parent, "PH_Hut", 0.6f, 4.6f, LedgeTop, LedgeTop + 2.5f, 3.6f, 6.6f);
        }

        /// <summary>The upper stair's first flight climbs east from the foot at the west end.</summary>
        const bool UpperFirstEast = true;

        /// <summary>West end of the upper stair's top landing (the gap in the bridge parapet).</summary>
        static float UpperTopX0 => (UpperFlights % 2 == 1) == UpperFirstEast ? UpperX0 + UpperLength - LandingDepth : UpperX0;

        /// <summary>West end of the lower stair's top landing (where it opens onto the ledge).</summary>
        static float LowerTopX0 => LowerFlights % 2 == 0 ? LowerX0 : LowerX0 + LowerLength - LandingDepth;

        /// <summary>
        /// Lower stairs: a strip on the block's front face (x 3..10, z -6.6..-4), four flights of 1.6 m.
        /// Step east off the end of the guide wall onto the bottom landing; the top landing (west end)
        /// opens north onto the ledge.
        /// </summary>
        static void BuildLowerStairs(Transform parent)
        {
            StackedStair(parent, LowerX0, LowerZ0, LowerFlights, LowerRise, LowerRun, WallTop, true, Base);
            parent.gameObject.AddComponent<NoClamber>(); // the only way up is from the foot
        }

        /// <summary>
        /// Upper stairs: seven flights of 1.37 m against the pier face (x 13.6..20, z 4.4..7). The foot is
        /// at the west end; the top landing at the east end opens north onto the bridge through a gap
        /// in its south parapet (x 18.5..20). Solid under the first two flights (the crawl-in hiding
        /// space was removed after playtest). The foot is watched by guard S1 and L's searchlight.
        /// 1.9 m clear between its east end and the ledge's east rail.
        /// </summary>
        static void BuildUpperStairs(Transform parent)
        {
            StackedStair(parent, UpperX0, UpperZ0, UpperFlights, UpperRise, UpperRun, LedgeTop, UpperFirstEast, LedgeTop);
            parent.gameObject.AddComponent<NoClamber>(); // the only way up is from the foot
        }

        /// <summary>
        /// A zig-zag stair up a wall face, two lanes deep (z0 outer, z0 + Lane inner, wall at z0 + 2 Lane).
        /// Flight i is in the outer lane when i is even, the inner one when odd, and climbs `rise` over
        /// `run`, with LandingDepth landings across both lanes at each end. So flight i + 2 runs straight
        /// over flight i. The bottom landing, the first two flights and the first turn are solid down to
        /// floorY (nothing is under them); the rest are slabs.
        ///
        /// hollowUnder: a hiding space in the inner lane under flight 2. Flight 2 is a slab; the first
        /// turn is solid only in the outer lane (a slab over the inner one, so the hiding space opens
        /// under it at that end, crawl height); a wall on the lane divider closes it off from the
        /// stair, and a wall at the foot end closes that end.
        /// </summary>
        static void StackedStair(Transform parent, float x0, float z0, int flights, float rise, float run,
            float yBottom, bool firstEast, float floorY, bool hollowUnder = false)
        {
            float x1 = x0 + LandingDepth * 2f + run;
            float fx0 = x0 + LandingDepth, fx1 = x1 - LandingDepth;
            float zMid = z0 + Lane, zWall = z0 + 2f * Lane;

            if (yBottom > floorY + 0.01f)
                Landing(parent, "PH_Landing_Bottom", firstEast ? x0 : fx1, firstEast ? fx0 : x1, z0, zWall, yBottom, floorY);

            for (int i = 0; i < flights; i++)
            {
                bool east = (i % 2 == 0) == firstEast;
                bool outer = i % 2 == 0;
                float za = outer ? z0 : zMid, zb = za + Lane;
                float ya = yBottom + i * rise, yb = ya + rise;
                float from = east ? fx0 : fx1, to = east ? fx1 : fx0;
                string name = $"PH_Flight_{i + 1}";
                if (i == 0 || (i == 1 && !hollowUnder)) Wedge(parent, name, from, to, ya, yb, floorY, za, zb);
                else SlopedSlab(parent, name, from, to, ya, yb, za, zb);
                // Rails: the outer edge (outer lane) and the lane divider (every flight).
                if (outer) SlopedRail(parent, new Vector3(from, ya, z0 + 0.05f), new Vector3(to, yb, z0 + 0.05f));
                SlopedRail(parent, new Vector3(from, ya, zMid), new Vector3(to, yb, zMid));

                // The landing at this flight's top end, across both lanes: a turn, or the top.
                bool top = i == flights - 1;
                float lx0 = east ? fx1 : x0, lx1 = east ? x1 : fx0;
                string lname = top ? "PH_Landing_Top" : $"PH_Landing_Turn{i + 1}";
                if (i == 0 && hollowUnder)
                {
                    Landing(parent, lname, lx0, lx1, z0, zMid, yb, floorY);
                    Box(parent, lname + "_Over", lx0, lx1, yb - SlabThick, yb, zMid, zWall);
                }
                else if (i == 0) Landing(parent, lname, lx0, lx1, z0, zWall, yb, floorY);
                else Box(parent, lname, lx0, lx1, yb - SlabThick, yb, z0, zWall);
                // Rails on its open sides: the far end and the outer (south) edge.
                RailZ(parent, z0, zWall, east ? x1 - 0.05f : x0 + 0.05f, yb);
                RailX(parent, lx0, lx1, z0 + 0.05f, yb);
            }

            if (hollowUnder && flights > 1)
            {
                // Under flight 2 (from the first turn's end back to the foot end): the divider wall up
                // into the slab's underside, then the same under the second turn, and a wall across
                // the inner lane at the foot end.
                const float Wall = 0.15f, Bury = 0.1f;
                bool firstGoesEast = firstEast;
                float turnEdge = firstGoesEast ? fx1 : fx0;   // flight 2 starts here, low
                float footEdge = firstGoesEast ? fx0 : fx1;   // and ends here, high
                float footEnd = firstGoesEast ? x0 : x1;
                float lowTop = yBottom + rise - SlabThick + Bury, highTop = yBottom + 2f * rise - SlabThick + Bury;
                Wedge(parent, "PH_HideWall_Divider", turnEdge, footEdge, lowTop, highTop, floorY, zMid, zMid + Wall);
                Box(parent, "PH_HideWall_Divider", Mathf.Min(footEdge, footEnd), Mathf.Max(footEdge, footEnd), floorY, highTop, zMid, zMid + Wall);
                float capA = firstGoesEast ? footEnd : footEnd - Wall;
                Box(parent, "PH_HideWall_End", capA, capA + Wall, floorY, highTop, zMid, zWall);
            }
        }

        /// <summary>A 0.3 m slab whose top runs from (xFrom, yFrom) to (xTo, yTo), across z0..z1.</summary>
        static void SlopedSlab(Transform parent, string name, float xFrom, float xTo, float yFrom, float yTo, float z0, float z1)
        {
            float zc = (z0 + z1) * 0.5f;
            Vector3 a = new Vector3(xFrom, yFrom, zc), b = new Vector3(xTo, yTo, zc);
            Vector3 dir = b - a;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 centre = (a + b) * 0.5f - rot * Vector3.up * (SlabThick * 0.5f);
            G.Block(parent, name, centre, new Vector3(z1 - z0, SlabThick, dir.magnitude), rot);
        }

        /// <summary>
        /// The H6 bridge: a 15.6 m, four-lane road (z 7..22.6) from x -40 to 66 over chamber 1 (x -34..0, centre -17),
        /// the pier and onto the east bank. Walled off at both ends.
        /// </summary>
        static void BuildBridge(Transform parent)
        {
            const float z0 = RoadZ0, z1 = RoadZ1;

            // The pier under the bridge and the deck: solid from the water up (x 0..36, z 7..46.6).
            Box(parent, "PH_Pier", 0f, 36f, Base, DeckTop, z0, 44f + DeckShift);
            // Road slabs either side of the pier: over chamber 1 to the west, onto the bank to the east.
            Box(parent, "PH_Road_West", -40f, 0f, DeckTop - 1.5f, DeckTop, z0, z1);
            Box(parent, "PH_Road_East", 36f, 66f, DeckTop - 1.5f, DeckTop, z0, z1);
            // Chamber 1's far (west) wall under the bridge, and the walls framing the chamber.
            Box(parent, "PH_ChamberWall_West", -40f, -34f, Base, DeckTop - 1.5f, z0 - 4f, z1 + 4f);

            // Parapets. South: all along, with a gap at the top of the upper stairs.
            float pz = z0 + ParapetThick * 0.5f;
            RailX(parent, -40f, UpperTopX0, pz, DeckTop, ParapetThick);
            RailX(parent, UpperTopX0 + LandingDepth, 66f, pz, DeckTop, ParapetThick);
            // North: only where there's no pier deck beyond.
            float nz = z1 - ParapetThick * 0.5f;
            RailX(parent, -40f, 0f, nz, DeckTop, ParapetThick);
            RailX(parent, 36f, 66f, nz, DeckTop, ParapetThick);

            // Dead ends: tall walls past the climb limit at both ends.
            Box(parent, "PH_EndWall_West", -41f, -40f, DeckTop, DeckTop + 8f, z0, z1);
            Box(parent, "PH_EndWall_East", 66f, 67f, DeckTop, DeckTop + 8f, z0, z1);

            // Four lanes (3.9 m each): a double yellow centre line and dashed white lane lines.
            var yellow = G.MakeLitMaterial("PH_RoadYellow", null, new Color(0.85f, 0.7f, 0.15f));
            var white = G.MakeLitMaterial("PH_RoadWhite", null, new Color(0.85f, 0.85f, 0.82f));
            var marks = G.Group(parent, "PH_RoadMarkings");
            void Mark(Material m, float xa, float xb, float mz, float w)
            {
                var v = G.Visual(PrimitiveType.Cube, marks, "PH_RoadMark",
                    new Vector3((xa + xb) * 0.5f, DeckTop + 0.01f, mz), new Vector3(xb - xa, 0.02f, w), m);
                v.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            float zc = (z0 + z1) * 0.5f;
            Mark(yellow, -40f, 66f, zc - 0.12f, 0.12f);
            Mark(yellow, -40f, 66f, zc + 0.12f, 0.12f);
            foreach (float lz in new[] { z0 + RoadLane, z1 - RoadLane })
                for (float x = -38f; x < 66f; x += 9f)
                    Mark(white, x, Mathf.Min(x + 3f, 66f), lz, 0.12f);

            // Vehicles and cover on the road: BuildRoadLayout.
        }

        /// <summary>
        /// The pier deck north of the bridge (x 0..36, z 22.6..46.6): the gatehouse against the chamber edge,
        /// a small box between it and the bridge, open deck to the east, and an outbuilding further north.
        /// </summary>
        static void BuildPierDeck(Transform parent)
        {
            // Edges: a railed strip along the chamber (west), parapets east and north.
            const float s = DeckShift;
            RailZ(parent, 20f + s, 44f + s, 0.3f, DeckTop);
            RailZ(parent, 20f + s, 44f + s, 36f - ParapetThick * 0.5f, DeckTop, ParapetThick);
            RailX(parent, 0f, 36f, 44f + s - ParapetThick * 0.5f, DeckTop, ParapetThick);

            // Gatehouse: 15 x 13 m, 4 m tall. Door on its south face (x 13..15.5).
            var gatehouse = Box(parent, "PH_Gatehouse", 2.5f, 17.5f, DeckTop, DeckTop + 4f, 26f + s, 39f + s);
            var door = G.Visual(PrimitiveType.Cube, gatehouse.transform, "PH_GatehouseDoor",
                new Vector3(14.25f - 10f, 1.1f - 2f, -6.5f - 0.03f), new Vector3(2.5f, 2.2f, 0.06f), G.noseMat);
            door.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Box(parent, "PH_SmallBox", 5f, 11f, DeckTop, DeckTop + 2.5f, 21.5f + s, 24.5f + s);
            Box(parent, "PH_Outbuilding", 24f, 32f, DeckTop, DeckTop + 3.5f, 35f + s, 41f + s);
        }

        /// <summary>
        /// Lamps from the road layout planner (design/tutorial-gatehouse/road/road-layout-v1.json):
        /// lampposts 5 m up on the road, 4 m up on the pier deck.
        /// </summary>
        static readonly (float x, float z)[] LayoutLamps =
        {
            (-18f, 7.8f),
            (-18f, 21.8f),
            (-3f, 8f),
            (-3f, 21.6f),
            (1.3f, 43.6f),
            (13.5f, 8f),
            (13.5f, 21.5f),
            (29.5f, 21.6f),
            (45.5f, 21.6f),
            (29.5f, 8f),
            (45.5f, 8f),
        };

        static void BuildLamps(Transform parent)
        {
            foreach (var (x, z) in LayoutLamps)
            {
                bool road = z <= RoadZ1;
                float h = road ? 5f : 4f;
                G.Lamp(parent, new Vector3(x, DeckTop + h, z), road ? 7f : 5.5f);
                G.Visual(PrimitiveType.Cylinder, parent, "PH_LampPost",
                    new Vector3(x, DeckTop + h * 0.5f, z), new Vector3(0.15f, h * 0.5f, 0.15f), G.gridMat);
            }
        }

        // ---------------------------------------------------------------- road layout (planner)

        /// <summary>
        /// Props from the road layout planner, exactly as placed: type, centre (x, z), yaw (degrees
        /// clockwise from the piece's long axis pointing east; front = east at yaw 0), stack height.
        /// Source: design/tutorial-gatehouse/road/road-layout-v1.json (planner version 401).
        /// </summary>
        static readonly (string type, float x, float z, float yaw, int stack)[] LayoutProps =
        {
            ("humvee", -25f, 11.5f, 0f, 1),
            ("humvee", -25f, 18.5f, 0f, 1),
            ("container40", 50.5f, 17f, 47f, 1),
            ("container20", 53.5f, 10f, 326f, 1),
            ("cab", 40.5f, 8.5f, 180f, 1),
            ("trailer", 43.5f, 14f, 105f, 1),
            ("boom", 32.5f, 29f, 0f, 1),
            ("boom", 21f, 29f, 0f, 1),
            ("boom", 26.5f, 29f, 0f, 1),
            ("jersey", -10.5f, 19.5f, 270f, 1),
            ("jersey", -10.5f, 16f, 270f, 1),
            ("jersey", -10.5f, 12.5f, 270f, 1),
            ("jersey", -10.5f, 9f, 270f, 1),
            ("container40", 18f, 14.5f, 105f, 1),
            ("container20", 24f, 14f, 330f, 1),
            ("humvee", 37.5f, 12.5f, 217f, 1),
            ("car", 40.5f, 19f, 42f, 1),
            ("crate", 25.5f, 19.5f, 0f, 1),
            ("crate", 23.5f, 20f, 0f, 1),
            ("crate", 26.5f, 17.5f, 0f, 1),
            ("humvee", 30f, 33f, 132f, 1),
            ("car", 23f, 22.5f, 2f, 1),
            ("car", 32f, 19.5f, 19f, 1),
            ("container40", 59f, 13.5f, 273f, 1),
            ("container20", 54.5f, 19.5f, 337f, 1),
            ("container40", 63f, 16f, 74f, 1),
            ("trailer", 6f, 12.5f, 197f, 1),
            ("cab", -0.5f, 12.5f, 136f, 1),
        };

        const float HumveeBedFloor = 0.9f;

        /// <summary>The middle of a Humvee's open bed, on its floor (for a guard riding in the back).</summary>
        static Vector3 HumveeBed(float x, float z, float yaw)
            => new Vector3(x, DeckTop, z) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-1.43f, HumveeBedFloor, 0f);

        /// <summary>
        /// The road's vehicles and cover. Sizes match the planner. Trucks are a cab plus a flatbed
        /// trailer whose deck sits 1 m up, so you can crawl underneath. Humvees have headlights.
        /// </summary>
        static void BuildRoadLayout(Transform parent)
        {
            var lights = G.Group(root, "PH_RoadLayoutLights");
            foreach (var (type, x, z, yaw, stack) in LayoutProps)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 c = new Vector3(x, DeckTop, z);
                // A box in the piece's own frame: lx along its length (+ = front), lz across, y up from the deck.
                void Part(string name, float lx0, float lx1, float y0, float y1, float lz0, float lz1)
                {
                    Vector3 local = new Vector3((lx0 + lx1) * 0.5f, (y0 + y1) * 0.5f, (lz0 + lz1) * 0.5f);
                    G.Block(parent, name, c + rot * local, new Vector3(lx1 - lx0, y1 - y0, lz1 - lz0), rot);
                }
                switch (type)
                {
                    case "container40": Part("PH_Container40", -6.1f, 6.1f, 0f, 2.6f * stack, -1.22f, 1.22f); break;
                    case "container20": Part("PH_Container20", -3.05f, 3.05f, 0f, 2.6f * stack, -1.22f, 1.22f); break;
                    case "car": Part("PH_Car", -1.9f, 1.9f, 0f, 1.6f, -1.3f, 1.3f); break;
                    case "jersey": Part("PH_JerseyBarrier", -1.5f, 1.5f, 0f, 0.8f, -0.3f, 0.3f); break;
                    case "sandbag": Part("PH_Sandbags", -1.2f, 1.2f, 0f, 1f, -0.4f, 0.4f); break;
                    case "crate": Part("PH_Crate", -0.6f, 0.6f, 0f, 1.2f, -0.6f, 0.6f); break;
                    case "boom":
                        // Post at the west end, arm at about 1 m: crawl under it or clamber over.
                        Part("PH_BoomPost", -3f, -2.6f, 0f, 1.1f, -0.2f, 0.2f);
                        Part("PH_BoomArm", -2.6f, 3f, 0.95f, 1.1f, -0.08f, 0.08f);
                        break;
                    case "humvee":
                        // Two-door pickup: hood, cab, and an open bed at the back (floor 0.9 m, sides to 1.3 m).
                        Part("PH_HumveeHood", 1f, 2.45f, 0f, 1.2f, -1.1f, 1.1f);
                        Part("PH_HumveeCab", -0.5f, 1f, 0f, 1.9f, -1.1f, 1.1f);
                        Part("PH_HumveeBedFloor", -2.45f, -0.5f, 0f, HumveeBedFloor, -1.1f, 1.1f);
                        Part("PH_HumveeBedSide", -2.45f, -0.5f, HumveeBedFloor, 1.3f, -1.1f, -1f);
                        Part("PH_HumveeBedSide", -2.45f, -0.5f, HumveeBedFloor, 1.3f, 1f, 1.1f);
                        Part("PH_HumveeTailgate", -2.45f, -2.35f, HumveeBedFloor, 1.3f, -1f, 1f);
                        // Headlights: one wide beam out of the front, Lit down the road ahead.
                        Vector3 hl = c + rot * new Vector3(2.65f, 1f, 0f);
                        G.SpotLamp(lights, "PH_HumveeHeadlight", hl, hl + rot * new Vector3(12f, -1f, 0f), 34f, 14f);
                        break;
                    case "cab":
                        // Tractor unit: cab at the front, a low chassis behind it (a step up).
                        Part("PH_TruckCab", -0.5f, 3.1f, 0f, 3.2f, -1.25f, 1.25f);
                        Part("PH_TruckChassis", -3.1f, -0.5f, 0f, 1.2f, -1.25f, 1.25f);
                        break;
                    case "trailer":
                        // Deck 1.0..1.4 m up, open underneath for crawling. Wheels at the back, legs at the front.
                        Part("PH_TrailerDeck", -6.25f, 6.25f, 1f, 1.4f, -1.25f, 1.25f);
                        foreach (float side in new[] { -1f, 1f })
                        {
                            Part("PH_TrailerWheels", -5.6f, -3.2f, 0f, 1f, side * 1.25f - (side > 0 ? 0.5f : 0f), side * 1.25f + (side < 0 ? 0.5f : 0f));
                            Part("PH_TrailerLeg", 3.6f, 3.8f, 0f, 1f, side * 1f - 0.1f, side * 1f + 0.1f);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// First-pass guards from design/guard-vision/guard-vision-spec.md (map A placement).
        /// Each stands at one point (a one-point route: no back-and-forth) and sweeps its head.
        ///   W  top of the lower stairs; comes down and walks the guide wall toward you when you near
        ///      its end (PatrolOnCue). Teaches: get off his path and lie low (the hiding step).
        ///   L, L2  bridge south parapet, 9.6 m above the ledge, facing south, each with a swivel
        ///          spotlight sweeping the ledge, out of step so a gap opens.
        ///   Road guards (R, G, T): from the road layout planner, see below.
        /// Plus a lamp on the ledge east of the upper stair's foot. Open in the design: which way W and L2 face, and
        /// whether the Humvee light wall gets a guard (it doesn't yet).
        /// </summary>
        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;
            // sweep: total head swivel in degrees (0 = still, < 0 = the default 45 each way);
            // sweepTime: seconds from one side to the other.
            void Post(string id, Vector3 position, float yaw, float sweep = -1f, float sweepTime = 8f)
            {
                var route = G.Route(routes, "PH_Route_" + id, true, 3f, (position, yaw));
                G.BuildGuard("PH_Guard_" + id, position, yaw, route, player);
                if (sweep < 0f) return;
                var brain = GameObject.Find("PH_Guard_" + id).GetComponent<GuardBrain>();
                brain.sweepAngle = sweep * 0.5f;
                brain.sweepSpeed = Mathf.PI / Mathf.Max(1f, sweepTime);
            }
            void Patrol(string id, params Vector3[] points)
            {
                var pts = new (Vector3 pos, float yaw)[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 to = points[(i + 1) % points.Length] - points[i];
                    pts[i] = (points[i], to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : 0f);
                }
                var route = G.Route(routes, "PH_Route_" + id, true, 2f, pts);
                G.BuildGuard("PH_Guard_" + id, points[0], pts[0].yaw, route, player);
            }
            Vector3 D(float x, float z) => new Vector3(x, DeckTop, z);

            // W: starts at the top of the lower stairs, on the ledge, facing out over them. When you
            // get near the end of the guide wall he comes down (his flashlight shows him on the
            // flights), then walks south down the walkway toward you, past the hiding step, and
            // on to a beat well down the wall, clear of the stairs (PatrolOnCue).
            Vector3 wTop = new Vector3(LowerX0 + LandingDepth * 0.5f, LedgeTop, BlockFront + 1.2f);
            var wPost = G.Route(routes, "PH_Route_W_Post", true, 3f, (wTop, 180f));
            G.BuildGuard("PH_Guard_W", wTop, 180f, wPost, player);
            var wBrain = GameObject.Find("PH_Guard_W").GetComponent<GuardBrain>();
            wBrain.sweepAngle = 20f; // a small look round while he waits up top
            Vector3 Wk(float x, float z) => new Vector3(x, WallTop, z);
            // Rendezvous: timed so a player walking straight up the wall meets him on the walkway
            // right beside the hiding step (walk point 2). He sets off while you're still in the
            // dam view and paces himself to keep the appointment (PatrolOnCue).
            var wWalk = G.RouteTimed(routes, "PH_Route_W_Walk", 0f,
                (new Vector3(LowerX0 + LandingDepth * 0.5f, WallTop, LowerZ0 + Lane), 225f, 0f), // bottom landing
                (Wk(1.5f, LowerZ0 - 1f), 180f, 0f),
                (Wk(1.5f, HideStepZ + 1.5f), 180f, 0f),                                           // beside the hiding step
                (Wk(1.5f, -38f), 180f, 1f));
            var wBeat = G.Route(routes, "PH_Route_W_Beat", true, 2f, (Wk(1.5f, -62f), 0f), (Wk(1.5f, -38f), 180f));
            var wCue = new GameObject("PH_W_Cue").AddComponent<PatrolOnCue>();
            wCue.guard = wBrain;
            wCue.walk = wWalk;
            wCue.thenRoute = wBeat;
            wCue.player = player;
            wCue.meetIndex = 2;
            // The over-the-shoulder shot ends (top-down again) the moment he's further south than you,
            // or you reach the end of the walkway, whichever comes first.
            wCue.zoneUntilPassed = GameObject.Find("PH_Cam_Approach").GetComponent<CameraZone>();
            wCue.zoneEndZ = BlockFront - 0.5f;
            wCue.cueZone = new Bounds(new Vector3(2.5f, WallTop + 1f, (-120f + HideStepZ) * 0.5f), new Vector3(12f, 6f, HideStepZ + 120f)); // the wall, south of the step
            Post("L", new Vector3(8f, DeckTop, 7.6f), 180f);
            Post("L2", new Vector3(3f, DeckTop, 7.8f), 180f);

            // Shift change on the ledge (landing planner). S1 stands south of the upper stair's foot,
            // looking north-west across the ledge; S2 stands at the post on the bridge, west of the stair's top. Nothing
            // happens until you step onto the ledge: then S2 comes down the stairs to relieve S1.
            // They meet mid-ledge, swap, and S1 walks back up to the post. Then it repeats every 45 s
            // with roles swapped.
            //
            // (Planner edit, v51: S1 no longer walks a beat, and L's light loops on its own rather than
            // being kept in step with him by StairWatch.) While the relief walks back up, the ledge
            // post is empty: that's your gap to follow S1 up.
            Vector3 Ld(float x, float z) => new Vector3(x, LedgeTop, z);
            // S1 stands 5 m south-south-west of the upper stair's entrance (the open floor west of
            // flight 1), facing it, so the far edge of his flashlight's Lit cone (6 m) covers it.
            Vector3 stairEntrance = Ld(UpperX0 + 0.9f, UpperZ0 + Lane * 0.5f);
            Vector3 watchPos = Ld(13f, 0.3f);
            Vector3 toEntrance = stairEntrance - watchPos;
            var shiftLoop = G.Route(routes, "PH_Route_Shift_Loop", true, 3f, (watchPos, Mathf.Atan2(toEntrance.x, toEntrance.z) * Mathf.Rad2Deg));
            Vector3 postPos = D(14f, 7.8f);
            var shiftPost = G.Route(routes, "PH_Route_Shift_Post", true, 3f, (postPos, 160f));
            var shiftMeet = G.Route(routes, "PH_Route_Shift_Meet", true, 3f, (Ld(9.5f, 0.5f), 90f));
            // After the swap the newcomer doesn't take S1's spot: he goes to the ledge's south edge,
            // east, facing out over the water (landing planner, "S2 endpoint").
            var shiftAfter = G.Route(routes, "PH_Route_Shift_After", true, 3f, (Ld(17f, -3f), 180f));
            var shiftPartner = G.Route(routes, "PH_Route_Shift_Partner", true, 3f, (Ld(10.8f, 0.5f), 270f));
            G.BuildGuard("PH_Guard_S1", watchPos, shiftLoop.GetPoint(0).eulerAngles.y, shiftLoop, player);
            G.BuildGuard("PH_Guard_S2", postPos, 160f, shiftPost, player);
            var s1 = GameObject.Find("PH_Guard_S1").GetComponent<GuardBrain>();
            var s2 = GameObject.Find("PH_Guard_S2").GetComponent<GuardBrain>();
            s1.sweepAngle = s2.sweepAngle = 0f; // heads still: the flashlight stays on what he faces
            var shift = new GameObject("PH_ShiftChange").AddComponent<ShiftChange>();
            shift.patrolling = s1;
            shift.onPost = s2;
            shift.patrolRoute = shiftLoop;
            shift.postRoute = shiftPost;
            shift.meetRoute = shiftMeet;
            shift.partnerRoute = shiftPartner;
            shift.reliefInterval = 45f;
            shift.handoverRoute = shiftAfter;
            shift.player = player;
            shift.startZone = new Bounds(new Vector3(11f, LedgeTop + 1.5f, (BlockFront + PierFace) * 0.5f), new Vector3(22f, 3f, LedgeDepth));

            // Road guards from the layout planner (road-layout-v1.json). R1-R3 hold the jersey-barrier
            // line at the west end, facing east. R4 stands in the back of the mid-road Humvee, swivelling. G1-G3 watch the
            // gatehouse and the boom gates; G4 stands still by the gatehouse's east side.
            Post("R1", D(-13.5f, 9.5f), 90f, 0f);
            Post("R2", D(-13.5f, 13.5f), 90f, 0f);
            Post("R3", D(-13.5f, 17f), 90f, 0f);
            Post("R4", HumveeBed(37.5f, 12.5f, 217f), 275f, 120f); // in the back of the mid-road Humvee
            Post("G1", D(4.5f, 24.5f), 240f, 115f);
            Post("G2", D(4.5f, 26.5f), 255f, 90f);
            Post("G3", D(32.5f, 30f), 180f, 80f);
            Post("G4", D(18.5f, 26f), 90f, 0f);
            // Patrols: T1 loops round the wrecked truck and the first container; T2 walks the north
            // edge of the road from the west end to the gatehouse and back.
            Patrol("T1", D(14.5f, 9f), D(17.5f, 20f), D(10.5f, 17.5f), D(14.5f, 8.5f));
            Patrol("T2", D(-30f, 21.5f), D(9.5f, 21.5f), D(16f, 27f), D(18f, 24.5f), D(10f, 20f), D(-3f, 21.5f));

            // Swivel spotlights for L and L2: on the parapet's outer face, aimed down at the ledge.
            // Each is its guard's eyes: standing in the beam gets you spotted at once (GuardVision.searchlights).
            var lights = G.Group(root, "PH_GuardLights");
            // Both slide a big circle round a looped rail over the ledge (landing planner, v51), at
            // about the speeds they had before (L 1.3 m/s, L2 1 m/s), half a lap out of step.
            // L's triangle: east of mid-ledge -> west -> north toward the upper stair's foot.
            // L2's triangle: off the lower stair's top -> north-east -> mid-ledge.
            var swivelL = Swivel(lights, "PH_Swivel_L", Ld(13.5f, -0.5f), Ld(11f, 4f), 12f, 0f, true, 3.2f, Ld(7.5f, 1f));
            var swivelL2 = Swivel(lights, "PH_Swivel_L2", Ld(2f, -3.5f), Ld(7f, 0f), 18f, 0.5f, true, 3f, Ld(4.5f, 4f));
            GameObject.Find("PH_Guard_L").GetComponent<GuardVision>().searchlights = new[] { swivelL };
            GameObject.Find("PH_Guard_L2").GetComponent<GuardVision>().searchlights = new[] { swivelL2 };

            // Ledge lamp: its Dim ring is Slow walking, Very slow crawling; the ledge's
            // front edge stays dark.
            // (Planner, v51: moved east of the upper stair's foot, smaller.)
            // Planner radius = the pool's radius on the floor, so the cone is widened to reach it and
            // the range reaches the pool's rim with room to spare (a plain Lamp with range 3.5 m from
            // 3.4 m up barely touched the floor: v51-v53 showed no light).
            const float lampPool = 7f, lampHeight = 3.4f;
            Vector3 lampAt = new Vector3(17f, LedgeTop + lampHeight, 0f);
            G.SpotLamp(lights, "PH_Lamp", lampAt, lampAt + Vector3.down, 2f * Mathf.Atan(lampPool / lampHeight) * Mathf.Rad2Deg,
                1.3f * Mathf.Sqrt(lampPool * lampPool + lampHeight * lampHeight));
            G.Visual(PrimitiveType.Cylinder, lights, "PH_LampPost",
                new Vector3(lampAt.x, LedgeTop + 1.7f, lampAt.z), new Vector3(0.15f, 1.7f, 0.15f), G.gridMat);

            // Crates by the ledge's east end (landing planner): 1.2 m cubes, low enough to clamber.
            foreach (var (cx, cz) in new[] { (18.5f, -2f), (19.5f, -1f), (20f, 2.5f) })
                G.Block(root, "PH_Crate", new Vector3(cx, LedgeTop + 0.6f, cz), Vector3.one * 1.2f, Quaternion.identity);
        }

        /// <summary>
        /// A ledge searchlight: hangs from a rail cantilevered off the bridge parapet, pointing straight
        /// down, and slides along the rail over the ledge (from, any stops, to: floor points),
        /// so its pool is a big circle (radius) sliding over the floor. The whole pool is Lit.
        /// </summary>
        static StealthLight Swivel(Transform parent, string name, Vector3 from, Vector3 to, float sweepTime, float phase, bool loop, float radius, params Vector3[] stops)
        {
            float y = DeckTop + 1.3f, drop = y - LedgeTop;
            Vector3 a = new Vector3(from.x, y, from.z), b = new Vector3(to.x, y, to.z);
            var mid = new Vector3[stops.Length];
            for (int i = 0; i < stops.Length; i++) mid[i] = new Vector3(stops[i].x, y, stops[i].z);
            float angle = 2f * Mathf.Atan(radius / drop) * Mathf.Rad2Deg;
            var light = G.SpotLamp(parent, name, a, new Vector3(a.x, LedgeTop, a.z), angle, drop * 1.25f, 40f);
            var stealth = light.GetComponent<StealthLight>();
            stealth.litFraction = 1f;       // the whole beam is Lit
            stealth.litRangeFraction = 1f;
            var swivel = light.gameObject.AddComponent<SwivelLight>();
            swivel.useTrack = true;
            swivel.trackFrom = a;
            swivel.trackTo = b;
            swivel.trackStops = mid;
            swivel.trackLoop = loop;
            swivel.sweepTime = sweepTime;
            swivel.phase = phase;

            // The rail (visual only): along the track just above the lamp, then a strut back to the
            // parapet from the rail point nearest it.
            Vector3 up = Vector3.up * 0.35f;
            void Beam(string beamName, Vector3 p, Vector3 q)
            {
                Vector3 d = q - p;
                var go = G.Visual(PrimitiveType.Cube, parent, beamName, (p + q) * 0.5f, new Vector3(0.15f, 0.15f, d.magnitude), G.gridMat);
                go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            }
            Vector3 prev = a, near = a;
            foreach (var m in mid) { Beam(name + "_Rail", prev + up, m + up); prev = m; if (m.z > near.z) near = m; }
            Beam(name + "_Rail", prev + up, b + up);
            if (loop) Beam(name + "_Rail", b + up, a + up); // closes the loop
            if (b.z > near.z) near = b;
            Beam(name + "_Strut", near + up, new Vector3(near.x, DeckTop + 1.1f, RoadZ0 + ParapetThick * 0.5f));
            return stealth;
        }

        static void BuildWater()
        {
            var mat = G.MakeLitMaterial("PH_Water", null, new Color(0.05f, 0.09f, 0.13f));
            var water = G.Visual(PrimitiveType.Cube, null, "PH_Water",
                new Vector3(10f, WaterY - 0.05f, -60f), new Vector3(260f, 0.1f, 280f), mat);
            water.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A trigger under the water. Falling off anywhere downs you, which restarts the scene.</summary>
        static void BuildFallHazard()
        {
            var hazard = new GameObject("PH_FallHazard");
            hazard.transform.position = new Vector3(10f, HazardY, -60f);
            var box = hazard.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(260f, 4f, 280f);
            hazard.AddComponent<FallHazard>();
        }

        /// <summary>
        /// Cinematic shots. Guide wall: top-down at the spawn (so you see the dolphins), then the camera
        /// swoops round to your right for the dam view, then settles over your right shoulder looking
        /// up the wall at the lock head (and guard W coming at you) to the wall's end. Each stair has a fixed
        /// camera out over the water, looking at it side-on.
        /// </summary>
        static void BuildCameraZones()
        {
            var zones = new GameObject("PH_CameraZones").transform; // outside PH_Level: triggers stay off the NavMesh

            // Along the wall (spawn at z -118): 22 m of plain top-down, the dam view (z -96..-62, 34 m),
            // then the over-the-shoulder approach (z -62 to the wall's end).
            // Dam view: tracks alongside you, 9 m to your right (east), looking west past you over the river.
            var dam = Zone(zones, "PH_Cam_DamView", CameraZone.Kind.Track, new Vector3(1.5f, 0.5f, -79f), new Vector3(10f, 8f, 34f), 1.8f);
            dam.eyeRelativeToPlayer = true;
            dam.eyeStart = dam.eyeEnd = new Vector3(9f, 3.5f, -2f);
            dam.lookStart = dam.lookEnd = new Vector3(-30f, 4f, 8f);
            dam.fovStart = dam.fovEnd = 50f;
            // Up on the stick stays "along the wall" (north) for the whole walk, so stopping or
            // turning round never re-maps the controls to the side-on camera.
            dam.fixedControls = true;
            dam.controlYaw = 0f;

            // Approach: from the end of the dam view to the end of the walkway, one over-the-shoulder
            // shot looking up the wall at the lock head, so you watch W come down the stairs (his
            // flashlight) and walk straight at you. It starts a little high and wide behind your right
            // shoulder and slowly pushes in tight as you close in. Up on the stick stays north.
            var approach = Zone(zones, "PH_Cam_Approach", CameraZone.Kind.Crane,
                new Vector3(2f, 0f, (-62f + BlockFront) * 0.5f), new Vector3(10f, 6f, BlockFront + 62f), 1.5f);
            approach.progressStart = new Vector3(1.5f, 0f, -62f);
            approach.progressEnd = new Vector3(1.5f, 0f, HideStepZ);
            approach.easePower = 1.5f;
            approach.eyeRelativeToPlayer = true;
            approach.eyeStart = new Vector3(1.4f, 3.2f, -5.5f);
            approach.eyeEnd = new Vector3(0.75f, 1.85f, -2.6f);
            approach.lookStart = new Vector3(-0.2f, 2.6f, 16f);
            approach.lookEnd = new Vector3(-0.3f, 1.5f, 8f);
            approach.fovStart = 46f;
            approach.fovEnd = 52f;
            approach.fixedControls = true;
            approach.controlYaw = 0f;

            // Stairs: fixed cameras over the water to the south, turning to keep you in frame. On a
            // stacked stair every flight but the last two has another flight straight over it, which
            // hides you from the top-down camera. So each zone is a box round its stair capped at the
            // turn under the last two flights (feet up to 15 cm above it): the side view lasts exactly
            // while there's stair over your head, and it's top-down from the first step up the
            // second-to-last flight (and back to the side view on the way down at the same point).
            float lx0 = LowerX0, lx1 = LowerX0 + LowerLength;
            float lowY0 = WallTop - 1f, lowY1 = WallTop + (LowerFlights - 2) * LowerRise + 0.15f;
            var lower = Zone(zones, "PH_Cam_LowerStairs", CameraZone.Kind.Fixed,
                new Vector3((lx0 - 0.5f + lx1 + 1f) * 0.5f, (lowY0 + lowY1) * 0.5f, (LowerZ0 - 1f + BlockFront) * 0.5f),
                new Vector3(lx1 + 1f - (lx0 - 0.5f), lowY1 - lowY0, BlockFront - (LowerZ0 - 1f)), 1f);
            lower.priority = 1; // wins over the approach shot where they meet at the walkway's end
            lower.eyeRelativeToPlayer = false;
            lower.eyeStart = lower.eyeEnd = new Vector3((lx0 + lx1) * 0.5f, WallTop + 5f, LowerZ0 - 12f);
            lower.lookStart = lower.lookEnd = new Vector3(0f, 1.2f, 0f);
            lower.fovStart = lower.fovEnd = 45f;

            float ux0 = UpperX0, ux1 = UpperX0 + UpperLength;
            float upY0 = LedgeTop - 1f, upY1 = LedgeTop + (UpperFlights - 2) * UpperRise + 0.15f;
            var upper = Zone(zones, "PH_Cam_UpperStairs", CameraZone.Kind.Fixed,
                new Vector3((ux0 - 0.5f + ux1 + 1f) * 0.5f, (upY0 + upY1) * 0.5f, (UpperZ0 - 0.5f + PierFace) * 0.5f),
                new Vector3(ux1 + 1f - (ux0 - 0.5f), upY1 - upY0, PierFace - (UpperZ0 - 0.5f)), 1f);
            upper.eyeRelativeToPlayer = false;
            upper.eyeStart = upper.eyeEnd = new Vector3((ux0 + ux1) * 0.5f, LedgeTop + 6.5f, BlockFront - 11f);
            upper.lookStart = upper.lookEnd = new Vector3(0f, 1.2f, 0f);
            upper.fovStart = upper.fovEnd = 45f;

        }

        static CameraZone Zone(Transform parent, string name, CameraZone.Kind kind, Vector3 centre, Vector3 size, float blendTime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var zone = go.AddComponent<CameraZone>();
            zone.kind = kind;
            zone.blendTime = blendTime;
            return zone;
        }

        /// <summary>
        /// Every lamp, headlight and searchlight in the level looks like the guard flashlights: the real
        /// light isn't drawn, and its pool is a crisp flat overlay showing exactly where it makes you Lit
        /// or Dim (LightFootprintMesh). Guard flashlights already work this way.
        /// </summary>
        static void StarkLights()
        {
            var group = new GameObject("PH_LightFootprints").transform; // world-space meshes, at the origin
            foreach (var sl in Object.FindObjectsByType<StealthLight>(FindObjectsSortMode.None))
            {
                if (sl.GetComponentInParent<GuardBrain>() != null) continue;
                var light = sl.GetComponent<Light>();
                if (light == null || light.type != LightType.Spot) continue;
                light.cullingMask = 0;
                light.renderingLayerMask = 0;
                light.shadows = LightShadows.None;
                var fp = new GameObject("PH_Footprint_" + sl.name);
                fp.transform.SetParent(group, false);
                fp.AddComponent<MeshFilter>();
                fp.AddComponent<MeshRenderer>().sharedMaterial = G.coneMat;
                fp.AddComponent<LightFootprintMesh>().source = sl;
            }
        }

        /// <summary>The goal: the gatehouse door. The level carries on into the chamber later.</summary>
        static void BuildGoal()
        {
            var goal = new GameObject("PH_GatehouseGoal");
            goal.transform.position = new Vector3(14.25f, DeckTop + 1.5f, 25f + DeckShift);
            var box = goal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.5f, 3f, 2f);
            goal.AddComponent<SandboxGoal>().objective = "Reach the gatehouse door";

            var strip = G.Visual(PrimitiveType.Cube, goal.transform, "PH_GoalMarker",
                new Vector3(0f, -1.48f, 0f), new Vector3(2.1f, 0.02f, 1.6f), G.noseMat);
            strip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
