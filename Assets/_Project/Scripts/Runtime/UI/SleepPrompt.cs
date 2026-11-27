using System.Collections;
using Alcax.Audio;
using Alcax.Core;
using Alcax.FX;
using Alcax.Player;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// Sleeping in a bed: pick one of the next two times of day (from Day: Dusk or Night), fade out, wake up then,
    /// rested (PlayerVitals.Sleep). Esc cancels.
    /// </summary>
    public class SleepPrompt : MonoBehaviour
    {
        static SleepPrompt instance;
        public static bool Busy => instance != null && instance.busy;

        CanvasGroup group, fade;
        RectTransform panel;
        TextMeshProUGUI aLabel, bLabel;
        TimePreset a, b;
        bool shown, busy;

        static TimePreset Next(TimePreset p) => p == TimePreset.Day ? TimePreset.Dusk : p == TimePreset.Dusk ? TimePreset.Night : TimePreset.Day;

        public static void Open()
        {
            if (instance == null)
            {
                var go = new GameObject("SleepPrompt");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SleepPrompt>();
                instance.Build();
            }
            instance.Show();
        }

        void OnDestroy() { if (instance == this) instance = null; }

        void Build()
        {
            var canvas = UIFactory.Canvas("SleepCanvas", transform, 12);
            var root = (RectTransform)canvas.transform;
            var pg = UIFactory.Rect("Prompt", root);
            UIFactory.Stretch(pg);
            group = pg.gameObject.AddComponent<CanvasGroup>();
            var scrim = UIFactory.Image("Scrim", pg, Palette.WithAlpha(Palette.UIDark, 0.5f));
            scrim.raycastTarget = true;
            UIFactory.Stretch(scrim.rectTransform);
            var box = UIFactory.Image("Box", pg, Palette.WithAlpha(Palette.UIPanel, 0.97f));
            box.raycastTarget = true;
            panel = box.rectTransform;
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 270f));
            var line = UIFactory.Image("Line", panel, Palette.Accent);
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(440f, 3f), new Vector2(0.5f, 1f));
            var title = UIFactory.Text("Title", panel, "SLEEP UNTIL", 24f, Palette.Accent, TextAlignmentOptions.Center, 5f, 0f);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(400f, 34f), new Vector2(0.5f, 1f));
            var ba = UIFactory.SmallButton(panel, "", () => Sleep(a), new Vector2(360f, 52f), 20f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)ba.transform, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(360f, 52f), new Vector2(0.5f, 1f));
            aLabel = ba.GetComponentInChildren<TextMeshProUGUI>();
            var bb = UIFactory.SmallButton(panel, "", () => Sleep(b), new Vector2(360f, 52f), 20f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)bb.transform, new Vector2(0.5f, 1f), new Vector2(0f, -136f), new Vector2(360f, 52f), new Vector2(0.5f, 1f));
            bLabel = bb.GetComponentInChildren<TextMeshProUGUI>();
            var cancel = UIFactory.SmallButton(panel, "CANCEL", Hide, new Vector2(360f, 44f), 16f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)cancel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(360f, 44f), new Vector2(0.5f, 1f));

            var fg = UIFactory.Image("Fade", root, Color.black);
            UIFactory.Stretch(fg.rectTransform);
            fade = fg.gameObject.AddComponent<CanvasGroup>();
            fade.alpha = 0f; fade.blocksRaycasts = false;
            SetShown(false);
        }

        void Show()
        {
            var gm = GameManager.Instance;
            if (gm == null || busy) return;
            a = Next(TimeOfDay.Current); b = Next(a);
            aLabel.text = a.ToString().ToUpperInvariant();
            bLabel.text = b.ToString().ToUpperInvariant();
            gm.SetState(GameState.Prompt);
            SetShown(true);
        }

        void SetShown(bool on)
        {
            shown = on;
            group.alpha = on ? 1f : 0f;
            group.interactable = group.blocksRaycasts = on;
        }

        void Hide()
        {
            SetShown(false);
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.Prompt) GameManager.Instance.SetState(GameState.Playing);
        }

        void Update()
        {
            if (!shown || busy) return;
            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Prompt) { SetShown(false); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.digit1Key.wasPressedThisFrame) Sleep(a);
            else if (kb != null && kb.digit2Key.wasPressedThisFrame) Sleep(b);
        }

        void Sleep(TimePreset until)
        {
            if (busy) return;
            SetShown(false);
            StartCoroutine(SleepRoutine(until));
        }

        IEnumerator SleepRoutine(TimePreset until)
        {
            busy = true;
            SoundManager.Play2D(Sfx.UIClick, 0.6f, 0.03f);
            yield return Ease.Tween(0.7f, k => fade.alpha = k, Ease.InOutCubic);
            TimeOfDay.Set(until);
            PlayerVitals.Instance?.Sleep();
            yield return new WaitForSecondsRealtime(0.9f);
            if (GameManager.Instance != null) GameManager.Instance.SetState(GameState.Playing);
            yield return Ease.Tween(0.9f, k => fade.alpha = 1f - k, Ease.InOutCubic);
            fade.alpha = 0f;
            busy = false;
        }
    }
}
