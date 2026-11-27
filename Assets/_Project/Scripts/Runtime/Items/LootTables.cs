using System.Collections.Generic;

namespace Alcax.Items
{
    /// <summary>
    /// What people and containers carry, by table name. Each line is a chance to roll an item with a count range;
    /// a table also has a credit range. Rolled once when the carrier spawns, so a body holds what that person had.
    /// Data in code for now; it can move to assets once tables are tuned by hand.
    /// </summary>
    public static class LootTables
    {
        struct Line
        {
            public string id; public float chance; public int min, max;
            public Line(string id, float chance, int min = 1, int max = 1) { this.id = id; this.chance = chance; this.min = min; this.max = max; }
        }

        class Table
        {
            public Line[] lines;
            public int creditsMin, creditsMax;
            public int maxItems = 6;
        }

        static readonly Dictionary<string, Table> tables = new Dictionary<string, Table>
        {
            // People
            ["security"] = new Table { creditsMin = 20, creditsMax = 90, lines = new[] {
                new Line("ammo_light", 0.9f, 8, 24), new Line("bandage", 0.5f, 1, 2), new Line("ration", 0.3f), new Line("coffee", 0.25f),
                new Line("keycard", 0.2f), new Line("helmet_security", 0.25f), new Line("gloves_tactical", 0.15f) } },
            ["insurgents"] = new Table { creditsMin = 0, creditsMax = 45, lines = new[] {
                new Line("ammo_rifle", 0.85f, 10, 40), new Line("ammo_shells", 0.25f, 3, 10), new Line("bandage", 0.45f, 1, 2), new Line("ration", 0.4f, 1, 2), new Line("charge", 0.25f), new Line("water", 0.3f),
                new Line("cap", 0.2f), new Line("datapad", 0.1f), new Line("machete", 0.08f) } },
            ["special ops"] = new Table { creditsMin = 30, creditsMax = 130, lines = new[] {
                new Line("ammo_rifle", 0.95f, 20, 60), new Line("medkit", 0.35f), new Line("bandage", 0.6f, 1, 3), new Line("gloves_tactical", 0.4f),
                new Line("vest_armored", 0.18f), new Line("helmet_security", 0.12f), new Line("ration", 0.3f) } },
            ["civilian"] = new Table { creditsMin = 5, creditsMax = 70, maxItems = 3, lines = new[] {
                new Line("coffee", 0.25f), new Line("ration", 0.25f), new Line("water", 0.25f), new Line("keycard", 0.15f), new Line("datapad", 0.08f),
                new Line("bandage", 0.15f), new Line("mug", 0.05f) } },
            // Containers
            ["supply_crate"] = new Table { creditsMin = 0, creditsMax = 0, lines = new[] {
                new Line("ration", 0.7f, 1, 4), new Line("water", 0.6f, 1, 3), new Line("bandage", 0.6f, 1, 3), new Line("ammo_light", 0.4f, 10, 30), new Line("ammo_rifle", 0.25f, 15, 45),
                new Line("medkit", 0.15f), new Line("gloves_tactical", 0.1f) } },
            ["locker"] = new Table { creditsMin = 0, creditsMax = 40, maxItems = 4, lines = new[] {
                new Line("jacket_work", 0.2f), new Line("cap", 0.25f), new Line("boots_work", 0.1f), new Line("bandage", 0.35f),
                new Line("coffee", 0.2f), new Line("keycard", 0.15f), new Line("datapad", 0.1f) } },
            ["armory_locker"] = new Table { creditsMin = 0, creditsMax = 0, lines = new[] {
                new Line("ammo_light", 1f, 24, 48), new Line("ammo_shells", 0.8f, 6, 18), new Line("ammo_rifle", 0.8f, 30, 60),
                new Line("helmet_security", 0.4f), new Line("vest_armored", 0.25f), new Line("medkit", 0.5f, 1, 2) } },
            ["register"] = new Table { creditsMin = 60, creditsMax = 220, maxItems = 1, lines = new[] { new Line("keycard", 0.1f) } },
            ["fridge"] = new Table { creditsMin = 0, creditsMax = 0, maxItems = 4, lines = new[] {
                new Line("coffee", 0.6f, 1, 2), new Line("ration", 0.6f, 1, 3), new Line("noodles", 0.4f), new Line("wrap", 0.4f) } },
            ["cabinet"] = new Table { creditsMin = 0, creditsMax = 0, maxItems = 4, lines = new[] {
                new Line("ration", 0.45f, 1, 3), new Line("water", 0.5f, 1, 2), new Line("mug", 0.5f), new Line("coffee", 0.2f), new Line("bandage", 0.15f) } },
            ["vanity"] = new Table { creditsMin = 0, creditsMax = 0, maxItems = 3, lines = new[] {
                new Line("bandage", 0.6f, 1, 2), new Line("water", 0.2f), new Line("keycard", 0.1f) } },
            ["nightstand"] = new Table { creditsMin = 0, creditsMax = 30, maxItems = 3, lines = new[] {
                new Line("datapad", 0.2f), new Line("keycard", 0.25f), new Line("charge", 0.3f), new Line("bandage", 0.25f) } },
            ["medical"] = new Table { creditsMin = 0, creditsMax = 0, maxItems = 4, lines = new[] {
                new Line("bandage", 0.95f, 2, 5), new Line("medkit", 0.5f), new Line("water", 0.3f) } },
            ["island"] = new Table { creditsMin = 0, creditsMax = 15, maxItems = 3, lines = new[] {
                new Line("charge", 0.4f), new Line("ration", 0.4f), new Line("keycard", 0.15f), new Line("mug", 0.3f) } },
            ["lost_and_found"] = new Table { creditsMin = 0, creditsMax = 25, maxItems = 5, lines = new[] {
                new Line("cap", 0.6f), new Line("keycard", 0.5f), new Line("datapad", 0.35f), new Line("mug", 0.5f), new Line("gloves_tactical", 0.2f), new Line("jacket_work", 0.2f) } },
        };

        /// <summary>Roll a table. Unknown tables give nothing.</summary>
        public static List<ItemStack> Roll(string table, System.Random rng, out int credits)
        {
            var list = new List<ItemStack>();
            credits = 0;
            if (string.IsNullOrEmpty(table) || !tables.TryGetValue(table.ToLowerInvariant(), out var t)) return list;
            credits = t.creditsMax > 0 ? rng.Next(t.creditsMin, t.creditsMax + 1) : 0;
            foreach (var l in t.lines)
            {
                if (list.Count >= t.maxItems) break;
                if (rng.NextDouble() > l.chance) continue;
                var def = ItemDatabase.Get(l.id);
                if (def == null) continue;
                int n = rng.Next(l.min, l.max + 1);
                while (n > 0)
                {
                    int k = System.Math.Min(n, def.maxStack);
                    list.Add(new ItemStack(def, k));
                    n -= k;
                }
            }
            return list;
        }

        /// <summary>The item for an NPC's gun (by the archetype's weapon name).</summary>
        public static ItemDefinition ForNpcWeapon(string weaponName) => ItemDatabase.Get(weaponName switch
        {
            "Pistol" => "pistol",
            "Shotgun" => "shotgun",
            _ => "rifle",
        });
    }
}
