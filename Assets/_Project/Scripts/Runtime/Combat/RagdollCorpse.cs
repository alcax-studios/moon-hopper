using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// Turns a dead character on the shared rig (Rig > Body > Head, ArmL/ArmR > Elbow, LegL/LegR > Knee) into a loose
    /// ragdoll: a rigidbody per segment, character joints between them, and the killing hit's impulse on the segment it
    /// struck. Once it settles (or after a few seconds) every segment turns kinematic: the corpse stays where it fell,
    /// stays physical: it comes to rest with heavier damping and can be pushed around.
    /// </summary>
    public class RagdollCorpse : MonoBehaviour
    {
        readonly List<Rigidbody> bodies = new List<Rigidbody>();

        /// <summary>Ragdoll this character. Call after its animation scripts are disabled.</summary>
        public static RagdollCorpse Go(GameObject character, Vector3 hitDir, Vector3 hitPoint, float impulse)
        {
            // An animated human (no primitive rig): the humanoid ragdoll
            if (character.transform.Find("Rig") == null && character.TryGetComponent<Animator>(out var a) && a.isHuman) { HumanRagdoll.Go(character, hitDir, hitPoint, impulse); return null; }
            var c = character.GetComponent<RagdollCorpse>();
            if (c == null) c = character.AddComponent<RagdollCorpse>();
            c.Build(hitDir, hitPoint, impulse);
            return c;
        }

        void Build(Vector3 hitDir, Vector3 hitPoint, float impulse)
        {
            var rig = transform.Find("Rig");
            if (rig == null) return;
            var body = rig.Find("Body");
            if (body == null) return;
            // The root's own body (used while alive for kinematic movement) stays still
            var rootRb = GetComponent<Rigidbody>();
            if (rootRb != null) { rootRb.isKinematic = true; rootRb.detectCollisions = false; }

            var torso = Segment(body, 30f, null, 0f, 0f, 0f);
            var head = body.Find("Head");
            if (head != null) Segment(head, 5f, torso, 25f, 35f, 30f);
            foreach (var side in new[] { "ArmL", "ArmR" })
            {
                var arm = body.Find(side);
                if (arm == null) continue;
                var upper = Segment(arm, 2.5f, torso, 70f, 60f, 40f);
                var elbow = arm.Find("Elbow");
                if (elbow != null) Segment(elbow, 2f, upper, 5f, 90f, 10f);
            }
            foreach (var side in new[] { "LegL", "LegR" })
            {
                var leg = rig.Find(side);
                if (leg == null) continue;
                var thigh = Segment(leg, 7f, torso, 40f, 50f, 20f);
                var knee = leg.Find("Knee");
                if (knee != null) Segment(knee, 5f, thigh, 0f, 100f, 5f);
            }


            // The killing blow: push the segment nearest the hit, plus a little spin so no two fall alike
            Rigidbody hitRb = torso; float best = float.MaxValue;
            foreach (var rb in bodies)
            {
                float d = (rb.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (d < best) { best = d; hitRb = rb; }
            }
            var dir = hitDir.sqrMagnitude > 0.01f ? hitDir.normalized : -transform.forward;
            if (hitRb != null) hitRb.AddForceAtPosition(dir * impulse * 0.6f, hitPoint, ForceMode.Impulse);
            if (torso != null)
            {
                torso.AddForce(dir * impulse * 0.4f, ForceMode.Impulse);
                torso.AddTorque(Random.insideUnitSphere * impulse * 0.05f, ForceMode.Impulse);
            }
            StartCoroutine(Settle());
        }

        Rigidbody Segment(Transform t, float mass, Rigidbody parent, float twist, float swing1, float swing2)
        {
            if (t.GetComponentInChildren<Collider>() == null)
            {
                // No hit colliders on this part (a non-combat rig): give it a small box so it has something to land on
                var box = t.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.18f, 0.3f, 0.18f);
                box.center = new Vector3(0f, -0.15f, 0f);
            }
            var rb = t.gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12;
            rb.isKinematic = false;
            bodies.Add(rb);
            // The player can drag the body by any part (hold E)
            if (t.GetComponent<Alcax.Items.Grabbable>() == null) { var g = t.gameObject.AddComponent<Alcax.Items.Grabbable>(); g.label = "BODY"; g.carryMass = 4f; }
            if (parent != null)
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.enableProjection = true;
                j.lowTwistLimit = new SoftJointLimit { limit = -twist };
                j.highTwistLimit = new SoftJointLimit { limit = twist };
                j.swing1Limit = new SoftJointLimit { limit = swing1 };
                j.swing2Limit = new SoftJointLimit { limit = swing2 };
            }
            return rb;
        }

IEnumerator Settle()
        {
            // Let it fall loose, then let it rest: heavier damping so it stops twitching, but it stays physical (pushable)
            yield return new WaitForSeconds(3f);
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 2.5f;
            }
        }
    }
}
