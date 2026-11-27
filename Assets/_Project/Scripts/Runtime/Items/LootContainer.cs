using System.Collections.Generic;
using Alcax.Combat;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// Something that holds items you can take: a crate, a locker, a register, a fridge, or a body. E opens the loot
    /// window (take one, take all, or put things in). Contents roll from a loot table when it first appears. An owner
    /// makes it private: taking from it is stealing. On a person it stays closed until they're dead.
    /// </summary>
    public class LootContainer : Interactable
    {
        public static readonly List<LootContainer> All = new List<LootContainer>();

        [SerializeField] string title = "Container";
        [SerializeField] string table;
        [Tooltip("Who it belongs to (empty: nobody's). Taking from an owned container is theft.")]
        [SerializeField] string owner;
        [Tooltip("Station property: nobody's in particular, still a crime to take from (anyone who sees it reports it).")]
        [SerializeField] bool stationProperty;
        [SerializeField, Min(1)] int capacity = 12;
        [Tooltip("Optional: the focus point (a body's torso).")]
        [SerializeField] Transform focus;

        ItemStack[] slots;
        int credits;
        bool rolled;
        Health life;   // a person: only lootable once dead

        public string Title => title;
        public string Owner => owner;
        public bool Owned => stationProperty || !string.IsNullOrEmpty(owner);
        /// <summary>"Noor Rahimi's" or "Station property".</summary>
        public string OwnerLabel => !string.IsNullOrEmpty(owner) ? owner + "'s" : "Station property";
        public int Capacity => capacity;
        /// <summary>Cash inside (as a Credits stack).</summary>
        public int Credits { get { EnsureRolled(); int n = credits; foreach (var s in slots) if (s != null && s.def != null && s.def.category == ItemCategory.Currency) n += s.count; return n; } }
        public bool IsBody => life != null;

        public override string Label => IsBody ? $"SEARCH {title.ToUpperInvariant()}" : $"OPEN {title.ToUpperInvariant()}";
        public override Vector3 FocusPoint => focus != null ? focus.position : TryGetComponent<Collider>(out var c) ? c.bounds.center : transform.position + Vector3.up * 0.5f;
        public override bool Available => life == null || life.IsDead;

        /// <summary>Set up from code (people at spawn, builders).</summary>
        public void Configure(string title, string table, string owner = null, int capacity = 12, Transform focus = null, Health life = null)
        {
            this.title = title; this.table = table; this.owner = owner; this.capacity = capacity; this.focus = focus; this.life = life;
            if (life != null && reach < 2.4f) reach = 2.4f;
        }

        protected override void OnEnable() { base.OnEnable(); All.Add(this); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        void EnsureRolled()
        {
            if (rolled) return;
            rolled = true;
            slots = new ItemStack[capacity];
            var rng = new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            var items = LootTables.Roll(table, rng, out credits);
            var cash = PlayerInventory.CreditsDef;
            if (credits > 0 && cash != null) { items.Insert(0, new ItemStack(cash, credits)); credits = 0; }
            for (int i = 0; i < items.Count && i < capacity; i++) slots[i] = items[i];
        }

        void Start() => EnsureRolled();

        public ItemStack Get(int i) { EnsureRolled(); return i >= 0 && i < slots.Length && slots[i] != null && !slots[i].IsEmpty ? slots[i] : null; }
        public void Set(int i, ItemStack s) { EnsureRolled(); if (i >= 0 && i < slots.Length) slots[i] = s != null && !s.IsEmpty ? s : null; }

        public void Add(ItemStack s)
        {
            EnsureRolled();
            if (s == null || s.IsEmpty) return;
            for (int i = 0; i < slots.Length; i++) if (slots[i] == null) { slots[i] = s; return; }
        }

        public bool HasFreeSlot() { EnsureRolled(); foreach (var s in slots) if (s == null) return true; return false; }
        public bool Empty { get { EnsureRolled(); if (credits > 0) return false; foreach (var s in slots) if (s != null) return false; return true; } }

        public int TakeCredits() { int c = credits; credits = 0; return c; }

        public override void Interact(GameObject who)
        {
            EnsureRolled();
            Alcax.UI.TransferWindow.OpenLoot(this);
        }
    }
}
