using System.Collections.Generic;
using Alcax.Audio;
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
    /// The two-sided window for containers and traders. Left: the container (or the trader's goods, with prices).
    /// Right: your backpack and hotbar. Loot: double-click or drag to move items either way, Take All (R) takes
    /// everything and the cash. Trade: click an item to see its price, Buy or Sell it (one, or the whole stack);
    /// double-click buys or sells one, Shift+double-click the stack; dragging across does the same. E, Tab, or Esc close.
    /// </summary>
    public class TransferWindow : MonoBehaviour, ISlotPanel
    {
        enum Mode { Loot, Trade }
        const float Slot = 80f, Gap = 6f;
        const int LeftCols = 6, LeftRows = 4;

        static TransferWindow instance;
        Mode mode;
        LootContainer container;
        Trader trader;

        CanvasGroup group;
        RectTransform window, leftRoot, rightRoot;
        readonly List<ItemSlotView> leftSlots = new List<ItemSlotView>(), rightSlots = new List<ItemSlotView>();
        TextMeshProUGUI title, subtitle, youLabel, info, message, hint;
        Button takeAll, buyOne, buyAll;
        TextMeshProUGUI takeAllLabel;
        Image infoBox;
        float nextWatch;
        string watcher;
        TextMeshProUGUI buyOneLabel, buyAllLabel;
        RawImage ghost;
        ItemSlotView hovered, selected, lastClickSlot;
        float lastClick, messageUntil;
        bool shown;
        float openedAt;   // the E press that opened it must not also close it

        PlayerInventory Inv => PlayerInventory.Instance;

        public static void OpenLoot(LootContainer c) { Ensure(); instance.Open(Mode.Loot, c, null); }
        public static void OpenTrade(Trader t) { Ensure(); instance.Open(Mode.Trade, null, t); }

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("TransferWindow");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TransferWindow>();
            instance.Build();
            instance.SetShown(false);
        }

        void OnDestroy() { if (instance == this) instance = null; }

        void Open(Mode m, LootContainer c, Trader t)
        {
            var gm = GameManager.Instance;
            if (gm == null || Inv == null) return;
            mode = m; container = c; trader = t; selected = null; hovered = null;
            gm.SetState(GameState.Transfer);
            SetShown(true);
            openedAt = Time.unscaledTime;
            SoundManager.Play2D(Sfx.UIClick, 0.6f, 0.03f);
        }

        void SetShown(bool on)
        {
            shown = on;
            group.alpha = on ? 1f : 0f;
            group.interactable = group.blocksRaycasts = on;
            if (!on) { ghost.enabled = false; container = null; trader = null; ItemMenus.CloseAll(); }
        }

        // ---------- Build ----------

        void Build()
        {
            var canvas = UIFactory.Canvas("TransferCanvas", transform, 9);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            var root = (RectTransform)canvas.transform;
            var scrim = UIFactory.Image("Scrim", root, Palette.WithAlpha(Palette.UIDark, 0.6f));
            scrim.raycastTarget = true;
            UIFactory.Stretch(scrim.rectTransform);
            var win = UIFactory.Image("Window", root, Palette.WithAlpha(Palette.UIPanel, 0.97f));
            win.raycastTarget = true;
            window = win.rectTransform;
            UIFactory.Place(window, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 820f));
            var line = UIFactory.Image("TopLine", window, Palette.Accent);
            UIFactory.Place(line.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1400f, 3f), new Vector2(0.5f, 1f));

            title = UIFactory.Text("Title", window, "", 28f, Palette.Accent, TextAlignmentOptions.MidlineLeft, 4f, 0f);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -22f), new Vector2(560f, 40f), new Vector2(0f, 1f));
            subtitle = UIFactory.Text("Sub", window, "", 15f, Palette.WithAlpha(Palette.UIBone, 0.65f), TextAlignmentOptions.MidlineLeft, 3f, 0f);
            UIFactory.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -62f), new Vector2(560f, 24f), new Vector2(0f, 1f));
            youLabel = UIFactory.Text("You", window, "", 18f, Palette.UIBone, TextAlignmentOptions.MidlineLeft, 4f, 0f);
            UIFactory.Place(youLabel.rectTransform, new Vector2(0f, 1f), new Vector2(660f, -30f), new Vector2(700f, 30f), new Vector2(0f, 1f));

            leftRoot = UIFactory.Rect("Left", window);
            UIFactory.Place(leftRoot, new Vector2(0f, 1f), new Vector2(40f, -100f), new Vector2(LeftCols * (Slot + Gap), LeftRows * (Slot + Gap)), new Vector2(0f, 1f));
            for (int i = 0; i < LeftCols * LeftRows; i++)
            {
                var v = ItemSlotView.Create(leftRoot, this, new SlotRef(SlotArea.Container, i), Slot);
                UIFactory.Place((RectTransform)v.transform, new Vector2(0f, 1f), new Vector2((i % LeftCols) * (Slot + Gap), -(i / LeftCols) * (Slot + Gap)), new Vector2(Slot, Slot), new Vector2(0f, 1f));
                leftSlots.Add(v);
            }

            rightRoot = UIFactory.Rect("Right", window);
            UIFactory.Place(rightRoot, new Vector2(0f, 1f), new Vector2(660f, -70f), new Vector2(700f, 560f), new Vector2(0f, 1f));
            for (int i = 0; i < PlayerInventory.GridSize; i++)
            {
                var v = ItemSlotView.Create(rightRoot, this, new SlotRef(SlotArea.Grid, i), Slot);
                UIFactory.Place((RectTransform)v.transform, new Vector2(0f, 1f), new Vector2((i % PlayerInventory.GridW) * (Slot + Gap), -30f - (i / PlayerInventory.GridW) * (Slot + Gap)), new Vector2(Slot, Slot), new Vector2(0f, 1f));
                rightSlots.Add(v);
            }
            float hbY = -30f - PlayerInventory.GridH * (Slot + Gap) - 16f;
            for (int i = 0; i < PlayerInventory.HotbarSize; i++)
            {
                var v = ItemSlotView.Create(rightRoot, this, new SlotRef(SlotArea.Hotbar, i), Slot, (i + 1).ToString());
                UIFactory.Place((RectTransform)v.transform, new Vector2(0f, 1f), new Vector2(i * (Slot + Gap), hbY), new Vector2(Slot, Slot), new Vector2(0f, 1f));
                rightSlots.Add(v);
            }

            // Info box (left, under the container) and actions
            infoBox = UIFactory.Image("Info", window, Palette.WithAlpha(Palette.UIDark, 0.7f));
            UIFactory.Place(infoBox.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -460f), new Vector2(510f, 200f), new Vector2(0f, 1f));
            info = UIFactory.Body("InfoText", infoBox.transform, "", 17f, Palette.UIBone);
            UIFactory.Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(16f, -12f), new Vector2(478f, 176f), new Vector2(0f, 1f));

            takeAll = UIFactory.SmallButton(window, "TAKE ALL   [R]", TakeAll, new Vector2(250f, 50f), 18f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)takeAll.transform, new Vector2(0f, 1f), new Vector2(40f, -680f), new Vector2(250f, 50f), new Vector2(0f, 1f));
            takeAllLabel = takeAll.GetComponentInChildren<TextMeshProUGUI>();
            buyOne = UIFactory.SmallButton(window, "BUY", () => TradeSelected(false), new Vector2(250f, 50f), 17f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)buyOne.transform, new Vector2(0f, 1f), new Vector2(40f, -680f), new Vector2(250f, 50f), new Vector2(0f, 1f));
            buyOneLabel = buyOne.GetComponentInChildren<TextMeshProUGUI>();
            buyAll = UIFactory.SmallButton(window, "BUY ALL", () => TradeSelected(true), new Vector2(250f, 50f), 17f, TextAlignmentOptions.Center);
            UIFactory.Place((RectTransform)buyAll.transform, new Vector2(0f, 1f), new Vector2(300f, -680f), new Vector2(250f, 50f), new Vector2(0f, 1f));
            buyAllLabel = buyAll.GetComponentInChildren<TextMeshProUGUI>();

            message = UIFactory.Text("Message", window, "", 17f, Palette.AccentHot, TextAlignmentOptions.MidlineLeft, 3f, 0f);
            UIFactory.Place(message.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(900f, 30f), new Vector2(0f, 0f));
            hint = UIFactory.Text("Hint", window, "", 14f, Palette.WithAlpha(Palette.UIBone, 0.45f), TextAlignmentOptions.MidlineRight, 2f, 0f);
            UIFactory.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(760f, 30f), new Vector2(1f, 0f));

            var g = UIFactory.Rect("Ghost", window);
            g.sizeDelta = new Vector2(Slot * 0.85f, Slot * 0.85f);
            ghost = g.gameObject.AddComponent<RawImage>();
            ghost.raycastTarget = false;
            ghost.enabled = false;
        }

        // ---------- Per frame ----------

        void Update()
        {
            var gm = GameManager.Instance;
            bool want = gm != null && gm.State == GameState.Transfer && (container != null || trader != null);
            if (shown && !want) { SetShown(false); if (gm != null && gm.State == GameState.Transfer) gm.SetState(GameState.Playing); return; }
            if (!shown) return;
            var inv = Inv;
            if (inv == null) return;
            var kb = Keyboard.current;
            bool fresh = Time.unscaledTime - openedAt < 0.2f;
            if (kb != null && !fresh && (kb.eKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) { gm.SetState(GameState.Playing); return; }
            if (kb != null && kb.rKey.wasPressedThisFrame && mode == Mode.Loot) TakeAll();

            bool loot = mode == Mode.Loot;
            takeAll.gameObject.SetActive(loot);
            buyOne.gameObject.SetActive(!loot);
            buyAll.gameObject.SetActive(!loot);
            if (loot)
            {
                title.text = (container.IsBody ? "BODY" : container.Title.ToUpperInvariant());
                subtitle.text = "";
                takeAllLabel.text = container.Owned ? "<color=#E86A5A>STEAL ALL</color>   [R]" : "TAKE ALL   [R]";
                hint.text = "";
            }
            else
            {
                title.text = trader.shopName.ToUpperInvariant();
                subtitle.text = $"BUDGET  {trader.Budget} CR";
                hint.text = "";
            }
            youLabel.text = $"<color=#FFD27A>{inv.Credits} CR</color>";

            for (int i = 0; i < leftSlots.Count; i++)
            {
                var v = leftSlots[i];
                var s = LeftStack(i);
                bool exists = loot ? i < container.Capacity : true;
                v.gameObject.SetActive(exists);
                if (!exists) continue;
                v.Show(s, v == selected, v == hovered, s == null ? null : !loot ? $"{trader.SellPrice(s.def)}" : container.Owned ? "<color=#E86A5A>STEAL</color>" : null);
            }
            foreach (var v in rightSlots)
            {
                var s = inv.Get(v.Ref);
                string corner = null;
                if (s != null && !loot) corner = s.stolen ? "<color=#E86A5A>STOLEN</color>" : trader.Deals(s.def) ? $"{trader.BuyPrice(s.def)}" : "<color=#E8E1D060>-</color>";
                v.Show(s, v == selected, v == hovered, corner);
            }
            ShowInfo();
            infoBox.enabled = info.enabled = !string.IsNullOrEmpty(info.text);
            if (Time.unscaledTime > messageUntil) message.text = "";
        }

        ItemStack LeftStack(int i)
        {
            if (mode == Mode.Loot) return container != null ? container.Get(i) : null;
            return trader != null && i < trader.Goods.Count ? trader.Goods[i] : null;
        }

        ItemStack StackOf(ItemSlotView v) => v == null ? null : v.Ref.Area == SlotArea.Container ? LeftStack(v.Ref.Index) : Inv.Get(v.Ref);

        void ShowInfo()
        {
            var v = hovered != null ? hovered : selected;
            var s = StackOf(v);
            if (s == null)
            {
                info.text = "";
                if (mode == Mode.Trade) { buyOne.interactable = buyAll.interactable = false; buyOneLabel.text = "BUY / SELL"; buyAllLabel.text = "STACK"; }
                return;
            }
            var sb = new System.Text.StringBuilder();
            bool owned = mode == Mode.Loot && v.Ref.Area == SlotArea.Container && container.Owned;
            sb.Append($"<b><color=#F2A23A>{s.def.displayName.ToUpperInvariant()}</color></b>{(s.count > 1 ? $"  x{s.count}" : "")}{(s.stolen ? "   <color=#E86A5A>STOLEN</color>" : "")}");
            if (owned) sb.Append("   <color=#E86A5A>STEAL</color>");
            sb.Append('\n');
            sb.Append($"<size=13><color=#E8E1D099>{s.def.CategoryLabel}   ·   VALUE {s.def.value} CR</color></size>\n");
            sb.Append(s.def.description);
            string stats = s.def.StatLines();
            if (!string.IsNullOrEmpty(stats)) sb.Append($"\n<color=#FFD27A>{stats}</color>");
            info.text = sb.ToString();

            if (mode != Mode.Trade) return;
            var sel = selected != null ? StackOf(selected) : null;
            if (sel == null) { buyOne.interactable = buyAll.interactable = false; buyOneLabel.text = "BUY / SELL"; buyAllLabel.text = "STACK"; return; }
            bool theirs = selected.Ref.Area == SlotArea.Container;
            int each = theirs ? trader.SellPrice(sel.def) : trader.BuyPrice(sel.def);
            buyOne.interactable = theirs || trader.Deals(sel.def);
            buyAll.interactable = sel.count > 1 && buyOne.interactable;
            buyOneLabel.text = $"{(theirs ? "BUY" : "SELL")} 1   ·   {each} CR";
            buyAllLabel.text = sel.count > 1 ? $"{(theirs ? "BUY" : "SELL")} {sel.count}   ·   {each * sel.count} CR" : "STACK";
        }

        void Say(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            message.text = msg;
            messageUntil = Time.unscaledTime + 3f;
        }

        // ---------- Loot actions ----------

        void TakeAll()
        {
            if (mode != Mode.Loot || container == null) return;
            int cash = container.TakeCredits();
            if (cash > 0) { Inv.AddCredits(cash); if (container.Owned) Theft.TookCredits(cash, container.OwnerLabel); }
            bool full = false;
            for (int i = 0; i < container.Capacity; i++)
            {
                var s = container.Get(i);
                if (s == null) continue;
                if (!TakeFromContainer(i)) full = true;
            }
            if (full) Say("INVENTORY FULL");
            SoundManager.Play2D(Sfx.Pickup, 0.6f, 0.05f);
        }

        bool TakeFromContainer(int i, SlotRef? into = null)
        {
            var s = container.Get(i);
            if (s == null) return true;
            if (container.Owned)
            {
                if (s.def.category == ItemCategory.Currency) Theft.TookCredits(s.count, container.OwnerLabel);
                else Theft.Took(s, container.OwnerLabel);
            }
            bool ok;
            if (into.HasValue && Inv.PlaceAt(into.Value, s)) ok = true;
            else ok = Inv.AddStack(s);
            if (ok || s.count <= 0) container.Set(i, null);
            return ok;
        }

        void PutIntoContainer(SlotRef from, int at = -1)
        {
            var s = Inv.Get(from);
            if (s == null) return;
            if (at >= 0 && container.Get(at) != null) at = -1;
            if (at < 0 && !container.HasFreeSlot()) { Say("IT'S FULL"); return; }
            var taken = Inv.TakeFrom(from);
            if (at >= 0) container.Set(at, taken); else container.Add(taken);
        }

        // ---------- Trade actions ----------

        void TradeSelected(bool stack)
        {
            var s = StackOf(selected);
            if (s == null) return;
            TradeSlot(selected, stack ? s.count : 1);
        }

        void TradeSlot(ItemSlotView v, int n)
        {
            if (trader == null || v == null) return;
            string msg = v.Ref.Area == SlotArea.Container ? trader.Sell(Inv, v.Ref.Index, n) : trader.Buy(Inv, v.Ref, n);
            Say(msg);
            if (StackOf(v) == null && selected == v) selected = null;
            // Fencing stolen goods ends the conversation: security is on the way
            if (msg != null && msg.StartsWith("STOLEN")) GameManager.Instance?.SetState(GameState.Playing);
        }

// ---------- Item menu (right click) ----------

        void OpenMenu(ItemSlotView v, ItemStack s, Vector2 screen)
        {
            var inv = Inv;
            var o = new List<(string, System.Action)>();
            bool theirs = v.Ref.Area == SlotArea.Container;
            if (mode == Mode.Loot)
            {
                if (theirs)
                {
                    o.Add((container.Owned ? "Steal" : "Take", () => { if (!TakeFromContainer(v.Ref.Index)) Say("INVENTORY FULL"); }));
                    if (s.def.Consumable) o.Add(("Take and use", () => { if (TakeFromContainer(v.Ref.Index)) UseFromInventory(s); }));
                    if (s.def.Wearable) o.Add(("Take and wear", () => { if (TakeFromContainer(v.Ref.Index)) WearFromInventory(s); }));
                }
                else
                {
                    o.Add(("Put in", () => PutIntoContainer(v.Ref)));
                    AddOwnItemOptions(o, v.Ref, s);
                }
            }
            else
            {
                if (theirs)
                {
                    int each = trader.SellPrice(s.def);
                    o.Add(($"Buy 1   ·   {each} CR", () => TradeSlot(v, 1)));
                    if (s.count > 1) o.Add(($"Buy {s.count}   ·   {each * s.count} CR", () => TradeSlot(v, s.count)));
                }
                else
                {
                    if (trader.Deals(s.def))
                    {
                        int each = trader.BuyPrice(s.def);
                        o.Add(($"Sell 1   ·   {each} CR", () => TradeSlot(v, 1)));
                        if (s.count > 1) o.Add(($"Sell {s.count}   ·   {each * s.count} CR", () => TradeSlot(v, s.count)));
                    }
                    AddOwnItemOptions(o, v.Ref, s);
                }
            }
            o.Add(("Details", () => ItemMenus.Details(window, s)));
            ItemMenus.Open(window, screen, s.def.displayName, o);
        }

        void AddOwnItemOptions(List<(string, System.Action)> o, SlotRef r, ItemStack s)
        {
            var inv = Inv;
            if (s.def.Consumable) o.Add(("Use", () => inv.Consume(r)));
            if (s.def.Wearable) o.Add((r.Area == SlotArea.Equip ? "Take off" : "Wear", () => inv.ToggleWear(r)));
            if (r.Area == SlotArea.Grid && !s.def.Wearable) o.Add(("To hotbar", () => inv.ToHotbar(r)));
        }

        void UseFromInventory(ItemStack s)
        {
            foreach (var (r, st) in Inv.AllStacks(false)) if (st == s) { Inv.Consume(r); return; }
        }

        void WearFromInventory(ItemStack s)
        {
            foreach (var (r, st) in Inv.AllStacks(false)) if (st == s) { Inv.ToggleWear(r); return; }
        }

        // ---------- ISlotPanel ----------

        public void Hover(ItemSlotView v) => hovered = v;
        public void Unhover(ItemSlotView v) { if (hovered == v) hovered = null; }

        public void Clicked(ItemSlotView v, PointerEventData.InputButton button, Vector2 screen)
        {
            var s = StackOf(v);
            bool dbl = lastClickSlot == v && Time.unscaledTime - lastClick < 0.3f;
            lastClick = Time.unscaledTime; lastClickSlot = v;
            if (s == null) { selected = null; return; }
            selected = v;
            if (button == PointerEventData.InputButton.Right) { OpenMenu(v, s, screen); lastClickSlot = null; return; }
            var kb = Keyboard.current;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            if (!dbl && !(mode == Mode.Loot && shift)) return;
            if (mode == Mode.Loot)
            {
                if (v.Ref.Area == SlotArea.Container) { if (!TakeFromContainer(v.Ref.Index)) Say("INVENTORY FULL"); }
                else PutIntoContainer(v.Ref);
                selected = null;
            }
            else TradeSlot(v, shift ? s.count : 1);
            lastClickSlot = null;
        }

        public bool BeginDrag(ItemSlotView v, Vector2 screen)
        {
            var s = StackOf(v);
            if (s == null) return false;
            ItemMenus.Close();
            ghost.texture = PreviewStudio.Instance.Icon(s.def);
            ghost.enabled = true;
            ghost.transform.SetAsLastSibling();
            DragTo(screen);
            return true;
        }

        public void DragTo(Vector2 screen)
        {
            if (!ghost.enabled) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(window, screen, null, out var local);
            var rt = (RectTransform)ghost.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local;
        }

        public void EndDrag(ItemSlotView from, ItemSlotView to, Vector2 screen)
        {
            ghost.enabled = false;
            if (to == null || to == from) return;
            bool fromLeft = from.Ref.Area == SlotArea.Container, toLeft = to.Ref.Area == SlotArea.Container;
            if (mode == Mode.Loot)
            {
                if (fromLeft && !toLeft) { if (!TakeFromContainer(from.Ref.Index, to.Ref)) Say("INVENTORY FULL"); }
                else if (!fromLeft && toLeft) PutIntoContainer(from.Ref, to.Ref.Index);
                else if (!fromLeft && !toLeft) Inv.Move(from.Ref, to.Ref);
                else
                {
                    // Rearranging inside the container
                    var a = container.Get(from.Ref.Index); var b = container.Get(to.Ref.Index);
                    container.Set(to.Ref.Index, a); container.Set(from.Ref.Index, b);
                }
                return;
            }
            var s = StackOf(from);
            if (s == null) return;
            if (fromLeft && !toLeft) TradeSlot(from, s.count);
            else if (!fromLeft && toLeft) TradeSlot(from, s.count);
            else if (!fromLeft && !toLeft) Inv.Move(from.Ref, to.Ref);
        }
    }
}
