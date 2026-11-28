using Alcax.Combat;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// One kind of item: its name, category, stack size, world model, and what it does. Instances in the world or the
    /// inventory are ItemStacks that point at one of these. Written by ItemAssets (Alcax > Rebuild Item Assets).
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/Item", fileName = "Item")]
    public class ItemDefinition : ScriptableObject
    {
        public string id = "item";
        public string displayName = "Item";
        [TextArea(2, 5)] public string description;
        public ItemCategory category = ItemCategory.Misc;
        [Min(1)] public int maxStack = 1;
        [Tooltip("Visual only (no scripts or colliders): drops, icons, and the in-hand view use it.")]
        public GameObject worldModel;
        [Tooltip("Scale of the world model when held in first person.")]
        public float heldScale = 1f;
        public float mass = 0.5f;
        [Tooltip("Base price in credits (traders sell above it and buy below it).")]
        public int value = 10;

        [Header("Weapon")]
        public WeaponData weapon;

        [Header("Ammo")]
        public AmmoType ammoType = AmmoType.None;

        [Header("Aid and food")]
        public float heal;
        [Tooltip("Seconds to use it from the hand (from the inventory it's instant).")]
        public float useTime = 1f;
        [Tooltip("Survival needs restored (0 to 100 scale).")]
        public float hunger, thirst, energy;
        [Tooltip("A timed effect it gives (PlayerVitals): energized, caffeinated...")]
        public string effect;

        [Header("Wearable")]
        public EquipSlot slot = EquipSlot.None;
        public WearStyle wearStyle = WearStyle.None;
        public Color wearColor = new Color(0.4f, 0.42f, 0.45f);
        [Tooltip("Damage reduction points. Head armor only counts against headshots; the rest against body hits.")]
        public float armor;

        public bool Consumable => category == ItemCategory.Aid || category == ItemCategory.Food || category == ItemCategory.Drink;
        public bool Wearable => category == ItemCategory.Wearable && slot != EquipSlot.None;
        public bool Stackable => maxStack > 1;

        public string CategoryLabel => category switch
        {
            ItemCategory.Aid => "AID",
            ItemCategory.Food => "FOOD",
            ItemCategory.Drink => "DRINK",
            ItemCategory.Ammo => "AMMO",
            ItemCategory.Currency => "CURRENCY",
            ItemCategory.Weapon => "WEAPON",
            ItemCategory.Wearable => "WEARABLE · " + slot.ToString().ToUpperInvariant(),
            _ => "MISC",
        };

        /// <summary>Short stat lines for tooltips and details.</summary>
        public string StatLines()
        {
            var sb = new System.Text.StringBuilder();
            if (weapon != null)
            {
                sb.Append($"Damage {weapon.damage:0}");
                if (weapon.pellets > 1) sb.Append($" x{weapon.pellets}");
                if (weapon.IsGun) sb.Append($"   Magazine {weapon.magazineSize}   Range {weapon.range:0} m");
                else sb.Append("   Melee");
            }
            if (Consumable)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (heal > 0f) parts.Add($"Heals {heal:0}");
                if (hunger > 0f) parts.Add($"Food +{hunger:0}");
                if (thirst > 0f) parts.Add($"Water +{thirst:0}");
                if (energy > 0f) parts.Add($"Energy +{energy:0}");
                if (!string.IsNullOrEmpty(effect)) parts.Add(effect == "energized" ? "No stamina drain 12 s, then a crash" : effect == "caffeinated" ? "Stamina recovers faster" : effect);
                parts.Add($"Use {useTime:0.#} s");
                sb.Append(string.Join("   ", parts));
            }
            if (Wearable) sb.Append(armor > 0f ? $"Armor {armor:0}" : "No protection");
            if (category == ItemCategory.Ammo) sb.Append($"Stacks to {maxStack}");
            return sb.ToString();
        }
    }
}
