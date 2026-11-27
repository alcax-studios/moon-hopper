using System;
using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.FX;
using Alcax.Items;
using Alcax.Player;
using UnityEngine;

namespace Alcax.Loot
{
    public enum PickupKind { Weapon, Ammo, Medkit }

    /// <summary>
    /// A loot item on the ground. Weapons need Interact (E). Ammo is collected by walking over it,
    /// and medkits too when the player is hurt. Spins, bobs, and glows lantern-amber.
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> Active = new();
        public static event Action<string> Collected;

        public PickupKind kind;
        public WeaponData weapon;
        public AmmoType ammoType = AmmoType.Light;
        public int amount = 12;
        public float heal = 40f;

        [SerializeField] Transform visual;
        [SerializeField] Light glow;
        [SerializeField] float autoRadius = 1.3f;

        Vector3 visualBase;
        float glowBase;
        float seed;
        Transform player;
        bool collected;

        /// <summary>Everything is picked up with E (nothing is collected just by walking over it).</summary>
        public bool RequiresInteract => true;
        public Vector3 FocusPoint => transform.position + Vector3.up * 0.35f;

        public string Label => kind switch
        {
            PickupKind.Weapon => weapon != null ? weapon.displayName.ToUpperInvariant() : "WEAPON",
            PickupKind.Ammo => WeaponHolder.AmmoName(ammoType),
            _ => "MEDKIT",
        };

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (glow == null) glow = GetComponentInChildren<Light>();
            if (visual != null) visualBase = visual.localPosition;
            if (glow != null) glowBase = glow.intensity;
            seed = UnityEngine.Random.value * 10f;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
        }

        void Update()
        {
            float t = Time.time + seed;
            if (visual != null)
            {
                visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(t * Mathf.PI * 2f * 0.6f) * 0.06f + 0.06f);
                visual.Rotate(0f, 50f * Time.deltaTime, 0f, Space.World);
            }
            if (glow != null) glow.intensity = glowBase * (0.8f + 0.2f * Mathf.Sin(t * 3f));

            if (!RequiresInteract && player != null && !collected &&
                (player.position - transform.position).sqrMagnitude < autoRadius * autoRadius)
            {
                Collect(player.gameObject);
            }
        }

        public bool Collect(GameObject collector)
        {
            if (collected) return false;
            string msg = null;
            switch (kind)
            {
                case PickupKind.Weapon:
                    var holder = collector.GetComponent<WeaponHolder>();
                    if (holder == null || weapon == null) return false;
                    msg = holder.Give(weapon);
                    if (msg == null) return false;
                    if (!msg.StartsWith("+")) msg = "PICKED UP " + msg;
                    break;
                case PickupKind.Ammo:
                    var h = collector.GetComponent<WeaponHolder>();
                    if (h == null || !h.AddAmmo(ammoType, amount)) return false;
                    msg = $"+{amount} {WeaponHolder.AmmoName(ammoType)}";
                    break;
                case PickupKind.Medkit:
                    var inv = collector.GetComponent<PlayerInventory>();
                    var kit = ItemDatabase.Get("medkit");
                    if (inv == null || kit == null || inv.Add(kit, 1) > 0) return false;
                    msg = "PICKED UP MEDKIT";
                    break;
            }

            collected = true;
            SoundManager.Play2D(Sfx.Pickup, 0.8f, 0.08f);
            FXManager.PickupBurst(FocusPoint);
            Collected?.Invoke(msg);
            Destroy(gameObject);
            return true;
        }
    }
}
