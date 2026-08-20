// Host-mode copy of the first-person view. Camera and input handling are pure local view code with
// no networking in them, so this is identical to the shared-mode version by design: each variant
// is self-contained, so you can retune FirstPersonHostCamera here without touching the shared variant.
using UnityEngine;
using UnityEngine.InputSystem;

// Local first-person view. It sits on a camera child of the player prefab and, unlike the
// third-person rig, drives the body's facing: the yaw it accumulates is sent through
// NetworkInputData and applied to the character by FirstPersonPlayer, while pitch stays a
// camera-only rotation (also networked, so remote players can see head aim).
//
// The camera writes its own world rotation every frame rather than inheriting the parent's.
// Rotating the body only happens on a network tick, so inheriting it would make looking around
// feel stepped at low tick rates; this way look is always frame-rate smooth.
public class FirstPersonHostCamera : MonoBehaviour, ILocalPlayerInput
{
    [Header("Look Sensitivity")]
    [Tooltip("Base mouse look speed on PC. Scaled at runtime by the pause menu's sensitivity slider.")]
    [SerializeField] private float mouseSensitivity = 1f;
    [Tooltip("Base touch-drag look speed on mobile. Scaled at runtime by the pause menu's sensitivity slider.")]
    [SerializeField] private float touchLookSensitivity = 0.05f;

    // Wider than the third-person range: in first person you expect to look nearly straight
    // up and down, stopping just short of the neck-breaking angles that flip the view.
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private Transform _anchor;
    private float _yaw;
    private float _pitch;
    private bool _jumpQueued;

    // True on the mobile build target, where MobileControls has enabled its joystick canvas;
    // false on PC, where it self-disabled and this stays null. Kept as one property so
    // camera-look and movement branch on exactly the same condition.
    private static bool UseTouchControls => MobileControls.Instance != null;

    public float Yaw => _yaw;
    public float Pitch => _pitch;

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
    public void ActivateAsLocalView(Transform anchor)
    {
        SetAnchor(anchor);
        LocalPlayerInput.Register(this);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Head position this camera snaps to each frame, set by FirstPersonPlayer on spawn.</summary>
    public void SetAnchor(Transform anchor)
    {
        _anchor = anchor;
        _yaw = anchor.eulerAngles.y;
        _pitch = 0f;
    }

    private void LateUpdate()
    {
        // Runs after Fusion's Render() interpolation has moved the anchor for this frame, so the
        // camera never reads a stale head position.
        if (_anchor == null) return;

        // Look input stops while paused, but the camera keeps tracking the anchor below -
        // returning early here would leave it stranded if the body is still being pushed around.
        if (!PauseMenuUI.IsPaused)
        {
            ReadLookInput();
        }

        transform.SetPositionAndRotation(_anchor.position, Quaternion.Euler(_pitch, _yaw, 0f));
    }

    private void ReadLookInput()
    {
        if (UseTouchControls)
        {
            Vector2 delta = MobileControls.Instance.ConsumeLookDelta();
            _yaw += delta.x * touchLookSensitivity * LookSensitivity.Multiplier;
            _pitch -= delta.y * touchLookSensitivity * LookSensitivity.Multiplier;

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
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _jumpQueued = true;
            }
        }

        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
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

        // Yaw only: pitching the camera down must not drive the character into the floor.
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
