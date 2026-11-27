using System;
using System.Collections.Generic;
using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// The Inventory tab: the backpack grid, the hotbar row under it, and on the right the character preview with the
    /// body slots and an info box. Drag to move or swap (drop outside the window to drop it on the floor); click or
    /// right-click for the item menu (Use, Wear, To hotbar, Split, Drop, Details); Shift+click to quick-move;
    /// double-click to use or wear. Drag on the character to turn it.
    /// </summary>
    public class InventoryPanel : MonoBehaviour, ISlotPanel
    {
        const float Slot = 96f, Gap = 8f;

        readonly List<ItemSlotView> slots = new List<ItemSlotView>();
        RectTransform root, windowRect;
        RawImage ghost, doll;
        TextMeshProUGUI armorText, infoName, infoCat, infoDesc, infoStats;
        Image infoBox;
        ItemSlotView hovered, dragFrom, focus;
        ItemSlotView pendingMenu; float pendingMenuAt; Vector2 pendingMenuScreen;
        float lastClick; ItemSlotView lastClickSlot;

        PlayerInventory Inv => PlayerInventory.Instance;

        public void Build(RectTransform parent, RectTransform window)
        {
            root = parent;
            windowRect = window;

            // ---- Backpack ----
            Label("BACKPACK", new Vector2(40f, -8f));
            for (int i = 0; i < PlayerInventory.GridSize; i++)
            {
                int c = i % PlayerInventory.GridW, r = i / PlayerInventory.GridW;
                AddSlot(new SlotRef(SlotArea.Grid, i), new Vector2(40f + c * (Slot + Gap), -40f - r * (Slot + Gap)));
            }
            float gridBottom = -40f - PlayerInventory.GridH * (Slot + Gap);

            // ---- Hotbar ----
            Label("HOTBAR", new Vector2(40f, gridBottom - 14f));
            for (int i = 0; i < PlayerInventory.HotbarSize; i++)
                AddSlot(new SlotRef(SlotArea.Hotbar, i), new Vector2(40f + i * (Slot + Gap), gridBottom - 46f), (i + 1).ToString());

            // ---- Character ----
            float cx = 920f;
            Label("CHARACTER", new Vector2(cx, -8f));
            var dollRt = UIFactory.Rect("Doll", root);
            UIFactory.Place(dollRt, new Vector2(0f, 1f), new Vector2(cx + 170f, -34f), new Vector2(300f, 460f), new Vector2(0f, 1f));
            doll = dollRt.gameObject.AddComponent<RawImage>();
            doll.raycastTarget = true;
            var spin = dollRt.gameObject.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            e.callback.AddListener(d => PreviewStudio.Instance.Yaw -= ((PointerEventData)d).delta.x * 0.5f);
            spin.triggers.Add(e);

            float lx = cx + 40f, rx = cx + 500f;
            AddSlot(SlotRef.Equip(EquipSlot.Head), new Vector2(lx, -60f), null, "HEAD");
            AddSlot(SlotRef.Equip(EquipSlot.Chest), new Vector2(lx, -190f), null, "CHEST");
            AddSlot(SlotRef.Equip(EquipSlot.Gloves), new Vector2(lx, -320f), null, "GLOVES");
            AddSlot(SlotRef.Equip(EquipSlot.Pants), new Vector2(rx, -190f), null, "PANTS");
            AddSlot(SlotRef.Equip(EquipSlot.Shoes), new Vector2(rx, -320f), null, "SHOES");

            armorText = UIFactory.Text("Armor", root, "", 17f, Palette.UIBone, TextAlignmentOptions.Center, 3f, 0f);
            UIFactory.Place(armorText.rectTransform, new Vector2(0f, 1f), new Vector2(cx, -500f), new Vector2(640f, 26f), new Vector2(0f, 1f));

            // ---- Info box ----
            infoBox = UIFactory.Image("Info", root, Palette.WithAlpha(Palette.UIDark, 0.7f));
            UIFactory.Place(infoBox.rectTransform, new Vector2(0f, 1f), new Vector2(cx, -536f), new Vector2(640f, 192f), new Vector2(0f, 1f));
            infoName = UIFactory.Text("Name", infoBox.transform, "", 24f, Palette.Accent, TextAlignmentOptions.TopLeft, 3f, 0f);
            UIFactory.Place(infoName.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(600f, 30f), new Vector2(0f, 1f));
            infoCat = UIFactory.Text("Cat", infoBox.transform, "", 13f, Palette.WithAlpha(Palette.UIBone, 0.55f), TextAlignmentOptions.TopLeft, 3f, 0f);
            UIFactory.Place(infoCat.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -44f), new Vector2(600f, 18f), new Vector2(0f, 1f));
            infoDesc = UIFactory.Body("Desc", infoBox.transform, "", 17f, Palette.UIBone);
            UIFactory.Place(infoDesc.rectTransform, new Vector2(0f, 1f), new Vector2(18f, -68f), new Vector2(604f, 80f), new Vector2(0f, 1f));
            infoStats = UIFactory.Text("Stats", infoBox.transform, "", 15f, Palette.AccentHot, TextAlignmentOptions.BottomLeft, 2f, 0f);
            UIFactory.Place(infoStats.rectTransform, new Vector2(0f, 0f), new Vector2(18f, 12f), new Vector2(604f, 22f), new Vector2(0f, 0f));

            // ---- Drag ghost (on top) ----
            var g = UIFactory.Rect("Ghost", root);
            g.sizeDelta = new Vector2(Slot * 0.85f, Slot * 0.85f);
            ghost = g.gameObject.AddComponent<RawImage>();
            ghost.raycastTarget = false;
            ghost.enabled = false;
        }

        void Label(string text, Vector2 pos)
        {
            var t = UIFactory.Text(text, root, text, 18f, Palette.WithAlpha(Palette.UIBone, 0.8f), TextAlignmentOptions.MidlineLeft, 5f, 0f);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), pos, new Vector2(400f, 26f), new Vector2(0f, 1f));
        }

        void AddSlot(SlotRef r, Vector2 pos, string key = null, string empty = null)
        {
            var v = ItemSlotView.Create(root, this, r, Slot, key, empty);
            UIFactory.Place((RectTransform)v.transform, new Vector2(0f, 1f), pos, new Vector2(Slot, Slot), new Vector2(0f, 1f));
            slots.Add(v);
            if (empty != null)
            {
                var cap = UIFactory.Text("Caption", root, empty, 12f, Palette.WithAlpha(Palette.UIBone, 0.5f), TextAlignmentOptions.Center, 3f, 0f);
                UIFactory.Place(cap.rectTransform, new Vector2(0f, 1f), pos + new Vector2(0f, -Slot - 4f), new Vector2(Slot, 18f), new Vector2(0f, 1f));
            }
        }

        // ---------- Every frame while open ----------

        void Update()
        {
            var inv = Inv;
            if (inv == null) return;
            foreach (var s in slots)
                s.Show(inv.Get(s.Ref), s.Ref.Area == SlotArea.Hotbar && s.Ref.Index == inv.Selected, s == hovered);
            var studio = PreviewStudio.Instance;
            studio.Dress(inv);
            studio.RenderDoll();
            doll.texture = studio.DollTexture;
            armorText.text = $"ARMOR   HEAD {inv.Armor(true):0}   ·   BODY {inv.Armor(false):0}      <color=#FFD27A>{inv.Credits} CR</color>";

            var show = hovered != null ? inv.Get(hovered.Ref) : focus != null ? inv.Get(focus.Ref) : null;
            ShowInfo(show);

            var kb = Keyboard.current;
if (ItemMenus.IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) CloseMenu();
            // A single left click opens the menu once it's clear it wasn't the first half of a double-click
            if (pendingMenu != null && Time.unscaledTime >= pendingMenuAt)
            {
                var pv = pendingMenu; pendingMenu = null;
                var ps = Inv.Get(pv.Ref);
                if (ps != null) OpenMenu(pv.Ref, ps, pendingMenuScreen);
            }
        }

        void ShowInfo(ItemStack s)
        {
            if (s == null)
            {
                infoName.text = "";
                infoCat.text = "";
                infoDesc.text = "";
                infoStats.text = "";
                return;
            }
            infoName.text = s.def.displayName.ToUpperInvariant() + (s.count > 1 ? $"  <size=18><color=#E8E1D0AA>x{s.count}</color></size>" : "");
            infoCat.text = s.def.CategoryLabel;
            infoDesc.text = s.def.description;
            infoStats.text = s.def.StatLines();
        }

        public void OnHidden()
        {
            CloseMenu();
            CloseDetails();
            ghost.enabled = false;
            dragFrom = null;
            hovered = null;
        }

        // ---------- Hover and click ----------

        public void Hover(ItemSlotView v) => hovered = v;
        public void Unhover(ItemSlotView v) { if (hovered == v) hovered = null; }

        public void Clicked(ItemSlotView v, PointerEventData.InputButton button, Vector2 screen)
        {
            var inv = Inv;
            var s = inv?.Get(v.Ref);
            focus = v;
            if (s == null) { CloseMenu(); return; }
            var kb = Keyboard.current;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            bool dbl = button == PointerEventData.InputButton.Left && lastClickSlot == v && Time.unscaledTime - lastClick < 0.3f;
            lastClick = Time.unscaledTime; lastClickSlot = v;
            if (shift && button == PointerEventData.InputButton.Left) { QuickMove(v.Ref, s); CloseMenu(); return; }
            if (dbl) { DefaultAction(v.Ref, s); CloseMenu(); lastClickSlot = null; return; }
            if (button == PointerEventData.InputButton.Right) { pendingMenu = null; OpenMenu(v.Ref, s, screen); return; }
            CloseMenu();
            pendingMenu = v; pendingMenuAt = Time.unscaledTime + 0.28f; pendingMenuScreen = screen;
        }

        void QuickMove(SlotRef r, ItemStack s)
        {
            var inv = Inv;
            if (r.Area == SlotArea.Grid)
            {
                if (s.def.Wearable) inv.ToggleWear(r);
                else inv.ToHotbar(r);
            }
            else if (r.Area == SlotArea.Equip) inv.ToggleWear(r);
            else MoveToGrid(r);
        }

        void MoveToGrid(SlotRef r)
        {
            var inv = Inv;
            for (int i = 0; i < PlayerInventory.GridSize; i++)
                if (inv.Get(new SlotRef(SlotArea.Grid, i)) == null) { inv.Move(r, new SlotRef(SlotArea.Grid, i)); return; }
        }

        void DefaultAction(SlotRef r, ItemStack s)
        {
            if (s.def.Consumable) Inv.Consume(r);
            else if (s.def.Wearable) Inv.ToggleWear(r);
            else QuickMove(r, s);
        }

        // ---------- Item menu ----------

        void OpenMenu(SlotRef r, ItemStack s, Vector2 screen)
        {
            CloseMenu();
            var inv = Inv;
            var options = new List<(string, Action)>();
            if (s.def.Consumable) options.Add(("Use", () => inv.Consume(r)));
            if (s.def.Wearable) options.Add((r.Area == SlotArea.Equip ? "Take off" : "Wear", () => inv.ToggleWear(r)));
            if (r.Area == SlotArea.Grid && !s.def.Wearable) options.Add(("To hotbar", () => inv.ToHotbar(r)));
            if (r.Area == SlotArea.Hotbar) options.Add(("To backpack", () => MoveToGrid(r)));
            if (r.Area == SlotArea.Hotbar && inv.Selected != r.Index) options.Add(("Hold", () => inv.Select(r.Index)));
            if (s.count > 1)
            {
                options.Add(("Split", () => SplitHalf(r, s)));
                options.Add(("Drop one", () => inv.Drop(r, 1)));
                options.Add(("Drop all", () => inv.Drop(r)));
            }
            else options.Add(("Drop", () => inv.Drop(r)));
            options.Add(("Details", () => OpenDetails(s)));

            ItemMenus.Open(windowRect, screen, s.def.displayName, options);
        }

        void SplitHalf(SlotRef r, ItemStack s)
        {
            var inv = Inv;
            int half = s.count / 2;
            for (int i = 0; i < PlayerInventory.GridSize; i++)
                if (inv.Get(new SlotRef(SlotArea.Grid, i)) == null) { inv.Move(r, new SlotRef(SlotArea.Grid, i), half); return; }
        }

