using System;
using System.Collections.Generic;
using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// The item context menu and the Details popup, shared by the inventory tab, the loot window, and the trade
    /// window. One menu at a time; clicking anywhere else closes it.
    /// </summary>
    public static class ItemMenus
    {
        static RectTransform menu, blocker, details;

        public static bool IsOpen => menu != null;

        /// <summary>A menu of actions at a screen point, kept inside the window.</summary>
        public static void Open(RectTransform window, Vector2 screen, string title, List<(string label, Action act)> options)
        {
            Close();
            if (window == null || options == null || options.Count == 0) return;
            var b = UIFactory.Image("MenuBlocker", window, new Color(0f, 0f, 0f, 0.001f));
            b.raycastTarget = true;
            UIFactory.Stretch(b.rectTransform);
            b.gameObject.AddComponent<Button>().onClick.AddListener(Close);
            blocker = b.rectTransform;

            float w = 240f, h = 38f;
            var bg = UIFactory.Image("ItemMenu", window, Palette.WithAlpha(Palette.UIDark, 0.96f));
            menu = bg.rectTransform;
            menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 0.5f);
            menu.pivot = new Vector2(0f, 1f);
            menu.sizeDelta = new Vector2(w + 8f, options.Count * (h + 2f) + 36f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(window, screen, null, out var local);
            var half = window.rect.size * 0.5f;
            local.x = Mathf.Min(local.x + 6f, half.x - menu.sizeDelta.x - 8f);
            local.y = Mathf.Max(local.y, -half.y + menu.sizeDelta.y + 8f);
            menu.anchoredPosition = local;
            var t = UIFactory.Text("Title", menu, title.ToUpperInvariant(), 14f, Palette.Accent, TextAlignmentOptions.MidlineLeft, 2f, 0f);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(14f, -6f), new Vector2(w - 10f, 26f), new Vector2(0f, 1f));
            for (int i = 0; i < options.Count; i++)
            {
                var (label, act) = options[i];
                var btn = UIFactory.SmallButton(menu, label, () => { Close(); act?.Invoke(); }, new Vector2(w, h), 18f);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f), new Vector2(4f, -32f - i * (h + 2f)), new Vector2(w, h), new Vector2(0f, 1f));
            }
        }

        public static void Close()
        {
            if (menu != null) UnityEngine.Object.Destroy(menu.gameObject);
            if (blocker != null) UnityEngine.Object.Destroy(blocker.gameObject);
            menu = null; blocker = null;
        }

        /// <summary>The full description of an item, over the window.</summary>
        public static void Details(RectTransform window, ItemStack s)
        {
            CloseDetails();
            if (window == null || s == null) return;
            var scrim = UIFactory.Image("DetailsScrim", window, new Color(0f, 0f, 0f, 0.55f));
            scrim.raycastTarget = true;
            UIFactory.Stretch(scrim.rectTransform);
            scrim.gameObject.AddComponent<Button>().onClick.AddListener(CloseDetails);
            details = scrim.rectTransform;
            var box = UIFactory.Image("Details", details, Palette.UIPanel);
            box.raycastTarget = true;
            UIFactory.Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 440f));
            var o = box.gameObject.AddComponent<Outline>();
            o.effectColor = Palette.WithAlpha(Palette.Accent, 0.4f); o.effectDistance = new Vector2(2f, -2f);
            var iconBg = UIFactory.Image("IconBg", box.transform, Palette.WithAlpha(Palette.UIDark, 0.8f));
            UIFactory.Place(iconBg.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(240f, 240f), new Vector2(0f, 1f));
            var icon = UIFactory.Rect("Icon", iconBg.transform);
            UIFactory.Stretch(icon);
            icon.gameObject.AddComponent<RawImage>().texture = PreviewStudio.Instance.Icon(s.def);
            var name = UIFactory.Text("Name", box.transform, s.def.displayName.ToUpperInvariant(), 34f, Palette.Accent, TextAlignmentOptions.TopLeft, 4f, 0f);
            UIFactory.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(300f, -30f), new Vector2(490f, 44f), new Vector2(0f, 1f));
            string catLine = s.def.CategoryLabel + (s.count > 1 ? $"   ·   x{s.count}" : "") + $"   ·   VALUE {s.def.value} CR" + (s.stolen ? "   ·   <color=#E86A5A>STOLEN</color>" : "");
            var cat = UIFactory.Text("Cat", box.transform, catLine, 15f, Palette.WithAlpha(Palette.UIBone, 0.6f), TextAlignmentOptions.TopLeft, 3f, 0f);
            UIFactory.Place(cat.rectTransform, new Vector2(0f, 1f), new Vector2(300f, -80f), new Vector2(490f, 22f), new Vector2(0f, 1f));
            var desc = UIFactory.Body("Desc", box.transform, s.def.description, 20f, Palette.UIBone);
            UIFactory.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(300f, -116f), new Vector2(490f, 150f), new Vector2(0f, 1f));
            string stats = s.def.StatLines();
            var inv = PlayerInventory.Instance;
            if (s.def.weapon != null && s.def.weapon.ammoType != Combat.AmmoType.None)
                stats += $"\nLoaded {(s.loaded < 0 ? s.def.weapon.magazineSize : s.loaded)} / {s.def.weapon.magazineSize}" + (inv != null ? $"   ·   Carried ammo {inv.CountAmmo(s.def.weapon.ammoType)}" : "");
            var st = UIFactory.Body("Stats", box.transform, stats, 17f, Palette.AccentHot);
            UIFactory.Place(st.rectTransform, new Vector2(0f, 1f), new Vector2(300f, -280f), new Vector2(490f, 70f), new Vector2(0f, 1f));
            var close = UIFactory.SmallButton(box.transform, "Close", CloseDetails, new Vector2(160f, 44f), 18f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(160f, 44f), new Vector2(1f, 0f));
        }

        public static void CloseDetails()
        {
            if (details != null) UnityEngine.Object.Destroy(details.gameObject);
            details = null;
        }

        public static void CloseAll() { Close(); CloseDetails(); }
    }
}
