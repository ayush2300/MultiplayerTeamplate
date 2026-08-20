using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour
{
    public static bool IsPaused { get; private set; }
    public static PauseMenuUI Instance { get; private set; }

    [SerializeField] private GameObject pausePanel;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button copyCodeButton;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityLabel;

    private void Awake()
    {
        Instance = this;

        resumeButton.onClick.AddListener(Resume);
        leaveButton.onClick.AddListener(LeaveRoom);
        if (copyCodeButton != null)
        {
            copyCodeButton.onClick.AddListener(CopyRoomCode);
        }

        if (sensitivitySlider != null)
        {
            // SetValueWithoutNotify so this initial sync doesn't re-trigger SetSensitivity and
            // re-write the value we just read back to PlayerPrefs.
            sensitivitySlider.SetValueWithoutNotify(LookSensitivity.Multiplier);
            UpdateSensitivityLabel(LookSensitivity.Multiplier);
            sensitivitySlider.onValueChanged.AddListener(SetSensitivity);
        }

        pausePanel.SetActive(false);
        IsPaused = false;
    }

    private void SetSensitivity(float value)
    {
        LookSensitivity.SetMultiplier(value);
        UpdateSensitivityLabel(LookSensitivity.Multiplier);
    }

    private void UpdateSensitivityLabel(float value)
    {
        if (sensitivityLabel != null)
        {
            sensitivityLabel.text = $"Look Sensitivity: {value:0.00}x";
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

        if (IsPaused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    private void Pause()
    {
        IsPaused = true;
        pausePanel.SetActive(true);

        if (roomCodeText != null)
        {
            string code = PhotonManager.Instance != null ? PhotonManager.Instance.CurrentRoomCode : null;
            roomCodeText.text = string.IsNullOrEmpty(code) ? "Room Code: -----" : $"Room Code: {code}";
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Resume()
    {
        IsPaused = false;
        pausePanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LeaveRoom()
    {
        IsPaused = false;

        // Released, not locked: leaving drops back to the menu, which is driven by the mouse.
        // Resume() locks the cursor because it hands control back to the camera - this does the
        // opposite, so it must not copy that.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (PhotonManager.Instance != null)
        {
            PhotonManager.Instance.LeaveRoom();
        }
    }

    private void CopyRoomCode()
    {
        if (PhotonManager.Instance != null && !string.IsNullOrEmpty(PhotonManager.Instance.CurrentRoomCode))
        {
            GUIUtility.systemCopyBuffer = PhotonManager.Instance.CurrentRoomCode;
        }
    }

    private void OnDestroy()
    {
        IsPaused = false;
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
