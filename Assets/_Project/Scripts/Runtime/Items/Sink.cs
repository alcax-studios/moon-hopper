using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>A tap: E to drink (thirst). Water from the station's recyclers, free and endless.</summary>
    public class Sink : Interactable
    {
        [SerializeField] float thirst = 25f;

        public override string Label => "DRINK";
        public override Vector3 FocusPoint => transform.position;

        void Awake() { if (reach < 2f) reach = 2f; }

        public override void Interact(GameObject who)
        {
            var v = who.GetComponent<Alcax.Player.PlayerVitals>();
            if (v == null) return;
            if (!v.Drink(thirst)) { PlayerInventory.Instance?.Say("NOT THIRSTY"); return; }
            SoundManager.PlayAt(Sfx.Pickup, transform.position, 0.35f, 0.15f);
        }
    }
}
