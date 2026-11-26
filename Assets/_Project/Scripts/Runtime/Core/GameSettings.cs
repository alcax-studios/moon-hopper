using UnityEngine;

namespace Alcax.Core
{
    /// <summary>Player-facing settings, saved to PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string SensKey = "look_sensitivity";

        /// <summary>A multiplier on the base mouse sensitivity (0.25 to 3).</summary>
        static float sens = -1f;

        public static float LookSensitivity
        {
            get { if (sens < 0f) sens = PlayerPrefs.GetFloat(SensKey, 1f); return sens; }
            set { sens = Mathf.Clamp(value, 0.25f, 3f); PlayerPrefs.SetFloat(SensKey, sens); }
        }
    }
}
