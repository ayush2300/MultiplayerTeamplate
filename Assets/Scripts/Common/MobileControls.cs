using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

public class MobileControls : MonoBehaviour
{
    public static MobileControls Instance { get; private set; }

    [SerializeField] private FixedJoystick moveJoystick;
    [SerializeField] private TouchLookArea lookArea;
    [SerializeField] private Button jumpButton;
    [SerializeField] private Button pauseButton;

    private bool _jumpPressed;

    public Vector2 MoveInput => moveJoystick != null ? moveJoystick.Direction : Vector2.zero;

    private void Awake()
    {
        // Only the mobile build target gets on-screen controls; PC keeps WASD + mouse.
        if (!Application.isMobilePlatform)
        {
            gameObject.SetActive(false);
            return;
        }

        // Without this, the Input System's UI module can drop or coalesce touches when more
        // than one finger is down at once (e.g. left thumb on the move joystick while the right
        // thumb drags to look), which reads as "the joystick stopped responding". Guarded so a
        // redundant/failed Enable() call can't abort the rest of Awake() (button wiring below).
        if (!EnhancedTouchSupport.enabled)
        {
            EnhancedTouchSupport.Enable();
        }

        if (jumpButton != null)
        {
            jumpButton.onClick.AddListener(() => _jumpPressed = true);
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(() =>
            {
                if (PauseMenuUI.Instance != null)
                {
                    PauseMenuUI.Instance.TogglePause();
                }
            });
        }
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool ConsumeJumpPressed()
    {
        bool value = _jumpPressed;
        _jumpPressed = false;
        return value;
    }

    public Vector2 ConsumeLookDelta()
    {
        return lookArea != null ? lookArea.ConsumeDelta() : Vector2.zero;
    }
}
