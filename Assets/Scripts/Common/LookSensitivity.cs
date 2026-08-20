using UnityEngine;

// Look-sensitivity multiplier shared by every variant's camera, driven by the pause menu's
// slider and persisted so it survives between sessions. It lives in Common (rather than on a
// camera script) so the common pause UI can read and set it without depending on whichever
// perspective is loaded, and so a first-person and third-person camera agree on one setting.
public static class LookSensitivity
{
    private const string PrefKey = "LookSensitivityMultiplier";

    public const float MinMultiplier = 0.25f;
    public const float MaxMultiplier = 3f;

    // Lazily loaded on first access (not a field initializer) so the pause menu can read/set it
    // before any player has spawned - Unity forbids PlayerPrefs calls from a static field
    // initializer/type constructor, only from Awake/Start or later.
    private static float? _multiplier;

    public static float Multiplier
    {
        get
        {
            _multiplier ??= Mathf.Clamp(PlayerPrefs.GetFloat(PrefKey, 1f), MinMultiplier, MaxMultiplier);
            return _multiplier.Value;
        }
    }

    public static void SetMultiplier(float value)
    {
        _multiplier = Mathf.Clamp(value, MinMultiplier, MaxMultiplier);
        PlayerPrefs.SetFloat(PrefKey, _multiplier.Value);
    }
}
