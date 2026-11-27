using System;
using System.Collections.Generic;

namespace Alcax.Items
{
    public class CodexEntry
    {
        public string id, category, title, body;
    }

    /// <summary>
    /// What the player has learned: places, people, factions, items. Entries unlock as they're met (an item picked up,
    /// a person talked to, a faction seen) and stay unlocked across map loads.
    /// </summary>
    public static class Codex
    {
        public static readonly string[] Categories = { "Station", "People", "Factions", "Items" };
        public static readonly List<CodexEntry> Unlocked = new List<CodexEntry>();
        public static event Action Changed;
        public static event Action<string> Notice;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { Unlocked.Clear(); Changed = null; Notice = null; }

        public static bool Has(string id) => Unlocked.Exists(e => e.id == id);

        public static void Unlock(string id, string category, string title, string body, bool announce = true)
        {
            if (Has(id)) return;
            Unlocked.Add(new CodexEntry { id = id, category = category, title = title, body = body });
            if (announce) Notice?.Invoke("CODEX:  " + title.ToUpperInvariant());
            Changed?.Invoke();
        }

        public static void UnlockItem(ItemDefinition d)
        {
            if (d == null) return;
            Unlock("item:" + d.id, "Items", d.displayName, d.description + (string.IsNullOrEmpty(d.StatLines()) ? "" : "\n\n" + d.StatLines()), announce: false);
        }
    }
}
