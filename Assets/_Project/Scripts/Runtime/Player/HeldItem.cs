using Alcax.Audio;
using Alcax.Core;
using Alcax.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>
    /// A non-weapon item in the hand (the selected hotbar slot): its model in first person, and what the attack
    /// button does with it. Aid and food: hold to use (the item's use time). Wearables: click to put on. Misc: just held.
    /// </summary>
    [RequireComponent(typeof(WeaponHolder))]
    public class HeldItem : MonoBehaviour
    {
        static readonly Vector3 HoldPos = new Vector3(0.22f, -0.24f, 0.5f);

        WeaponHolder holder;
        PlayerController controller;
        ItemDefinition shown;
        GameObject model;
        float useT = -1f, bob;

        /// <summary>0..1 while using an item from the hand, -1 otherwise (the HUD shows a bar).</summary>
        public float UseProgress => useT;
        public ItemDefinition Using => useT >= 0f ? shown : null;

        void Awake()
        {
            holder = GetComponent<WeaponHolder>();
            controller = GetComponent<PlayerController>();
        }

        void Update()
        {
            var inv = holder.Inventory;
            var s = inv != null && holder.HoldingItem ? inv.SelectedStack : null;
            var def = s?.def;
            if (def != shown) Show(def);
            if (def == null || controller.Health.IsDead) { useT = -1f; return; }

            // Gentle sway and a lift while using
            bob += Time.deltaTime * (1f + controller.SpeedNormalized * 5f);
            if (model != null)
            {
                float lift = useT >= 0f ? Mathf.Sin(Mathf.Min(1f, useT * 3f) * Mathf.PI * 0.5f) : 0f;
                model.transform.localPosition = HoldPos + new Vector3(Mathf.Sin(bob) * 0.006f, Mathf.Abs(Mathf.Cos(bob)) * 0.008f, 0f)
                    + new Vector3(-0.12f, 0.1f, -0.1f) * lift;
                model.transform.localRotation = Quaternion.Euler(-10f - 30f * lift, -25f + 15f * lift, 5f);
            }

            if (!GameManager.InputLive || PlayerGrabber.Holding || Time.time - PlayerGrabber.LastRelease < 0.25f) { useT = -1f; return; }
            var mouse = Mouse.current;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

            if (def.Wearable)
            {
                if (pressed) inv.ToggleWear(new SlotRef(SlotArea.Hotbar, inv.Selected));
                return;
            }
            if (!def.Consumable) return;
            // A click uses it, there and then
            useT = -1f;
            if (pressed) inv.Consume(new SlotRef(SlotArea.Hotbar, inv.Selected));
        }

        void Show(ItemDefinition def)
        {
            if (model != null) Destroy(model);
            model = null;
            shown = def;
            useT = -1f;
            if (def == null || def.worldModel == null || holder.ViewRoot == null) return;
            model = Instantiate(def.worldModel, holder.ViewRoot, false);
            model.name = "Held_" + def.id;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            model.transform.localScale = Vector3.one * def.heldScale;
            model.transform.localPosition = HoldPos;
        }
    }
}
