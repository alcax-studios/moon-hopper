using System.Collections.Generic;
using Alcax.AI;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// A person who buys and sells (reached through their dialogue: "Let's trade."). Sells their stock above its value,
    /// buys what they deal in below it, and only up to their budget (it grows when you buy from them). Traders here
    /// won't take stolen goods: offering one is a crime and they call security.
    /// </summary>
    public class Trader : MonoBehaviour
    {
        [System.Serializable]
        public class StockLine
        {
            public string item;
            public int count = 1;
        }

        public string shopName = "Shop";
        public List<StockLine> stock = new List<StockLine>();
        [Tooltip("What they buy from you (empty: anything).")]
        public List<ItemCategory> buys = new List<ItemCategory>();
        [Tooltip("Price you pay = value x markup.")]
        public float markup = 1.15f;
        [Tooltip("Price you get = value x buyRate.")]
        public float buyRate = 0.45f;
        public Vector2Int budgetRange = new Vector2Int(150, 300);
        public bool refusesStolen = true;

        readonly List<ItemStack> goods = new List<ItemStack>();
        public int Budget { get; private set; }
        public IReadOnlyList<ItemStack> Goods => goods;
        public string Who => TryGetComponent<Alcax.Dialogue.Npc>(out var n) ? n.displayName : shopName;

        void OnEnable() => Alcax.Dialogue.DialogueUI.ActionPicked += OnAction;
        void OnDisable() => Alcax.Dialogue.DialogueUI.ActionPicked -= OnAction;

        void OnAction(string action, Alcax.Dialogue.Npc npc)
        {
            if (action == "@trade" && npc != null && npc.gameObject == gameObject) Alcax.UI.TransferWindow.OpenTrade(this);
        }

        void Start()
        {
            Budget = Random.Range(budgetRange.x, budgetRange.y + 1);
            foreach (var l in stock)
            {
                var def = ItemDatabase.Get(l.item);
                if (def == null) continue;
                int n = l.count;
                while (n > 0) { int k = Mathf.Min(n, def.maxStack); goods.Add(new ItemStack(def, k)); n -= k; }
            }
        }

        public int SellPrice(ItemDefinition d) => Mathf.Max(1, Mathf.RoundToInt(d.value * markup));
        public int BuyPrice(ItemDefinition d) => Mathf.Max(d.value > 0 ? 1 : 0, Mathf.FloorToInt(d.value * buyRate));
        public bool Deals(ItemDefinition d) => d != null && d.value > 0 && d.category != ItemCategory.Currency && (buys.Count == 0 || buys.Contains(d.category));

        /// <summary>The player buys n of a stock stack. Returns a message (or null when it went through).</summary>
        public string Sell(PlayerInventory inv, int index, int n)
        {
            if (index < 0 || index >= goods.Count) return null;
            var s = goods[index];
            n = Mathf.Clamp(n, 1, s.count);
            int price = SellPrice(s.def) * n;
            if (inv.Credits < price) return "NOT ENOUGH CREDITS";
            int left = inv.Add(s.def, n, -1, quiet: true);
            int bought = n - left;
            if (bought <= 0) return "INVENTORY FULL";
            inv.Spend(SellPrice(s.def) * bought);
            Budget += SellPrice(s.def) * bought;
            s.count -= bought;
            if (s.count <= 0) goods.RemoveAt(index);
            Audio.SoundManager.Play2D(Audio.Sfx.Pickup, 0.6f, 0.05f);
            return null;
        }

        /// <summary>The player sells n from a slot. Stolen goods: a crime. Returns a message (null on success).</summary>
        public string Buy(PlayerInventory inv, SlotRef from, int n)
        {
            var s = inv.Get(from);
            if (s == null) return null;
            if (!Deals(s.def)) return "NOT BUYING THAT";
            if (s.stolen && refusesStolen)
            {
                StationSecurity.Instance?.ReportCrime(PlayerTarget.Instance != null ? PlayerTarget.Instance.Position : transform.position, "FENCING STOLEN GOODS");
                return "STOLEN GOODS";
            }
            n = Mathf.Clamp(n, 1, s.count);
            int each = BuyPrice(s.def);
            int afford = each > 0 ? Mathf.Min(n, Budget / each) : n;
            if (afford <= 0) return "CAN'T AFFORD IT";
            var taken = inv.TakeFrom(from, afford);
            if (taken == null) return null;
            Budget -= each * afford;
            inv.AddCredits(each * afford, quiet: true);
            // Into their stock (merging with the same item)
            var same = goods.Find(g => g.def == taken.def && g.def.Stackable && g.Room > 0);
            if (same != null) { int k = Mathf.Min(same.Room, taken.count); same.count += k; taken.count -= k; }
            if (taken.count > 0) { taken.stolen = false; goods.Add(taken); }
            Audio.SoundManager.Play2D(Audio.Sfx.Pickup, 0.5f, 0.05f);
            return afford < n ? $"BOUGHT {afford}" : null;
        }
    }
}
