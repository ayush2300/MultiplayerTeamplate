using UnityEngine;

// Lets the common PhotonManager gather input without knowing which variant is running.
// Each variant's local camera/controller implements this and registers itself while it is
// the active local view; PhotonManager.OnInput just reads whoever is currently registered.
public interface ILocalPlayerInput
{
    /// <summary>World-space movement direction for this tick, already clamped to length 1.</summary>
    Vector3 ComputeMoveDirection();

    /// <summary>Returns true once per queued jump, clearing the queue.</summary>
    bool ConsumeJumpPressed();

    /// <summary>Current horizontal look angle in degrees.</summary>
    float Yaw { get; }

    /// <summary>Current vertical look angle in degrees, already clamped to the variant's limits.</summary>
    float Pitch { get; }
}

public static class LocalPlayerInput
{
    public static ILocalPlayerInput Current { get; private set; }

    public static void Register(ILocalPlayerInput source)
    {
        Current = source;
    }

    public static void Unregister(ILocalPlayerInput source)
    {
        if (ReferenceEquals(Current, source))
        {
            Current = null;
        }
    }
}