void CloseMenu() { ItemMenus.Close(); pendingMenu = null; }

        void OpenDetails(ItemStack s) => ItemMenus.Details(windowRect, s);

        void CloseDetails() => ItemMenus.CloseDetails();

                // ---------- Drag and drop ----------

        public bool BeginDrag(ItemSlotView v, Vector2 screen)
        {
            var s = Inv?.Get(v.Ref);
            if (s == null) return false;
            CloseMenu();
            dragFrom = v;
            ghost.texture = PreviewStudio.Instance.Icon(s.def);
            ghost.enabled = true;
            ghost.transform.SetAsLastSibling();
            DragTo(screen);
            return true;
        }

        public void DragTo(Vector2 screen)
        {
            if (!ghost.enabled) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            var rt = (RectTransform)ghost.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            // root's anchors are top-left-ish; convert from local (pivot-relative) to anchored
            rt.anchoredPosition = local + (root.pivot - new Vector2(0.5f, 0.5f)) * root.rect.size;
        }

        public void EndDrag(ItemSlotView from, ItemSlotView to, Vector2 screen)
        {
            ghost.enabled = false;
            dragFrom = null;
            var inv = Inv;
            if (inv == null) return;
            if (to != null && to != from)
            {
                if (!inv.Move(from.Ref, to.Ref)) SoundFail();
                return;
            }
            // Released outside the window: drop it on the floor
            if (to == null && !RectTransformUtility.RectangleContainsScreenPoint(windowRect, screen, null)) inv.Drop(from.Ref);
        }

        static void SoundFail() => Audio.SoundManager.Play2D(Audio.Sfx.Click, 0.5f, 0.05f);
    }
}
