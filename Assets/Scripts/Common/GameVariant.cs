// Identifies which of the four template variants a session runs as. Every variant pairs a
// camera perspective with a Fusion topology, and each pairing owns its own gameplay scene and
// its own player prefab + player/camera scripts; only matchmaking, the Runner prefab and the
// UI live in Common and are reused by all four.

public enum PlayerPerspective
{
    FirstPerson,
    ThirdPerson,
}

public enum NetworkPlayMode
{
    /// <summary>Every client owns and spawns its own avatar (Fusion GameMode.Shared).</summary>
    Shared,

    /// <summary>One peer is the server and spawns every avatar (Fusion GameMode.Host/Client).</summary>
    Host,
}
