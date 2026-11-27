using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>
    /// Drags physics objects (Grabbable): hold E on one to pick it up by the spot you grabbed, let go of E to drop it,
    /// click to throw it. Light things are carried in front of you; heavy ones (a crate, a body) are dragged along
    /// the floor. The object never pushes you, and it slips out of your grip if it snags too far behind.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerGrabber : MonoBehaviour
    {
        public static PlayerGrabber Instance { get; private set; }
        public static bool Holding => Instance != null && Instance.body != null;
        /// <summary>When the last object was let go (the click that throws shouldn't also fire the gun).</summary>
        public static float LastRelease = -10f;

        Camera cam;
        CharacterController cc;
        Rigidbody body;
        Grabbable grab;
        Vector3 localPoint;
        float holdDist, savedDamping, savedAngular;
        bool heavy;
        readonly List<Collider> ignored = new List<Collider>();

        public string HeldLabel => grab != null ? grab.Label : "";

        void Awake()
        {
            Instance = this;
            cam = Camera.main;
            cc = GetComponent<CharacterController>();
        }

        public bool Grab(Grabbable g, Vector3 at)
        {
            var rb = g != null ? g.Body : null;
            if (rb == null || rb.isKinematic) return false;
            // Not the thing you're standing on
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var under, 0.6f, ~(1 << 2), QueryTriggerInteraction.Ignore) && under.rigidbody == rb) return false;
            if (cam == null) cam = Camera.main;
            body = rb; grab = g;
            localPoint = rb.transform.InverseTransformPoint(at);
            holdDist = Mathf.Clamp(Vector3.Distance(cam.transform.position, at), 1.2f, 2.2f);
            heavy = rb.mass > g.carryMass;
            savedDamping = rb.linearDamping; savedAngular = rb.angularDamping;
            rb.linearDamping = heavy ? 2f : 6f;
            rb.angularDamping = heavy ? 2f : 8f;
            rb.WakeUp();
            ignored.Clear();
            if (cc != null)
                foreach (var c in rb.GetComponentsInChildren<Collider>())
                    if (c.attachedRigidbody == rb) { Physics.IgnoreCollision(cc, c, true); ignored.Add(c); }
            return true;
        }

        public void Release(Vector3 throwVelocity = default)
        {
            if (body == null) return;
            LastRelease = Time.time;
            body.linearDamping = savedDamping;
            body.angularDamping = savedAngular;
            if (throwVelocity != Vector3.zero) body.linearVelocity += throwVelocity;
            var cols = new List<Collider>(ignored);
            ignored.Clear();
            body = null; grab = null;
            // Let it fall clear of you before it can bump you again
            StartCoroutine(Restore(cols));
        }

        System.Collections.IEnumerator Restore(List<Collider> cols)
        {
            yield return new WaitForSeconds(0.6f);
            if (cc == null) yield break;
            foreach (var c in cols) if (c != null) Physics.IgnoreCollision(cc, c, false);
        }

        void Update()
        {
            if (body == null) return;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                float speed = heavy ? 3f : Mathf.Clamp(14f / Mathf.Sqrt(Mathf.Max(0.2f, body.mass)), 4f, 14f);
                Release(cam.transform.forward * speed);
                SoundManager.Play2D(Sfx.Swing, 0.5f, 0.1f);
            }
        }

        void FixedUpdate()
        {
            if (body == null) return;
            if (!body.gameObject.activeInHierarchy || body.isKinematic) { Release(); return; }
            var eye = cam.transform.position;
            var target = eye + cam.transform.forward * holdDist;
            var point = body.transform.TransformPoint(localPoint);
            if (heavy)
            {
                // Dragged: stays low, follows slower
                target.y = Mathf.Min(target.y, point.y + 0.25f, transform.position.y + 0.9f);
            }
            var delta = target - point;
            if (delta.magnitude > 2.2f) { Release(); return; }   // snagged on something: you lose your grip
            float gain = heavy ? 6f : 14f, max = heavy ? 3f : 10f;
            var want = Vector3.ClampMagnitude(delta * gain, max);
            // Move the grabbed spot (not the center), so it swings naturally around the grip
            var pointVel = body.GetPointVelocity(point);
            var change = want - pointVel;
            float k = heavy ? Mathf.Clamp01(40f / body.mass) : 1f;
            body.AddForceAtPosition(change * k, point, ForceMode.VelocityChange);
            if (!heavy) body.angularVelocity *= 0.85f;
        }
    }
}
