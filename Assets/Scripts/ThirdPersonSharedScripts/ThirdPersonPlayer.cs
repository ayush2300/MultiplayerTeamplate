using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkCharacterController))]
public class ThirdPersonPlayer : NetworkBehaviour
{
    [SerializeField] private GameObject cameraRig;

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

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            mainCamera.gameObject.SetActive(false);
        }

        var thirdPersonCamera = cameraRig.GetComponent<ThirdPersonCamera>();
        if (thirdPersonCamera != null)
        {
            thirdPersonCamera.ActivateAsLocalView(transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetworkInputData input))
        {
            _cc.Move(input.MoveDirection);
            if (input.Jump)
            {
                _cc.Jump();
            }
        }
    }
}
