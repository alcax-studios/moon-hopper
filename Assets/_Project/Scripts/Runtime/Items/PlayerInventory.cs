using System;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Items
{
    /// <summary>
    /// The player's things: a 6 x 5 backpack grid, an 8-slot hotbar (keys 1 to 8, mouse wheel; the selected slot is
    /// what's in your hands), and five body slots (head, chest, pants, shoes, gloves). Ammo for the guns is read from
    /// here, wearables add armor, and consumables heal. Added to the player by WeaponHolder.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        public const int GridW = 6, GridH = 5, GridSize = GridW * GridH, HotbarSize = 8;
        public static readonly EquipSlot[] BodySlots = { EquipSlot.Head, EquipSlot.Chest, EquipSlot.Pants, EquipSlot.Shoes, EquipSlot.Gloves };

        public static PlayerInventory Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { Notice = null; ItemGained = null; ItemUsed = null; }
        /// <summary>Short messages for the HUD ("+12 ROUNDS", "INVENTORY FULL").</summary>
        public static event Action<string> Notice;
        /// <summary>Show a short HUD message.</summary>
        public void Say(string msg) => Notice?.Invoke(msg);
        public static event Action<ItemDefinition, int> ItemGained;
        public static event Action<ItemDefinition> ItemUsed;

        // ---------- Money ----------
// Money is an item (Credits, one stack with no limit): the balance is what you carry
        public const int StartCredits = 150;
        public static ItemDefinition CreditsDef => ItemDatabase.Get("credits");
        public int Credits => Count(CreditsDef);
        public event Action CreditsChanged;

        public void AddCredits(int n, bool quiet = false)
        {
            if (n <= 0 || CreditsDef == null) return;
            int left = Add(CreditsDef, n, quiet: true);
            // No room for a new stack: the cash drops at your feet rather than vanishing
            if (left > 0) WorldItem.Spawn(new ItemStack(CreditsDef, left), transform.position + transform.forward * 0.6f + Vector3.up * 0.5f, Quaternion.identity, Vector3.zero);
            if (!quiet) Notice?.Invoke($"+{n} CR");
            CreditsChanged?.Invoke();
        }

        public bool Spend(int n)
        {
            if (n < 0 || Credits < n) return false;
            if (n > 0) Take(CreditsDef, n);
            CreditsChanged?.Invoke();
            return true;
        }

        readonly ItemStack[] grid = new ItemStack[GridSize];
        readonly ItemStack[] hotbar = new ItemStack[HotbarSize];
        readonly ItemStack[] equip = new ItemStack[6];

        public int Selected { get; private set; }
        public ItemStack SelectedStack => hotbar[Selected] != null && !hotbar[Selected].IsEmpty ? hotbar[Selected] : null;
        /// <summary>Any change of contents.</summary>
        public event Action Changed;
        /// <summary>The selected hotbar slot changed.</summary>
        public event Action SelectionChanged;

        Health health;
        bool started;

        void Awake()
        {
            Instance = this;
            health = GetComponent<Health>();
            if (health != null) health.DamageScale = ArmorScale;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var db = ItemDatabase.Instance;
            if (db != null)
            {
                if (db.startWorn != null) foreach (var d in db.startWorn) if (d != null && d.Wearable) equip[(int)d.slot] = new ItemStack(d);
                if (db.startCarried != null) foreach (var d in db.startCarried) if (d != null) Add(d, 1, quiet: true);
                if (CreditsDef != null) Add(CreditsDef, StartCredits, quiet: true);
            }
            started = true;
            Changed?.Invoke();
        }

        void Update()
        {
            if (!GameManager.InputLive || AdminSettings.DevMenuOpen) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                var keys = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key };
                for (int i = 0; i < keys.Length; i++) if (keys[i].wasPressedThisFrame) Select(i);
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll > 0.1f) Cycle(-1);
                else if (scroll < -0.1f) Cycle(1);
            }
        }

        // ---------- Slots ----------

        ItemStack[] Slots(SlotArea a) => a == SlotArea.Grid ? grid : a == SlotArea.Hotbar ? hotbar : equip;

        public ItemStack Get(SlotRef r)
        {
            var arr = Slots(r.Area);
            if (r.Index < 0 || r.Index >= arr.Length) return null;
            var s = arr[r.Index];
            return s != null && !s.IsEmpty ? s : null;
        }

        void Put(SlotRef r, ItemStack s) => Slots(r.Area)[r.Index] = s != null && !s.IsEmpty ? s : null;

        public bool Accepts(SlotRef r, ItemStack s)
        {
            if (s == null) return true;
            if (r.Area != SlotArea.Equip) return true;
            return s.def.Wearable && (int)s.def.slot == r.Index;
        }

        public ItemStack Worn(EquipSlot slot) => Get(SlotRef.Equip(slot));

        public void Select(int i)
        {
            i = Mathf.Clamp(i, 0, HotbarSize - 1);
            if (i == Selected) return;
            Selected = i;
            SoundManager.Play2D(Sfx.UIClick, 0.35f, 0.05f);
            SelectionChanged?.Invoke();
        }

