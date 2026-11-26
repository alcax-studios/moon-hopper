using System;
using System.Collections;
using Alcax.Audio;
using Alcax.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Alcax.UI
{
    /// <summary>
    /// The game shell: title, pause (with volume settings), and the death screen,
    /// with 0.3 s fades between states. The title's backdrop is the live forest.
    /// </summary>
    public class ShellUI : MonoBehaviour
    {
        [SerializeField] string gameTitle = "";
        [SerializeField] string tagline = "";

        CanvasGroup title, pause, ended, fade;
        TextMeshProUGUI titlePrompt, endedBanner, endedStats;
        Coroutine endedRoutine;

        void Awake()
        {
            EnsureEventSystem();
            Build();
        }

        void OnEnable()
        {
            if (GameManager.Instance != null) Hook();
        }

        void Start()
        {
            Hook();
            // Fade in from dark on every scene load.
            fade.alpha = 1f;
            StartCoroutine(FadeTo(fade, 0f, 0.6f));
        }

        bool hooked;
        void Hook()
        {
            if (hooked || GameManager.Instance == null) return;
            hooked = true;
            GameManager.Instance.StateChanged += OnStateChanged;
            GameManager.Instance.FadeOutThen = FadeOutThen;
            OnStateChanged(GameManager.Instance.State, GameManager.Instance.State);
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= OnStateChanged;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        void Build()
        {
            var canvas = UIFactory.Canvas("ShellCanvas", transform, 10);
            var root = canvas.transform;

            // ---- Title ----
            title = Group("Title", root);
            var titleScrim = UIFactory.Image("Scrim", title.transform, Palette.WithAlpha(Palette.UIDark, 0.35f), UIFactory.RadialEdgeSprite());
            UIFactory.Stretch(titleScrim.rectTransform);
            var nameShadow = UIFactory.Text("NameShadow", title.transform, gameTitle, 190f, Palette.WithAlpha(Palette.Shadow, 0.85f), TextAlignmentOptions.Center, 24f, 0f);
            UIFactory.Place(nameShadow.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(6f, -8f), new Vector2(1800f, 230f));
            var name = UIFactory.Text("Name", title.transform, gameTitle, 190f, Palette.Accent, TextAlignmentOptions.Center, 24f, 0.12f);
            UIFactory.Place(name.rectTransform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1800f, 230f));
            var tag = UIFactory.Text("Tagline", title.transform, tagline, 26f, Palette.WithAlpha(Palette.UIBone, 0.8f), TextAlignmentOptions.Center, 10f);
            UIFactory.Place(tag.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(0f, -135f), new Vector2(1600f, 40f));
            titlePrompt = UIFactory.Text("Prompt", title.transform, "CLICK TO BEGIN", 30f, Palette.UIBone, TextAlignmentOptions.Center, 16f);
            UIFactory.Place(titlePrompt.rectTransform, new Vector2(0.5f, 0.25f), Vector2.zero, new Vector2(1000f, 44f));
            var controls = UIFactory.Text("Controls", title.transform,
                "WASD  MOVE    MOUSE  LOOK    LMB  ATTACK    E  PICK UP    R  RELOAD    1-5  WEAPONS    F  FLASHLIGHT    SHIFT  SPRINT    C  CROUCH    ESC  PAUSE",
                18f, Palette.WithAlpha(Palette.UIBone, 0.55f), TextAlignmentOptions.Center, 4f, 0.1f);
            UIFactory.Place(controls.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1800f, 30f));

            // ---- Pause ----
            pause = Group("Pause", root);
            var pScrim = UIFactory.Image("Scrim", pause.transform, Palette.WithAlpha(Palette.UIDark, 0.72f));
            UIFactory.Stretch(pScrim.rectTransform);
            var pTitle = UIFactory.Text("Title", pause.transform, "PAUSED", 96f, Palette.Accent, TextAlignmentOptions.Center, 18f, 0.2f);
            UIFactory.Place(pTitle.rectTransform, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(1000f, 120f));
            var col = UIFactory.Rect("Column", pause.transform);
            UIFactory.Place(col, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(420f, 520f), new Vector2(0.5f, 1f));
            var layout = col.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            UIFactory.Button(col, "Resume", () => GameManager.Instance.Resume(), new Vector2(420f, 64f));
            UIFactory.Button(col, "Restart", () => GameManager.Instance.Restart(), new Vector2(420f, 64f));
            UIFactory.Button(col, "Settings", OpenSettings, new Vector2(420f, 64f));
            // Map switching: every other map in the list
            string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            foreach (var (mapName, path) in GameManager.Maps)
            {
                if (path == current) continue;
                string p = path;
                UIFactory.Button(col, "Go to: " + mapName, () => GameManager.Instance.LoadMap(p), new Vector2(420f, 64f));
            }
            UIFactory.Button(col, "Quit", () => GameManager.Instance.Quit(), new Vector2(420f, 64f));

            BuildSettings(root);

            // ---- Ended ----
            ended = Group("Ended", root);
            var eScrim = UIFactory.Image("Scrim", ended.transform, Palette.WithAlpha(Palette.UIDark, 0.6f), UIFactory.RadialEdgeSprite());
            UIFactory.Stretch(eScrim.rectTransform);
            var eScrim2 = UIFactory.Image("Scrim2", ended.transform, Palette.WithAlpha(Palette.Blood, 0.25f));
            UIFactory.Stretch(eScrim2.rectTransform);
            endedBanner = UIFactory.Text("Banner", ended.transform, "YOU DIED", 170f, Palette.BloodBright, TextAlignmentOptions.Center, 20f, 0.22f);
            UIFactory.Place(endedBanner.rectTransform, new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(1600f, 200f));
            endedStats = UIFactory.Text("Stats", ended.transform, "", 34f, Palette.UIBone, TextAlignmentOptions.Center, 10f);
            UIFactory.Place(endedStats.rectTransform, new Vector2(0.5f, 0.64f), new Vector2(0f, -150f), new Vector2(1400f, 50f));
            var row = UIFactory.Rect("Buttons", ended.transform);
            UIFactory.Place(row, new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(880f, 64f));
            var h = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            h.spacing = 40f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false;
            h.childControlHeight = false;
            UIFactory.Button(row, "Try again  (R)", () => GameManager.Instance.Restart(), new Vector2(420f, 64f));


            // ---- Fade overlay (on top of everything) ----
            fade = Group("Fade", root);
            var fadeImg = UIFactory.Image("Black", fade.transform, Palette.UIDark);
            UIFactory.Stretch(fadeImg.rectTransform);
            fade.blocksRaycasts = false;
            fade.alpha = 1f;

            title.alpha = pause.alpha = ended.alpha = 0f;
            SetInteractive(title, false);
            SetInteractive(pause, false);
            SetInteractive(ended, false);
        }

        // ---------- Settings (General + Admin tabs) ----------

        CanvasGroup settings, generalTab, adminTab;
        UnityEngine.UI.Image tabUnderline;
        RectTransform tabGeneralBtn, tabAdminBtn;

        void BuildSettings(Transform root)
        {
            settings = Group("Settings", root);
            var scrim = UIFactory.Image("Scrim", settings.transform, Palette.WithAlpha(Palette.UIDark, 0.88f));
            UIFactory.Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;

            var panel = UIFactory.Image("Panel", settings.transform, Palette.UIPanel);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 800f));
            var po = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
            po.effectColor = Palette.WithAlpha(Palette.Accent, 0.35f);
            po.effectDistance = new Vector2(3f, -3f);
            var p = panel.transform;

            var title = UIFactory.Text("Title", p, "SETTINGS", 64f, Palette.Accent, TextAlignmentOptions.Center, 16f, 0.2f);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 80f), new Vector2(0.5f, 1f));

            // Tabs
            tabGeneralBtn = (RectTransform)UIFactory.Button(p, "General", () => ShowTab(false), new Vector2(260f, 56f)).transform;
            UIFactory.Place(tabGeneralBtn, new Vector2(0.5f, 1f), new Vector2(-140f, -130f), new Vector2(260f, 56f), new Vector2(0.5f, 1f));
            tabAdminBtn = (RectTransform)UIFactory.Button(p, "Admin", () => ShowTab(true), new Vector2(260f, 56f)).transform;
            UIFactory.Place(tabAdminBtn, new Vector2(0.5f, 1f), new Vector2(140f, -130f), new Vector2(260f, 56f), new Vector2(0.5f, 1f));
            tabUnderline = UIFactory.Image("TabUnderline", p, Palette.Accent);
            UIFactory.Place(tabUnderline.rectTransform, new Vector2(0.5f, 1f), new Vector2(-140f, -192f), new Vector2(260f, 6f), new Vector2(0.5f, 1f));

            // General tab
            generalTab = Group("GeneralTab", p);
            var gcol = UIFactory.Rect("Column", generalTab.transform);
            UIFactory.Place(gcol, new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(600f, 400f), new Vector2(0.5f, 1f));
            var gl = gcol.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            gl.spacing = 34f; gl.childAlignment = TextAnchor.UpperCenter;
            gl.childControlWidth = false; gl.childControlHeight = false; gl.childForceExpandHeight = false;
            UIFactory.Slider(gcol, "Music volume", SoundManager.MusicGain, SoundManager.SetMusic, 600f);
            UIFactory.Slider(gcol, "Effects volume", SoundManager.SfxGain, SoundManager.SetSfx, 600f);
            UIFactory.Slider(gcol, "Mouse sensitivity", (GameSettings.LookSensitivity - 0.25f) / 2.75f,
                v => GameSettings.LookSensitivity = 0.25f + v * 2.75f, 600f);

            // Admin tab
            adminTab = Group("AdminTab", p);
            var note = UIFactory.Text("Note", adminTab.transform, "TESTING CHEATS. EVERYTHING STARTS OFF.", 20f,
                Palette.WithAlpha(Palette.UIBone, 0.6f), TextAlignmentOptions.Center, 6f, 0.1f);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -222f), new Vector2(1000f, 30f), new Vector2(0.5f, 1f));

            var left = UIFactory.Rect("Toggles", adminTab.transform);
            UIFactory.Place(left, new Vector2(0.5f, 1f), new Vector2(-250f, -270f), new Vector2(560f, 420f), new Vector2(0.5f, 1f));
            var ll = left.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            ll.spacing = 12f; ll.childAlignment = TextAnchor.UpperLeft;
            ll.childControlWidth = false; ll.childControlHeight = false; ll.childForceExpandHeight = false;
            UIFactory.Toggle(left, "God mode (no damage)", AdminSettings.GodMode, v => AdminSettings.GodMode = v);
            UIFactory.Toggle(left, "Infinite ammo", AdminSettings.InfiniteAmmo, v => AdminSettings.InfiniteAmmo = v);
            UIFactory.Toggle(left, "All weapons", AdminSettings.AllWeapons, AdminSettings.SetAllWeapons);
            UIFactory.Toggle(left, "One-hit kills", AdminSettings.OneHitKills, v => AdminSettings.OneHitKills = v);

            var right = UIFactory.Rect("Actions", adminTab.transform);
            UIFactory.Place(right, new Vector2(0.5f, 1f), new Vector2(330f, -270f), new Vector2(380f, 420f), new Vector2(0.5f, 1f));
            var rl = right.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            rl.spacing = 16f; rl.childAlignment = TextAnchor.UpperCenter;
            rl.childControlWidth = false; rl.childControlHeight = false; rl.childForceExpandHeight = false;
            UIFactory.Button(right, "Full heal", AdminSettings.RequestFullHeal, new Vector2(380f, 58f));
            UIFactory.Button(right, "+50 ammo", AdminSettings.RequestAmmo, new Vector2(380f, 58f));

            var back = UIFactory.Button(p, "Back", CloseSettings, new Vector2(320f, 60f));
            UIFactory.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(320f, 60f), new Vector2(0.5f, 0f));

            settings.alpha = 0f;
            SetInteractive(settings, false);
            ShowTab(false);
        }

        void ShowTab(bool admin)
        {
            generalTab.alpha = admin ? 0f : 1f;
            adminTab.alpha = admin ? 1f : 0f;
            SetInteractive(generalTab, !admin);
            SetInteractive(adminTab, admin);
            var x = (admin ? tabAdminBtn : tabGeneralBtn).anchoredPosition.x;
            tabUnderline.rectTransform.anchoredPosition = new Vector2(x, tabUnderline.rectTransform.anchoredPosition.y);
        }

        void OpenSettings()
        {
            ShowTab(false);
            Show(settings, true);
        }

        void CloseSettings() => Show(settings, false);

        static CanvasGroup Group(string name, Transform parent)
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(name, parent));
            return rt.gameObject.AddComponent<CanvasGroup>();
        }

        static void SetInteractive(CanvasGroup g, bool on)
        {
            g.interactable = on;
            g.blocksRaycasts = on;
        }

        void Update()
        {
            if (title.alpha > 0f)
                titlePrompt.alpha = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.6f);
        }

        void OnStateChanged(GameState prev, GameState next)
        {
            Show(title, next == GameState.Title);
            Show(pause, next == GameState.Paused);
            Show(ended, next == GameState.Ended);
            if (settings != null && next != GameState.Paused && settings.alpha > 0f) Show(settings, false);
            if (next == GameState.Ended)
            {
                if (endedRoutine != null) StopCoroutine(endedRoutine);
                endedRoutine = StartCoroutine(EndedMoment());
            }
        }

        readonly System.Collections.Generic.Dictionary<CanvasGroup, Coroutine> fades = new();

        void Show(CanvasGroup g, bool on)
        {
            SetInteractive(g, on);
            // One fade per panel: a new one cancels the old (otherwise a fade-in started on the first frame of a
            // map load could outlive the fade-out and leave the title stuck on screen)
            if (fades.TryGetValue(g, out var running) && running != null) StopCoroutine(running);
            fades[g] = StartCoroutine(FadeTo(g, on ? 1f : 0f, 0.3f));
        }

        static IEnumerator FadeTo(CanvasGroup g, float target, float duration)
        {
            float start = g.alpha;
            if (Mathf.Approximately(start, target)) yield break;
            yield return Ease.Tween(duration, t => g.alpha = Mathf.Lerp(start, target, t), Ease.OutCubic);
        }

        IEnumerator EndedMoment()
        {
            var gm = GameManager.Instance;
            endedStats.text = "";
            endedBanner.rectTransform.localScale = Vector3.zero;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ease.Tween(0.45f, t => endedBanner.rectTransform.localScale = Vector3.one * t, Ease.OutBack);

            int kills = gm.Kills;
            float time = gm.TimeSurvived;
            yield return Ease.Tween(0.8f, t =>
            {
                int s = Mathf.FloorToInt(time * t);
                endedStats.text = $"KILLS {Mathf.RoundToInt(kills * t)}     SURVIVED {s / 60}:{s % 60:00}";
            }, Ease.OutCubic);
            endedRoutine = null;
        }

        void FadeOutThen(Action action)
        {
            StartCoroutine(FadeOutRoutine(action));
        }

        IEnumerator FadeOutRoutine(Action action)
        {
            fade.blocksRaycasts = true;
            yield return FadeTo(fade, 1f, 0.3f);
            action?.Invoke();
        }
    }
}
