using System;
using System.Collections.Generic;

namespace Alcax.Core
{
    /// <summary>
    /// Testing cheats, toggled from Pause > Settings > Admin. All start off.
    /// They're static, so they survive a Restart within the same Editor session.
    /// </summary>
    public static class AdminSettings
    {
        public static bool GodMode;
        /// <summary>A dev menu is open and owns the number keys (weapon switching pauses).</summary>
        public static bool DevMenuOpen;
        public static bool InfiniteAmmo;
        public static bool OneHitKills;

        /// <summary>Raised when "give all weapons" is pressed or toggled.</summary>
        public static event Action GiveAllWeaponsRequested;
        public static event Action FullHealRequested;
        public static event Action AddAmmoRequested;

        public static bool AllWeapons;

        public static void SetAllWeapons(bool on)
        {
            AllWeapons = on;
            if (on) GiveAllWeaponsRequested?.Invoke();
        }

        public static void RequestFullHeal() => FullHealRequested?.Invoke();
        public static void RequestAmmo() => AddAmmoRequested?.Invoke();

        public static bool AnyActive => GodMode || InfiniteAmmo || OneHitKills || AllWeapons;

        /// <summary>Short labels of the active cheats, for the HUD reminder.</summary>
        public static string ActiveSummary()
        {
            var list = new List<string>();
            if (GodMode) list.Add("GOD");
            if (InfiniteAmmo) list.Add("INF AMMO");
            if (OneHitKills) list.Add("ONE-HIT");
            if (AllWeapons) list.Add("ALL WEAPONS");
            return string.Join("  |  ", list);
        }
    }
}
