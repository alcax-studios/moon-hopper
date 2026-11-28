using System.Collections.Generic;
using Alcax.Items;
using UnityEngine;

namespace Alcax.Characters
{
    /// <summary>Clothing slots (bit values match the body's OutfitMask, written by Tools/blender/characters/human_npc.py).</summary>
    public enum OutfitSlot { Chest = 1, Pants = 2, Shoes = 4, Gloves = 8, Head = 16 }

    /// <summary>
    /// What a character is wearing, piece by piece: each garment is its own mesh (child named *_Outfit_&lt;Slot&gt;) over a
    /// whole body. The body's Alcax/Skin Lit material cuts out the skin under worn pieces (no poke-through) and shows
    /// underwear where nothing is worn. Pieces come off and go back on (Remove, Wear). When the character dies, the
    /// worn pieces go into its body's loot (LootContainer) as wearable items; taking one out takes it off the body,
    /// putting it back puts it on.
    /// </summary>
    public class CharacterOutfit : MonoBehaviour
    {
        [System.Serializable]
        public class Piece
        {
            public OutfitSlot slot;
            public SkinnedMeshRenderer renderer;
            public string itemId;
        }

        [SerializeField] List<Piece> pieces = new List<Piece>();
        [Tooltip("Civilians: tint the clothes a little per person (stable: from the name), so a crowd isn't in matching outfits.")]
        [SerializeField] bool varyColours;
        SkinnedMeshRenderer body;
        MaterialPropertyBlock block;
        LootContainer loot;
        readonly Dictionary<Piece, ItemDefinition> lootDefs = new Dictionary<Piece, ItemDefinition>();
        bool inLoot;
        static readonly int HideMaskId = Shader.PropertyToID("_HideMask");

        public IReadOnlyList<Piece> Pieces => pieces;
        public event System.Action<OutfitSlot, bool> Changed;

        void Awake()
        {
            if (pieces.Count == 0) Discover();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.EndsWith("_Body")) body = r;
            Apply();
            if (varyColours) VaryColours();
            var h = GetComponent<Alcax.Combat.Health>();
            if (h != null) h.Died += OnDied;
        }


        static readonly Color[] ChestTints = { Color.white, new Color(0.6f, 0.72f, 0.95f), new Color(0.95f, 0.62f, 0.56f), new Color(0.66f, 0.85f, 0.62f), new Color(0.92f, 0.85f, 0.6f), new Color(0.55f, 0.55f, 0.6f), new Color(0.84f, 0.66f, 0.9f), new Color(0.6f, 0.86f, 0.86f) };
        static readonly Color[] PantsTints = { Color.white, new Color(0.45f, 0.45f, 0.5f), new Color(0.86f, 0.78f, 0.6f), new Color(0.38f, 0.42f, 0.62f), new Color(0.28f, 0.28f, 0.3f), new Color(0.6f, 0.52f, 0.42f) };
        static readonly Color[] ShoeTints = { Color.white, new Color(0.55f, 0.42f, 0.3f), new Color(0.32f, 0.32f, 0.34f), new Color(0.85f, 0.85f, 0.88f) };

