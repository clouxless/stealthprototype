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
        const string Root = "Assets/_Placeholder";
        const string ScenePath = Root + "/Scenes/PH_GrayboxTest.unity";

        static Material gridMat, playerMat, guardMat, noseMat, coneMat, lampMat;
        static readonly Dictionary<Vector3, Mesh> MeshCache = new Dictionary<Vector3, Mesh>();

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
            BuildGuards(player.transform);

            new GameObject("PH_DebugHUD").AddComponent<DebugHUD>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PH] Graybox scene built at {ScenePath}. Press Play.");
        }

        // ---------------------------------------------------------------- materials

        static void CreateMaterials()
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
            guardMat = MakeLitMaterial("PH_Guard", checkerTex, new Color(1f, 0.55f, 0.2f));
            noseMat = MakeLitMaterial("PH_Nose", null, new Color(1f, 0f, 1f));
            lampMat = MakeLitMaterial("PH_Lamp", null, new Color(1f, 0.9f, 0.6f));
            lampMat.EnableKeyword("_EMISSION");
            lampMat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.5f) * 2f);

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

        static Material MakeLitMaterial(string name, Texture2D tex, Color color)
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

        static void SetupLighting()
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

        static void BuildLamps(Transform parent)
        {
            var lamps = new GameObject("PH_Lamps").transform;
            lamps.SetParent(parent, false);
            Lamp(lamps, new Vector3(-8f, 3.2f, -3f), 7f);
            Lamp(lamps, new Vector3(8f, 3.4f, 6f), 7f);
            Lamp(lamps, new Vector3(-14f, 3.2f, 7f), 6f);
            Lamp(lamps, new Vector3(16f, 3.2f, -3f), 6f);
            Lamp(lamps, new Vector3(0f, 6.6f, 13f), 6f);
            Lamp(lamps, new Vector3(-5f, 3.2f, -12f), 5f);
        }

        static void Lamp(Transform parent, Vector3 position, float range)
        {
            var go = new GameObject("PH_Lamp");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = 9f;
            light.color = new Color(1f, 0.85f, 0.6f);
            light.shadows = LightShadows.Soft;
            go.AddComponent<StealthLight>();

            // Small visible bulb, no collider so it never blocks its own light.
            var bulb = Visual(PrimitiveType.Cube, go.transform, "PH_LampBulb", Vector3.up * 0.2f, new Vector3(0.4f, 0.15f, 0.4f), lampMat);
            bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
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
            Block(walls, "PH_Wall_Inner", new Vector3(-16f, 2f, -6f), new Vector3(8f, 4f, 0.5f));

            // North catwalk at 4 m with ramps at both ends and a railing.
            var catwalk = Group(root, "PH_Catwalk");
            Block(catwalk, "PH_CatwalkDeck", new Vector3(0f, 3.85f, 13f), new Vector3(40f, 0.3f, 4f));
            Block(catwalk, "PH_CatwalkRail", new Vector3(0f, 4.5f, 11.05f), new Vector3(31.6f, 1f, 0.1f));
            Ramp(catwalk, "PH_RampWest", new Vector3(-17.5f, 0f, 1.5f), new Vector3(-17.5f, 4f, 11f), 2.6f);
            Ramp(catwalk, "PH_RampEast", new Vector3(17.5f, 0f, 1.5f), new Vector3(17.5f, 4f, 11f), 2.6f);

            // Overhead beam from the catwalk out over the room, ending in a perch.
            var beam = Group(root, "PH_Beam");
            Block(beam, "PH_BeamWalk", new Vector3(0f, 3.85f, 2.75f), new Vector3(1.2f, 0.3f, 16.5f));
            Block(beam, "PH_Perch", new Vector3(0f, 3.85f, -7f), new Vector3(3f, 0.3f, 3f));
            Block(beam, "PH_PerchPillar", new Vector3(0f, 1.85f, -7f), new Vector3(0.8f, 3.7f, 0.8f));

            // Raised platform with a ramp.
            var platform = Group(root, "PH_Platform");
            Block(platform, "PH_PlatformBlock", new Vector3(9f, 0.75f, -5f), new Vector3(7f, 1.5f, 7f));
            Ramp(platform, "PH_PlatformRamp", new Vector3(9f, 0f, -12.5f), new Vector3(9f, 1.5f, -8.5f), 2.2f);

            // Cover: low crates (see over them) and tall crates (full cover).
            var crates = Group(root, "PH_Crates");
            Block(crates, "PH_CrateLow", new Vector3(-10f, 0.55f, -3f), new Vector3(2f, 1.1f, 1.2f));
            Block(crates, "PH_CrateLow", new Vector3(-4f, 0.55f, -10f), new Vector3(1.2f, 1.1f, 2f));
            Block(crates, "PH_CrateLow", new Vector3(4f, 0.55f, 4f), new Vector3(2f, 1.1f, 1.2f));
            Block(crates, "PH_CrateLow", new Vector3(14f, 0.55f, 4f), new Vector3(1.2f, 1.1f, 2f));
            Block(crates, "PH_CrateTall", new Vector3(-12f, 1.3f, 4f), new Vector3(2.5f, 2.6f, 2.5f));
            Block(crates, "PH_CrateTall", new Vector3(-6f, 1.3f, -1f), new Vector3(1.5f, 2.6f, 3f));
            Block(crates, "PH_CrateTall", new Vector3(13f, 1.3f, -12f), new Vector3(2.5f, 2.6f, 2.5f));
            Block(crates, "PH_CrateTall", new Vector3(6f, 1.3f, 9f), new Vector3(2.5f, 2.6f, 2.5f));
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rotation = null)
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
        static void Ramp(Transform parent, string name, Vector3 bottom, Vector3 top, float width)
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

        static GameObject Visual(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat)
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

        static GameObject BuildPlayer(Vector3 position)
        {
            var p = new GameObject("PH_Player") { tag = "Player" };
            p.transform.position = position;

            var cc = p.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
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

            var body = Visual(PrimitiveType.Capsule, p.transform, "PH_Body", new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 0.9f, 0.8f), playerMat);
            Visual(PrimitiveType.Cube, p.transform, "PH_Nose", new Vector3(0f, 1.35f, 0.38f), new Vector3(0.25f, 0.15f, 0.3f), noseMat);

            var tint = p.AddComponent<PlayerShadowTint>();
            tint.renderers = new[] { body.GetComponent<Renderer>() };
            return p;
        }

        static void BuildCamera(GameObject player)
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

        static void BuildGuards(Transform player)
        {
            var routes = new GameObject("PH_PatrolRoutes").transform;

            // Floor guard: a loop around the middle of the room.
            var floorRoute = Route(routes, "PH_Route_Floor", true, 1.5f,
                (new Vector3(-10f, 0f, 1.5f), 270f),
                (new Vector3(3f, 0f, 1.5f), 90f),
                (new Vector3(3f, 0f, -11f), 180f),
                (new Vector3(-9f, 0f, -11f), 270f));
            BuildGuard("PH_Guard_Floor", new Vector3(-10f, 0f, 1.5f), 90f, floorRoute, player);

            // Catwalk guard: back and forth along the catwalk, looking down into the room.
            var catwalkRoute = Route(routes, "PH_Route_Catwalk", false, 2.5f,
                (new Vector3(-14f, 4f, 13f), 180f),
                (new Vector3(0f, 4f, 13f), 180f),
                (new Vector3(14f, 4f, 13f), 180f));
            BuildGuard("PH_Guard_Catwalk", new Vector3(-14f, 4f, 13f), 90f, catwalkRoute, player);

            // Platform guard: a mostly stationary lookout.
            var platformRoute = Route(routes, "PH_Route_Platform", true, 4f,
                (new Vector3(8f, 1.5f, -5f), 270f),
                (new Vector3(10f, 1.5f, -3f), 0f));
            BuildGuard("PH_Guard_Platform", new Vector3(8f, 1.5f, -5f), 270f, platformRoute, player);
        }

        static PatrolRoute Route(Transform parent, string name, bool loop, float wait, params (Vector3 pos, float yaw)[] points)
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

        static void BuildGuard(string name, Vector3 position, float yaw, PatrolRoute route, Transform player)
        {
            var g = new GameObject(name);
            g.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var col = g.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1f, 0f);
            col.height = 2f;
            col.radius = 0.4f;

            var agent = g.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.speed = 1.8f;
            agent.angularSpeed = 540f;
            agent.acceleration = 24f;
            agent.stoppingDistance = 0.2f;
            agent.enabled = false; // enabled by GuardBrain after the runtime bake

            var health = g.AddComponent<Health>();
            health.maxHealth = 3;

            var body = Visual(PrimitiveType.Capsule, g.transform, "PH_Body", new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), guardMat);
            Visual(PrimitiveType.Cube, g.transform, "PH_Nose", new Vector3(0f, 1.45f, 0.4f), new Vector3(0.3f, 0.15f, 0.3f), noseMat);

            var head = new GameObject("Head").transform;
            head.SetParent(g.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);

            var flashGO = new GameObject("PH_Flashlight");
            flashGO.transform.SetParent(head, false);
            flashGO.transform.localPosition = new Vector3(0f, -0.15f, 0.6f); // outside the body and nose so they never shadow the beam
            flashGO.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var light = flashGO.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 14f;
            light.spotAngle = 70f;
            light.innerSpotAngle = 40f;
            light.intensity = 30f;
            light.color = new Color(1f, 0.93f, 0.78f);
            light.shadows = LightShadows.Soft;
            var stealthLight = flashGO.AddComponent<StealthLight>();
            stealthLight.weight = 1.5f;
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
            brain.bodyRenderer = body.GetComponent<Renderer>();

            var cone = new GameObject("PH_VisionCone");
            cone.transform.SetParent(g.transform, false);
            cone.AddComponent<MeshFilter>();
            cone.AddComponent<MeshRenderer>().sharedMaterial = coneMat;
            var coneMesh = cone.AddComponent<VisionConeMesh>();
            coneMesh.vision = vision;
            coneMesh.brain = brain;
        }

        // ---------------------------------------------------------------- utils

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
