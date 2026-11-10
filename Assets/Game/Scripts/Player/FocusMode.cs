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
    /// Focus only shows sound: still guards, lights and sightlines stay hidden. Sounds off screen
    /// get no marker, but their ripples still sweep in across the screen edge.
    /// Moving, attacking, aiming, dashing or interacting breaks it. Cues linger, then fade.
    /// Like guards, you only pick up sounds from the space you're in (SoundGrid).
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
        public float detailTime = 0.75f;
        public float identifyTime = 3f;
        public float lingerTime = 1.2f;
        [Tooltip("How long one ripple lasts on screen. Long and slow reads as calm water, short as sonar.")]
        public float rippleLife = 2.6f;
        [Tooltip("How far a ripple spreads, as a multiple of the sound's actual radius.")]
        public float rippleScale = 1f;
        [Tooltip("Ripple size at full focus, as a multiple of the sound's radius. Ripples shrink toward this as focus deepens (while the camera pulls out), so sources are easier to pinpoint.")]
        public float rippleScaleFocused = 0.2f;
        [Tooltip("At most one new ripple per source this often (footsteps otherwise ping constantly). Critical sounds always show.")]
        public float rippleMinInterval = 1.2f;
        [Tooltip("Peak opacity of a ripple.")]
        [Range(0f, 1f)] public float rippleOpacity = 0.45f;
        [Tooltip("Trailing rings behind each ripple, like water.")]
        [Range(0, 3)] public int rippleTrails = 2;

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
        PlayerCover cover;
        Camera cam;
        CameraRig rig;
        readonly List<SoundEvent> sounds = new List<SoundEvent>();
        readonly Dictionary<GameObject, float> lastRipple = new Dictionary<GameObject, float>();
        bool latchedOff; // broken focus needs a fresh press
        float releasedAt = -999f;
        float holdAtRelease;
        Texture2D ringTex, holeTex;
        GUIStyle labelStyle;

        void Awake()
        {
            input = GetComponent<PlayerInputReader>();
            motor = GetComponent<PlayerMotor>();
            cover = GetComponent<PlayerCover>();
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
            // Same rule as guards: you only pick up sounds from the space you're in. A sealed room
            // next door is silent, and so is everything outside while you're in a vent or grate.
            if (e.source != gameObject && !SoundGrid.Connected(e.space, SoundGrid.SpaceAt(transform.position))) return;
            // Calm, not sonar: one ripple per source at a time, except critical sounds.
            if (e.source != null && e.loudness < NoiseSystem.CriticalLoudness)
            {
                if (lastRipple.TryGetValue(e.source, out float last) && e.time - last < rippleMinInterval) return;
                lastRipple[e.source] = e.time;
            }
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
            // Cover takes over movement (ExternalControl) but you can still focus from it.
            bool inCover = cover != null && cover.InCover;
            bool blocked = (motor.ExternalControl && !inCover) || motor.IsDashing || (rig != null && rig.IsFirstPerson);

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
            Vector3 sp = cam.WorldToScreenPoint(s.position);
            if (sp.z <= 0f) return; // behind the camera (never happens top-down)

            // Pixel size of the sound's radius at its position, so a ripple matches its reach.
            // Drawn even when the source is off screen, so you see its ripple drift in from the edge.
            // Deeper focus, tighter ripples: they shrink over the whole blackout and pull-out.
            float scale = Mathf.Lerp(rippleScale, rippleScaleFocused, Phase(0f, activateTime + zoomTime));
            Vector3 edge = cam.WorldToScreenPoint(s.position + cam.transform.right * (s.radius * scale));
            float maxPx = Mathf.Abs(edge.x - sp.x);
            Color baseColor = Color.Lerp(KindColor(s.kind), Color.white, 0.25f);

            // A main ring and fainter trailing rings, each easing out (fast at first, settling
            // slowly) and fading in and out softly, like a ripple on still water.
            for (int ring = 0; ring <= rippleTrails; ring++)
            {
                float rk = k - ring * 0.18f;
                if (rk <= 0f || rk >= 1f) continue;
                float spread = 1f - Mathf.Pow(1f - rk, 3f);
                float px = Mathf.Max(6f, Mathf.Lerp(maxPx * 0.05f, maxPx, spread));
                var rect = new Rect(sp.x - px, Screen.height - sp.y - px, px * 2f, px * 2f);
                if (!rect.Overlaps(new Rect(0, 0, Screen.width, Screen.height))) continue;
                float envelope = Mathf.SmoothStep(0f, 1f, rk / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, rk)));
                Color c = baseColor;
                c.a = rippleOpacity * envelope * linger * (ring == 0 ? 1f : 0.45f / ring);
                GUI.color = c;
                GUI.DrawTexture(rect, ringTex);
            }

            bool onScreen = sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
            if (onScreen && tier >= 3 && s.source != null)
            {
                var guard = s.source.GetComponent<GuardBrain>();
                string who = guard != null ? $"{s.source.name}  {guard.State}" : s.source.name;
                Color lc = baseColor;
                lc.a = linger * (1f - Mathf.SmoothStep(0f, 1f, k));
                labelStyle.normal.textColor = lc;
                GUI.Label(new Rect(sp.x - 120, Screen.height - sp.y + 6, 240, 20), who, labelStyle);
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
                    float d = (r - 0.92f) / 0.035f;
                    float a = Mathf.Exp(-d * d); // thin, soft-edged line
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