/// <summary>The next or previous hotbar slot (wrapping); an empty slot is bare hands.</summary>
        void Cycle(int dir) => Select(((Selected + dir) % HotbarSize + HotbarSize) % HotbarSize);

        /// <summary>Move a stack: merge into the same item, otherwise swap (when both sides accept the other).</summary>
        public bool Move(SlotRef from, SlotRef to, int amount = -1)
        {
            if (from.Same(to)) return false;
            var a = Get(from);
            if (a == null) return false;
            var b = Get(to);
            if (!Accepts(to, a)) return false;
            if (b != null && b.def == a.def && a.def.Stackable && a.stolen == b.stolen)
            {
                int n = Mathf.Min(amount < 0 ? a.count : amount, b.Room);
                if (n <= 0) return false;
                b.count += n; a.count -= n;
                Put(from, a);
                Raise();
                return true;
            }
            if (amount > 0 && amount < a.count && b == null)
            {
                // Split
                a.count -= amount;
                Put(to, new ItemStack(a.def, amount, -1, a.stolen));
                Raise();
                return true;
            }
            if (b != null && !Accepts(from, b)) return false;
            Put(to, a);
            Put(from, b);
            if (to.Area == SlotArea.Equip || from.Area == SlotArea.Equip) SoundManager.Play2D(Sfx.Equip, 0.4f, 0.05f);
            Raise();
            return true;
        }

        void Raise()
        {
            Changed?.Invoke();
        }

        // ---------- Adding and taking ----------

        /// <summary>Add items. Returns how many didn't fit. Stacks merge first; weapons prefer the hotbar,
        /// a wearable goes straight onto an empty body slot.</summary>
        public int Add(ItemDefinition def, int count = 1, int loaded = -1, bool quiet = false, bool stolen = false)
        {
            if (def == null || count <= 0) return count;
            int left = count;
            if (def.Stackable)
            {
                foreach (var arr in new[] { hotbar, grid })
                    for (int i = 0; i < arr.Length && left > 0; i++)
                    {
                        var s = arr[i];
                        if (s == null || s.def != def || s.Room <= 0 || s.stolen != stolen) continue;
                        int n = Mathf.Min(left, s.Room);
                        s.count += n; left -= n;
                    }
            }
            while (left > 0)
            {
                int n = Mathf.Min(left, def.maxStack);
                var stack = new ItemStack(def, n, loaded, stolen);
                if (def.Wearable && equip[(int)def.slot] == null) equip[(int)def.slot] = stack;
                else if (!PlaceFree(stack, def.category == ItemCategory.Weapon)) break;
                left -= n;
            }
            int added = count - left;
            if (added > 0)
            {
                if (started) ItemGained?.Invoke(def, added);
                Raise();
            }
            if (left > 0 && !quiet) Notice?.Invoke("INVENTORY FULL");
            return left;
        }

        /// <summary>Put a whole stack (from the world) in. Returns true when all of it fit.</summary>
        public bool AddStack(ItemStack s)
        {
            if (s == null || s.IsEmpty) return false;
            if (!s.def.Stackable)
            {
                if (s.def.Wearable && equip[(int)s.def.slot] == null) { equip[(int)s.def.slot] = s; }
                else if (!PlaceFree(s, s.def.category == ItemCategory.Weapon)) { Notice?.Invoke("INVENTORY FULL"); return false; }
                ItemGained?.Invoke(s.def, s.count);
                Raise();
                return true;
            }
            int left = Add(s.def, s.count, s.loaded, false, s.stolen);
            s.count = left;
            return left == 0;
        }

        bool PlaceFree(ItemStack s, bool hotbarFirst)
        {
            var order = hotbarFirst ? new[] { hotbar, grid } : new[] { grid, hotbar };
            foreach (var arr in order)
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] == null || arr[i].IsEmpty)
                    {
                        arr[i] = s;
                        // A new weapon goes into your hands unless you are already holding one
                        if (arr == hotbar && s.def.weapon != null && (SelectedStack == null || SelectedStack.def.weapon == null) && started)
                        { Selected = i; SelectionChanged?.Invoke(); }
                        return true;
                    }
            return false;
        }

        public int Count(ItemDefinition def)
        {
            if (def == null) return 0;
            int n = 0;
            foreach (var arr in new[] { hotbar, grid, equip })
                foreach (var s in arr) if (s != null && s.def == def) n += s.count;
            return n;
        }

        public int CountAmmo(AmmoType t) => Count(ItemDatabase.ForAmmo(t));

        /// <summary>Remove up to n of an item (backpack first, then the hotbar). Returns how many were removed.</summary>
        public int Take(ItemDefinition def, int n)
        {
            if (def == null || n <= 0) return 0;
            int taken = 0;
            foreach (var arr in new[] { grid, hotbar })
                for (int i = arr.Length - 1; i >= 0 && taken < n; i--)
                {
                    var s = arr[i];
                    if (s == null || s.def != def) continue;
                    int k = Mathf.Min(n - taken, s.count);
                    s.count -= k; taken += k;
                    if (s.count <= 0) arr[i] = null;
                }
            if (taken > 0) Raise();
            return taken;
        }

        public int TakeAmmo(AmmoType t, int n) => Take(ItemDatabase.ForAmmo(t), n);

        // ---------- Using ----------

        /// <summary>Consume one from a slot (heal). False (with a notice) when it can't be used now.</summary>
        public bool Consume(SlotRef r)
        {
            var s = Get(r);
            if (s == null || !s.def.Consumable) return false;
            var vitals = GetComponent<Alcax.Player.PlayerVitals>();
            bool helps = s.def.heal > 0f && health != null && health.Current < health.Max || vitals != null && vitals.WouldHelp(s.def);
            if (!helps) { Notice?.Invoke(s.def.heal > 0f ? "ALREADY AT FULL HEALTH" : "NOT NOW"); return false; }
            if (health != null && s.def.heal > 0f) health.Heal(s.def.heal);
            vitals?.Apply(s.def);
            s.count--;
            Put(r, s);
            SoundManager.Play2D(Sfx.Pickup, 0.5f, 0.1f);
            Notice?.Invoke(s.def.displayName.ToUpperInvariant());
            ItemUsed?.Invoke(s.def);
            Raise();
            return true;
        }

        /// <summary>Put a wearable on (swapping out what was there), or take it off into the backpack.</summary>
        public bool ToggleWear(SlotRef r)
        {
            var s = Get(r);
            if (s == null || !s.def.Wearable) return false;
            if (r.Area == SlotArea.Equip)
            {
                for (int i = 0; i < grid.Length; i++) if (grid[i] == null) return Move(r, new SlotRef(SlotArea.Grid, i));
                Notice?.Invoke("INVENTORY FULL");
                return false;
            }
            return Move(r, SlotRef.Equip(s.def.slot));
        }

        /// <summary>Put an item onto the first free hotbar slot.</summary>
        public bool ToHotbar(SlotRef r)
        {
            if (r.Area == SlotArea.Hotbar) return false;
            for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] == null) return Move(r, new SlotRef(SlotArea.Hotbar, i));
            Notice?.Invoke("HOTBAR FULL");
            return false;
        }

        /// <summary>Drop a stack (or part of it) in front of the player. It becomes a WorldItem you can take back.</summary>
        public bool Drop(SlotRef r, int amount = -1)
        {
            var s = Get(r);
            if (s == null) return false;
            ItemStack dropped;
            if (amount > 0 && amount < s.count) { s.count -= amount; dropped = new ItemStack(s.def, amount, -1, s.stolen); }
            else { dropped = s; Put(r, null); }
if (dropped.stolen)
            {
                bool drone = Alcax.Ambient.ScannerDrone.AnyScanningPlayer;
                foreach (var d in Alcax.Ambient.ScannerDrone.All) d.SawDrop(dropped.Value);
                if (!drone && Theft.Watched(out var who)) Alcax.AI.StationSecurity.Instance?.ReportTheft(dropped.Value, who);
            }
            var cam = Camera.main != null ? Camera.main.transform : transform;
            var fwd = cam.forward; fwd.y = Mathf.Min(fwd.y, 0.3f);
            WorldItem.Spawn(dropped, cam.position + fwd.normalized * 0.7f - Vector3.up * 0.25f, Quaternion.Euler(0f, cam.eulerAngles.y, 0f), fwd.normalized * 2f);
            SoundManager.Play2D(Sfx.Swing, 0.25f, 0.1f);
            Raise();
            return true;
        }

