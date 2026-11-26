using System;
using Alcax.Audio;
using Alcax.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>Builds palette-styled uGUI elements from code (bold, letter-spaced, outlined text).</summary>
    public static class UIFactory
    {
        public static Canvas Canvas(string name, Transform parent, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Places a rect by anchor (0..1 of its parent), offset in reference pixels, and size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float spacing = 4f, float outline = 0.18f)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = spacing;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (outline > 0f)
            {
                t.outlineWidth = outline;
                t.outlineColor = Palette.UIDark;
            }
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Vector2 size)
        {
            var img = Image("Button_" + label, parent, Palette.Neutral);
            img.raycastTarget = true;
            img.rectTransform.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Palette.UIPanelLight;
            colors.highlightedColor = Palette.Accent;
            colors.selectedColor = Palette.UIPanelLight;
            colors.pressedColor = Palette.AccentHot;
            colors.disabledColor = Palette.UIPanel;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.targetGraphic = img;
            var outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.WithAlpha(Palette.Accent, 0.45f);
            outline.effectDistance = new Vector2(2f, -2f);
            btn.onClick.AddListener(() =>
            {
                SoundManager.Play2D(Sfx.UIClick, 0.8f, 0.03f);
                onClick?.Invoke();
            });

            var txt = Text("Label", img.transform, label.ToUpperInvariant(), 30f, Palette.UIBone, TextAlignmentOptions.Center, 6f, 0.15f);
            Stretch(txt.rectTransform);
            return btn;
        }

        public static Slider Slider(Transform parent, string label, float value, Action<float> onChange, float width = 420f)
        {
            var row = Rect("Slider_" + label, parent);
            row.sizeDelta = new Vector2(width, 54f);

            var txt = Text("Label", row, label.ToUpperInvariant(), 22f, Palette.UIBone, TextAlignmentOptions.Left, 6f, 0.15f);
            Place(txt.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(width, 24f), new Vector2(0f, 1f));

            var track = Rect("Track", row);
            Place(track, new Vector2(0f, 0f), new Vector2(0f, 8f), new Vector2(width, 16f), new Vector2(0f, 0f));
            var bg = track.gameObject.AddComponent<Image>();
            bg.color = Palette.UIPanel;

            var fillArea = Rect("FillArea", track);
            Stretch(fillArea);
            var fill = Image("Fill", fillArea, Palette.Accent);
            Stretch(fill.rectTransform);

            var handleArea = Rect("HandleArea", track);
            Stretch(handleArea);
            var handle = Image("Handle", handleArea, Palette.UIBone);
            handle.rectTransform.sizeDelta = new Vector2(18f, 28f);
            handle.raycastTarget = true;

            var slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            var colors = slider.colors;
            colors.highlightedColor = Palette.AccentHot;
            slider.colors = colors;
            slider.onValueChanged.AddListener(v => onChange?.Invoke(v));
            return slider;
        }

        /// <summary>A labeled checkbox styled from the palette.</summary>
        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange, float width = 560f)
        {
            var row = Rect("Toggle_" + label, parent);
            row.sizeDelta = new Vector2(width, 44f);

            var box = Image("Box", row, Palette.UIPanel);
            box.raycastTarget = true;
            Place(box.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(34f, 34f), new Vector2(0f, 0.5f));
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.WithAlpha(Palette.Accent, 0.6f);
            outline.effectDistance = new Vector2(2f, -2f);

            var check = Image("Check", box.transform, Palette.Accent);
            Place(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));

            var txt = Text("Label", row, label.ToUpperInvariant(), 24f, Palette.UIBone, TextAlignmentOptions.Left, 5f, 0.12f);
            Place(txt.rectTransform, new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(width - 52f, 40f), new Vector2(0f, 0.5f));
            txt.raycastTarget = true;

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = value;
            var colors = toggle.colors;
            colors.normalColor = Palette.Neutral;
            colors.highlightedColor = Palette.AccentHot;
            colors.pressedColor = Palette.Accent;
            colors.selectedColor = Palette.Neutral;
            toggle.colors = colors;
            toggle.onValueChanged.AddListener(v =>
            {
                SoundManager.Play2D(Sfx.UIClick, 0.8f, 0.03f);
                onChange?.Invoke(v);
            });
            return toggle;
        }

        /// <summary>A soft radial texture (alpha rises toward the edges), for damage and vignette overlays.</summary>
        public static Sprite RadialEdgeSprite(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, d));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
