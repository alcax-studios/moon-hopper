using Alcax.Dialogue;
using UnityEngine;

namespace Alcax.Player
{
    /// <summary>
    /// Frames whoever you're talking to: when a conversation opens, the view turns to them (even from behind you) and
    /// zooms so their head and shoulders sit in the upper half of the screen, above the dialogue panel. When it
    /// closes, the zoom eases back (you keep facing them).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class DialogueCamera : MonoBehaviour
    {
        /// <summary>0..1: how much the dialogue framing owns the field of view (WeaponHolder blends with it).</summary>
        public static float Weight { get; private set; }
        public static float Fov { get; private set; } = 60f;

        PlayerController pc;
        Npc talkingTo;
        const float FrameHeight = 1.0f;   // head to chest, in meters

        void Awake() => pc = GetComponent<PlayerController>();
        void OnEnable() => DialogueUI.Opened += OnOpened;
        void OnDisable() { DialogueUI.Opened -= OnOpened; Weight = 0f; }

        void OnOpened(Npc n) => talkingTo = n;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            bool on = talkingTo != null && DialogueUI.Instance != null && DialogueUI.Instance.IsOpen && talkingTo.isActiveAndEnabled;
            if (!on) talkingTo = null;
            Weight = Mathf.MoveTowards(Weight, on ? 1f : 0f, dt / 0.35f);
            if (!on) return;

            var cam = pc.Head;
            var head = talkingTo.FocusPoint;
            float dist = Mathf.Max(0.8f, Vector3.Distance(cam.position, head));
            // Zoom: the head-to-chest frame fills the upper part of the screen
            Fov = Mathf.Clamp(2f * Mathf.Atan(FrameHeight / 0.55f * 0.5f / dist) * Mathf.Rad2Deg, 26f, 60f);
            // Aim a little below the face so it sits in the upper third, the panel below it
            float drop = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad) * dist * 0.38f;
            pc.TurnToward(head - Vector3.up * drop, 1f - Mathf.Exp(-7f * dt));
        }
    }
}
