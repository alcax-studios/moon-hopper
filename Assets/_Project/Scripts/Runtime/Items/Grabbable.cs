using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// Marks a physics object the player can drag (hold E): props like chairs and crates, world items, and the bones
    /// of a corpse. The rigidbody is found on this object or a parent.
    /// </summary>
    public class Grabbable : MonoBehaviour
    {
        public static readonly List<Grabbable> Active = new List<Grabbable>();

        [Tooltip("Shown on the prompt: [Hold E]  DRAG <label>.")]
        public string label;
        [Tooltip("Heavier than this and it is dragged along the floor instead of carried.")]
        public float carryMass = 25f;

        Rigidbody rb;
        public Rigidbody Body => rb != null ? rb : rb = GetComponentInParent<Rigidbody>();
        public string Label => !string.IsNullOrEmpty(label) ? label : GetComponent<WorldItem>() is WorldItem w && w.Def != null ? w.Def.displayName.ToUpperInvariant() : "OBJECT";

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);
    }
}
