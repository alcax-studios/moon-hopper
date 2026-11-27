using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// A door on furniture (a fridge, a cupboard): E opens or closes it, swinging on its hinge. What's inside is
    /// physical (world items you pick up by hand) and can only be reached while it's open.
    /// </summary>
    public class HingedDoor : Interactable
    {
        [SerializeField] string thing = "Fridge";
        [Tooltip("The moving leaf, pivoted on the hinge.")]
        [SerializeField] Transform leaf;
        [SerializeField] float openAngle = -105f;
        [SerializeField] float seconds = 0.35f;

        bool open;
        float t;   // 0 closed, 1 open

        public bool IsOpen => open;
        public override string Label => (open ? "CLOSE " : "OPEN ") + thing.ToUpperInvariant();
        public override Vector3 FocusPoint => leaf != null && leaf.GetComponentInChildren<Collider>() != null ? leaf.GetComponentInChildren<Collider>().bounds.center : transform.position;

        public override void Interact(GameObject who)
        {
            open = !open;
            SoundManager.PlayAt(open ? Sfx.DoorOpen : Sfx.DoorClose, transform.position, 0.45f, 0.1f);
        }

        void Update()
        {
            if (leaf == null) return;
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.05f, seconds));
            leaf.localRotation = Quaternion.Euler(0f, openAngle * Ease.OutCubic(t), 0f);
        }
    }
}
