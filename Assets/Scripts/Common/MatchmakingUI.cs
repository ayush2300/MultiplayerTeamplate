using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchmakingUI : MonoBehaviour
{
    [Header("Room")]
    [SerializeField] private Button createRoomButton;
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private TMP_InputField roomCodeInputField;
    [SerializeField] private Slider maxPlayersSlider;
    [SerializeField] private TMP_Text maxPlayersLabel;
    [SerializeField] private TMP_Text statusText;

    [Header("Variant Selection")]
    [Tooltip("Optional - leave unassigned to lock the build to PhotonManager's inspector defaults.")]
    [SerializeField] private Button firstPersonButton;
    [SerializeField] private Button thirdPersonButton;
    [SerializeField] private Button sharedModeButton;
    [SerializeField] private Button hostModeButton;
    [SerializeField] private TMP_Text variantLabel;

    [SerializeField] private Color selectedTint = new Color(0.30f, 0.65f, 1f);
    [SerializeField] private Color unselectedTint = Color.white;

    private void Awake()
    {
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
        maxPlayersSlider.onValueChanged.AddListener(OnMaxPlayersChanged);
        OnMaxPlayersChanged(maxPlayersSlider.value);

        AddVariantListener(firstPersonButton, () => SelectPerspective(PlayerPerspective.FirstPerson));
        AddVariantListener(thirdPersonButton, () => SelectPerspective(PlayerPerspective.ThirdPerson));
        AddVariantListener(sharedModeButton, () => SelectMode(NetworkPlayMode.Shared));
        AddVariantListener(hostModeButton, () => SelectMode(NetworkPlayMode.Host));
    }

    private void Start()
    {
        // The menu is mouse-driven, so it releases the cursor itself rather than trusting
        // whatever left it locked. Gameplay locks the cursor for the camera, and there are
        // several ways back here - the Leave button, a disconnect, a failed connection - so
        // asserting it here covers all of them instead of every exit path remembering to.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Deferred to Start so PhotonManager.Awake has run and Instance is available - the
        // manager persists across scenes, so on a second visit to the menu it already holds
        // the selection made last time and the buttons must reflect that, not a default.
        RefreshVariantVisuals();
    }

    private static void AddVariantListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private void SelectPerspective(PlayerPerspective perspective)
    {
        if (PhotonManager.Instance == null) return;
        PhotonManager.Instance.SelectPerspective(perspective);
        RefreshVariantVisuals();
    }

    private void SelectMode(NetworkPlayMode mode)
    {
        if (PhotonManager.Instance == null) return;
        PhotonManager.Instance.SelectMode(mode);
        RefreshVariantVisuals();
    }

    private void RefreshVariantVisuals()
    {
        if (PhotonManager.Instance == null) return;

        var perspective = PhotonManager.Instance.SelectedPerspective;
        var mode = PhotonManager.Instance.SelectedMode;

        Tint(firstPersonButton, perspective == PlayerPerspective.FirstPerson);
        Tint(thirdPersonButton, perspective == PlayerPerspective.ThirdPerson);
        Tint(sharedModeButton, mode == NetworkPlayMode.Shared);
        Tint(hostModeButton, mode == NetworkPlayMode.Host);

        if (variantLabel != null)
        {
            string perspectiveName = perspective == PlayerPerspective.FirstPerson ? "First Person" : "Third Person";
            variantLabel.text = $"{perspectiveName} - {mode}";
        }
    }

    private void Tint(Button button, bool isSelected)
    {
        if (button == null) return;
        var image = button.targetGraphic;
        if (image != null)
        {
            image.color = isSelected ? selectedTint : unselectedTint;
        }
    }

    private void OnCreateRoomClicked()
    {
        if (PhotonManager.Instance == null) return;
        SetInteractable(false);
        statusText.text = "Creating room...";
        PhotonManager.Instance.CreateRoom((int)maxPlayersSlider.value);
    }

    private void OnJoinRoomClicked()
    {
        if (PhotonManager.Instance == null) return;

        if (string.IsNullOrWhiteSpace(roomCodeInputField.text))
        {
            statusText.text = "Enter a room code first.";
            return;
        }

        SetInteractable(false);
        statusText.text = "Joining room...";
        PhotonManager.Instance.JoinRoom(roomCodeInputField.text);
    }

    private void OnMaxPlayersChanged(float value)
    {
        maxPlayersLabel.text = $"Max Players: {(int)value}";
    }

    public void ShowError(string reason)
    {
        statusText.text = $"Failed: {reason}";
        SetInteractable(true);
    }

    private void SetInteractable(bool value)
    {
        createRoomButton.interactable = value;
        joinRoomButton.interactable = value;
    }
}
