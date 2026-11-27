using Alcax.Audio;
using Alcax.Core;
using Alcax.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Alcax.UI
{
    /// <summary>
    /// The in-game menu (Tab or I): tabs for Inventory, Quests, Codex, and Status, a frame to add more to. The world
    /// keeps running while it's open (GameState.Menu: the cursor is free, the player stops). Tab, I, or Esc closes it;
    /// Q and E switch tabs. Created once at startup and kept across maps.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        public static GameMenu Instance { get; private set; }
        static readonly string[] TabNames = { "INVENTORY", "QUESTS", "CODEX", "STATUS" };

        CanvasGroup group;
        RectTransform window;
        readonly RectTransform[] pages = new RectTransform[4];
        readonly Button[] tabs = new Button[4];
        Image underline;
        InventoryPanel inventory;
        int tab;
        bool shown;

        public bool IsOpen => shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("GameMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMenu>();
        }

        void Awake()
        {
            Instance = this;
            Build();
            SetShown(false);
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureEventSystem();
        }

        // Play mode starts without a domain reload here: static events outlive this object, so unsubscribe
        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; if (Instance == this) Instance = null; }

        void OnSceneLoaded(Scene s, LoadSceneMode m) { SetShown(false); EnsureEventSystem(); }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        void Build()
        {
            var canvas = UIFactory.Canvas("GameMenuCanvas", transform, 8);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            var root = (RectTransform)canvas.transform;

            var scrim = UIFactory.Image("Scrim", root, Palette.WithAlpha(Palette.UIDark, 0.72f));
            scrim.raycastTarget = true;
            UIFactory.Stretch(scrim.rectTransform);

            var win = UIFactory.Image("Window", root, Palette.WithAlpha(Palette.UIPanel, 0.97f));
            win.raycastTarget = true;
            window = win.rectTransform;
            UIFactory.Place(window, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1680f, 900f));
            var o = win.gameObject.AddComponent<Outline>();
            o.effectColor = Palette.WithAlpha(Palette.Accent, 0.3f);
            o.effectDistance = new Vector2(2f, -2f);
            var topLine = UIFactory.Image("TopLine", window, Palette.Accent);
            UIFactory.Place(topLine.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1680f, 3f), new Vector2(0.5f, 1f));

            for (int i = 0; i < TabNames.Length; i++)
            {
                int k = i;
                var b = UIFactory.SmallButton(window, TabNames[i], () => Show(k), new Vector2(220f, 54f), 20f, TextAlignmentOptions.Center);
                UIFactory.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(40f + i * 232f, -26f), new Vector2(220f, 54f), new Vector2(0f, 1f));
                tabs[i] = b;
            }
            underline = UIFactory.Image("Underline", window, Palette.Accent);
            UIFactory.Place(underline.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -82f), new Vector2(220f, 4f), new Vector2(0f, 1f));
            var rule = UIFactory.Image("Rule", window, Palette.WithAlpha(Palette.UIBone, 0.08f));
            UIFactory.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1600f, 1f), new Vector2(0.5f, 1f));

            for (int i = 0; i < pages.Length; i++)
            {
                var p = UIFactory.Rect("Page_" + TabNames[i], window);
                UIFactory.Place(p, new Vector2(0f, 1f), new Vector2(0f, -116f), new Vector2(1680f, 760f), new Vector2(0f, 1f));
                pages[i] = p;
            }
            inventory = pages[0].gameObject.AddComponent<InventoryPanel>();
            inventory.Build(pages[0], window);
            pages[1].gameObject.AddComponent<QuestsPanel>().Build(pages[1]);
            pages[2].gameObject.AddComponent<CodexPanel>().Build(pages[2]);
            pages[3].gameObject.AddComponent<StatusPanel>().Build(pages[3]);
            Show(0);
        }

        public void Show(int i)
        {
            tab = Mathf.Clamp(i, 0, pages.Length - 1);
            for (int k = 0; k < pages.Length; k++) pages[k].gameObject.SetActive(k == tab);
            if (tab != 0) inventory.OnHidden();
            underline.rectTransform.anchoredPosition = new Vector2(40f + tab * 232f, -82f);
            for (int k = 0; k < tabs.Length; k++)
                tabs[k].GetComponentInChildren<TextMeshProUGUI>().color = k == tab ? Palette.AccentHot : Palette.WithAlpha(Palette.UIBone, 0.75f);
        }

        void SetShown(bool on)
        {
            shown = on;
            group.alpha = on ? 1f : 0f;
            group.interactable = group.blocksRaycasts = on;
            canvasActive(on);
            if (!on) inventory.OnHidden();
        }

        void canvasActive(bool on)
        {
            // Pages run their Update only while visible
            for (int k = 0; k < pages.Length; k++) pages[k].gameObject.SetActive(on && k == tab);
        }

        void Update()
        {
            var gm = GameManager.Instance;
            var kb = Keyboard.current;
            if (gm == null || kb == null) return;
            bool toggle = kb.tabKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame;
            if (gm.State == GameState.Playing && toggle && PlayerInventory.Instance != null && !AdminSettings.ConsoleOpen)
            {
                gm.SetState(GameState.Menu);
                SoundManager.Play2D(Sfx.UIClick, 0.6f, 0.03f);
            }
            else if (gm.State == GameState.Menu && toggle) gm.SetState(GameState.Playing);

            bool want = gm.State == GameState.Menu;
            if (want != shown) SetShown(want);
            if (!shown) return;
            if (kb.qKey.wasPressedThisFrame) Show((tab + pages.Length - 1) % pages.Length);
            if (kb.eKey.wasPressedThisFrame) Show((tab + 1) % pages.Length);
        }
    }
}
