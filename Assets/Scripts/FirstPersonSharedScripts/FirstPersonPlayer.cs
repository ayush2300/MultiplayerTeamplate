using Fusion;
using UnityEngine;

// Shared-mode first-person avatar: every client spawns and holds state authority over its own,
// so the same peer that produces the input also applies it.
[RequireComponent(typeof(NetworkCharacterController))]
public class FirstPersonPlayer : NetworkBehaviour
{
    [Tooltip("Child holding the Camera and FirstPersonCamera. Enabled only on the local player.")]
    [SerializeField] private GameObject cameraRig;

    [Tooltip("Head position the local camera snaps to. Falls back to the camera rig's transform.")]
    [SerializeField] private Transform headAnchor;

    [Tooltip("Pivot rotated by networked pitch so remote players can see where this player is aiming.")]
    [SerializeField] private Transform headPivot;

    [Tooltip("Renderers hidden for the local player, who would otherwise be looking at the inside of their own body.")]
    [SerializeField] private GameObject[] hideForLocalPlayer;

    // Networked rather than local-only so remote players see head aim. Yaw needs no equivalent:
    // it is applied to the transform itself, which Fusion already replicates.
    [Networked] public float Pitch { get; set; }

    private NetworkCharacterController _cc;

    private void Awake()
    {
        _cc = GetComponent<NetworkCharacterController>();
    }

    public override void Spawned()
    {
        bool isLocal = Object.HasInputAuthority;

        if (cameraRig == null) return;

        cameraRig.SetActive(isLocal);

        if (!isLocal) return;

        // The scene's own camera would otherwise keep rendering over the player's view.
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

        var firstPersonCamera = cameraRig.GetComponent<FirstPersonCamera>();
        if (firstPersonCamera != null)
        {
            firstPersonCamera.ActivateAsLocalView(headAnchor != null ? headAnchor : cameraRig.transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!GetInput(out NetworkInputData input)) return;

        _cc.Move(input.MoveDirection);

        // Applied after Move, which turns the character toward its movement vector - in first
        // person the body must face where the camera looks instead, or strafing would spin it.
        transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);

        if (input.Jump)
        {
            _cc.Jump();
        }

        Pitch = input.Pitch;
    }

    public override void Render()
    {
        // Local player's head aim is already shown by the camera itself; this is what makes it
        // visible on everyone else's screen.
        if (headPivot != null && !Object.HasInputAuthority)
        {
            headPivot.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
        }
    }
}
