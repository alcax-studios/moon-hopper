namespace Alcax.Items
{
    /// <summary>What kind of thing an item is. Decides where it can go and what "use" means.</summary>
    public enum ItemCategory { Weapon, Ammo, Aid, Food, Wearable, Misc, Currency, Drink }

    /// <summary>A body slot for wearables. None for everything else.</summary>
    public enum EquipSlot { None, Head, Chest, Pants, Shoes, Gloves }

    /// <summary>How a wearable is drawn on the character preview (until wearables get their own models).</summary>
    public enum WearStyle { None, Helmet, Cap, Vest, Jacket, Pants, Boots, Gloves }

    /// <summary>Where a stack sits in the player's inventory.</summary>
    public enum SlotArea { Grid, Hotbar, Equip, Container }

    public readonly struct SlotRef
    {
        public readonly SlotArea Area;
        public readonly int Index;
        public SlotRef(SlotArea area, int index) { Area = area; Index = index; }
        public static SlotRef Equip(EquipSlot s) => new SlotRef(SlotArea.Equip, (int)s);
        public bool Same(SlotRef o) => o.Area == Area && o.Index == Index;
        public override string ToString() => $"{Area}[{Index}]";
    }
}
