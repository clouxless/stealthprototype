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
        public float distance = 20f;
        public float fieldOfView = 40f;
        public float followSharpness = 10f;
        public Vector3 targetOffset = new Vector3(0f, 1f, 0f);

        [Header("First person")]
        public float firstPersonFov = 70f;
        public float eyeHeight = 1.6f;
        public float mouseSensitivity = 0.1f;
        public float stickSpeed = 160f;
        public float pitchLimit = 70f;

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
            else TopDown(Time.deltaTime);

            trauma = Mathf.Max(0f, trauma - shakeDecay * Time.unscaledDeltaTime);
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
            followPosition = dt > 0f ? Vector3.Lerp(followPosition, desired, 1f - Mathf.Exp(-followSharpness * dt)) : desired;
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
            cam.transform.SetPositionAndRotation(target.position + Vector3.up * eyeHeight, Quaternion.Euler(fpPitch, fpYaw, 0f));
        }
    }
}
