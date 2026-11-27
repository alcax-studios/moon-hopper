using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>The Codex tab: discovered entries grouped by category on the left, the selected entry on the right.</summary>
    public class CodexPanel : MonoBehaviour
    {
        RectTransform root, list;
        TextMeshProUGUI title, cat, body, count;
        RawImage icon;
        CodexEntry selected;
        string category = "Station";
        bool dirty = true;

        public void Build(RectTransform parent)
        {
            root = parent;
            // Category tabs
            for (int i = 0; i < Codex.Categories.Length; i++)
            {
                string c = Codex.Categories[i];
                var b = UIFactory.SmallButton(root, c.ToUpperInvariant(), () => { category = c; selected = null; dirty = true; }, new Vector2(112f, 40f), 15f, TextAlignmentOptions.Center);
                UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(40f + i * 120f, -8f), new Vector2(112f, 40f), new Vector2(0f, 1f));
                b.name = "Cat_" + c;
            }
            list = UIFactory.Rect("List", root);
            UIFactory.Place(list, new Vector2(0f, 1f), new Vector2(40f, -64f), new Vector2(480f, 640f), new Vector2(0f, 1f));
            count = UIFactory.Text("Count", root, "", 14f, Palette.WithAlpha(Palette.UIBone, 0.45f), TextAlignmentOptions.BottomLeft, 2f, 0f);
            UIFactory.Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -700f), new Vector2(480f, 24f), new Vector2(0f, 1f));

            var detail = UIFactory.Image("Detail", root, Palette.WithAlpha(Palette.UIDark, 0.6f));
            UIFactory.Place(detail.rectTransform, new Vector2(0f, 1f), new Vector2(560f, -8f), new Vector2(1000f, 720f), new Vector2(0f, 1f));
            var d = detail.transform;
            var iconRt = UIFactory.Rect("Icon", d);
            UIFactory.Place(iconRt, new Vector2(1f, 1f), new Vector2(-30f, -24f), new Vector2(170f, 170f), new Vector2(1f, 1f));
            icon = iconRt.gameObject.AddComponent<RawImage>();
            icon.raycastTarget = false;
            title = UIFactory.Text("Title", d, "", 34f, Palette.Accent, TextAlignmentOptions.TopLeft, 4f, 0f);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -30f), new Vector2(740f, 44f), new Vector2(0f, 1f));
            cat = UIFactory.Text("Cat", d, "", 15f, Palette.WithAlpha(Palette.UIBone, 0.55f), TextAlignmentOptions.TopLeft, 3f, 0f);
            UIFactory.Place(cat.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -80f), new Vector2(740f, 22f), new Vector2(0f, 1f));
            body = UIFactory.Body("Body", d, "", 21f, Palette.UIBone);
            UIFactory.Place(body.rectTransform, new Vector2(0f, 1f), new Vector2(36f, -210f), new Vector2(920f, 480f), new Vector2(0f, 1f));
            Codex.Changed += MarkDirty;
        }

        void MarkDirty() => dirty = true;
        void OnDestroy() => Codex.Changed -= MarkDirty;
        void OnEnable() => dirty = true;

        void Update()
        {
            if (dirty) Rebuild();
        }

        void Rebuild()
        {
            dirty = false;
            foreach (Transform c in list) Destroy(c.gameObject);
            foreach (Transform c in root)
                if (c.name.StartsWith("Cat_")) c.GetComponent<Image>().color = c.name == "Cat_" + category ? Palette.WithAlpha(Palette.Accent, 0.5f) : Color.white;
            var entries = Codex.Unlocked.FindAll(e => e.category == category);
            if (selected == null || selected.category != category) selected = entries.Count > 0 ? entries[0] : null;
            float y = 0f;
            foreach (var e in entries)
            {
                var entry = e;
                var btn = UIFactory.SmallButton(list, e.title.ToUpperInvariant(), () => { selected = entry; dirty = true; }, new Vector2(480f, 46f), 18f);
                UIFactory.Place((RectTransform)btn.transform, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(480f, 46f), new Vector2(0f, 1f));
                if (e == selected) btn.GetComponent<Image>().color = Palette.WithAlpha(Palette.Accent, 0.35f);
                y -= 50f;
                if (y < -600f) break;
            }
            count.text = $"{Codex.Unlocked.Count} ENTRIES DISCOVERED";
            title.text = selected?.title.ToUpperInvariant() ?? "";
            cat.text = selected?.category.ToUpperInvariant() ?? "";
            body.text = selected?.body ?? (entries.Count == 0 ? "<color=#E8E1D080>Nothing here yet. Explore, talk to people, pick things up.</color>" : "");
            body.rectTransform.anchoredPosition = new Vector2(36f, -120f);
            var item = selected != null && selected.id.StartsWith("item:") ? ItemDatabase.Get(selected.id.Substring(5)) : null;
            icon.enabled = item != null;
            if (item != null) icon.texture = PreviewStudio.Instance.Icon(item);
        }
    }
}
