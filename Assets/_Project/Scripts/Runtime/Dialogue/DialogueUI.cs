using System.Collections;
using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Core;
using Alcax.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Alcax.Dialogue
{
    /// <summary>
    /// The conversation panel: speaker header, a typed-out line, a divider, then roomy choice rows.
    /// One choice is always selected (the first by default). Select by hovering, W/S, or the arrow keys,
    /// and confirm with Enter, Space, E, or a click. Keys 1 to 5 pick directly. Already-asked topics are dimmed.
    /// The game enters GameState.Dialogue while it's open.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        public static DialogueUI Instance { get; private set; }
        /// <summary>A conversation started (quests and the codex listen).</summary>
        public static event System.Action<Npc> Opened;
        /// <summary>A choice led to an action node ("@trade", "@fine_pay"...): the conversation closes and listeners act.</summary>
        public static event System.Action<string, Npc> ActionPicked;

        [SerializeField] float charsPerSecond = 48f;

        const float PanelWidth = 1280f, RowHeight = 50f, RowSpacing = 8f;

        class Row
        {
            public RectTransform Rect;
            public Image Bg, Accent, Badge;
            public TextMeshProUGUI Label, BadgeText;
            public CanvasGroup Group;
            public bool Visited;
            public float Highlight;
            public System.Action OnPick;
        }

        CanvasGroup group;
        RectTransform panel, choiceRoot;
        TextMeshProUGUI speaker, role, line, hint;
        Image divider;
        readonly List<Row> rows = new();
        int selected;
        Npc npc;
        DialogueTree tree;
        DialogueTree.Node node;
        Coroutine typing;
        bool typingDone;
        string fullLine;
        readonly HashSet<string> visited = new();

        public bool IsOpen => npc != null;

        void Awake()
        {
            Instance = this;
            Build();
        }

        void Start()
        {
            if (GameManager.Instance != null) GameManager.Instance.StateChanged += OnState;
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= OnState;
        }

        void OnState(GameState prev, GameState next)
        {
            if (prev == GameState.Dialogue && next != GameState.Dialogue && IsOpen) CloseInternal();
        }

        // ---------- Layout ----------

        void Build()
        {
            var canvas = UIFactory.Canvas("DialogueCanvas", transform, 5);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;

            var bg = UIFactory.Image("Panel", canvas.transform, Palette.WithAlpha(Palette.UIDark, 0.9f));
            bg.raycastTarget = true;
            panel = bg.rectTransform;
            UIFactory.Place(panel, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(PanelWidth, 400f), new Vector2(0.5f, 0f));
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = Palette.WithAlpha(Palette.StTechCyan, 0.35f);
            outline.effectDistance = new Vector2(2f, -2f);
            var edge = UIFactory.Image("Edge", panel, Palette.StTechCyan);
            edge.rectTransform.anchorMin = new Vector2(0f, 0f);
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            edge.rectTransform.sizeDelta = new Vector2(6f, 0f);
            edge.rectTransform.anchoredPosition = Vector2.zero;

            speaker = UIFactory.Text("Speaker", panel, "", 32f, Palette.StTechCyan, TextAlignmentOptions.TopLeft, 6f, 0.15f);
            UIFactory.Place(speaker.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -26f), new Vector2(800f, 40f), new Vector2(0f, 1f));
            role = UIFactory.Text("Role", panel, "", 17f, Palette.WithAlpha(Palette.UIBone, 0.5f), TextAlignmentOptions.TopLeft, 6f, 0.1f);
            UIFactory.Place(role.rectTransform, new Vector2(0f, 1f), new Vector2(46f, -64f), new Vector2(800f, 24f), new Vector2(0f, 1f));
            hint = UIFactory.Text("Hint", panel, "W / S  SELECT     ENTER  CONFIRM     SPACE  SKIP     ESC  LEAVE", 15f,
                Palette.WithAlpha(Palette.UIBone, 0.38f), TextAlignmentOptions.TopRight, 5f, 0f);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(-32f, -32f), new Vector2(700f, 22f), new Vector2(1f, 1f));

            line = UIFactory.Text("Line", panel, "", 27f, Palette.UIBone, TextAlignmentOptions.TopLeft, 1f, 0.1f);
            line.fontStyle = FontStyles.Normal;
            line.lineSpacing = 8f;
            line.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -104f), new Vector2(PanelWidth - 88f, 100f), new Vector2(0f, 1f));

            divider = UIFactory.Image("Divider", panel, Palette.WithAlpha(Palette.StTechCyan, 0.25f));
            UIFactory.Place(divider.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -220f), new Vector2(PanelWidth - 88f, 2f), new Vector2(0f, 1f));

            choiceRoot = UIFactory.Rect("Choices", panel);
            UIFactory.Place(choiceRoot, new Vector2(0f, 1f), new Vector2(36f, -238f), new Vector2(PanelWidth - 72f, 300f), new Vector2(0f, 1f));
        }

        /// <summary>Fit the panel and divider to the line's height and the number of choices.</summary>
        void Relayout(int choiceCount)
        {
            line.ForceMeshUpdate();
            float lineH = Mathf.Max(40f, line.GetPreferredValues(fullLine ?? "", PanelWidth - 88f, 0f).y);
            line.rectTransform.sizeDelta = new Vector2(PanelWidth - 88f, lineH);
            float dividerY = 104f + lineH + 22f;
            divider.rectTransform.anchoredPosition = new Vector2(44f, -dividerY);
            choiceRoot.anchoredPosition = new Vector2(36f, -(dividerY + 18f));
            float choicesH = choiceCount * RowHeight + Mathf.Max(0, choiceCount - 1) * RowSpacing;
            panel.sizeDelta = new Vector2(PanelWidth, dividerY + 18f + choicesH + 30f);
        }

        // ---------- Flow ----------

        public void Open(Npc who)
        {
            if (who == null || who.dialogue == null || IsOpen) return;
            npc = who;
            npc.InConversation = true;
            tree = who.dialogue;
            visited.Clear();
            speaker.text = who.displayName.ToUpperInvariant();
            role.text = who.role.ToUpperInvariant();
            GameManager.Instance.SetState(GameState.Dialogue);
            Opened?.Invoke(who);
            group.interactable = group.blocksRaycasts = true;
            StartCoroutine(Ease.Tween(0.2f, t => group.alpha = t, Ease.OutCubic));
            Show(tree.startNode);
        }

        void Show(string id)
        {
            node = string.IsNullOrEmpty(id) ? null : tree.Get(id);
            if (node == null) { Close(); return; }
            visited.Add(node.id);
            ClearRows();
            if (typing != null) StopCoroutine(typing);
            fullLine = node.line;
            Relayout(node.choices.Count == 0 ? 1 : node.choices.Count);
            typing = StartCoroutine(TypeLine(node.line));
        }

        IEnumerator TypeLine(string text)
        {
            typingDone = false;
            npc.Speaking = true;
            line.text = text;
            line.maxVisibleCharacters = 0;
            float shown = 0f;
            int lastBlip = 0;
            while (shown < text.Length)
            {
                shown += Time.unscaledDeltaTime * charsPerSecond;
                int n = Mathf.Min(text.Length, Mathf.FloorToInt(shown));
                line.maxVisibleCharacters = n;
                if (n - lastBlip >= 3) { lastBlip = n; SoundManager.Play2D(Sfx.TalkBlip, 0.25f, 0.12f); }
                yield return null;
            }
            FinishTyping();
        }

        void FinishTyping()
        {
            if (typingDone) return;
            if (typing != null) StopCoroutine(typing);
            typing = null;
            typingDone = true;
            line.maxVisibleCharacters = fullLine.Length;
            if (npc != null) npc.Speaking = false;
            BuildRows();
        }

        void BuildRows()
        {
            if (node.choices.Count == 0)
                AddRow("Leave", false, Close);
            else
                for (int i = 0; i < node.choices.Count; i++)
                {
                    var c = node.choices[i];
                    int index = i;
                    bool seen = !string.IsNullOrEmpty(c.next) && c.next != tree.startNode && visited.Contains(c.next);
                    AddRow(c.text, seen, () => Pick(index));
                }
            selected = 0;
            StartCoroutine(RevealRows());
        }

        IEnumerator RevealRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                StartCoroutine(Ease.Tween(0.18f, t => { if (r.Group != null) { r.Group.alpha = t; r.Rect.anchoredPosition = new Vector2(Mathf.Lerp(-16f, 0f, t), r.Rect.anchoredPosition.y); } }, Ease.OutCubic));
                yield return new WaitForSecondsRealtime(0.05f);
            }
        }

        void AddRow(string text, bool seen, System.Action onPick)
        {
            int index = rows.Count;
            var rt = UIFactory.Rect("Choice" + index, choiceRoot);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(PanelWidth - 72f, RowHeight);
            rt.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            var bg = UIFactory.Image("Bg", rt, Palette.WithAlpha(Palette.StTechCyan, 0f));
            UIFactory.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            var accent = UIFactory.Image("Accent", rt, Palette.StTechCyan);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.sizeDelta = new Vector2(4f, 0f);
            accent.rectTransform.anchoredPosition = Vector2.zero;

            var badge = UIFactory.Image("Badge", rt, Palette.WithAlpha(Palette.UIBone, 0.1f));
            UIFactory.Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(32f, 32f), new Vector2(0f, 0.5f));
            var badgeText = UIFactory.Text("Key", badge.transform, (index + 1).ToString(), 18f, Palette.UIBone, TextAlignmentOptions.Center, 0f, 0f);
            UIFactory.Stretch(badgeText.rectTransform);

            var label = UIFactory.Text("Label", rt, text, 24f, Palette.UIBone, TextAlignmentOptions.Left, 1.5f, 0.08f);
            label.fontStyle = FontStyles.Normal;
            UIFactory.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(68f, 0f);
            label.rectTransform.offsetMax = new Vector2(-16f, 0f);

            var row = new Row { Rect = rt, Bg = bg, Accent = accent, Badge = badge, Label = label, BadgeText = badgeText, Group = cg, Visited = seen, OnPick = onPick };
            rows.Add(row);

            // Mouse: hover selects, click confirms
            var trigger = bg.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Select(index, playSound: true));
            trigger.triggers.Add(enter);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ => Confirm(index));
            trigger.triggers.Add(click);
        }

        void ClearRows()
        {
            foreach (var r in rows) if (r.Rect != null) Destroy(r.Rect.gameObject);
            rows.Clear();
        }

        void Select(int index, bool playSound)
        {
            if (index < 0 || index >= rows.Count || index == selected) return;
            selected = index;
            if (playSound) SoundManager.Play2D(Sfx.UIClick, 0.35f, 0.05f);
        }

        void Confirm(int index)
        {
            if (!typingDone || index < 0 || index >= rows.Count) return;
            SoundManager.Play2D(Sfx.UIClick, 0.8f, 0.03f);
            rows[index].OnPick?.Invoke();
        }

        void Pick(int index)
        {
            if (node == null || index >= node.choices.Count) return;
            var next = node.choices[index].next;
            if (!string.IsNullOrEmpty(next) && next.StartsWith("@"))
            {
                var who = npc;
                Close();
                ActionPicked?.Invoke(next, who);
                return;
            }
            // The NPC starts answering the chosen line
            if (!string.IsNullOrEmpty(node.choices[index].next) && tree.Get(node.choices[index].next) != null) SoundManager.Play2D(Sfx.DialogueReply, 0.8f, 0.03f);
            Show(node.choices[index].next);
        }

        // ---------- Per frame ----------

        void Update()
        {
            if (!IsOpen) return;
            AnimateRows();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;
            if (!typingDone)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                    FinishTyping();
                return;
            }

            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) Select((selected + 1) % rows.Count, true);
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) Select((selected - 1 + rows.Count) % rows.Count, true);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                Confirm(selected);
                return;
            }
            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int i = 0; i < keys.Length && i < rows.Count; i++)
                if (kb[keys[i]].wasPressedThisFrame) { selected = i; Confirm(i); return; }
        }

        void AnimateRows()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Rect == null) continue;
                float target = i == selected ? 1f : 0f;
                r.Highlight = Mathf.MoveTowards(r.Highlight, target, dt / 0.12f);
                float h = Ease.OutCubic(r.Highlight);
                r.Bg.color = Palette.WithAlpha(Palette.StTechCyan, 0.16f * h);
                r.Accent.color = Palette.WithAlpha(Palette.StTechCyan, h);
                r.Badge.color = Color.Lerp(Palette.WithAlpha(Palette.UIBone, 0.1f), Palette.StTechCyan, h);
                r.BadgeText.color = Color.Lerp(Palette.UIBone, Palette.UIDark, h);
                var baseText = r.Visited ? Palette.WithAlpha(Palette.UIBone, 0.45f) : Palette.UIBone;
                r.Label.color = Color.Lerp(baseText, Palette.StPanelLight, h);
                r.Label.rectTransform.offsetMin = new Vector2(68f + 8f * h, 0f);
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.Dialogue)
                GameManager.Instance.SetState(GameState.Playing);   // OnState closes the panel
            else CloseInternal();
        }

        void CloseInternal()
        {
            if (typing != null) StopCoroutine(typing);
            typing = null;
            if (npc != null) { npc.Speaking = false; npc.InConversation = false; }
            npc = null;
            node = null;
            ClearRows();
            group.interactable = group.blocksRaycasts = false;
            StartCoroutine(Ease.Tween(0.15f, t => group.alpha = 1f - t, Ease.OutCubic));
        }
    }
}
