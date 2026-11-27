using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// Gates an Interactable behind an item (a key, a keycard, a tool). With the item anywhere on you (backpack,
    /// hotbar, or in hand), using the thing unlocks it for good and goes ahead; without it, it stays shut. Works on
    /// anything interactable: hinged doors, containers, buttons.
    /// </summary>
    public class Lock : MonoBehaviour
    {
        [Tooltip("The item that opens it (an item id).")]
        public string key = "safe_key";
        [Tooltip("The key is used up when it opens (a one-time code); otherwise you keep it.")]
        public bool consumeKey;
        public bool unlocked;

        public bool Locked => !unlocked;

        /// <summary>True when it's open to use (already unlocked, or unlocked now with the key you carry).</summary>
        public bool TryUnlock(PlayerInventory inv)
        {
            if (unlocked) return true;
            var def = ItemDatabase.Get(key);
            if (inv == null || def == null || inv.Count(def) <= 0)
            {
                SoundManager.PlayAt(Sfx.Click, transform.position, 0.6f, 0.05f);
                inv?.Say("LOCKED");
                return false;
            }
            unlocked = true;
            if (consumeKey) inv.Take(def, 1);
            SoundManager.PlayAt(Sfx.SlideRack, transform.position, 0.5f, 0.1f);
            inv.Say("UNLOCKED");
            return true;
        }
    }
}
