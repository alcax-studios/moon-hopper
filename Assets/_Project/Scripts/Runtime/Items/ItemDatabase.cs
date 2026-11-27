using Alcax.Combat;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>Every item definition, loaded from Resources/ItemDatabase (written by ItemAssets).</summary>
    [CreateAssetMenu(menuName = "Alcax/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabase : ScriptableObject
    {
        public ItemDefinition[] items;
        [Tooltip("Worn and carried when a map starts.")]
        public ItemDefinition[] startWorn;
        public ItemDefinition[] startCarried;

        static ItemDatabase cached;
        public static ItemDatabase Instance
        {
            get
            {
                if (cached == null) cached = Resources.Load<ItemDatabase>("ItemDatabase");
                return cached;
            }
        }

        public static ItemDefinition Get(string id)
        {
            var db = Instance;
            if (db == null || db.items == null) return null;
            foreach (var i in db.items) if (i != null && i.id == id) return i;
            return null;
        }

        public static ItemDefinition ForWeapon(WeaponData w)
        {
            var db = Instance;
            if (db == null || db.items == null || w == null) return null;
            foreach (var i in db.items) if (i != null && i.weapon == w) return i;
            return null;
        }

        public static ItemDefinition ForAmmo(AmmoType t)
        {
            var db = Instance;
            if (db == null || db.items == null || t == AmmoType.None) return null;
            foreach (var i in db.items) if (i != null && i.category == ItemCategory.Ammo && i.ammoType == t) return i;
            return null;
        }
    }
}
