using Fusion;
using UnityEngine;

// Host-mode first-person avatar.
//
// The authority split is what separates this from the shared-mode version: there, the client
// that owns an avatar also holds state authority over it. Here the server holds state authority
// over every avatar and is the only peer that spawns them (see PhotonManager.OnPlayerJoined),
// while a client only holds *input* authority over its own. So "is this mine?" is
// HasInputAuthority, and "may I write networked state?" is HasStateAuthority - two questions
// that happen to have the same answer in shared mode and different answers here.
[RequireComponent(typeof(NetworkCharacterController))]
public class FirstPersonHostPlayer : NetworkBehaviour
{
    [Tooltip("Child holding the Camera and FirstPersonHostCamera. Enabled only on the local player.")]
    [SerializeField] private GameObject cameraRig;

    [Tooltip("Head position the local camera snaps to. Falls back to the camera rig's transform.")]
    [SerializeField] private Transform headAnchor;

    [Tooltip("Pivot rotated by networked pitch so remote players can see where this player is aiming.")]
    [SerializeField] private Transform headPivot;

    [Tooltip("Renderers hidden for the local player, who would otherwise be looking at the inside of their own body.")]
    [SerializeField] private GameObject[] hideForLocalPlayer;

    [Networked] public float Pitch { get; set; }

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

        foreach (var visual in hideForLocalPlayer)
        {
            if (visual != null)
            {
                visual.SetActive(false);
            }
        }

        var camera = cameraRig.GetComponent<FirstPersonHostCamera>();
        if (camera != null)
        {
            camera.ActivateAsLocalView(headAnchor != null ? headAnchor : cameraRig.transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // True on the server for every avatar, and on the owning client for its own, which is
        // what lets that client predict its movement instead of waiting a round trip for it.
        if (!GetInput(out NetworkInputData input)) return;

        _cc.Move(input.MoveDirection);

        // Applied after Move, which turns the character toward its movement vector - in first
        // person the body must face where the camera looks instead, or strafing would spin it.
        transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);

        if (input.Jump)
        {
            _cc.Jump();
        }

        // Only the server may write networked state here. The owning client already sees its own
        // aim through its camera, so letting it predict this would buy nothing.
        if (Object.HasStateAuthority)
        {
            Pitch = input.Pitch;
        }
    }

    public override void Render()
    {
        if (headPivot != null && !Object.HasInputAuthority)
        {
            headPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }
}
