using Alcax.Core;
using Alcax.Dialogue;
using Alcax.Items;
using Alcax.Loot;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>
    /// What the player can use with E: people (talk), pickups, interactables (buttons, doors), world items (tap E to
    /// take, hold E to drag), and physics props (hold E to drag; see PlayerGrabber).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        const float HoldToDrag = 0.28f;

        [SerializeField] float reach = 2.6f;
        [SerializeField] float maxAngle = 35f;

        [Header("Debug drive (verification only)")]
        public bool debugInteract;
        [Tooltip("Acts as holding E down (tests).")]
        public bool debugHold;

        PlayerController controller;
        PlayerGrabber grabber;
        InputAction interact;
        Camera cam;
        Interactable pending;   // E went down on an item or a body: a tap takes or searches it, a hold drags it
        float pressedAt;
        Vector3 grabPoint;

        public Pickup Focused { get; private set; }
        public Npc FocusedNpc { get; private set; }
        public Interactable FocusedThing { get; private set; }
        public Grabbable FocusedGrab { get; private set; }
        /// <summary>The crosshair shows the grab cue: something movable is under it, or you're holding something.</summary>
        public bool GrabCue => PlayerGrabber.Holding || FocusedGrab != null;

        /// <summary>The HUD prompt for whatever is focused, or null.</summary>
        public string FocusedPrompt
        {
            get
            {
                if (PlayerGrabber.Holding) return null;
                if (FocusedNpc != null) return $"[E]   TALK TO {FocusedNpc.displayName.ToUpperInvariant()}";
                if (FocusedThing is WorldItem wi && wi.Owned)
                    return $"<color=#E86A5A>[E]   {FocusedThing.Label}</color>";
                if (FocusedThing != null) return FocusedThing.TryGetComponent<Lock>(out var lk) && lk.Locked ? $"[E]   {FocusedThing.Label}   <color=#E8E1D088>LOCKED</color>" : $"[E]   {FocusedThing.Label}";
                if (Focused != null) return Focused.Owned ? $"<color=#E86A5A>[E]   STEAL {Focused.Label}</color>" : $"[E]   PICK UP {Focused.Label}";
                return null;
            }
        }

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            grabber = GetComponent<PlayerGrabber>();
            if (grabber == null) grabber = gameObject.AddComponent<PlayerGrabber>();
            interact = controller.Actions.FindActionMap("Player", true).FindAction("Interact", true);
            cam = Camera.main;
        }

        void Update()
        {
            Focused = null;
            FocusedNpc = null;
            FocusedThing = null;
            FocusedGrab = null;
            if (!GameManager.InputLive || controller.Health.IsDead)
            {
                if (PlayerGrabber.Holding) grabber.Release();
                pending = null;
                return;
            }

            bool down = interact.IsPressed() || debugHold;
            if (PlayerGrabber.Holding)
            {
                if (!down) grabber.Release();
                return;
            }

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
                // World items are small: they need you to actually look at them
                if (angle > (it is WorldItem ? 14f : maxAngle) && dist > 0.9f) continue;
                if (it is WorldItem && Physics.Linecast(eye, it.FocusPoint, out var block, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && block.collider.attachedRigidbody != it.GetComponent<Rigidbody>()) continue;
                float score = angle + dist * 8f;
                if (score < best) { best = score; FocusedThing = it; FocusedNpc = null; Focused = null; }
            }

            // Draggable props: whatever the crosshair is on
            if (Physics.Raycast(eye, fwd, out var hit, reach + 0.4f, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                var g = hit.collider.GetComponentInParent<Grabbable>();
                if (g != null && g.Body != null && !g.Body.isKinematic)
                {
                    grabPoint = hit.point;
                    if (FocusedThing is WorldItem w && w.gameObject == g.gameObject) FocusedGrab = g;
                    else if (FocusedThing is LootContainer body && body.IsBody && g.transform.IsChildOf(body.transform)) FocusedGrab = g;
                    else if (FocusedThing == null && FocusedNpc == null && Focused == null) FocusedGrab = g;
                }
            }

            // Tap or hold on a world item
            if (pending != null)
            {
                if (!down)
                {
                    if (FocusedThing == pending || Time.time - pressedAt < HoldToDrag) pending.Interact(gameObject);
                    pending = null;
                }
                else if (Time.time - pressedAt >= HoldToDrag)
                {
                    // The part under the crosshair (a body: the limb you look at), else the item itself
                    var g = FocusedGrab != null ? FocusedGrab : pending.GetComponentInChildren<Grabbable>();
                    var at = FocusedGrab != null ? grabPoint : pending.FocusPoint;
                    if (g != null) grabber.Grab(g, at);
                    pending = null;
                }
                return;
            }

            bool pressed = debugInteract || interact.WasPressedThisFrame();
            bool debug = debugInteract;
            debugInteract = false;
            if (!pressed) return;
            if (FocusedNpc != null && DialogueUI.Instance != null) DialogueUI.Instance.Open(FocusedNpc);
            else if (FocusedThing is WorldItem || FocusedThing is LootContainer { IsBody: true })
            {
                if (debug) FocusedThing.Interact(gameObject);
                else { pending = FocusedThing; pressedAt = Time.time; }
            }
            else if (FocusedThing != null)
            {
                if (FocusedThing.TryGetComponent<Lock>(out var lk) && !lk.TryUnlock(GetComponent<PlayerInventory>())) return;
                FocusedThing.Interact(gameObject);
            }
            else if (Focused != null) Focused.Collect(gameObject);
            else if (FocusedGrab != null) grabber.Grab(FocusedGrab, grabPoint);
        }
    }
}