/// <summary>Every stack (backpack, hotbar, worn), for loot and trade windows.</summary>
        public System.Collections.Generic.IEnumerable<(SlotRef slot, ItemStack stack)> AllStacks(bool includeWorn = true)
        {
            for (int i = 0; i < hotbar.Length; i++) if (hotbar[i] != null && !hotbar[i].IsEmpty) yield return (new SlotRef(SlotArea.Hotbar, i), hotbar[i]);
            for (int i = 0; i < grid.Length; i++) if (grid[i] != null && !grid[i].IsEmpty) yield return (new SlotRef(SlotArea.Grid, i), grid[i]);
            if (includeWorn) for (int i = 0; i < equip.Length; i++) if (equip[i] != null && !equip[i].IsEmpty) yield return (new SlotRef(SlotArea.Equip, i), equip[i]);
        }

        public int StolenValue()
        {
            int v = 0;
            foreach (var (_, st) in AllStacks()) if (st.stolen) v += st.Value;
            return v;
        }

        /// <summary>Security takes back everything stolen. Returns its value.</summary>
        public int ConfiscateStolen()
        {
            int v = 0;
            foreach (var arr in new[] { hotbar, grid, equip })
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i] != null && arr[i].stolen) { v += arr[i].Value; arr[i] = null; }
            if (v > 0) Raise();
            return v;
        }

        /// <summary>Put a stack into a specific empty slot that accepts it. False otherwise.</summary>
        public bool PlaceAt(SlotRef r, ItemStack s)
        {
            if (s == null || s.IsEmpty || Get(r) != null || !Accepts(r, s)) return false;
            Put(r, s);
            ItemGained?.Invoke(s.def, s.count);
            Raise();
            return true;
        }

        /// <summary>Remove a specific amount from a slot (selling, moving into a container). Returns what was taken.</summary>
        public ItemStack TakeFrom(SlotRef r, int amount = -1)
        {
            var s = Get(r);
            if (s == null) return null;
            if (amount <= 0 || amount >= s.count) { Put(r, null); Raise(); return s; }
            s.count -= amount;
            Raise();
            return new ItemStack(s.def, amount, s.loaded, s.stolen);
        }

        // ---------- Armor ----------

        public float Armor(bool head)
        {
            float a = 0f;
            foreach (var slot in BodySlots)
            {
                if ((slot == EquipSlot.Head) != head) continue;
                var s = Worn(slot);
                if (s != null) a += s.def.armor;
            }
            return a;
        }

        /// <summary>Damage multiplier from armor: 100 / (100 + armor). Head armor against headshots only.</summary>
        float ArmorScale(DamageInfo info)
        {
            float a = Armor(info.Headshot);
            return 100f / (100f + Mathf.Max(0f, a));
        }
    }
}
