using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Core
{
    /// <summary>
    /// Testing cheats, toggled from Pause > Settings > Admin (and their hotkeys). They stick between sessions (saved
    /// in PlayerPrefs, loaded before the first scene), so quick testing doesn't start with re-toggling everything.
    /// UI state (a dev menu or the console being open) is not saved.
    /// </summary>
    public static class AdminSettings
    {
        static bool godMode, noNeeds, infiniteStamina, invisible, infiniteAmmo, oneHitKills, allWeapons, noClip;

        public static bool GodMode { get => godMode; set { godMode = value; Save("god", value); } }
        /// <summary>Hunger, thirst, and energy don't drain.</summary>
        public static bool NoNeeds { get => noNeeds; set { noNeeds = value; Save("noNeeds", value); } }
        /// <summary>Sprinting and jumping cost no stamina.</summary>
        public static bool InfiniteStamina { get => infiniteStamina; set { infiniteStamina = value; Save("stamina", value); } }
        /// <summary>No AI can see or target the player (like no-clip, without flying).</summary>
        public static bool Invisible { get => invisible; set { invisible = value; Save("invisible", value); } }
        /// <summary>Shots don't use ammo; reloading still plays (R always works, refilling for free).</summary>
        public static bool InfiniteAmmo { get => infiniteAmmo; set { infiniteAmmo = value; Save("ammo", value); } }
        public static bool OneHitKills { get => oneHitKills; set { oneHitKills = value; Save("oneHit", value); } }
        public static bool AllWeapons => allWeapons;
        /// <summary>No-clip as last left (the player controller applies it when it spawns, and reports changes here).</summary>
        public static bool NoClip { get => noClip; set { noClip = value; Save("noClip", value); } }

        /// <summary>For automated runs (AI experiments): god mode for this session only, not saved.</summary>
        public static void SetGodModeForSession(bool on) => godMode = on;

        /// <summary>A dev menu is open and owns the number keys (weapon switching pauses).</summary>
        public static bool DevMenuOpen;
        /// <summary>The admin console is open: the cursor is free, mouse look and attacks pause (movement keeps working).</summary>
        public static bool ConsoleOpen;

        /// <summary>Raised when "give all weapons" is pressed or toggled.</summary>
        public static event Action GiveAllWeaponsRequested;
        public static event Action FullHealRequested;
        public static event Action AddAmmoRequested;

        public static void SetAllWeapons(bool on)
        {
            allWeapons = on;
            Save("allWeapons", on);
            if (on) GiveAllWeaponsRequested?.Invoke();
        }

        public static void RequestFullHeal() => FullHealRequested?.Invoke();
        public static void RequestAmmo() => AddAmmoRequested?.Invoke();

        public static bool AnyActive => GodMode || Invisible || InfiniteAmmo || OneHitKills || AllWeapons;

        /// <summary>Short labels of the active cheats, for the HUD reminder.</summary>
        public static string ActiveSummary()
        {
            var list = new List<string>();
            if (GodMode) list.Add("GOD");
            if (Invisible) list.Add("INVISIBLE");
            if (InfiniteAmmo) list.Add("INF AMMO");
            if (OneHitKills) list.Add("ONE-HIT");
            if (AllWeapons) list.Add("ALL WEAPONS");
            return string.Join("  |  ", list);
        }

        // ---------- Persistence ----------

        const string Prefix = "admin.";

        static void Save(string key, bool on)
        {
            PlayerPrefs.SetInt(Prefix + key, on ? 1 : 0);
            PlayerPrefs.Save();
        }

        static bool Load(string key) => PlayerPrefs.GetInt(Prefix + key, 0) == 1;

        /// <summary>Restore the toggles before anything reads them (entering Play mode resets statics).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void LoadAll()
        {
            godMode = Load("god"); noNeeds = Load("noNeeds"); infiniteStamina = Load("stamina"); invisible = Load("invisible");
            infiniteAmmo = Load("ammo"); oneHitKills = Load("oneHit"); allWeapons = Load("allWeapons"); noClip = Load("noClip");
            DevMenuOpen = false; ConsoleOpen = false;
            GiveAllWeaponsRequested = null; FullHealRequested = null; AddAmmoRequested = null;
        }
    }
}
