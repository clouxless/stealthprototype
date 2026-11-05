using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace StealthProto
{
    /// <summary>
    /// Placeholder debug HUD drawn with IMGUI. Everything is labelled [PH] so it is never
    /// mistaken for final UI (see ASSETS.md).
    /// F1 vision cones, F2 noise rings, F3 help, R restart.
    /// </summary>
    public class DebugHUD : MonoBehaviour
    {
        public PlayerMotor player;

        LightExposure exposure;
        PlayerContextActions context;
        Health health;
        CameraRig rig;
        Camera cam;
        bool showHelp = true;
        GUIStyle label, marker, prompt;

        void Start()
        {
            if (player == null) player = FindAnyObjectByType<PlayerMotor>();
            if (player != null)
            {
                exposure = player.GetComponent<LightExposure>();
                health = player.GetComponent<Health>();
                context = player.GetComponent<PlayerContextActions>();
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

            GUILayout.BeginArea(new Rect(12, 12, 300, 400), GUI.skin.box);
            GUILayout.Label("[PH] DEBUG HUD", label);
            if (health != null) GUILayout.Label($"HP  {health.Current} / {health.maxHealth}", label);
            if (exposure != null) GUILayout.Label($"Light tier  {exposure.Tier.ToString().ToUpper()}  (shown on the character)", label);
            if (player != null)
            {
                string mode = rig != null && rig.IsFirstPerson ? "FIRST PERSON" : player.IsSneaking ? "SNEAKING" : "TOP-DOWN";
                GUILayout.Label($"Mode  {mode}", label);
            }
            GUILayout.Space(6);
            foreach (var g in GuardBrain.All)
            {
                GUILayout.Label($"{g.name}  {g.State}", label);
                Bar(g.Awareness, g.CurrentColor);
            }
            GUILayout.EndArea();

            DrawGuardMarkers();
            DrawContextPrompt();

            if (showHelp)
            {
                const string help =
                    "[PH] CONTROLS (F3 hide)\n" +
                    "Move WASD / left stick    Sneak (silent) Shift / LB\n" +
                    "Dash Space / A            Melee LMB / X\n" +
                    "Context E / Y: takedown (behind an unalerted guard), clamber, climb\n" +
                    "Aim RMB / LT, fire LMB / RT\n" +
                    "First-person look V / R3  (can't move, fire along view)\n" +
                    "Zoom mouse wheel / d-pad   F1 vision cones   F2 noise rings   R restart";
                GUI.Label(new Rect(12, Screen.height - 140, 640, 130), help, label);
            }

            if (health != null && health.IsDead)
            {
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60), "[PH] DOWN. Restarting...", marker);
            }
        }

        void DrawGuardMarkers()
        {
            if (cam == null || (rig != null && rig.IsFirstPerson)) return;
            foreach (var g in GuardBrain.All)
            {
                // Tells are big; the ongoing state is a small reminder.
                var tell = g.CurrentTell;
                string text;
                int size;
                if (tell == GuardBrain.Tell.Exclamation) { text = "!"; size = 44; }
                else if (tell == GuardBrain.Tell.Question) { text = "?"; size = 40; }
                else if (g.State == GuardState.Alert) { text = "!"; size = 22; }
                else if (g.State == GuardState.Suspicious || g.State == GuardState.Search) { text = "?"; size = 22; }
                else continue;

                Vector3 sp = cam.WorldToScreenPoint(g.transform.position + Vector3.up * 2.3f);
                if (sp.z <= 0f) continue;
                marker.fontSize = size;
                marker.normal.textColor = g.CurrentColor;
                GUI.Label(new Rect(sp.x - 30, Screen.height - sp.y - 30, 60, 60), text, marker);
            }
        }

        void DrawContextPrompt()
        {
            if (context == null || cam == null) return;
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
