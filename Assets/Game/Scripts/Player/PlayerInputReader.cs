using UnityEngine;
using UnityEngine.InputSystem;

namespace StealthProto
{
    /// <summary>
    /// Input actions defined in code so there is no extra asset to manage yet.
    /// Keyboard + mouse and gamepad are both bound.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction move, lookMouse, lookStick, aimStick, pointer, sneak, dash, melee, fire, aim, firstPerson;

        public Vector2 Move => move.ReadValue<Vector2>();
        public Vector2 LookMouse => lookMouse.ReadValue<Vector2>();
        public Vector2 LookStick => lookStick.ReadValue<Vector2>();
        public Vector2 AimStick => aimStick.ReadValue<Vector2>();
        public Vector2 PointerPosition => pointer.ReadValue<Vector2>();
        public bool SneakHeld => sneak.IsPressed();
        public bool AimHeld => aim.IsPressed();
        public bool DashPressed => dash.WasPressedThisFrame();
        public bool MeleePressed => melee.WasPressedThisFrame();
        public bool FirePressed => fire.WasPressedThisFrame();
        public bool FirstPersonToggled => firstPerson.WasPressedThisFrame();
        /// <summary>True when the gamepad was the last device used for aiming.</summary>
        public bool UsingGamepad { get; private set; }

        void Awake()
        {
            move = new InputAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");

            lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");
            aimStick = new InputAction("AimStick", InputActionType.Value, "<Gamepad>/rightStick");
            pointer = new InputAction("Pointer", InputActionType.Value, "<Pointer>/position");

            sneak = new InputAction("Sneak", InputActionType.Button, "<Keyboard>/leftShift");
            sneak.AddBinding("<Gamepad>/leftShoulder");
            dash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dash.AddBinding("<Gamepad>/buttonSouth");
            melee = new InputAction("Melee", InputActionType.Button, "<Mouse>/leftButton");
            melee.AddBinding("<Gamepad>/buttonWest");
            aim = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            aim.AddBinding("<Gamepad>/leftTrigger");
            fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            fire.AddBinding("<Gamepad>/rightTrigger");
            firstPerson = new InputAction("FirstPerson", InputActionType.Button, "<Keyboard>/v");
            firstPerson.AddBinding("<Gamepad>/rightStickPress");
        }

        InputAction[] AllActions => new[] { move, lookMouse, lookStick, aimStick, pointer, sneak, dash, melee, fire, aim, firstPerson };

        void OnEnable()
        {
            foreach (var a in AllActions) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in AllActions) a.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in AllActions) a.Dispose();
        }

        void Update()
        {
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if (pad != null && (pad.rightStick.ReadValue().sqrMagnitude > 0.04f || pad.leftStick.ReadValue().sqrMagnitude > 0.04f))
                UsingGamepad = true;
            else if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 1f || mouse.leftButton.wasPressedThisFrame))
                UsingGamepad = false;
        }
    }
}
