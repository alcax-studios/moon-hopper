using UnityEngine;

namespace Alcax.Combat
{
    public enum WeaponKind { Melee, Hitscan }
    public enum AmmoType { None, Light, Shells, Rifle }
    public enum WeaponSound { Fists, Blunt, Blade, Pistol, Shotgun, Rifle }

    [CreateAssetMenu(menuName = "Alcax/Weapon", fileName = "Weapon")]
    public class WeaponData : ScriptableObject
    {
        public string displayName = "Weapon";
        public WeaponKind kind = WeaponKind.Melee;
        public WeaponSound sound = WeaponSound.Fists;
        public GameObject viewModel;

        [Header("Damage")]
        public float damage = 10f;
        public float range = 2f;
        public float cooldown = 0.5f;
        public bool automatic;

        [Header("Melee")]
        public float windup = 0.08f;
        public float swingAngle = 80f;
        public int maxTargets = 1;

        [Header("Hitscan")]
        public int pellets = 1;
        public float spread = 1f;
        public int magazineSize = 8;
        public AmmoType ammoType = AmmoType.None;
        public int startingReserve;
        public float reloadTime = 1.2f;
        [Tooltip("Load one round at a time (shotgun). reloadTime is then per round.")]
        public bool reloadPerShell;
        public float noiseRadius = 35f;

        [Header("Feel")]
        public float knockback = 1f;
        public float recoil = 2f;
        public float shake = 0.2f;
        public float hitStop = 0.04f;
    }
}
