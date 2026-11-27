using System;

namespace Alcax.Items
{
    /// <summary>
    /// Some amount of one item, in an inventory slot or lying in the world. The object itself moves between slots
    /// and the world (so a dropped gun keeps the rounds in its magazine).
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        public ItemDefinition def;
        public int count = 1;
        /// <summary>Weapons: rounds in the magazine (-1 = full when first equipped).</summary>
        public int loaded = -1;
        /// <summary>Taken from someone else (an owned item or container). Traders who mind won't touch it.</summary>
        public bool stolen;

        public ItemStack(ItemDefinition def, int count = 1, int loaded = -1, bool stolen = false)
        {
            this.def = def;
            this.count = count;
            this.loaded = loaded;
            this.stolen = stolen;
        }

        public int Value => def != null ? def.value * count : 0;

        public bool IsEmpty => def == null || count <= 0;
        public string Name => def != null ? def.displayName : "";
        public int Room => def != null ? def.maxStack - count : 0;
    }
}
