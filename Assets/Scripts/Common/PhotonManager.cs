using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

// Common matchmaking + runner lifecycle, shared by all four template variants. It owns nothing
// perspective-specific: the selected PlayerPerspective/NetworkPlayMode pair picks a
// VariantConfig, and that config supplies the gameplay scene and player prefab to use.
public class PhotonManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static PhotonManager Instance { get; private set; }

    [Serializable]
    public class VariantConfig
    {
        public PlayerPerspective perspective;
        public NetworkPlayMode mode;

        [Tooltip("Gameplay scene for this variant, e.g. Assets/Scenes/ThirdPersonShared.unity. Must be in Build Settings.")]
        public string gameplayScenePath;

        [Tooltip("Player prefab spawned for this variant. Each variant has its own player/camera scripts.")]
        public NetworkObject playerPrefab;

        [Tooltip("Photon custom lobby this variant's sessions are published in. Leave blank to derive it, e.g. FPPShared.")]
        public string lobbyName;
    }

    [Header("Variants")]
    [SerializeField] private PlayerPerspective selectedPerspective = PlayerPerspective.ThirdPerson;
    [SerializeField] private NetworkPlayMode selectedMode = NetworkPlayMode.Shared;
    [SerializeField] private VariantConfig[] variants = new VariantConfig[0];

    private const string BaseSceneName = "BaseScene";
    private const int DefaultMaxPlayers = 8;

    public NetworkRunner Runner { get; private set; }
    public string CurrentRoomCode { get; private set; }

    public PlayerPerspective SelectedPerspective => selectedPerspective;
    public NetworkPlayMode SelectedMode => selectedMode;

    /// <summary>Lobby the active session is published in, or null when not in a session.</summary>
    public string CurrentLobbyName => _activeVariant != null ? LobbyNameFor(_activeVariant) : null;

    // Only the host/server spawns in Host mode, so it is the only peer that needs to remember
    // which NetworkObject belongs to which player in order to despawn it on leave. In Shared
    // mode each client spawns (and Fusion cleans up) its own avatar, so this stays empty.
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    private VariantConfig _activeVariant;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SelectPerspective(PlayerPerspective perspective)
    {
        if (Runner != null) return;
        selectedPerspective = perspective;
    }

    public void SelectMode(NetworkPlayMode mode)
    {
        if (Runner != null) return;
        selectedMode = mode;
    }

    private VariantConfig FindVariant(PlayerPerspective perspective, NetworkPlayMode mode)
    {
        foreach (var variant in variants)
        {
            if (variant != null && variant.perspective == perspective && variant.mode == mode)
            {
                return variant;
            }
        }
        return null;
    }

    /// <summary>
    /// Lobby a variant's sessions live in, e.g. "FPPShared". Each variant gets its own so a
    /// session list only ever shows rooms you can actually play in.
    /// </summary>
    public static string LobbyNameFor(PlayerPerspective perspective, NetworkPlayMode mode)
    {
        return (perspective == PlayerPerspective.FirstPerson ? "FPP" : "TPP") + mode;
    }

    private static string LobbyNameFor(VariantConfig variant)
    {
        return string.IsNullOrWhiteSpace(variant.lobbyName)
            ? LobbyNameFor(variant.perspective, variant.mode)
            : variant.lobbyName.Trim();
    }

    /// <summary>
    /// Full Photon session name for a room code. The lobby is prefixed onto it so the same code
    /// in two different variants is two different sessions - otherwise a third-person player
    /// could type a first-person room's code and land in a session whose prefab they cannot use.
    /// Players only ever see and share the bare code.
    /// </summary>
    private static string SessionNameFor(VariantConfig variant, string roomCode)
    {
        return LobbyNameFor(variant) + "-" + roomCode;
    }

    private static string GenerateRoomCode(int length = 5)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
        }
        return sb.ToString();
    }

    public void CreateRoom(int maxPlayers)
    {
        if (Runner != null) return;

        // Creating in Host mode makes this peer the server; in Shared mode there is no server
        // and the first peer in the session simply seeds it.
        var mode = selectedMode == NetworkPlayMode.Host ? GameMode.Host : GameMode.Shared;
        CurrentRoomCode = GenerateRoomCode();
        _ = StartRunner(mode, CurrentRoomCode, maxPlayers > 0 ? maxPlayers : DefaultMaxPlayers);
    }

    public void JoinRoom(string roomCode)
    {
        if (Runner != null) return;
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            ShowMatchmakingError("Enter a room code first.");
            return;
        }

        // Joining an existing Host-mode session means connecting as a plain client; Shared-mode
        // peers are all equal, so joining uses the same GameMode as creating.
        var mode = selectedMode == NetworkPlayMode.Host ? GameMode.Client : GameMode.Shared;
        CurrentRoomCode = roomCode.Trim().ToUpperInvariant();
        _ = StartRunner(mode, CurrentRoomCode, DefaultMaxPlayers);
    }

    public void LeaveRoom()
    {
        if (Runner != null)
        {
            Runner.Shutdown();
        }
    }

    private async System.Threading.Tasks.Task StartRunner(GameMode mode, string roomCode, int maxPlayers)
    {
        var variant = FindVariant(selectedPerspective, selectedMode);
        if (variant == null)
        {
            ShowMatchmakingError($"No variant configured for {selectedPerspective} / {selectedMode}.");
            CurrentRoomCode = null;
            return;
        }

        var sceneRef = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath(variant.gameplayScenePath));
        if (!sceneRef.IsValid)
        {
            ShowMatchmakingError($"'{variant.gameplayScenePath}' is not in Build Settings.");
            Debug.LogError($"PhotonManager: '{variant.gameplayScenePath}' is not in Build Settings. Add it via File > Build Profiles.");
            CurrentRoomCode = null;
            return;
        }

        if (variant.playerPrefab == null)
        {
            // Not fatal - the session still starts and the scene still loads, which is what you
            // want while a variant is still being built out; you just get no avatar.
            Debug.LogWarning($"PhotonManager: variant {selectedPerspective}/{selectedMode} has no player prefab assigned, so no player will spawn.");
        }

        _activeVariant = variant;

        var runnerObject = new GameObject($"NetworkRunner-{mode}");
        DontDestroyOnLoad(runnerObject);

        var runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        runner.AddCallbacks(this);
        Runner = runner;

        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        runnerObject.AddComponent<NetworkObjectProviderDefault>();

        var sceneInfo = new NetworkSceneInfo();
        sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);

        var result = await runner.StartGame(new StartGameArgs
        {
            GameMode = mode,
            SessionName = SessionNameFor(variant, roomCode),
            CustomLobbyName = LobbyNameFor(variant),
            PlayerCount = maxPlayers,
            // Sessions start listed and joinable. Once full they are hidden from the lobby
            // listing (see UpdateSessionVisibility) but deliberately never closed, so the room
            // code starts working again the moment someone leaves.
            IsVisible = true,
            IsOpen = true,
            Scene = sceneInfo,
            SceneManager = sceneManager,
        });

        if (!result.Ok)
        {
            ShowMatchmakingError(result.ShutdownReason.ToString());
            Runner = null;
            _activeVariant = null;
            Destroy(runnerObject);
        }
    }

    private static void ShowMatchmakingError(string reason)
    {
        var ui = FindAnyObjectByType<MatchmakingUI>();
        if (ui != null)
        {
            ui.ShowError(reason);
        }
        else
        {
            Debug.LogWarning($"PhotonManager: {reason}");
        }
    }

    /// <summary>
    /// Hides a full session from its lobby listing and re-lists it when a slot frees up. The
    /// session is never closed: Photon already refuses joins past PlayerCount, so closing it
    /// would only add a second thing to unwind when someone leaves.
    /// </summary>
    private static void UpdateSessionVisibility(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning) return;

        // Session properties belong to whoever owns the session - the server in Host mode, the
        // master client in Shared mode. Everyone else silently has no say, so don't try.
        if (!runner.IsServer && !runner.IsSharedModeMasterClient) return;

        var session = runner.SessionInfo;
        if (session == null || !session.IsValid) return;

        bool shouldBeVisible = session.PlayerCount < session.MaxPlayers;
        if (session.IsVisible == shouldBeVisible) return;

        session.IsVisible = shouldBeVisible;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        UpdateSessionVisibility(runner);

        var prefab = _activeVariant != null ? _activeVariant.playerPrefab : null;
        if (prefab == null) return;

        if (runner.GameMode == GameMode.Shared)
        {
            // Shared mode: every client spawns (and holds state authority over) its own avatar,
            // so react only to our own join and ignore everyone else's.
            if (player != runner.LocalPlayer) return;
        }
        else if (!runner.IsServer)
        {
            // Host mode: only the server spawns, and it spawns for every player including itself.
            return;
        }

        Vector3 spawnPosition = new Vector3(UnityEngine.Random.Range(-3f, 3f), 1f, UnityEngine.Random.Range(-3f, 3f));
        var avatar = runner.Spawn(prefab, spawnPosition, Quaternion.identity, player);

        if (runner.GameMode != GameMode.Shared && avatar != null)
        {
            _spawnedPlayers[player] = avatar;
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // A slot just freed up, so a previously-hidden full session becomes listable again.
        // This also covers inheriting the role: if the master client was the one who left, the
        // peer promoted in its place runs this and takes over maintaining visibility.
        UpdateSessionVisibility(runner);

        // Shared-mode avatars are owned by the client that spawned them and go away with it,
        // so this dictionary only ever has entries on a Host-mode server.
        if (!_spawnedPlayers.TryGetValue(player, out var avatar)) return;

        _spawnedPlayers.Remove(player);
        if (runner.IsServer && avatar != null)
        {
            runner.Despawn(avatar);
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        HandleReturnToBaseScene(runner);
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        HandleReturnToBaseScene(runner);
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        ShowMatchmakingError(reason.ToString());
        HandleReturnToBaseScene(runner);
    }

    private void HandleReturnToBaseScene(NetworkRunner runner)
    {
        if (Runner != runner) return;

        Runner = null;
        CurrentRoomCode = null;
        _activeVariant = null;
        _spawnedPlayers.Clear();

        if (SceneManager.GetActiveScene().name != BaseSceneName)
        {
            SceneManager.LoadScene(BaseSceneName, LoadSceneMode.Single);
        }

        if (runner != null && runner.gameObject != null)
        {
            Destroy(runner.gameObject);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        // Whichever variant's local camera is active has registered itself here, so this stays
        // free of any first/third-person specifics.
        var local = LocalPlayerInput.Current;
        if (local != null)
        {
            data.MoveDirection = local.ComputeMoveDirection();
            data.Jump = local.ConsumeJumpPressed();
            data.Yaw = local.Yaw;
            data.Pitch = local.Pitch;
        }

        input.Set(data);
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
}
