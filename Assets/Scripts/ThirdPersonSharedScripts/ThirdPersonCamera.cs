using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour, ILocalPlayerInput
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, -4f);

    [Header("Look Sensitivity")]
    [Tooltip("Base mouse look speed on PC. Scaled at runtime by the pause menu's sensitivity slider.")]
    [SerializeField] private float mouseSensitivity = 1f;
    [Tooltip("Base touch-drag look speed on mobile. Scaled at runtime by the pause menu's sensitivity slider.")]
    [SerializeField] private float touchLookSensitivity = 0.05f;

    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 60f;

    private Transform _target;
    private float _yaw;
    private float _pitch = 12f;
    private bool _jumpQueued;

    // True on the mobile build target, where MobileControls has enabled its joystick canvas;
    // false on PC, where it self-disabled and this stays null. Kept as one property so
    // camera-look and movement branch on exactly the same condition.
    private static bool UseTouchControls => MobileControls.Instance != null;

    private void OnDisable()
    {
        // Guarded inside Unregister, so a remote avatar's camera being switched off can never
        // clear the local player's registration.
        LocalPlayerInput.Unregister(this);
    }

    // Registration is explicit, driven by the player script for the avatar this client owns,
    // rather than done in OnEnable. Every replicated avatar carries this same camera component,
    // so if merely being enabled claimed the local-input slot, a remote player spawning would
    // steal it from the local player - and then null it out again on its way to being disabled.
    public void ActivateAsLocalView(Transform target)
    {
        SetTarget(target);
        LocalPlayerInput.Register(this);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public float Yaw => _yaw;
    public float Pitch => _pitch;

    public void SetTarget(Transform target)
    {
        _target = target;
        _yaw = target.eulerAngles.y;
    }

    private void LateUpdate()
    {
        // Runs after Fusion's Render() interpolation has updated _target's transform for this
        // frame, so the camera never reads a stale position — reading it in Update() is what
        // caused the turning glitch (camera and target updating out of order).
        if (_target == null || PauseMenuUI.IsPaused) return;

        if (UseTouchControls)
        {
            Vector2 delta = MobileControls.Instance.ConsumeLookDelta();
            _yaw += delta.x * touchLookSensitivity * LookSensitivity.Multiplier;
            _pitch -= delta.y * touchLookSensitivity * LookSensitivity.Multiplier;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            if (MobileControls.Instance.ConsumeJumpPressed())
            {
                _jumpQueued = true;
            }
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * mouseSensitivity * LookSensitivity.Multiplier * 0.1f;
                _pitch -= delta.y * mouseSensitivity * LookSensitivity.Multiplier * 0.1f;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _jumpQueued = true;
            }
        }

        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.position = _target.position + rotation * offset;
        transform.LookAt(_target.position + Vector3.up * 1.2f);
    }

    public Vector3 ComputeMoveDirection()
    {
        if (PauseMenuUI.IsPaused) return Vector3.zero;

        Vector2 move;
        if (UseTouchControls)
        {
            move = MobileControls.Instance.MoveInput;
        }
        else
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return Vector3.zero;

            move = Vector2.zero;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
        }
        move = Vector2.ClampMagnitude(move, 1f);

        var yawRotation = Quaternion.Euler(0f, _yaw, 0f);
        return yawRotation * new Vector3(move.x, 0f, move.y);
    }

    public bool ConsumeJumpPressed()
    {
        bool value = _jumpQueued;
        _jumpQueued = false;
        return value;
    }
}
