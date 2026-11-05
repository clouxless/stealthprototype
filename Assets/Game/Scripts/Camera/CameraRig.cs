using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StealthProto
{
    /// <summary>
    /// Top-down follow camera with a toggleable first-person look mode (movement locked while
    /// looking, like the classic stealth games). The rig's yaw defines "up" for movement input.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public Camera cam;

        [Header("Top-down")]
        public float pitch = 58f;
        public float yaw = 0f;
        public float distance = 12f;
        [Tooltip("Mouse wheel / d-pad zoom range.")]
        public float minDistance = 7f;
        public float maxDistance = 22f;
        [Tooltip("Metres per mouse-wheel notch.")]
        public float wheelZoomStep = 1f;
        [Tooltip("Metres per second while holding the d-pad.")]
        public float padZoomSpeed = 8f;
        public float fieldOfView = 38f;
        public float followSharpness = 10f;
        public Vector3 targetOffset = new Vector3(0f, 1f, 0f);

        [Header("First person")]
        public float firstPersonFov = 70f;
        public float eyeHeight = 1.6f;
        public float mouseSensitivity = 0.1f;
        public float stickSpeed = 160f;
        public float pitchLimit = 70f;

        [Header("Cutaway")]
        [Tooltip("Hide geometry taller than the player's head that blocks the camera's view of them (catwalks, beams, walls). Hidden geometry still casts shadows and still blocks light and vision.")]
        public bool cutaway = true;
        public float cutawayRadius = 0.5f;

        [Header("Shake")]
        public float maxShake = 0.6f;
        public float shakeDecay = 2.5f;

        static CameraRig instance;
        PlayerInputReader input;
        PlayerMotor motor;
        Renderer[] targetRenderers;
        Vector3 followPosition;
        float fpYaw, fpPitch;
        float trauma;
        readonly HashSet<Renderer> cutHidden = new HashSet<Renderer>();
        readonly HashSet<Renderer> cutThisFrame = new HashSet<Renderer>();
        static readonly RaycastHit[] CutHits = new RaycastHit[16];

        public bool IsFirstPerson { get; private set; }

        public static void Shake(float amount)
        {
            if (instance != null) instance.trauma = Mathf.Min(1f, instance.trauma + amount);
        }

        void Awake()
        {
            instance = this;
            if (cam == null) cam = GetComponentInChildren<Camera>();
        }

        void Start()
        {
            if (target == null) return;
            input = target.GetComponent<PlayerInputReader>();
            motor = target.GetComponent<PlayerMotor>();
            targetRenderers = target.GetComponentsInChildren<Renderer>();
            if (motor != null) motor.cameraYawReference = transform;
            followPosition = target.position + targetOffset;
            cam.fieldOfView = fieldOfView;
            TopDown(0f);
        }

        void LateUpdate()
        {
            if (target == null || cam == null) return;
            if (input != null && input.FirstPersonToggled) SetFirstPerson(!IsFirstPerson);

            if (IsFirstPerson) FirstPerson(Time.unscaledDeltaTime);
            else
            {
                if (input != null)
                {
                    float zoomIn = input.ZoomWheel * wheelZoomStep + input.ZoomPad * padZoomSpeed * Time.unscaledDeltaTime;
                    distance = Mathf.Clamp(distance - zoomIn, minDistance, maxDistance);
                }
                TopDown(Time.deltaTime);
            }

            trauma = Mathf.Max(0f, trauma - shakeDecay * Time.unscaledDeltaTime);
            UpdateCutaway();
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            foreach (var r in cutHidden)
                if (r != null) r.shadowCastingMode = ShadowCastingMode.On;
            cutHidden.Clear();
        }

        void UpdateCutaway()
        {
            cutThisFrame.Clear();
            if (cutaway && !IsFirstPerson)
            {
                Vector3 from = cam.transform.position;
                Vector3 to = target.position + Vector3.up * 1.2f;
                Vector3 delta = to - from;
                float distance = delta.magnitude - 0.6f;
                if (distance > 0f)
                {
                    int count = Physics.SphereCastNonAlloc(from, cutawayRadius, delta.normalized, CutHits, distance, ~0, QueryTriggerInteraction.Ignore);
                    float headHeight = target.position.y + 2f;
                    for (int i = 0; i < count; i++)
                    {
                        Collider col = CutHits[i].collider;
                        if (col.transform.IsChildOf(target)) continue;
                        if (col.GetComponentInParent<GuardBrain>() != null) continue;
                        if (col.bounds.max.y <= headHeight) continue; // low cover and floors stay visible
                        var r = col.GetComponent<Renderer>();
                        if (r != null) cutThisFrame.Add(r);
                    }
                }
            }

            foreach (var r in cutHidden)
                if (r != null && !cutThisFrame.Contains(r)) r.shadowCastingMode = ShadowCastingMode.On;
            foreach (var r in cutThisFrame) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            cutHidden.Clear();
            cutHidden.UnionWith(cutThisFrame);
        }

        public void SetFirstPerson(bool on)
        {
            IsFirstPerson = on;
            if (motor != null) motor.MovementLocked = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
            cam.fieldOfView = on ? firstPersonFov : fieldOfView;
            if (on)
            {
                fpYaw = target.eulerAngles.y;
                fpPitch = 0f;
            }
            else
            {
                followPosition = target.position + targetOffset;
            }
            // Hide the body in first person but keep its shadow.
            if (targetRenderers != null)
                foreach (var r in targetRenderers)
                    if (r != null) r.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        void TopDown(float dt)
        {
            Vector3 desired = target.position + targetOffset;
            // dt is 0 during hit-stop: hold position rather than snapping.
            if (dt > 0f) followPosition = Vector3.Lerp(followPosition, desired, 1f - Mathf.Exp(-followSharpness * dt));
            transform.SetPositionAndRotation(followPosition, Quaternion.Euler(0f, yaw, 0f));

            Quaternion tilt = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 shake = Random.insideUnitSphere * (trauma * trauma * maxShake);
            cam.transform.localPosition = tilt * new Vector3(0f, 0f, -distance) + shake;
            cam.transform.localRotation = tilt;
        }

        void FirstPerson(float dt)
        {
            Vector2 mouse = input != null ? input.LookMouse * mouseSensitivity : Vector2.zero;
            Vector2 stick = input != null ? input.LookStick * (stickSpeed * dt) : Vector2.zero;
            fpYaw += mouse.x + stick.x;
            fpPitch = Mathf.Clamp(fpPitch - mouse.y - stick.y, -pitchLimit, pitchLimit);

            target.rotation = Quaternion.Euler(0f, fpYaw, 0f);
            float eye = motor != null && motor.IsProne ? 0.35f : eyeHeight;
            cam.transform.SetPositionAndRotation(target.position + Vector3.up * eye, Quaternion.Euler(fpPitch, fpYaw, 0f));
        }
    }
}
