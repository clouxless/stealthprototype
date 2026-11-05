using System.Collections;
using UnityEngine;

namespace StealthProto
{
    /// <summary>
    /// Floor grates, Arkham-style. Standing over a GrateRun, press the context button (E / Y) to
    /// pull the grate and drop in. Inside you crawl freely along the run with the stick, silent
    /// and hidden under the grate. Press the button again to climb out wherever there's room to
    /// stand. Takes priority over the other context actions while it's offered.
    /// </summary>
    [DefaultExecutionOrder(-50)] // decide before PlayerContextActions reads the button
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerInputReader), typeof(CharacterController))]
    public class PlayerGrateCrawl : MonoBehaviour
    {
        public float crawlSpeed = 2f;
        [Tooltip("How far from the grate's centre line you can stand and still open it.")]
        public float reach = 0.8f;
        public float enterTime = 0.25f;
        public float exitTime = 0.3f;
        [Tooltip("Pulling or replacing the grate makes a small noise.")]
        public float grateNoiseRadius = 1.5f;

        public bool InGrate { get; private set; }
        public bool Transitioning { get; private set; }
        /// <summary>Enter or exit is available this frame.</summary>
        public bool Offering { get; private set; }
        /// <summary>True whenever this component owns the context button.</summary>
        public bool BlocksContext => Offering || InGrate || Transitioning;

        PlayerMotor motor;
        PlayerInputReader input;
        CharacterController controller;
        PlayerCombat combat;
        CameraRig rig;
        GrateRun run;
        float along;
        Vector3 promptPosition;
        GUIStyle promptStyle;
        readonly Collider[] standCheck = new Collider[8];

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputReader>();
            controller = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
        }

        void Start() => rig = FindAnyObjectByType<CameraRig>();

        void OnDisable()
        {
            if (InGrate || Transitioning)
            {
                controller.enabled = true;
                motor.ExternalControl = false;
                if (combat != null) combat.enabled = true;
            }
            InGrate = Transitioning = Offering = false;
        }

        void Update()
        {
            Offering = false;
            if (Transitioning || Time.deltaTime <= 0f) return;

            if (!InGrate)
            {
                if (motor.ExternalControl || motor.IsDashing || motor.AttackLock || !controller.isGrounded) return;
                if (rig != null && rig.IsFirstPerson) return;
                if (!GrateRun.Find(transform.position, reach, out var r, out float a)) return;
                Offering = true;
                promptPosition = transform.position + Vector3.up * 2f;
                if (input.InteractPressed) StartCoroutine(Enter(r, a));
                return;
            }

            // Crawling: the stick moves you along the run.
            Vector3 wish = motor.CameraRelative(input.Move);
            Vector3 dir = run.Direction;
            float push = Vector3.Dot(wish, new Vector3(dir.x, 0f, dir.z).normalized);
            if (Mathf.Abs(push) > 0.15f)
            {
                along = Mathf.Clamp(along + push * crawlSpeed * Time.deltaTime, 0f, run.Length);
                transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z) * Mathf.Sign(push));
            }
            transform.position = run.PointAt(along);

            Vector3 exitPoint = run.SurfacePointAt(along);
            if (CanStandAt(exitPoint))
            {
                Offering = true;
                promptPosition = exitPoint + Vector3.up * 1.2f;
                if (input.InteractPressed) StartCoroutine(Exit(exitPoint));
            }
        }

        IEnumerator Enter(GrateRun r, float a)
        {
            Transitioning = true;
            run = r;
            along = a;
            motor.ExternalControl = true;
            controller.enabled = false;
            if (combat != null) combat.enabled = false;
            motor.SetProne(true);
            NoiseSystem.Emit(transform.position, grateNoiseRadius, 0.3f, null, SoundKind.Movement, gameObject);

            Vector3 from = transform.position, to = run.PointAt(along);
            for (float t = 0f; t < 1f; t += Time.deltaTime / enterTime)
            {
                transform.position = Vector3.Lerp(from, to, t);
                yield return null;
            }
            transform.position = to;
            InGrate = true;
            Transitioning = false;
        }

        IEnumerator Exit(Vector3 exitPoint)
        {
            Transitioning = true;
            InGrate = false;
            NoiseSystem.Emit(exitPoint, grateNoiseRadius, 0.3f, null, SoundKind.Movement, gameObject);

            Vector3 from = transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / exitTime)
            {
                transform.position = Vector3.Lerp(from, exitPoint, t);
                yield return null;
            }
            transform.position = exitPoint;
            controller.enabled = true;
            motor.SetProne(false);
            motor.ExternalControl = false;
            if (combat != null) combat.enabled = true;
            Transitioning = false;
        }

        bool CanStandAt(Vector3 feet)
        {
            float r = controller.radius;
            Vector3 p1 = feet + Vector3.up * (r + 0.1f);
            Vector3 p2 = feet + Vector3.up * (1.8f - r);
            int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r * 0.95f, standCheck, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!standCheck[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        void OnGUI()
        {
            if (!Offering || Camera.main == null) return;
            if (promptStyle == null)
            {
                promptStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                promptStyle.normal.textColor = Color.white;
            }
            Vector3 sp = Camera.main.WorldToScreenPoint(promptPosition);
            if (sp.z <= 0f) return;
            string key = input.UsingGamepad ? "Y" : "E";
            string label = InGrate ? "Climb out" : "Enter grate";
            GUI.Box(new Rect(sp.x - 100, Screen.height - sp.y - 16, 200, 32), $"[PH] {label} ({key})", promptStyle);
        }
    }
}
