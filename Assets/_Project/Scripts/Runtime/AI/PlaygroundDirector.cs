using Alcax.Combat;
using Alcax.Player;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// The AI Playground's map-specific setup: the player's starting loadout and the map's spawn markers and patrol
    /// routes. The admin controls themselves (spawn menu, no-clip, freeze, god mode, overlay) are global: see AdminTools.
    /// </summary>
    public class PlaygroundDirector : MonoBehaviour
    {
        [SerializeField] Transform[] groundSpawns;
        [SerializeField] Transform[] highSpawns;
        [SerializeField] PatrolRoute[] routes;
        [SerializeField] WeaponData[] playerWeapons;

        public Transform[] GroundSpawns => groundSpawns;
        public Transform[] HighSpawns => highSpawns;
        public PatrolRoute[] Routes => routes;

        void Start()
        {
            var wh = FindAnyObjectByType<WeaponHolder>();
            if (wh != null && playerWeapons != null)
            {
                foreach (var w in playerWeapons) if (w != null) wh.Give(w, silent: true);
                wh.AddAmmo(AmmoType.Light, 240);
                wh.AddAmmo(AmmoType.Shells, 60);
                wh.AddAmmo(AmmoType.Rifle, 300);
            }
            if (FindAnyObjectByType<PlayerController>() is { } pc && pc.GetComponent<PlayerTarget>() == null)
                pc.gameObject.AddComponent<PlayerTarget>();
        }
    }
}
