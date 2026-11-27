using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>A bed: E to sleep until one of the next two times of day (see SleepPrompt).</summary>
    public class Bed : Interactable
    {
        public override string Label => "SLEEP";
        public override Vector3 FocusPoint => TryGetComponent<Collider>(out var c) ? c.bounds.center + Vector3.up * 0.2f : transform.position;
        public override bool Available => !Alcax.UI.SleepPrompt.Busy;

        void Awake() { if (reach < 2.6f) reach = 2.8f; }

        public override void Interact(GameObject who) => Alcax.UI.SleepPrompt.Open();
    }
}
