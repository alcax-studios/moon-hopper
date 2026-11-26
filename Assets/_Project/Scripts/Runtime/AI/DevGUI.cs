using UnityEngine;

namespace Alcax.AI
{
    /// <summary>Shared scale for the dev overlays (IMGUI), so they stay readable in a small Game view or on a 4K screen.</summary>
    public static class DevGUI
    {
        /// <summary>Extra size on top of resolution scaling (F1 panel: [ and ] adjust it).</summary>
        public static float UserScale = 1f;
        public static float Scale => Mathf.Max(1.35f, Screen.height / 760f) * UserScale;
    }
}