        /// <summary>Each piece a colour of its own (picked from the person's name, so it stays the same between sessions).</summary>
        void VaryColours()
        {
            var rng = new System.Random(name.GetHashCode());
            var mpb = new MaterialPropertyBlock();
            foreach (var p in pieces)
            {
                if (p.renderer == null) continue;
                var set = p.slot == OutfitSlot.Chest ? ChestTints : p.slot == OutfitSlot.Pants ? PantsTints : ShoeTints;
                var c = set[rng.Next(set.Length)];
                c = Color.Lerp(c, Color.white, (float)rng.NextDouble() * 0.25f);   // (a touch lighter or not)
                p.renderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                p.renderer.SetPropertyBlock(mpb);
            }
        }
        /// <summary>Find the garment meshes by name (*_Outfit_Chest, ...) and what item each one is.</summary>
        public void Discover()
        {
            pieces.Clear();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int at = r.name.IndexOf("_Outfit_");
                if (at < 0) continue;
                var tail = r.name.Substring(at + 8);
                int dot = tail.IndexOf('.');
                if (dot >= 0) tail = tail.Substring(0, dot);
                if (!System.Enum.TryParse(tail, out OutfitSlot slot)) continue;
                pieces.Add(new Piece { slot = slot, renderer = r, itemId = ItemFor(r.sharedMaterial != null ? r.sharedMaterial.name : r.name, slot) });
            }
        }

        /// <summary>The wearable item a garment is (by the clothing asset it came from).</summary>
        public static string ItemFor(string source, OutfitSlot slot)
        {
            var s = source.ToLowerInvariant();
            bool suit = s.Contains("elegantsuit");
            switch (slot)
            {
                case OutfitSlot.Chest: return suit ? "suit_jacket" : "shirt_casual";
                case OutfitSlot.Pants: return suit ? "suit_trousers" : "jeans";
                case OutfitSlot.Shoes: return s.Contains("shoes06") || s.Contains("shoes03") ? "shoes_dress" : "sneakers";
                case OutfitSlot.Gloves: return "gloves_tactical";
                default: return "cap";
            }
        }

        public bool IsWorn(OutfitSlot slot)
        {
            foreach (var p in pieces) if (p.slot == slot && p.renderer != null && p.renderer.enabled) return true;
            return false;
        }

        /// <summary>Take a piece off (it vanishes from the body). Returns false if nothing is worn there.</summary>
        public bool Remove(OutfitSlot slot) => Set(slot, false);

        /// <summary>Put a piece back on (only pieces this character owns).</summary>
        public bool Wear(OutfitSlot slot) => Set(slot, true);

        bool Set(OutfitSlot slot, bool on)
        {
            bool changed = false;
            foreach (var p in pieces)
                if (p.slot == slot && p.renderer != null && p.renderer.enabled != on) { p.renderer.enabled = on; changed = true; }
            if (changed) { Apply(); Changed?.Invoke(slot, on); }
            return changed;
        }

        /// <summary>Tell the body which slots are covered (the skin there is cut out).</summary>
        void Apply()
        {
            if (body == null) return;
            int bits = 0;
            foreach (var p in pieces) if (p.renderer != null && p.renderer.enabled) bits |= (int)p.slot;
            block ??= new MaterialPropertyBlock();
            body.GetPropertyBlock(block);
            block.SetFloat(HideMaskId, bits);
            body.SetPropertyBlock(block);
        }

        // ---------- Looting ----------

        void OnDied()
        {
            loot = GetComponent<LootContainer>();
            if (loot == null) return;
            foreach (var p in pieces)
            {
                if (p.renderer == null || !p.renderer.enabled || string.IsNullOrEmpty(p.itemId)) continue;
                var def = ItemDatabase.Get(p.itemId);
                if (def == null) continue;
                loot.AddGrow(new ItemStack(def, 1));   // (clothes always fit: random loot must not crowd them out)
                lootDefs[p] = def;
            }
            inLoot = lootDefs.Count > 0;
        }

        void LateUpdate()
        {
            if (!inLoot || loot == null) return;
            // Each piece is on the body exactly while its item is in the body's loot: taken out, it comes off;
            // put back, it goes back on
            var have = new Dictionary<ItemDefinition, int>();
            for (int i = 0; i < loot.Capacity; i++)
            {
                var s = loot.Get(i);
                if (s != null && s.def != null) have[s.def] = (have.TryGetValue(s.def, out var n) ? n : 0) + s.count;
            }
            foreach (var kv in lootDefs)
            {
                bool there = have.TryGetValue(kv.Value, out var n) && n > 0;
                if (there) have[kv.Value] = n - 1;
                if (kv.Key.renderer != null && kv.Key.renderer.enabled != there) Set(kv.Key.slot, there);
            }
        }
    }
}
