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
    /// gatehouse on the pier deck. Geometry, lamps, the Humvee light wall and the first-pass guards.
    ///
    /// Coordinates are the map's, in metres: x = east, z = north (upriver). y is height.
    /// Heights follow the real lowest head, measured from the guide wall top: ledge +6.4 (2 flights),
    /// bridge and deck +16 (3 more flights), about 15 m of real climb from wall top to bridge. See
    /// design/tutorial-gatehouse/real-heights.md in the project files for how that was estimated.
    /// Flight counts are LowerFlights / UpperFlights; the stairs and heights follow from them.
    /// Everything it makes is a placeholder (Assets/_Placeholder, PH_*).
    /// </summary>
    public static class TutorialGatehouseBuilder
    {
        const string ScenePath = G.Root + "/Scenes/PH_TutorialGatehouse.unity";

        // Elevations (top surfaces).
        const float WallTop = 0f;
        const float FlightRise = 3.2f;
        const int LowerFlights = 2;
        const int UpperFlights = 3;
        const float LedgeTop = WallTop + LowerFlights * FlightRise;   // 6.4: 2 flights of 3.2 m
        const float DeckTop = LedgeTop + UpperFlights * FlightRise;   // 16: 3 more flights
        const float Base = -8f;           // where the concrete meets the water (well under the surface)
        const float WaterY = -5f;         // visual only
        const float HazardY = -12f;       // falling in the water kills you

        // Stairs: two side-by-side lanes (a scissor stair). Every flight and landing is solid down to
        // the floor below it, so there's nothing to fall through, and no flight runs over another, so
        // the top-down camera always sees you. Run 8 m per flight.
        const float Lane = 1.5f;          // flight width
        const float LandingDepth = 1.5f;  // landing at each end: room to turn round
        const float StairLength = 11f;    // along the wall: 1.5 + 8 + 1.5

        // Lower stairs: x 3..14 on the block's front face. Upper stairs: x 10.5..21.5 on the pier face.
        // One lane per flight. The lower block reaches south past z 0 so the upper stair's extra lane
        // still leaves 2.8 m of open ledge in front of it.
        const float LowerX0 = 3f, UpperX0 = 10.5f;
        const float UpperLane = 1.4f;
        const float UpperZ0 = 7f - UpperFlights * UpperLane;   // 2.8..7
        const float BlockFront = UpperZ0 - 2.8f;               // 0
        const float LowerZ0 = BlockFront - LowerFlights * Lane; // -3..0

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

            var player = G.BuildPlayer(new Vector3(1.6f, WallTop, -118f));
            player.transform.rotation = Quaternion.identity; // facing north, up the wall
            player.GetComponent<Health>().maxHealth = 1;     // tutorial rule: one shot kills
            player.GetComponent<PlayerMotor>().dashNoiseRadius = 0f; // tutorial rule: silent dash (for now)
            G.BuildCamera(player);
            LookSetup.Apply(Camera.main);
            BuildGuards(player.transform);

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
        /// The right-side guide wall: a 3 m walkway, 120 m long, with the river to the west.
        /// Box buttresses under the walkway on the river side, and the mooring dolphins carrying on
        /// downstream in the water (out of reach).
        /// </summary>
        static void BuildGuideWall(Transform parent)
        {
            Box(parent, "PH_GuideWall", 0f, 3f, Base, WallTop, -120f, 0f);
            for (float z = -118f; z < -2f; z += 9f)
                Box(parent, "PH_Buttress", -2f, 0f, Base, WallTop - 0.6f, z, z + 3f);

            // No railings: the edge rules stop you walking off (you hang instead), and the open tip
            // lets you dash-jump out to the dolphins. A low concrete lip marks each edge from above.
            var lips = G.Group(parent, "PH_EdgeLips");
            G.Visual(PrimitiveType.Cube, lips, "PH_EdgeLip", new Vector3(0.075f, WallTop + 0.075f, -60f), new Vector3(0.15f, 0.15f, 120f), G.gridMat);
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

        /// <summary>The lower block of the lock head: its top is the ledge (22 x 7 m), with a small hut.</summary>
        static void BuildLowerBlock(Transform parent)
        {
            Box(parent, "PH_LowerBlock", 0f, 22f, Base, LedgeTop, BlockFront, 7f);

            // Ledge railings: the front edge (gap at the top of the lower stairs), the west and east ends.
            float gap0 = LowerTopX0, gap1 = LowerTopX0 + LandingDepth;
            RailX(parent, 0f, gap0, BlockFront + 0.05f, LedgeTop);
            RailX(parent, gap1, 22f, BlockFront + 0.05f, LedgeTop);
            RailZ(parent, BlockFront, 7f, 0.05f, LedgeTop);
            RailZ(parent, BlockFront, 7f, 21.95f, LedgeTop);

            // Hut near the top of the lower stairs: natural cover.
            Box(parent, "PH_Hut", 0.6f, 4.6f, LedgeTop, LedgeTop + 2.5f, 3.6f, 6.6f);
        }

        /// <summary>West end of the upper stair's top landing (the gap in the bridge parapet).</summary>
        static float UpperTopX0 => UpperX0 + StairLength - LandingDepth; // BuildUpperStairs always finishes eastward

        /// <summary>West end of the lower stair's top landing (where it opens onto the ledge).</summary>
        static float LowerTopX0 => LowerFlights % 2 == 0 ? LowerX0 : LowerX0 + StairLength - LandingDepth;

        /// <summary>
        /// Lower stairs: a strip on the block's front face (x 3..14), two flights of 3.2 m (22 degrees)
        /// in 1.5 m lanes. Step east off the end of the guide wall onto the bottom landing; the
        /// flights zig-zag, and the top landing opens north onto the ledge.
        /// </summary>
        static void BuildLowerStairs(Transform parent)
        {
            float x0 = LowerX0, fx0 = x0 + LandingDepth;
            Landing(parent, "PH_Landing_Bottom", x0, fx0, LowerZ0, LowerZ0 + Lane, WallTop, Base);
            ScissorStair(parent, LowerX0, LowerZ0, Lane, LowerFlights, WallTop, true, Base);
        }

        /// <summary>
        /// Upper stairs: three flights of 3.2 m against the upper pier face (x 10.5..21.5, z 2.8..7), side by
        /// side in 1.4 m lanes so none runs over another, with 2.8 m of open ledge in front. Flight 1
        /// (outer lane) starts on the ledge (west end for an odd count), then they zig-zag, and the top
        /// landing at the east end opens north onto the bridge through a gap in its south parapet.
        /// </summary>
        static void BuildUpperStairs(Transform parent)
        {
            // Even count: start westward so the top still comes out at the east end (by the bridge lamp).
            ScissorStair(parent, UpperX0, UpperZ0, UpperLane, UpperFlights, LedgeTop, UpperFlights % 2 == 1, LedgeTop);
        }

        /// <summary>
        /// A zig-zag stair: one flight per lane, lanes side by side from z0 northwards, each flight
        /// FlightRise high across StairLength (with LandingDepth landings at both ends). Turn landings
        /// span the two lanes they join; the top landing is in the last lane. Solid down to floorY.
        /// </summary>
        static void ScissorStair(Transform parent, float x0, float z0, float lane, int flights, float yBottom,
            bool firstEast, float floorY)
        {
            float x1 = x0 + StairLength;
            float fx0 = x0 + LandingDepth, fx1 = x1 - LandingDepth;
            for (int i = 0; i < flights; i++)
            {
                bool east = (i % 2 == 0) == firstEast;
                float za = z0 + i * lane, zb = za + lane;
                float ya = yBottom + i * FlightRise, yb = ya + FlightRise;
                float from = east ? fx0 : fx1, to = east ? fx1 : fx0;
                Flight(parent, $"PH_Flight_{i + 1}", from, to, ya, yb, floorY, za, zb, i == 0 ? za + 0.05f : za);

                // The landing at this flight's top end: a turn (two lanes) or the top (last lane).
                bool top = i == flights - 1;
                float lx0 = east ? fx1 : x0, lx1 = east ? x1 : fx0;
                float lz1 = top ? zb : zb + lane;
                Landing(parent, top ? "PH_Landing_Top" : $"PH_Landing_Turn{i + 1}", lx0, lx1, za, lz1, yb, floorY);
                // Rails on its open sides: the far end and the outer (south) edge.
                float endX = east ? x1 - 0.05f : x0 + 0.05f;
                RailZ(parent, za, lz1, endX, yb);
                RailX(parent, lx0, lx1, i == 0 ? za + 0.05f : za, yb);
            }
        }

        /// <summary>
        /// The H6 bridge: a 13 m road (z 7..20) from x -40 to 66 over chamber 1 (x -34..0, centre -17),
        /// the pier and onto the east bank. West end: two Humvees and their headlights. East end: a wall.
        /// </summary>
        static void BuildBridge(Transform parent)
        {
            const float z0 = 7f, z1 = 20f;

            // The pier under the bridge and the deck: solid from the water up (x 0..36, z 7..44).
            Box(parent, "PH_Pier", 0f, 36f, Base, DeckTop, z0, 44f);
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

            // Humvees (2 m tall boxes, climbable), headlights pointing east down the road.
            Box(parent, "PH_Humvee", -40f, -35f, DeckTop, DeckTop + 2f, 7.9f, 10.5f);
            Box(parent, "PH_Humvee", -40f, -35f, DeckTop, DeckTop + 2f, 16.7f, 19.3f);
        }

        /// <summary>
        /// The pier deck north of the bridge (x 0..36, z 20..44): the gatehouse against the chamber edge,
        /// a small box between it and the bridge, open deck to the east, and an outbuilding further north.
        /// </summary>
        static void BuildPierDeck(Transform parent)
        {
            // Edges: a railed strip along the chamber (west), parapets east and north.
            RailZ(parent, 20f, 44f, 0.3f, DeckTop);
            RailZ(parent, 20f, 44f, 36f - ParapetThick * 0.5f, DeckTop, ParapetThick);
            RailX(parent, 0f, 36f, 44f - ParapetThick * 0.5f, DeckTop, ParapetThick);

            // Gatehouse: 15 x 13 m, 4 m tall. Door on its south face (x 13..15.5).
            var gatehouse = Box(parent, "PH_Gatehouse", 2.5f, 17.5f, DeckTop, DeckTop + 4f, 26f, 39f);
            var door = G.Visual(PrimitiveType.Cube, gatehouse.transform, "PH_GatehouseDoor",
                new Vector3(14.25f - 10f, 1.1f - 2f, -6.5f - 0.03f), new Vector3(2.5f, 2.2f, 0.06f), G.noseMat);
            door.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Box(parent, "PH_SmallBox", 5f, 11f, DeckTop, DeckTop + 2.5f, 21.5f, 24.5f);
            Box(parent, "PH_Outbuilding", 24f, 32f, DeckTop, DeckTop + 3.5f, 35f, 41f);
        }

        static void BuildLamps(Transform parent)
        {
            // Bridge lampposts on both edges: lamps 5 m up.
            foreach (float x in new[] { -18f, 2f, 22f, 42f })
                foreach (float z in new[] { 7.8f, 19.2f })
                {
                    G.Lamp(parent, new Vector3(x, DeckTop + 5f, z), 7f);
                    G.Visual(PrimitiveType.Cylinder, parent, "PH_LampPost",
                        new Vector3(x, DeckTop + 2.5f, z), new Vector3(0.15f, 2.5f, 0.15f), G.gridMat);
                }

            // Pier deck lamps, 4 m up.
            G.Lamp(parent, new Vector3(1.3f, DeckTop + 4f, 30f), 5.5f);
            G.Lamp(parent, new Vector3(1.3f, DeckTop + 4f, 41f), 5.5f);
            G.Lamp(parent, new Vector3(26f, DeckTop + 4.5f, 23f), 6.5f);

            // Humvee headlights: a wall of light across the west end of the road.
            foreach (float z in new[] { 9.2f, 18f })
                G.SpotLamp(parent, "PH_HumveeHeadlight", new Vector3(-34.8f, DeckTop + 1f, z),
                    new Vector3(-22f, DeckTop, z), 34f, 14f);
        }

        /// <summary>
        /// First-pass guards from design/guard-vision/guard-vision-spec.md (map A placement).
        /// Each stands at one point (a one-point route: no back-and-forth) and sweeps its head.
        ///   W  guide wall end, facing west over the river. Teaches: behind is safe, beside is not.
        ///   L, L2  bridge south parapet, 9.6 m above the ledge, facing south, each with a swivel
        ///          spotlight sweeping the ledge, out of step so a gap opens.
        ///   P  in front of the gatehouse door, facing south over the road.
        /// Plus a lamp on the ledge by the hut. Open in the design: which way W and L2 face, and
        /// whether the Humvee light wall gets a guard (it doesn't yet).
        /// </summary>
        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;
            void Post(string id, Vector3 position, float yaw)
            {
                var route = G.Route(routes, "PH_Route_" + id, true, 3f, (position, yaw));
                G.BuildGuard("PH_Guard_" + id, position, yaw, route, player);
            }

            Post("W", new Vector3(0.6f, WallTop, -1.8f), 270f);
            Post("L", new Vector3(8f, DeckTop, 7.6f), 180f);
            Post("L2", new Vector3(3f, DeckTop, 7.8f), 180f);
            Post("P", new Vector3(14f, DeckTop, 21.2f), 180f);

            // Swivel spotlights for L and L2: on the parapet's outer face, aimed down at the ledge.
            // Each is its guard's eyes: standing in the beam gets you spotted at once (GuardVision.searchlights).
            var lights = G.Group(root, "PH_GuardLights");
            var swivelL = Swivel(lights, "PH_Swivel_L", new Vector3(8f, DeckTop + 1.3f, 6.8f), 0f);
            var swivelL2 = Swivel(lights, "PH_Swivel_L2", new Vector3(3f, DeckTop + 1.3f, 6.8f), 0.5f);
            GameObject.Find("PH_Guard_L").GetComponent<GuardVision>().searchlights = new[] { swivelL };
            GameObject.Find("PH_Guard_L2").GetComponent<GuardVision>().searchlights = new[] { swivelL2 };

            // Ledge lamp by the hut: its Dim ring is Slow walking, Very slow crawling; the ledge's
            // front edge stays dark.
            G.Lamp(lights, new Vector3(8f, LedgeTop + 3.4f, 5.6f), 6f);
        }

        static StealthLight Swivel(Transform parent, string name, Vector3 position, float phase)
        {
            // About a 3.5 m pool on the ledge (30 degrees from ~14 m up).
            var light = G.SpotLamp(parent, name, position, new Vector3(position.x, LedgeTop, 3.5f), 30f, 20f, 40f);
            var stealth = light.GetComponent<StealthLight>();
            stealth.litFraction = 1f;       // the whole beam is Lit
            stealth.litRangeFraction = 1f;
            var swivel = light.gameObject.AddComponent<SwivelLight>();
            swivel.sweepAngle = 80f;
            swivel.sweepTime = 11f; // slowed from 8 s after playtest
            swivel.phase = phase;
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
        /// swoops round to your right for the dam view, then slowly cranes back and up behind you onto
        /// the lock head, and drops back to top-down 20 m before the wall's end. Each stair has a fixed
        /// camera out over the water, looking at it side-on.
        /// </summary>
        static void BuildCameraZones()
        {
            var zones = new GameObject("PH_CameraZones").transform; // outside PH_Level: triggers stay off the NavMesh

            // Along the wall (spawn at z -118): 16 m of plain top-down, the dam view (z -102..-52, 50 m),
            // 12 m of plain top-down again, then the lock-head crane (z -40..-22, 18 m).
            // Dam view: tracks alongside you, 9 m to your right (east), looking west past you over the river.
            var dam = Zone(zones, "PH_Cam_DamView", CameraZone.Kind.Track, new Vector3(1.5f, 0.5f, -77f), new Vector3(10f, 8f, 50f), 1.8f);
            dam.eyeRelativeToPlayer = true;
            dam.eyeStart = dam.eyeEnd = new Vector3(9f, 3.5f, -2f);
            dam.lookStart = dam.lookEnd = new Vector3(-30f, 4f, 8f);
            dam.fovStart = dam.fovEnd = 50f;
            // Up on the stick stays "along the wall" (north) for the whole walk, so stopping or
            // turning round never re-maps the controls to the side-on camera.
            dam.fixedControls = true;
            dam.controlYaw = 0f;

            // Lock view: starts at your shoulder, then slowly cranes back and up until the head fills the frame.
            var lockView = Zone(zones, "PH_Cam_LockView", CameraZone.Kind.Crane, new Vector3(1.5f, 0.5f, -31f), new Vector3(10f, 8f, 18f), 2.5f);
            lockView.progressStart = new Vector3(1.5f, 0f, -40f);
            lockView.progressEnd = new Vector3(1.5f, 0f, -22f);
            lockView.easePower = 2f;
            lockView.eyeRelativeToPlayer = true;
            lockView.eyeStart = new Vector3(0.6f, 2f, -4.5f);
            lockView.eyeEnd = new Vector3(4f, 13f, -19f);
            lockView.lookStart = new Vector3(0f, 1.8f, 12f);
            lockView.lookEnd = new Vector3(0f, 11f, 45f);
            lockView.fovStart = 50f;
            lockView.fovEnd = 42f;
            lockView.fixedControls = true;
            lockView.controlYaw = 0f;

            // Stairs: fixed cameras over the water to the south, turning to keep you in frame.
            // The lower one covers only the first flight (the outer lane) and the turn: step onto the
            // second flight and it's back to top-down, so you can see the searchlights sweeping the ledge
            // before you reach it (playtest: the side view walked you straight into the beam).
            float lx0 = LowerX0, lx1 = LowerX0 + StairLength;
            float lowerZoneZ0 = LowerZ0 - 1f, lowerZoneZ1 = LowerZ0 + Lane;
            var lower = Zone(zones, "PH_Cam_LowerStairs", CameraZone.Kind.Fixed,
                new Vector3((lx0 - 0.5f + lx1 + 1f) * 0.5f, (WallTop + LedgeTop) * 0.5f, (lowerZoneZ0 + lowerZoneZ1) * 0.5f),
                new Vector3(lx1 + 1f - (lx0 - 0.5f), LedgeTop - WallTop + 4f, lowerZoneZ1 - lowerZoneZ0), 1f);
            lower.eyeRelativeToPlayer = false;
            lower.eyeStart = lower.eyeEnd = new Vector3((lx0 + lx1) * 0.5f, WallTop + 5f, -15f);
            lower.lookStart = lower.lookEnd = new Vector3(0f, 1.2f, 0f);
            lower.fovStart = lower.fovEnd = 45f;

            float ux0 = UpperX0, ux1 = UpperX0 + StairLength;
            var upper = Zone(zones, "PH_Cam_UpperStairs", CameraZone.Kind.Fixed,
                new Vector3((ux0 - 0.5f + ux1 + 1f) * 0.5f, (LedgeTop + DeckTop) * 0.5f, (UpperZ0 - 0.5f + 7.5f) * 0.5f),
                new Vector3(ux1 + 1f - (ux0 - 0.5f), DeckTop - LedgeTop + 4f, 7.5f - (UpperZ0 - 0.5f)), 1f);
            upper.eyeRelativeToPlayer = false;
            upper.eyeStart = upper.eyeEnd = new Vector3((ux0 + ux1) * 0.5f, LedgeTop + 6.5f, -11f);
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

        /// <summary>The goal: the gatehouse door. The level carries on into the chamber later.</summary>
        static void BuildGoal()
        {
            var goal = new GameObject("PH_GatehouseGoal");
            goal.transform.position = new Vector3(14.25f, DeckTop + 1.5f, 25f);
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
