using Alcax.Core;
using Alcax.Dialogue;
using Alcax.Loot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>Finds the pickup the player is looking at and collects it on Interact (E).</summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] float reach = 2.6f;
        [SerializeField] float maxAngle = 35f;

        [Header("Debug drive (verification only)")]
        public bool debugInteract;

        PlayerController controller;
        InputAction interact;
        Camera cam;

        public Pickup Focused { get; private set; }
        public Npc FocusedNpc { get; private set; }
        public Interactable FocusedThing { get; private set; }

        /// <summary>The HUD prompt for whatever is focused, or null.</summary>
        public string FocusedPrompt => FocusedNpc != null ? $"[E]   TALK TO {FocusedNpc.displayName.ToUpperInvariant()}"
            : FocusedThing != null ? $"[E]   {FocusedThing.Label}"
            : Focused != null ? $"[E]   PICK UP {Focused.Label}" : null;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            interact = controller.Actions.FindActionMap("Player", true).FindAction("Interact", true);
            cam = Camera.main;
        }

        void Update()
        {
            Focused = null;
            FocusedNpc = null;
            FocusedThing = null;
            if (!GameManager.InputLive || controller.Health.IsDead) return;

            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            float best = float.MaxValue;
            foreach (var p in Pickup.Active)
            {
                if (!p.RequiresInteract) continue;
                Vector3 to = p.FocusPoint - eye;
                float dist = to.magnitude;
                if (dist > reach) continue;
                float angle = Vector3.Angle(fwd, to);
                if (angle > maxAngle && dist > 1.1f) continue;
                float score = angle + dist * 8f;
                if (score < best) { best = score; Focused = p; }
            }

            foreach (var n in Npc.Active)
            {
                if (!n.CanTalk) continue;
                Vector3 to = n.FocusPoint - eye;
                float dist = to.magnitude;
                if (dist > reach + 1.2f) continue;
                float angle = Vector3.Angle(fwd, to);
                if (angle > maxAngle + 10f) continue;
                float score = angle + dist * 8f;
                if (score < best) { best = score; FocusedNpc = n; Focused = null; }
            }

            foreach (var it in Interactable.Active)
            {
                if (!it.Available) continue;
                Vector3 to = it.FocusPoint - eye;
                float dist = to.magnitude;
                if (dist > it.Reach) continue;
                float angle = Vector3.Angle(fwd, to);
                if (angle > maxAngle) continue;
                float score = angle + dist * 8f;
                if (score < best) { best = score; FocusedThing = it; FocusedNpc = null; Focused = null; }
            }

            bool pressed = debugInteract || interact.WasPressedThisFrame();
            debugInteract = false;
            if (!pressed) return;
            if (FocusedNpc != null && DialogueUI.Instance != null) DialogueUI.Instance.Open(FocusedNpc);
            else if (FocusedThing != null) FocusedThing.Interact(gameObject);
            else if (Focused != null) Focused.Collect(gameObject);
        }
    }
}
