using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StealthProto
{
    /// <summary>
    /// Placeholder debug HUD drawn with IMGUI. Everything is labelled [PH] so it is never
    /// mistaken for final UI (see ASSETS.md).
    /// F1 vision cones, F2 noise rings, F3 debug panel and help, F4 x-ray (guards through walls), R restart.
    /// </summary>
    public class DebugHUD : MonoBehaviour
    {
        public PlayerMotor player;

        LightExposure exposure;
        PlayerContextActions context;
        PlayerCover cover;
        Health health;
        CameraRig rig;
        Camera cam;
        bool showHelp = true;
        /// <summary>Testing aid: draw guards through walls. Off by default.</summary>
        public static bool XRay;
        static readonly int XRayId = Shader.PropertyToID("_PH_XRay");

        // Each press of Play starts with x-ray off, even with domain reload disabled (statics and
        // shader globals would otherwise carry over from the last session). R restart keeps it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            XRay = false;
            Shader.SetGlobalFloat(XRayId, 0f);
        }
        GUIStyle label, marker, prompt;

        void Start()
        {
            Shader.SetGlobalFloat(XRayId, XRay ? 1f : 0f);
            if (player == null) player = FindAnyObjectByType<PlayerMotor>();
            if (player != null)
            {
                exposure = player.GetComponent<LightExposure>();
                health = player.GetComponent<Health>();
                context = player.GetComponent<PlayerContextActions>();
                cover = player.GetComponent<PlayerCover>();
            }
            rig = FindAnyObjectByType<CameraRig>();
            cam = Camera.main;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) VisionConeMesh.Visible = !VisionConeMesh.Visible;
            if (kb.f2Key.wasPressedThisFrame) NoiseSystem.ShowNoise = !NoiseSystem.ShowNoise;
            if (kb.f3Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.f4Key.wasPressedThisFrame)
            {
                XRay = !XRay;
                Shader.SetGlobalFloat(XRayId, XRay ? 1f : 0f);
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }

        void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                label.normal.textColor = Color.white;
                marker = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                prompt = new GUIStyle(GUI.skin.box) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                prompt.normal.textColor = Color.white;
            }

            if (showHelp) DrawStatusPanel(); // F3 hides the debug panel along with the controls

            DrawGuardMarkers();
            DrawContextPrompt();

            if (showHelp)
            {
                const string help =
                    "[PH] CONTROLS (F3 hides these and the debug panel)\n" +
                    "Move WASD / left stick (silent)    Crawl C / B\n" +
                    "Dash Space / A (into a gap: jumps it if a ring shows a landing)   Melee LMB / X\n" +
                    "Context E / Y: takedown (behind an unalerted guard), clamber, climb, hang off edges\n" +
                    "Cover: hold Shift / LB by a wall, slide along it; near the end the camera looks round the corner\n" +
                    "Knock (in cover, or in a vent / grate): hold LMB / X to see its reach, release to knock\n" +
                    "Edges: walk into one to hang from it. Hanging: stick shimmies, E climbs up, Space / C lets go\n" +
                    "  (not over water or a pit: there a spent grip pulls you back up)\n" +
                    "Focus (hold, can't move) Q / L3: see sounds\n" +
                    "Aim RMB / LT, fire LMB / RT\n" +
                    "First-person look V / R3  (can't move, fire along view)\n" +
                    "Zoom mouse wheel / d-pad   F1 vision cones   F2 all noise rings   F4 x-ray   R restart";
                GUI.Label(new Rect(324, Screen.height - 196, 760, 186), help, label); // beside the 300-wide status panel
            }

            if (health != null && health.IsDead)
            {
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), "[PH] DOWN. Restarting...", marker);
            }
        }

        void DrawStatusPanel()
        {
            GUILayout.BeginArea(new Rect(12, 12, 300, 400), GUI.skin.box);
            GUILayout.Label("[PH] DEBUG HUD", label);
            if (health != null) GUILayout.Label($"HP  {health.Current} / {health.maxHealth}", label);
            if (exposure != null)
            {
                string seen = exposure.SeenTier.ToString().ToUpper();
                GUILayout.Label(exposure.SeenTier != exposure.Tier
                    ? $"Light tier  {seen}  (floor {exposure.Tier.ToString().ToUpper()}, crawling: one darker)"
                    : $"Light tier  {seen}  (shown on the character)", label);
            }
            if (player != null)
            {
                string mode = rig != null && rig.IsFirstPerson ? "FIRST PERSON"
                    : context != null && context.Jumping ? "JUMPING"
                    : context != null && context.Hanging ? (context.HangingOverDeadlyDrop ? "HANGING (deadly drop: can't let go)" : "HANGING")
                    : cover != null && cover.InCover ? "COVER"
                    : player.IsProne ? "CRAWLING" : "TOP-DOWN";
                GUILayout.Label($"Mode  {mode}", label);
                if (XRay) GUILayout.Label("X-RAY ON  (F4): guards drawn through walls", label);
            }
            GUILayout.Space(6);
            foreach (var g in GuardBrain.All)
            {
                string seenBy = g.vision != null && g.vision.Rate != DetectRate.None ? $"  zone {g.vision.Zone}: {g.vision.Rate}" : "";
                GUILayout.Label($"{g.name}  {g.State}{seenBy}", label);
                Bar(g.Awareness, g.CurrentColor);
            }
            GUILayout.EndArea();
        }

        [Header("Awareness meter")]
        [Tooltip("Width and height in pixels of the meter over each guard's head.")]
        public Vector2 meterSize = new Vector2(64f, 7f);
        [Tooltip("Hide the meter while a patrolling guard's awareness is below this.")]
        public float meterShowAbove = 0.02f;

        readonly System.Collections.Generic.Dictionary<GuardBrain, float> meterShown = new System.Collections.Generic.Dictionary<GuardBrain, float>();

        void DrawGuardMarkers()
        {
            if (cam == null || (rig != null && rig.IsFirstPerson)) return;
            foreach (var g in GuardBrain.All)
            {
                if (g.State == GuardState.Down) continue;
                Vector3 sp = cam.WorldToScreenPoint(g.transform.position + Vector3.up * 2.25f);
                if (sp.z <= 0f) continue;
                float x = sp.x, y = Screen.height - sp.y;

                bool meterOn = DrawAwarenessMeter(g, x, y);

                // Tells are big; the ongoing state is a small reminder. Sits above the meter.
                var tell = g.CurrentTell;
                string text;
                int size;
                if (tell == GuardBrain.Tell.Exclamation) { text = "!"; size = 44; }
                else if (tell == GuardBrain.Tell.Question) { text = "?"; size = 40; }
                else if (g.State == GuardState.Alert) { text = "!"; size = 22; }
                else if (g.State == GuardState.Suspicious || g.State == GuardState.Search) { text = "?"; size = 22; }
                else continue;

                float lift = meterOn ? 22f : 0f;
                marker.fontSize = size;
                marker.normal.textColor = g.CurrentColor;
                GUI.Label(new Rect(x - 30, y - 30 - lift, 60, 60), text, marker);
            }
        }

        /// <summary>
        /// Dishonored-style meter over a guard's head that fills as they notice you (GuardBrain.Awareness).
        /// White while it builds, with a notch at the suspicious threshold; amber once suspicious or
        /// searching; solid red at full alert. Returns whether it was drawn.
        /// </summary>
        bool DrawAwarenessMeter(GuardBrain g, float x, float y)
        {
            float target = g.State == GuardState.Alert ? 1f : g.Awareness;
            meterShown.TryGetValue(g, out float shown);
            // Ease the bar so it reads as filling, but never lag far behind a fast spot.
            shown = Mathf.MoveTowards(shown, target, Mathf.Max(1.5f, Mathf.Abs(target - shown) * 8f) * Time.unscaledDeltaTime);
            meterShown[g] = shown;
            if (g.State == GuardState.Patrol && shown < meterShowAbove) return false;

            float threshold = g.suspiciousThreshold;
            Color fill = g.State == GuardState.Alert ? g.alertColor
                       : g.State == GuardState.Patrol ? new Color(1f, 1f, 1f, 0.9f)
                       : g.suspiciousColor;
            // Fade in as it starts to build, so a glance isn't loud.
            float alpha = g.State == GuardState.Patrol ? Mathf.Clamp01(shown / threshold * 1.5f + 0.25f) : 1f;

            var r = new Rect(x - meterSize.x * 0.5f, y - meterSize.y * 0.5f, meterSize.x, meterSize.y);
            GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
            fill.a *= alpha;
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(shown), r.height), Texture2D.whiteTexture);
            // Notch at the suspicious threshold: crossing it is the "?".
            GUI.color = new Color(1f, 0.6f, 0.1f, alpha);
            GUI.DrawTexture(new Rect(r.x + r.width * threshold - 1f, r.y - 3f, 2f, r.height + 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            return true;
        }

        void DrawContextPrompt()
        {
            if (cam == null) return;
            if (cover != null && cover.Prompt != null && (cover.InCover || cover.PreviewingKnock || context == null || context.PromptLabel == null))
            {
                Vector3 cp = cam.WorldToScreenPoint(player.transform.position + Vector3.up * 2.3f);
                if (cp.z > 0f) GUI.Box(new Rect(cp.x - 170, Screen.height - cp.y - 16, 340, 32), $"[PH] {cover.Prompt}", prompt);
                return;
            }
            if (context == null) return;
            if (context.Hanging)
            {
                Vector3 hp = cam.WorldToScreenPoint(context.PromptPosition);
                if (hp.z <= 0f) return;
                var r = new Rect(hp.x - 130, Screen.height - hp.y - 16, 260, 32);
                GUI.Box(r, "[PH] E climb up  |  Space drop", prompt);
                GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.2f, 0.8f), new Color(1f, 1f, 1f, 0.5f), context.GripFraction);
                GUI.DrawTexture(new Rect(r.x, r.yMax, r.width * context.GripFraction, 6), Texture2D.whiteTexture);
                GUI.color = Color.white;
                return;
            }
            bool choking = context.TakedownProgress >= 0f;
            if (!choking && context.PromptLabel == null) return;
            Vector3 sp = cam.WorldToScreenPoint(context.PromptPosition);
            if (sp.z <= 0f) return;
            var rect = new Rect(sp.x - 100, Screen.height - sp.y - 16, 200, 32);
            if (!choking)
            {
                GUI.Box(rect, $"[PH] E  {context.PromptLabel}", prompt);
                return;
            }
            GUI.Box(rect, "[PH] HOLD E", prompt);
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax, rect.width * context.TakedownProgress, 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void Bar(float value, Color color)
        {
            Rect r = GUILayoutUtility.GetRect(260, 8);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
