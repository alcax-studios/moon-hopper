using Alcax.Audio;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// An item lying in the world: a physical object (it falls, rolls, can be dragged) that the player takes with a
    /// tap of E. Dropping from the inventory spawns one; level builders place them too.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WorldItem : Interactable
    {
        [SerializeField] ItemDefinition item;
        [SerializeField, Min(1)] int count = 1;
        [Tooltip("Who it belongs to (empty: nobody). Taking an owned item is stealing.")]
        [SerializeField] string owner;

        [Tooltip("Station property: nobody's in particular, still a crime to take (anyone who sees it reports it).")]
        [SerializeField] bool stationProperty;

        public string Owner => owner;
        public bool Owned => stationProperty || !string.IsNullOrEmpty(owner);
        /// <summary>"Noor Rahimi's" or "Station property".</summary>
        public string OwnerLabel => !string.IsNullOrEmpty(owner) ? owner + "'s" : "Station property";
        public void SetOwner(string o) => owner = o;

        ItemStack stack;
        Collider[] cols;

        public ItemStack Stack => stack ??= new ItemStack(item, count);
        public ItemDefinition Def => Stack.def;

        public override string Label => Def == null ? "TAKE" : $"{(Owned ? "STEAL" : "TAKE")} {Def.displayName.ToUpperInvariant()}{(Stack.count > 1 ? $"  x{Stack.count}" : "")}";
        public override Vector3 FocusPoint
        {
            get
            {
                if (cols == null || cols.Length == 0) cols = GetComponentsInChildren<Collider>();
                return cols.Length > 0 && cols[0] != null ? cols[0].bounds.center : transform.position;
            }
        }
        public override bool Available => Def != null;

        void Awake()
        {
            if (reach < 2.4f) reach = 2.6f;
            if (GetComponent<Grabbable>() == null) gameObject.AddComponent<Grabbable>();
        }

        public void Set(ItemStack s) { stack = s; item = s.def; count = s.count; }

        public override void Interact(GameObject who)
        {
            var inv = who.GetComponent<PlayerInventory>();
            if (inv == null || Def == null) return;
            var def = Def;
            int before = Stack.count;
            if (Owned) { if (def.category == ItemCategory.Currency) Theft.TookCredits(Stack.count, OwnerLabel); else Theft.Took(Stack, OwnerLabel); owner = null; stationProperty = false; }
            if (inv.AddStack(Stack) || Stack.count <= 0)
            {
                SoundManager.Play2D(Sfx.Pickup, 0.7f, 0.08f);
                Destroy(gameObject);
            }
            else if (Stack.count < before) SoundManager.Play2D(Sfx.Pickup, 0.5f, 0.08f);
        }

        /// <summary>Make a world item from a stack: the item's model with a fitted box collider and a rigidbody.</summary>
        public static WorldItem Spawn(ItemStack s, Vector3 pos, Quaternion rot, Vector3 velocity)
        {
            if (s == null || s.IsEmpty) return null;
            var go = new GameObject("Item_" + s.def.id);
            go.transform.SetPositionAndRotation(pos, rot);
            if (s.def.worldModel != null)
            {
                var m = Instantiate(s.def.worldModel, go.transform, false);
                m.name = "Model";
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            else
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(go.transform, false);
                c.transform.localScale = Vector3.one * 0.2f;
            }
            FitCollider(go);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, s.def.mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var wi = go.AddComponent<WorldItem>();
            wi.Set(s);
            rb.linearVelocity = velocity;
            return wi;
        }

        /// <summary>A box collider around the renderers (in local space).</summary>
        public static BoxCollider FitCollider(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var box = go.AddComponent<BoxCollider>();
            if (rs.Length == 0) { box.size = Vector3.one * 0.2f; return box; }
            var inv = go.transform.worldToLocalMatrix;
            Bounds b = default; bool first = true;
            foreach (var r in rs)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lc = inv.MultiplyPoint3x4(c);
                    if (first) { b = new Bounds(lc, Vector3.zero); first = false; } else b.Encapsulate(lc);
                }
            }
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * 0.04f);
            return box;
        }
    }
}
