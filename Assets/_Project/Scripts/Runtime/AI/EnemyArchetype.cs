using Alcax.Audio;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>Who an enemy is: traits, movement, weapon handling, and ranges. Each spawned unit jitters the traits slightly.</summary>
    [CreateAssetMenu(menuName = "Alcax/AI/Enemy Archetype", fileName = "Archetype")]
    public class EnemyArchetype : ScriptableObject
    {
        public string displayName = "Rifleman";

        [Header("Body")]
        public float maxHealth = 100f;
        public float walkSpeed = 1.7f;
        public float runSpeed = 4.3f;
        public float sprintSpeed = 5.6f;
        [Tooltip("How slowly long-term fatigue builds from sprinting (1 = average; higher is fitter).")] public float fitness = 1f;

        [Header("Personality (0 to 1)")]
        [Range(0f, 1f)] public float aggression = 0.5f;
        [Range(0f, 1f)] public float caution = 0.5f;
        [Range(0f, 1f)] public float discipline = 0.5f;
        [Range(0f, 1f)] public float patience = 0.5f;
        [Range(0f, 1f)] public float teamwork = 0.5f;
        [Range(0f, 1f)] public float skill = 0.6f;
        [Range(0f, 1f)] public float nerve = 0.5f;   // resistance to fear
        public Vector2 reactionTime = new Vector2(0.2f, 0.35f);
        [Tooltip("Random jitter applied to each trait per spawned unit.")] [Range(0f, 0.3f)] public float traitJitter = 0.12f;

        [Header("Ranges")]
        [Tooltip("Closest they'll willingly get to the threat or to where it probably is (unless rushing in or cornered).")]
        public float standoff = 6f;
        [Tooltip("Added to the faction's cover discipline.")] [Range(-0.4f, 0.4f)] public float coverDiscipline = 0f;
        public float preferredRangeMin = 10f;
        public float preferredRangeMax = 25f;

        [Header("Weapon (same model as the player's guns)")]
        public string weaponName = "Rifle";
        public Sfx shotSound = Sfx.RifleShot;
        public float damage = 14f;
        public int pellets = 1;
        public float fireInterval = 0.11f;
        public int magazine = 30;
        [Tooltip("Spare rounds carried beyond the loaded magazine (the ammo budget).")]
        public int reserveRounds = 120;
        public float reloadTime = 2.2f;
        public float range = 80f;
        [Tooltip("Base spread cone (degrees), plus bloom per shot up to max, recovering per second.")]
        public float spreadBase = 0.6f;
        public float spreadPerShot = 0.35f;
        public float spreadMax = 4f;
        public float spreadRecovery = 6f;
        [Tooltip("Muzzle climb per shot (degrees) that the shooter has to fight back down.")] public float recoilPerShot = 0.9f;
        public Vector2Int burst = new Vector2Int(3, 7);
        public float noiseRadius = 60f;

        [Header("Fighting-style tendencies for this role (multiplied with the faction's)")]
        public StyleWeights styles = StyleWeights.Even;

        [Header("Melee")]
        public bool canRush;
        public float meleeDamage = 30f;
        public float meleeRange = 1.7f;
    }
}
