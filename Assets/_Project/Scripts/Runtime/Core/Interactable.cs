using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Core
{
    /// <summary>
    /// Something the player can use with Interact (E) when looking at it from close by: a button, a switch, a terminal.
    /// PlayerInteractor picks the best one in reach and shows "[E]  Label" on the HUD.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> Active = new List<Interactable>();

        [SerializeField] protected string label = "USE";
        [SerializeField] protected float reach = 2.2f;

        public virtual string Label => label;
        public virtual Vector3 FocusPoint => transform.position;
        public float Reach => reach;
        public virtual bool Available => true;

        protected virtual void OnEnable() => Active.Add(this);
        protected virtual void OnDisable() => Active.Remove(this);

        public abstract void Interact(GameObject who);
    }
}
