using System.Collections.Generic;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Focus. Hold Q (gamepad L3) to listen: you can't move, the world fades to pitch black over
    /// activateTime, leaving only you and the sounds from NoiseSystem, drawn as ripples. Once it is
    /// fully black the camera pulls out over zoomTime. The longer you hold, the more you get:
    ///   tier 1 (from the start): loud, nearby sounds
    ///   tier 2 (after detailTime): every sound in range, so footstep rhythm shows
    ///   tier 3 (after identifyTime): which guard made it and their state
    /// Focus only shows sound: still guards, lights and sightlines stay hidden, and sounds off
    /// screen get no marker (the zoom-out is what widens what you can hear).
    /// Moving, attacking, aiming, dashing or interacting breaks it. Cues linger, then fade.
    /// Drawn with placeholder IMGUI ([PH]).
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerMotor))]
    public class FocusMode : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds of holding until the screen is fully black and the camera fully pulled out.")]
        public float activateTime = 3f;
        [Tooltip("Seconds the camera takes to pull out, starting once the screen is fully black.")]
        public float zoomTime = 2f;
        public float detailTime = 1.5f;
        public float identifyTime = 3f;
        public float lingerTime = 1.2f;
        [Tooltip("How long one ripple lasts on screen.")]
        public float rippleLife = 1.2f;
        [Tooltip("How far a ripple spreads, as a multiple of the sound's actual radius.")]
        public float rippleScale = 2f;

        [Header("Range")]
        public float hearingRange = 30f;
        public float loudRange = 20f;
        [Range(0f, 1f)] public float loudThreshold = 0.6f;

        [Header("Look")]
        [Tooltip("Metres the camera pulls back at full focus.")]
        public float zoomOut = 10f;
        [Tooltip("Radius of the clear spot around the player, in metres.")]
        public float selfRadius = 0.9f;

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
        Texture2D ringTex, holeTex;
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
            if (rig != null) rig.ExtraDistance = 0f;
        }

        void OnSound(SoundEvent e)
        {
            if (e.source == gameObject && e.kind == SoundKind.Footstep) return; // your own steps aren't news
            sounds.Add(e);
        }

        void Update()
        {
            if (rig != null) rig.ExtraDistance = zoomOut * Phase(activateTime, zoomTime);
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

        /// <summary>0..1: how far the blackout and zoom have gone, fading back out after release.</summary>
        float Activation() => Phase(0f, activateTime);

        /// <summary>0..1 progress of a stage that starts after `start` seconds of holding and lasts `length`.</summary>
        float Phase(float start, float length)
        {
            float hold = IsFocusing ? HoldTime : holdAtRelease;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hold - start) / Mathf.Max(0.01f, length)));
            if (!IsFocusing) a *= Mathf.Clamp01(1f - (Time.time - releasedAt) / lingerTime);
            return a;
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
            if (holeTex == null) holeTex = MakeHole();
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
            }

            GUI.depth = 1; // under the debug HUD
            DrawBlackout(Activation());

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
                Vector3 edge = cam.WorldToScreenPoint(s.position + cam.transform.right * (s.radius * rippleScale));
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
        }

        /// <summary>Black over everything except a soft spot on the player, at this opacity.</summary>
        void DrawBlackout(float alpha)
        {
            if (alpha <= 0.001f) return;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            Vector3 centre = transform.position + Vector3.up * 0.9f;
            Vector3 sp = cam.WorldToScreenPoint(centre);
            if (sp.z <= 0f)
            {
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                return;
            }
            Vector3 edge = cam.WorldToScreenPoint(centre + cam.transform.right * selfRadius);
            float r = Mathf.Max(8f, Mathf.Abs(edge.x - sp.x));
            float x0 = sp.x - r, x1 = sp.x + r;
            float y0 = Screen.height - sp.y - r, y1 = Screen.height - sp.y + r;
            var white = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Mathf.Max(0f, y0)), white);                       // above
            GUI.DrawTexture(new Rect(0, y1, Screen.width, Mathf.Max(0f, Screen.height - y1)), white);     // below
            GUI.DrawTexture(new Rect(0, y0, Mathf.Max(0f, x0), r * 2f), white);                            // left
            GUI.DrawTexture(new Rect(x1, y0, Mathf.Max(0f, Screen.width - x1), r * 2f), white);           // right
            GUI.DrawTexture(new Rect(x0, y0, r * 2f, r * 2f), holeTex);                                    // the spot
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

        /// <summary>Opaque square with a clear centre that fades out to the edge of the circle.</summary>
        static Texture2D MakeHole()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PH_FocusHole", wrapMode = TextureWrapMode.Clamp };
            float half = (size - 1) * 0.5f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, r));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

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
