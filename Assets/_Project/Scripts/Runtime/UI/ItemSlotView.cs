using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// One inventory slot on screen: background, icon, count, and an optional key number or body-slot label.
    /// Reports hover, clicks (left or right: the item menu), and drag and drop to its InventoryPanel.
    /// </summary>
    public class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [System.NonSerialized] public SlotRef Ref;
        public ISlotPanel Owner;

        Image bg, frame;
        RawImage icon;
        TextMeshProUGUI count, key, placeholder, corner;
        bool dragged;

        static readonly Color Empty = new Color(0.1f, 0.12f, 0.16f, 0.85f);
        static readonly Color Filled = new Color(0.16f, 0.19f, 0.25f, 0.95f);

        public static ItemSlotView Create(Transform parent, ISlotPanel owner, SlotRef r, float size, string keyLabel = null, string emptyLabel = null)
        {
            var bgImg = UIFactory.Image("Slot_" + r, parent, Empty);
            bgImg.raycastTarget = true;
            bgImg.rectTransform.sizeDelta = new Vector2(size, size);
            var v = bgImg.gameObject.AddComponent<ItemSlotView>();
            v.Ref = r; v.Owner = owner; v.bg = bgImg;

            v.frame = UIFactory.Image("Frame", bgImg.transform, Palette.WithAlpha(Palette.Accent, 0f));
            UIFactory.Stretch(v.frame.rectTransform);
            v.frame.rectTransform.offsetMin = new Vector2(-3, -3); v.frame.rectTransform.offsetMax = new Vector2(3, 3);
            v.frame.transform.SetAsFirstSibling();
            var inner = UIFactory.Image("Inner", bgImg.transform, Palette.WithAlpha(Color.white, 0.04f));
            UIFactory.Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(2, 2); inner.rectTransform.offsetMax = new Vector2(-2, -2);

            if (emptyLabel != null)
            {
                v.placeholder = UIFactory.Text("Empty", bgImg.transform, emptyLabel, 15f, Palette.WithAlpha(Palette.UIBone, 0.28f), TextAlignmentOptions.Center, 3f, 0f);
                UIFactory.Stretch(v.placeholder.rectTransform);
            }

            var iconRt = UIFactory.Rect("Icon", bgImg.transform);
            UIFactory.Stretch(iconRt);
            iconRt.offsetMin = new Vector2(6, 6); iconRt.offsetMax = new Vector2(-6, -6);
            v.icon = iconRt.gameObject.AddComponent<RawImage>();
            v.icon.raycastTarget = false;
            v.icon.enabled = false;

            v.count = UIFactory.Text("Count", bgImg.transform, "", 17f, Palette.UIBone, TextAlignmentOptions.BottomRight, 1f, 0.25f);
            UIFactory.Stretch(v.count.rectTransform);
            v.count.rectTransform.offsetMin = new Vector2(4, 3); v.count.rectTransform.offsetMax = new Vector2(-6, -3);

            v.corner = UIFactory.Text("Corner", bgImg.transform, "", 12f, Palette.AccentHot, TextAlignmentOptions.TopRight, 0f, 0.25f);
            UIFactory.Stretch(v.corner.rectTransform);
            v.corner.rectTransform.offsetMin = new Vector2(4, 3); v.corner.rectTransform.offsetMax = new Vector2(-5, -3);

            if (keyLabel != null)
            {
                v.key = UIFactory.Text("Key", bgImg.transform, keyLabel, 15f, Palette.WithAlpha(Palette.AccentHot, 0.85f), TextAlignmentOptions.TopLeft, 1f, 0.2f);
                UIFactory.Stretch(v.key.rectTransform);
                v.key.rectTransform.offsetMin = new Vector2(6, 3); v.key.rectTransform.offsetMax = new Vector2(-4, -4);
            }
            return v;
        }

        /// <summary>Draw a stack. Corner: a price or tag (stolen goods are tagged automatically).</summary>
        public void Show(ItemStack s, bool selected, bool hovered, string cornerText = null)
        {
            bool has = s != null && !s.IsEmpty;
            bg.color = has ? Filled : Empty;
            icon.enabled = has;
            if (has) icon.texture = PreviewStudio.Instance.Icon(s.def);
            if (placeholder != null) placeholder.enabled = !has;
            count.text = !has ? "" : s.def.Stackable ? s.count.ToString()
                : s.def.weapon != null && s.def.weapon.magazineSize > 0 && s.def.weapon.kind == Combat.WeaponKind.Hitscan ? (s.loaded < 0 ? s.def.weapon.magazineSize : s.loaded).ToString() : "";
            corner.text = !has ? "" : cornerText ?? (s.stolen ? "<color=#E86A5A>STOLEN</color>" : "");
            frame.color = selected ? Palette.Accent : hovered ? Palette.WithAlpha(Palette.AccentHot, 0.6f) : Palette.WithAlpha(Palette.Accent, has ? 0.12f : 0f);
        }

        public void OnPointerEnter(PointerEventData e) => Owner.Hover(this);
        public void OnPointerExit(PointerEventData e) => Owner.Unhover(this);

        public void OnPointerClick(PointerEventData e)
        {
            if (dragged) { dragged = false; return; }
            Owner.Clicked(this, e.button, e.position);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            dragged = Owner.BeginDrag(this, e.position);
        }

        public void OnDrag(PointerEventData e) { if (dragged) Owner.DragTo(e.position); }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragged) return;
            ItemSlotView target = null;
            var go = e.pointerCurrentRaycast.gameObject;
            if (go != null) target = go.GetComponentInParent<ItemSlotView>();
            Owner.EndDrag(this, target, e.position);
            // OnPointerClick may still fire after a drag; swallow it once
            dragged = true;
            Invoke(nameof(ClearDragged), 0.05f);
        }

        void ClearDragged() => dragged = false;
    }
}
