using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public Vector3 MoveDirection;
    public NetworkBool Jump;

    // Look angles in degrees. Third-person variants drive rotation from MoveDirection and
    // ignore these; first-person needs them because the body must face where the camera looks
    // (Yaw) and remote players should see head aim (Pitch).
    public float Yaw;
    public float Pitch;
}
