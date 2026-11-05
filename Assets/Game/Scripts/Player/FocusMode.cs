using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Focus, first pass. Hold Q (gamepad L3) to listen: you can't move, the screen dims, and
    /// sounds from NoiseSystem appear as ripples. The longer you hold, the more you get:
    ///   tier 1 (from the start): loud, nearby sounds
    ///   tier 2 (after detailTime): every sound in range, so footstep rhythm shows, plus arrows
    ///          at the screen edge pointing to sounds off screen
    ///   tier 3 (after identifyTime): which guard made it and their state
    /// Focus only shows sound: still guards, lights and sightlines stay hidden.
    /// Moving, attacking, aiming, dashing or interacting breaks it. Cues linger, then fade.
    /// Drawn with placeholder IMGUI ([PH]).
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    public class FocusMode : MonoBehaviour
    {
        [Header("Timing")]
        public float detailTime = 1f;
        public float identifyTime = 2.5f;
        public float lingerTime = 1.2f;
        [Tooltip("How long one ripple lasts on screen.")]
        public float rippleLife = 1.2f;

        [Header("Range")]
        public float hearingRange = 30f;
        public float loudRange = 20f;
        [Range(0f, 1f)] public float loudThreshold = 0.6f;

        [Header("Look")]
        [Range(0f, 1f)] public float dimAlpha = 0.55f;

        public bool IsFocusing { get; private set; }
        public float HoldTime { get; private set; }

        PlayerInputReader input;
        PlayerMotor motor;
        Camera cam;
        CameraRig rig;
        readonly List<SoundEvent> sounds = new List<SoundEvent>();
        bool latchedOff; // broken focus needs a fresh press
        float releasedAt = -999f;
        float holdAtRelease;
        Texture2D ringTex;
        GUIStyle labelStyle;

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
        }

        void Start()
        {
            cam = Camera.main;
            rig = FindAnyObjectByType<CameraRig>();
        }

        void OnEnable() => NoiseSystem.SoundMade += OnSound;

        void OnDisable()
        {
            NoiseSystem.SoundMade -= OnSound;
            if (IsFocusing) StopFocus();
        }

        void OnSound(SoundEvent e)
        {
            if (e.source == gameObject && e.kind == SoundKind.Footstep) return; // your own steps aren't news
            sounds.Add(e);
        }

        void Update()
        {
            sounds.RemoveAll(s => Time.time - s.time > rippleLife + lingerTime + 0.5f);

            bool held = input.FocusHeld;
            if (!held) latchedOff = false;

            bool breaking = input.Move.sqrMagnitude > 0.12f || input.DashPressed || input.MeleePressed
                            || input.FirePressed || input.AimHeld || input.InteractPressed;
            bool blocked = motor.ExternalControl || motor.IsDashing || (rig != null && rig.IsFirstPerson);

            if (IsFocusing)
            {
                if (!held || breaking || blocked)
                {
                    if (held) latchedOff = true;
                    StopFocus();
                }
                else HoldTime += Time.deltaTime;
            }
            else if (held && !latchedOff && !breaking && !blocked)
            {
                IsFocusing = true;
                HoldTime = 0f;
                motor.FocusLock = true;
            }
        }

        void StopFocus()
        {
            IsFocusing = false;
            motor.FocusLock = false;
            releasedAt = Time.time;
            holdAtRelease = HoldTime;
        }

        // ---------------------------------------------------------------- drawing

        void OnGUI()
        {
            float linger = IsFocusing ? 1f : 1f - (Time.time - releasedAt) / lingerTime;
            if (linger <= 0f || cam == null) return;
            float hold = IsFocusing ? HoldTime : holdAtRelease;

            if (ringTex == null) ringTex = MakeRing();
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
            }

            GUI.depth = 1; // under the debug HUD
            GUI.color = new Color(0f, 0f, 0f, dimAlpha * linger);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

            int tier = hold >= identifyTime ? 3 : hold >= detailTime ? 2 : 1;
            foreach (var s in sounds)
            {
                float age = Time.time - s.time;
                if (age > rippleLife) continue;
                float distance = Vector3.Distance(s.position, transform.position);
                if (distance > hearingRange) continue;
                bool loud = s.loudness >= loudThreshold && distance <= loudRange;
                if (tier == 1 && !loud) continue;
                DrawRipple(s, age / rippleLife, linger, tier);
            }
            GUI.color = Color.white;

            if (IsFocusing)
            {
                string label = tier == 3 ? "[PH] FOCUS: sources" : tier == 2 ? "[PH] FOCUS: all sounds" : "[PH] FOCUS: loud sounds";
                labelStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);
                GUI.Label(new Rect(0, 60, Screen.width, 24), label, labelStyle);
            }
        }

        void DrawRipple(SoundEvent s, float k, float linger, int tier)
        {
            Color c = KindColor(s.kind);
            c.a = (1f - k) * linger;
            Vector3 sp = cam.WorldToScreenPoint(s.position);
            bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;

            if (onScreen)
            {
                // Pixel size of the sound's radius at its position, so a ripple matches its reach.
                Vector3 edge = cam.WorldToScreenPoint(s.position + cam.transform.right * s.radius);
                float maxPx = Mathf.Abs(edge.x - sp.x);
                float px = Mathf.Lerp(maxPx * 0.15f, maxPx, Mathf.Sqrt(k));
                GUI.color = c;
                GUI.DrawTexture(new Rect(sp.x - px, Screen.height - sp.y - px, px * 2f, px * 2f), ringTex);

                if (tier >= 3 && s.source != null)
                {
                    var guard = s.source.GetComponent<GuardBrain>();
                    string who = guard != null ? $"{s.source.name}  {guard.State}" : s.source.name;
                    labelStyle.normal.textColor = c;
                    GUI.Label(new Rect(sp.x - 120, Screen.height - sp.y + 6, 240, 20), who, labelStyle);
                }
            }
            else if (tier >= 2)
            {
                // Off screen: an arrow on the screen edge pointing at it.
                Vector3 me = cam.WorldToScreenPoint(transform.position);
                Vector2 dir = new Vector2(sp.x - me.x, sp.y - me.y);
                if (sp.z < 0f) dir = -dir;
                if (dir.sqrMagnitude < 1f) return;
                dir.Normalize();
                Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                float t = Mathf.Min(Mathf.Abs((centre.x - 24f) / Mathf.Max(0.001f, Mathf.Abs(dir.x))),
                                    Mathf.Abs((centre.y - 24f) / Mathf.Max(0.001f, Mathf.Abs(dir.y))));
                Vector2 p = centre + dir * t;
                GUI.color = c;
                GUI.DrawTexture(new Rect(p.x - 10f, Screen.height - p.y - 10f, 20f, 20f), ringTex);
            }
        }

        static Color KindColor(SoundKind kind) => kind switch
        {
            SoundKind.Gunshot => new Color(1f, 0.25f, 0.2f),
            SoundKind.Shout => new Color(1f, 0.3f, 0.25f),
            SoundKind.Knock => new Color(1f, 0.85f, 0.3f),
            SoundKind.Impact => new Color(1f, 0.6f, 0.3f),
            SoundKind.Struggle => new Color(1f, 0.6f, 0.3f),
            SoundKind.Footstep => new Color(0.85f, 0.95f, 1f),
            _ => new Color(0.6f, 0.85f, 1f)
        };

        static Texture2D MakeRing()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PH_FocusRing", wrapMode = TextureWrapMode.Clamp };
            float half = (size - 1) * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.9f) / 0.08f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
