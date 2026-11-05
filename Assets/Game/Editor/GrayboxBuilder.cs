using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace StealthProto.EditorTools
{
    /// <summary>
    /// Builds the graybox test scene from code: a dark warehouse with a floor, a north catwalk,
    /// an overhead beam ending in a perch, a raised platform, crates, hanging lamps, three
    /// guards and the player. Everything it makes is a placeholder (Assets/_Placeholder, PH_*).
    /// Re-run it any time; it overwrites the scene.
    /// </summary>
    public static class GrayboxBuilder
    {
        internal const string Root = "Assets/_Placeholder";
        const string ScenePath = Root + "/Scenes/PH_GrayboxTest.unity";

        internal static Material gridMat, playerMat, guardMat, noseMat, coneMat, lampMat;
        internal static Material playerSilhouetteMat, guardSilhouetteMat;
        internal static readonly Dictionary<Vector3, Mesh> MeshCache = new Dictionary<Vector3, Mesh>();

        [MenuItem("Stealth Prototype/Build Graybox Test Scene", priority = 0)]
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

            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");
            EnsureFolder(Root + "/Scenes");
            MeshCache.Clear();
            CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            var level = new GameObject("PH_Level");
            BuildLevel(level.transform);
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            level.AddComponent<RuntimeNavMeshBaker>();

            BuildLamps(level.transform);

            var player = BuildPlayer(new Vector3(-17f, 0f, -12f));
            BuildCamera(player);
            LookSetup.Apply(Camera.main);
            BuildGuards(player.transform);

            new GameObject("PH_DebugHUD").AddComponent<DebugHUD>();
            new GameObject("PH_LastKnownMarker").AddComponent<LastKnownMarker>();
            new GameObject("HeightLayers").AddComponent<HeightLayers>(); // Floor < 0.75 m <= Platform < 2.75 m <= Catwalk

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PH] Graybox scene built at {ScenePath}. Press Play.");
        }

        // ---------------------------------------------------------------- materials

        internal static void CreateMaterials()
        {
            var magenta = new Color(1f, 0f, 1f);
            var grid = MakeTexture("PH_Grid", 64, (x, y) =>
            {
                if (x < 2 || y < 2) return magenta;
                bool checker = (x / 32 + y / 32) % 2 == 0;
                return checker ? new Color(0.42f, 0.42f, 0.42f) : new Color(0.5f, 0.5f, 0.5f);
            });
            var checkerTex = MakeTexture("PH_Checker", 32, (x, y) =>
                (x / 8 + y / 8) % 2 == 0 ? Color.white : new Color(0.55f, 0.55f, 0.55f));

            gridMat = MakeLitMaterial("PH_Grid", grid, Color.white);
            playerMat = MakeLitMaterial("PH_Player", checkerTex, new Color(0.25f, 0.9f, 1f));
            // Neutral: only guard state (amber, red) shows in colour. See LookSetup.
            guardMat = MakeLitMaterial("PH_Guard", checkerTex, new Color(0.8f, 0.8f, 0.8f));
            noseMat = MakeLitMaterial("PH_Nose", null, new Color(1f, 0f, 1f));
            lampMat = MakeLitMaterial("PH_Lamp", null, new Color(1f, 1f, 1f));
            lampMat.EnableKeyword("_EMISSION");
            lampMat.SetColor("_EmissionColor", Color.white * 2f);

            playerSilhouetteMat = MakeSilhouetteMaterial("PH_SilhouettePlayer", new Color(0.35f, 0.95f, 1f, 0.5f));
            // You always see yourself behind walls; guards only with the x-ray testing toggle (F4).
            if (playerSilhouetteMat != null)
            {
                playerSilhouetteMat.SetFloat("_AlwaysOn", 1f);
                EditorUtility.SetDirty(playerSilhouetteMat);
            }
            guardSilhouetteMat = MakeSilhouetteMaterial("PH_SilhouetteGuard", new Color(1f, 0.6f, 0.2f, 0.55f));

            string conePath = Root + "/Materials/PH_VisionCone.mat";
            coneMat = AssetDatabase.LoadAssetAtPath<Material>(conePath);
            if (coneMat == null)
            {
                coneMat = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(coneMat, conePath);
            }
        }

        static Texture2D MakeTexture(string name, int size, System.Func<int, int, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel(x, y));
            tex.Apply();

            string path = $"{Root}/Textures/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>X-ray silhouette (Assets/_Placeholder/Shaders/PH_Silhouette.shader). Null if the shader is missing.</summary>
        internal static Material MakeSilhouetteMaterial(string name, Color color)
        {
            var shader = Shader.Find("StealthProto/PH_Silhouette");
            if (shader == null)
            {
                Debug.LogWarning("[PH] PH_Silhouette shader not found; characters won't show through walls.");
                return null;
            }
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static Material MakeLitMaterial(string name, Texture2D tex, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- lighting

        internal static void SetupLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.09f);

            var lightingPath = Root + "/Scenes/PH_Lighting.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(lightingPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "PH_Lighting" };
                AssetDatabase.CreateAsset(settings, lightingPath);
            }
            settings.bakedGI = false;
            settings.realtimeGI = false;
            Lightmapping.lightingSettings = settings;

            var moon = new GameObject("PH_Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.65f, 1f);
            moon.intensity = 0.12f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        /// <summary>
        /// Darkness is scarce: wide, crisp pools close enough that only narrow dark lanes run
        /// between them. The player starts in the dark south-west corner.
        /// </summary>
        static void BuildLamps(Transform parent)
        {
            var lamps = new GameObject("PH_Lamps").transform;
            lamps.SetParent(parent, false);
            const float h = 3.4f;     // under the 4 m catwalk and beam
            const float angle = 100f; // about a 4 m pool radius from this height
            const float range = 7f;

            // South row (the start corner at -17, -12 stays dark).
            WideLamp(lamps, new Vector3(-9f, h, -11f), angle, range);
            WideLamp(lamps, new Vector3(4f, h, -11.5f), angle, range);
            WideLamp(lamps, new Vector3(16f, h, -10f), angle, range);
            // Middle row. The vent comes out in the dark west of the first pool.
            WideLamp(lamps, new Vector3(-12.5f, h, -2.5f), angle, range);
            WideLamp(lamps, new Vector3(-5f, h, -4.5f), angle, range);
            WideLamp(lamps, new Vector3(9f, 4.9f, -5f), angle, range); // over the platform
            WideLamp(lamps, new Vector3(16.5f, h, 1f), angle, range);
            // North row.
            WideLamp(lamps, new Vector3(-13f, h, 6.5f), angle, range);
            WideLamp(lamps, new Vector3(-5f, h, 4.5f), angle, range);
            WideLamp(lamps, new Vector3(4.5f, h, 5f), angle, range);
            WideLamp(lamps, new Vector3(13f, h, 7f), angle, range);
            // Catwalk: the beam junction and the west half are lit, the east half is the dark way along.
            Lamp(lamps, new Vector3(0f, 6.8f, 13f), 6f);
            Lamp(lamps, new Vector3(-11f, 6.8f, 13f), 6f);

            // The counter to the beam and perch: a lamp lighting the east half of the perch, so
            // only a narrow dark strip on its west side is safe (and the catwalk guard checks it).
            SpotLamp(lamps, "PH_Lamp_Perch", new Vector3(2.6f, 6.4f, -7f), new Vector3(1.1f, 4f, -7f), 42f, 6f);

            // Alarm floods: off until the room is alerted, then on for good (the lasting cost).
            var south = SpotLamp(lamps, "PH_AlarmFlood", new Vector3(-6f, 7.2f, -14.6f), new Vector3(-6f, 0f, -3f), 75f, 18f, 18f);
            south.color = new Color(1f, 0.55f, 0.5f);
            south.gameObject.AddComponent<AlarmLight>();
            var north = SpotLamp(lamps, "PH_AlarmFlood", new Vector3(10f, 7.2f, 14.6f), new Vector3(9f, 0f, 3f), 75f, 18f, 18f);
            north.color = new Color(1f, 0.55f, 0.5f);
            north.gameObject.AddComponent<AlarmLight>();
        }

        static Light WideLamp(Transform parent, Vector3 position, float angle, float range)
            => SpotLamp(parent, "PH_Lamp", position, position + Vector3.down, angle, range);

        /// <summary>
        /// Ceiling lamp: a downward spot with a crisp stepped pool (Lit core, Dim ring) so the floor
        /// shows exactly which light tier you'd be in. range is the light's reach.
        /// </summary>
        internal static Light Lamp(Transform parent, Vector3 position, float range)
            => SpotLamp(parent, "PH_Lamp", position, position + Vector3.down, 80f, range);

        /// <summary>A stealth spot light aimed at a point, with a visible fixture and hard shadows.</summary>
        internal static Light SpotLamp(Transform parent, string name, Vector3 position, Vector3 aimAt, float spotAngle, float range, float intensity = 14f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Vector3 dir = (aimAt - position).normalized;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up));
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle - 2f;
            light.range = range;
            light.intensity = intensity;
            light.color = Color.white; // warm light would survive the red/amber colour band
            light.shadows = LightShadows.Hard; // crisp pool edges
            go.AddComponent<StealthLight>();

            // Small visible fixture, no collider so it never blocks its own light.
            var bulb = Visual(PrimitiveType.Cube, go.transform, "PH_LampBulb", Vector3.back * 0.15f, new Vector3(0.4f, 0.4f, 0.15f), lampMat);
            bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return light;
        }

        // ---------------------------------------------------------------- level

        static void BuildLevel(Transform root)
        {
            // Floor: 40 x 30, split into 10 x 10 tiles to keep per-object light counts low.
            var floor = Group(root, "PH_Floor");
            for (int x = 0; x < 4; x++)
                for (int z = 0; z < 3; z++)
                    Block(floor, "PH_FloorTile", new Vector3(-15f + x * 10f, -0.25f, -10f + z * 10f), new Vector3(10f, 0.5f, 10f));

            var walls = Group(root, "PH_Walls");
            Block(walls, "PH_Wall_N", new Vector3(0f, 4f, 15.5f), new Vector3(42f, 8f, 1f));
            Block(walls, "PH_Wall_S", new Vector3(0f, 4f, -15.5f), new Vector3(42f, 8f, 1f));
            Block(walls, "PH_Wall_E", new Vector3(20.5f, 4f, 0f), new Vector3(1f, 8f, 30f));
            Block(walls, "PH_Wall_W", new Vector3(-20.5f, 4f, 0f), new Vector3(1f, 8f, 30f));
            // Inner wall between the start corner and the west side, x -20..-11. Walking around
            // its east end passes the floor guard's route; the vent goes through it instead.
            Block(walls, "PH_Wall_Inner", new Vector3(-19f, 2f, -6f), new Vector3(2f, 4f, 0.5f));
            Block(walls, "PH_Wall_Inner", new Vector3(-14f, 2f, -6f), new Vector3(6f, 4f, 0.5f));
            Block(walls, "PH_Wall_InnerOverVent", new Vector3(-17.5f, 2.35f, -6f), new Vector3(1f, 3.3f, 0.5f));
            BuildVent(root);

            // North catwalk at 4 m with ramps at both ends.
            var catwalk = Group(root, "PH_Catwalk");
            Block(catwalk, "PH_CatwalkDeck", new Vector3(0f, 3.85f, 13f), new Vector3(40f, 0.3f, 4f));
            // Solid base under the middle of the catwalk: a 4 m wall you can climb.
            Block(catwalk, "PH_CatwalkBase", new Vector3(0f, 1.85f, 13f), new Vector3(31.6f, 3.7f, 4f));
            Ramp(catwalk, "PH_RampWest", new Vector3(-17.5f, 0f, 1.5f), new Vector3(-17.5f, 4f, 11f), 2.6f);
            Ramp(catwalk, "PH_RampEast", new Vector3(17.5f, 0f, 1.5f), new Vector3(17.5f, 4f, 11f), 2.6f);

            // Overhead beam from the catwalk out over the room, ending in a perch. Wide enough for
            // the catwalk guard to walk out and check the perch. Its edges are good for hanging.
            var beam = Group(root, "PH_Beam");
            Block(beam, "PH_BeamWalk", new Vector3(0f, 3.85f, 2.75f), new Vector3(1.6f, 0.3f, 16.5f));
            Block(beam, "PH_Perch", new Vector3(0f, 3.85f, -7f), new Vector3(3f, 0.3f, 3f));
            Block(beam, "PH_PerchTower", new Vector3(0f, 1.85f, -7f), new Vector3(3f, 3.7f, 3f)); // climbable

            // Raised platform with a ramp.
            var platform = Group(root, "PH_Platform");
            Block(platform, "PH_PlatformBlock", new Vector3(9f, 0.75f, -5f), new Vector3(7f, 1.5f, 7f));
            Ramp(platform, "PH_PlatformRamp", new Vector3(9f, 0f, -12.5f), new Vector3(9f, 1.5f, -8.5f), 2.2f);

            // Cover: low crates (see over them, hide behind them prone) and tall crates (full cover, cling to them).
            var crates = Group(root, "PH_Crates");
            Block(crates, "PH_CrateLow", new Vector3(-10f, 0.55f, -3f), new Vector3(2f, 1.1f, 1.2f));
            Block(crates, "PH_CrateLow", new Vector3(-4f, 0.55f, -10f), new Vector3(1.2f, 1.1f, 2f));
            Block(crates, "PH_CrateLow", new Vector3(4f, 0.55f, 4f), new Vector3(2f, 1.1f, 1.2f));
            Block(crates, "PH_CrateLow", new Vector3(14f, 0.55f, 4f), new Vector3(1.2f, 1.1f, 2f));
            Block(crates, "PH_CrateTall", new Vector3(-12f, 1.3f, 4f), new Vector3(2.5f, 2.6f, 2.5f));
            Block(crates, "PH_CrateTall", new Vector3(-6f, 1.3f, -1f), new Vector3(1.5f, 2.6f, 3f));
            Block(crates, "PH_CrateTall", new Vector3(13f, 1.3f, -12f), new Vector3(2.5f, 2.6f, 2.5f));
            Block(crates, "PH_CrateTall", new Vector3(6f, 1.3f, 9f), new Vector3(2.5f, 2.6f, 2.5f));

            // Tables: crawl under them (0.75 m clearance; you can't stand up underneath).
            var tables = Group(root, "PH_Tables");
            Table(tables, new Vector3(-1f, 0f, 7.5f), new Vector2(3f, 1.4f));
            Table(tables, new Vector3(14.5f, 0f, -6.5f), new Vector2(1.4f, 3f));
        }

        /// <summary>
        /// A crawl-only vent through the inner wall: a 1 m wide, 0.7 m high gap with a short duct
        /// that comes out in a dark lane on the west side, behind the west guard's route.
        /// </summary>
        static void BuildVent(Transform root)
        {
            var vent = Group(root, "PH_Vent");
            const float x = -17.5f, z0 = -5.75f, z1 = -3.25f, height = 0.7f;
            float length = z1 - z0, zc = (z0 + z1) * 0.5f;
            Block(vent, "PH_VentSide", new Vector3(x - 0.55f, height * 0.5f, zc), new Vector3(0.1f, height, length));
            Block(vent, "PH_VentSide", new Vector3(x + 0.55f, height * 0.5f, zc), new Vector3(0.1f, height, length));
            Block(vent, "PH_VentTop", new Vector3(x, height + 0.05f, zc), new Vector3(1.2f, 0.1f, length));
        }

        /// <summary>A table: a top at height with four thin legs. Underside clearance is height - 0.1.</summary>
        internal static void Table(Transform parent, Vector3 feet, Vector2 size, float height = 0.85f)
        {
            var t = Group(parent, "PH_Table");
            t.position = feet;
            Block(t, "PH_TableTop", feet + Vector3.up * (height - 0.05f), new Vector3(size.x, 0.1f, size.y));
            float lx = size.x * 0.5f - 0.1f, lz = size.y * 0.5f - 0.1f;
            float legH = height - 0.1f;
            foreach (var corner in new[] { new Vector2(-lx, -lz), new Vector2(lx, -lz), new Vector2(-lx, lz), new Vector2(lx, lz) })
                Block(t, "PH_TableLeg", feet + new Vector3(corner.x, legH * 0.5f, corner.y), new Vector3(0.1f, legH, 0.1f));
        }

        internal static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        internal static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = gridMat;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>A ramp whose top surface runs from bottom to top.</summary>
        internal static void Ramp(Transform parent, string name, Vector3 bottom, Vector3 top, float width)
        {
            const float thickness = 0.3f;
            Vector3 dir = top - bottom;
            float length = dir.magnitude + 0.4f;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 center = (bottom + top) * 0.5f - rot * Vector3.up * (thickness * 0.5f);
            Block(parent, name, center, new Vector3(width, thickness, length), rot);
        }

        /// <summary>Box mesh with UVs in metres so the grid texture tiles at 1 m on every face.</summary>
        static Mesh BoxMesh(Vector3 size)
        {
            if (MeshCache.TryGetValue(size, out var cached)) return cached;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 h = size * 0.5f;

            void Face(Vector3 center, Vector3 right, Vector3 up, float w, float hgt)
            {
                int i = verts.Count;
                Vector3 normal = Vector3.Cross(up, right);
                Vector3 r = right * (w * 0.5f);
                Vector3 u = up * (hgt * 0.5f);
                verts.Add(center - r - u);
                verts.Add(center + r - u);
                verts.Add(center + r + u);
                verts.Add(center - r + u);
                for (int k = 0; k < 4; k++) normals.Add(normal);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(w, 0f));
                uvs.Add(new Vector2(w, hgt));
                uvs.Add(new Vector2(0f, hgt));
                tris.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });
            }

            Face(new Vector3(0f, 0f, h.z), Vector3.left, Vector3.up, size.x, size.y);     // +Z
            Face(new Vector3(0f, 0f, -h.z), Vector3.right, Vector3.up, size.x, size.y);   // -Z
            Face(new Vector3(h.x, 0f, 0f), Vector3.forward, Vector3.up, size.z, size.y);  // +X
            Face(new Vector3(-h.x, 0f, 0f), Vector3.back, Vector3.up, size.z, size.y);    // -X
            Face(new Vector3(0f, h.y, 0f), Vector3.right, Vector3.forward, size.x, size.z); // +Y
            Face(new Vector3(0f, -h.y, 0f), Vector3.left, Vector3.forward, size.x, size.z); // -Y

            var mesh = new Mesh { name = $"PH_Box_{size.x}x{size.y}x{size.z}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            MeshCache[size] = mesh;
            return mesh;
        }

        internal static GameObject Visual(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // ---------------------------------------------------------------- characters

        internal static GameObject BuildPlayer(Vector3 position)
        {
            var p = new GameObject("PH_Player") { tag = "Player" };
            p.transform.position = position;

            var cc = p.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;

            p.AddComponent<PlayerInputReader>();
            var health = p.AddComponent<Health>();
            health.maxHealth = 5;
            p.AddComponent<PlayerMotor>();
            p.AddComponent<LightExposure>();
            p.AddComponent<PlayerCombat>();
            p.AddComponent<PlayerLife>();
            p.AddComponent<PlayerContextActions>();
            p.AddComponent<PlayerCover>();
            p.AddComponent<FocusMode>();

            var tint = p.AddComponent<PlayerShadowTint>();
            tint.renderers = Mannequin(p.transform, playerMat, playerSilhouetteMat);
            return p;
        }

        internal static void BuildCamera(GameObject player)
        {
            var rigGO = new GameObject("CameraRig");
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            camGO.transform.SetParent(rigGO.transform, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;

            var rig = rigGO.AddComponent<CameraRig>();
            rig.target = player.transform;
            rig.cam = cam;
            rigGO.transform.position = player.transform.position;

            var combat = player.GetComponent<PlayerCombat>();
            combat.aimCamera = cam;
            combat.rig = rig;
        }

        /// <summary>
        /// Four guards with overlapping coverage: taking one out usually happens in another's view.
        /// All routes are deterministic loops with fixed pauses and look directions.
        /// </summary>
        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;

            // Floor guard: a loop around the middle of the room, past the vent wall's east end.
            var floorRoute = Route(routes, "PH_Route_Floor", true, 1.5f,
                (new Vector3(-10f, 0f, 1.5f), 270f),
                (new Vector3(3f, 0f, 1.5f), 90f),
                (new Vector3(3f, 0f, -11f), 180f),
                (new Vector3(-9f, 0f, -11f), 270f));
            BuildGuard("PH_Guard_Floor", new Vector3(-10f, 0f, 1.5f), 90f, floorRoute, player);

            // West guard: the west side north of the inner wall. Its route touches the floor
            // guard's at the north-west corner, so each covers the other's back there.
            var westRoute = Route(routes, "PH_Route_West", true, 2f,
                (new Vector3(-15f, 0f, 1f), 90f),
                (new Vector3(-15f, 0f, 9f), 0f),
                (new Vector3(-8.5f, 0f, 9f), 90f),
                (new Vector3(-8.5f, 0f, 3f), 180f));
            BuildGuard("PH_Guard_West", new Vector3(-15f, 0f, 1f), 0f, westRoute, player);

            // Catwalk guard: one big loop, never a straight back-and-forth (no sudden 180s).
            // East along the back of the deck, round the east end, west along the front, down the
            // beam to check the perch (round the perch, not a spin), back up, round the west end.
            // Wait 0 = a rounding point it walks through.
            var catwalkRoute = RouteTimed(routes, "PH_Route_Catwalk", 2.5f,
                (new Vector3(-13f, 4f, 14f), 90f, 0f),
                (new Vector3(12.5f, 4f, 14f), 90f, 0f),
                (new Vector3(14.5f, 4f, 13f), 180f, 0f),
                (new Vector3(13f, 4f, 12f), 180f, 2.5f),       // east end: looks into the room
                (new Vector3(1.5f, 4f, 12f), 270f, 0f),
                (new Vector3(0f, 4f, 11f), 180f, 2.5f),        // beam junction: looks down the beam
                (new Vector3(0.6f, 4f, -6f), 180f, 0f),
                (new Vector3(0.6f, 4f, -7.8f), 180f, 2.5f),    // perch: looks south over the room
                (new Vector3(-0.6f, 4f, -7.8f), 270f, 0f),
                (new Vector3(-0.6f, 4f, -6f), 0f, 0f),
                (new Vector3(0f, 4f, 11.2f), 0f, 0f),
                (new Vector3(-1.5f, 4f, 12f), 270f, 0f),
                (new Vector3(-14f, 4f, 12f), 180f, 2.5f),      // west end: looks into the room
                (new Vector3(-15.5f, 4f, 13f), 0f, 0f));
            BuildGuard("PH_Guard_Catwalk", new Vector3(-13f, 4f, 14f), 90f, catwalkRoute, player);

            // Platform guard: a slow square on the platform, looking out from each corner.
            var platformRoute = Route(routes, "PH_Route_Platform", true, 2.5f,
                (new Vector3(7f, 1.5f, -7f), 225f),
                (new Vector3(11f, 1.5f, -7f), 135f),
                (new Vector3(11f, 1.5f, -3f), 45f),
                (new Vector3(7f, 1.5f, -3f), 315f));
            BuildGuard("PH_Guard_Platform", new Vector3(7f, 1.5f, -7f), 90f, platformRoute, player);
        }

        /// <summary>A looping route with a wait per point (0 = walk through).</summary>
        internal static PatrolRoute RouteTimed(Transform parent, string name, float defaultWait, params (Vector3 pos, float yaw, float wait)[] points)
        {
            var simple = new (Vector3 pos, float yaw)[points.Length];
            for (int i = 0; i < points.Length; i++) simple[i] = (points[i].pos, points[i].yaw);
            var route = Route(parent, name, true, defaultWait, simple);
            for (int i = 0; i < points.Length; i++)
                route.GetPoint(i).gameObject.AddComponent<PatrolPoint>().wait = points[i].wait;
            return route;
        }

        internal static PatrolRoute Route(Transform parent, string name, bool loop, float wait, params (Vector3 pos, float yaw)[] points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var route = go.AddComponent<PatrolRoute>();
            route.loop = loop;
            route.waitTime = wait;
            for (int i = 0; i < points.Length; i++)
            {
                var p = new GameObject($"P{i}").transform;
                p.SetParent(go.transform, false);
                p.SetPositionAndRotation(points[i].pos, Quaternion.Euler(0f, points[i].yaw, 0f));
            }
            return route;
        }

        internal static void BuildGuard(string name, Vector3 position, float yaw, PatrolRoute route, Transform player)
        {
            var g = new GameObject(name);
            g.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var col = g.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.height = 1.8f;
            col.radius = 0.3f;

            var agent = g.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = 1.4f;
            agent.angularSpeed = 540f;
            agent.acceleration = 24f;
            agent.stoppingDistance = 0.2f;
            agent.enabled = false; // enabled by GuardBrain after the runtime bake

            var health = g.AddComponent<Health>();
            health.maxHealth = 3;

            var bodyRenderers = Mannequin(g.transform, guardMat, guardSilhouetteMat);

            var head = new GameObject("Head").transform;
            head.SetParent(g.transform, false);
            head.localPosition = new Vector3(0f, 1.65f, 0f);

            var flashGO = new GameObject("PH_Flashlight");
            flashGO.transform.SetParent(head, false);
            flashGO.transform.localPosition = new Vector3(0.1f, -0.2f, 0.45f); // held out in front so the body never shadows the beam
            flashGO.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var light = flashGO.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 14f;
            light.spotAngle = 70f;
            light.innerSpotAngle = 68f;
            light.intensity = 30f;
            light.color = Color.white;
            light.shadows = LightShadows.Hard;
            // The real light isn't drawn (it would be soft). Its pool is drawn instead as a crisp,
            // flat overlay showing exactly where it makes you Lit or Dim (LightFootprintMesh below).
            light.cullingMask = 0;
            light.renderingLayerMask = 0;
            light.shadows = LightShadows.None;
            var stealthLight = flashGO.AddComponent<StealthLight>();
            stealthLight.ignoreRoot = g.transform;

            var vision = g.AddComponent<GuardVision>();
            vision.eye = head;
            vision.target = player;
            vision.viewDistance = 13f;
            vision.halfAngle = 35f;

            var brain = g.AddComponent<GuardBrain>();
            brain.route = route;
            brain.vision = vision;
            brain.head = head;
            brain.flashlight = light;
            brain.bodyRenderers = bodyRenderers;

            var cone = new GameObject("PH_VisionCone");
            cone.transform.SetParent(g.transform, false);
            cone.AddComponent<MeshFilter>();
            cone.AddComponent<MeshRenderer>().sharedMaterial = coneMat;
            var coneMesh = cone.AddComponent<VisionConeMesh>();
            coneMesh.vision = vision;
            coneMesh.brain = brain;

            var footprint = new GameObject("PH_FlashlightFootprint");
            footprint.transform.SetParent(g.transform, false);
            footprint.AddComponent<MeshFilter>();
            footprint.AddComponent<MeshRenderer>().sharedMaterial = coneMat;
            footprint.AddComponent<LightFootprintMesh>().source = stealthLight;
        }

        /// <summary>
        /// Placeholder mannequin at human proportions (about 1.75 m, feet at the origin): legs, torso,
        /// arms, head, and a magenta visor showing facing. Returns the body renderers (not the visor).
        /// </summary>
        internal static Renderer[] Mannequin(Transform parent, Material bodyMat, Material silhouetteMat = null)
        {
            var root = new GameObject("PH_Mannequin").transform;
            root.SetParent(parent, false);
            var parts = new[]
            {
                Visual(PrimitiveType.Cube, root, "PH_LegL", new Vector3(-0.1f, 0.425f, 0f), new Vector3(0.15f, 0.85f, 0.17f), bodyMat),
                Visual(PrimitiveType.Cube, root, "PH_LegR", new Vector3(0.1f, 0.425f, 0f), new Vector3(0.15f, 0.85f, 0.17f), bodyMat),
                Visual(PrimitiveType.Cube, root, "PH_Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.4f, 0.6f, 0.22f), bodyMat),
                Visual(PrimitiveType.Cube, root, "PH_ArmL", new Vector3(-0.26f, 1.13f, 0f), new Vector3(0.1f, 0.62f, 0.12f), bodyMat),
                Visual(PrimitiveType.Cube, root, "PH_ArmR", new Vector3(0.26f, 1.13f, 0f), new Vector3(0.1f, 0.62f, 0.12f), bodyMat),
                Visual(PrimitiveType.Sphere, root, "PH_Head", new Vector3(0f, 1.6f, 0f), Vector3.one * 0.24f, bodyMat),
            };
            Visual(PrimitiveType.Cube, root, "PH_Visor", new Vector3(0f, 1.62f, 0.11f), new Vector3(0.17f, 0.06f, 0.06f), noseMat);

            var renderers = new Renderer[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                renderers[i] = parts[i].GetComponent<Renderer>();
                // Second material: drawn only where the part is hidden behind geometry.
                if (silhouetteMat != null) renderers[i].sharedMaterials = new[] { bodyMat, silhouetteMat };
            }
            return renderers;
        }

        // ---------------------------------------------------------------- utils

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
