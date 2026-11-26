using System;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// What the admin tools can spawn, on any map: the operative roster (archetype + prefab), the factions, the combat
    /// config, and the spawn-menu presets. Lives in Resources so it loads without a scene reference. Written by the
    /// Playground builder (which owns the AI data).
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/AI/Spawn Catalog", fileName = "SpawnCatalog")]
    public class SpawnCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public EnemyArchetype archetype;
            public GameObject prefab;
        }

        [Serializable]
        public class Preset
        {
            public string label;
            public string[] names;
            public bool high;
            public bool patrol;
            [Tooltip("Force this faction (display name) instead of the menu's faction choice.")]
            public string faction;
            [Tooltip("Skirmish: also spawn this squad of this faction 25 to 35 m away; the two know roughly where each other are.")]
            public string versusFaction;
            public string[] versusNames;
        }

        public Faction FactionNamed(string name)
        {
            if (string.IsNullOrEmpty(name) || factions == null) return null;
            foreach (var f in factions) if (f != null && f.displayName == name) return f;
            return null;
        }

        public CombatAIConfig config;
        public Faction[] factions;
        public Entry[] roster;
        public Preset[] presets;

        public Entry Find(string name)
        {
            if (roster == null || roster.Length == 0) return null;
            foreach (var e in roster) if (e.archetype != null && e.archetype.displayName == name) return e;
            return roster[0];
        }

        static SpawnCatalog cached;
        public static SpawnCatalog Load() => cached != null ? cached : cached = Resources.Load<SpawnCatalog>("SpawnCatalog");
    }
}
