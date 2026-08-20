using Fusion;
using UnityEngine;

// Host-mode third-person avatar.
//
// The authority split is what separates this from the shared-mode version: there, the client
// that owns an avatar also holds state authority over it. Here the server holds state authority
// over every avatar and is the only peer that spawns them (see PhotonManager.OnPlayerJoined),
// while a client only holds *input* authority over its own. So "is this mine?" is
// HasInputAuthority, and "may I write networked state?" is HasStateAuthority - two questions
// that happen to have the same answer in shared mode and different answers here.
[RequireComponent(typeof(NetworkCharacterController))]
public class ThirdPersonHostPlayer : NetworkBehaviour
{
    [SerializeField] private GameObject cameraRig;

    private NetworkCharacterController _cc;

    private void Awake()
    {
        _cc = GetComponent<NetworkCharacterController>();
    }

    public override void Spawned()
    {
        // Input authority, not state authority: on a client the server owns this object's state,
        // but this is still the avatar that client drives and views from.
        bool isLocal = Object.HasInputAuthority;

        if (cameraRig == null) return;

        cameraRig.SetActive(isLocal);

        if (!isLocal) return;

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.gameObject.SetActive(false);
        }

        var camera = cameraRig.GetComponent<ThirdPersonHostCamera>();
        if (camera != null)
        {
            camera.ActivateAsLocalView(transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // True on the server for every avatar, and on the owning client for its own, which is
        // what lets that client predict its movement instead of waiting a round trip for it.
        if (!GetInput(out NetworkInputData input)) return;

        _cc.Move(input.MoveDirection);

        if (input.Jump)
        {
            _cc.Jump();
        }
    }
}
