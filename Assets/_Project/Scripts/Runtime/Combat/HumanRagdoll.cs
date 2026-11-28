using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Combat
{
    /// <summary>
    /// Turns a dead Humanoid into a ragdoll: the Animator stops, every bone that carries a hit collider (a HitZone
    /// child) gets a rigidbody, joined to its nearest ragdoll ancestor with a CharacterJoint limited like the joint it
    /// is (knees and elbows bend one way, the neck and shoulders swing), and the killing hit pushes the bone it struck.
    /// It stays physical once it settles (heavier damping): it can be pushed, dragged, and picked up by any part.
    /// </summary>
    public class HumanRagdoll : MonoBehaviour
    {
        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        readonly List<(Collider a, Collider b)> parted = new List<(Collider, Collider)>();   // overlapping at death: ignored until apart

        public static HumanRagdoll Go(GameObject character, Vector3 hitDir, Vector3 hitPoint, float impulse)
        {
            var r = character.GetComponent<HumanRagdoll>();
            if (r == null) r = character.AddComponent<HumanRagdoll>();
            r.Build(hitDir, hitPoint, impulse);
            return r;
        }

        void Build(Vector3 hitDir, Vector3 hitPoint, float impulse)
        {
            var anim = GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return;
            var rootRb = GetComponent<Rigidbody>();
            if (rootRb != null) { rootRb.isKinematic = true; rootRb.detectCollisions = false; }
            anim.enabled = false;

            // (bone, mass kg, swing, twist, bends one way)
            var spec = new (HumanBodyBones bone, float mass, float swing, float twist, bool hinge)[]
            {
                (HumanBodyBones.Hips, 14f, 0f, 0f, false),
                (HumanBodyBones.Chest, 16f, 25f, 20f, false),
                (HumanBodyBones.Head, 5f, 60f, 50f, false),   // (a slumped head rests inside its range: lying on the limit, the solver kept bouncing it)
                (HumanBodyBones.LeftUpperArm, 2.5f, 80f, 45f, false), (HumanBodyBones.RightUpperArm, 2.5f, 80f, 45f, false),
                (HumanBodyBones.LeftLowerArm, 1.8f, 5f, 10f, true), (HumanBodyBones.RightLowerArm, 1.8f, 5f, 10f, true),
                (HumanBodyBones.LeftUpperLeg, 8f, 50f, 20f, false), (HumanBodyBones.RightUpperLeg, 8f, 50f, 20f, false),
                (HumanBodyBones.LeftLowerLeg, 4.5f, 5f, 5f, true), (HumanBodyBones.RightLowerLeg, 4.5f, 5f, 5f, true),
            };
            var made = new Dictionary<Transform, Rigidbody>();
            foreach (var s in spec)
            {
                var b = anim.GetBoneTransform(s.bone);
                if (b == null || b.GetComponentInChildren<Collider>() == null) continue;
                var rb = b.gameObject.GetComponent<Rigidbody>();
                if (rb == null) rb = b.gameObject.AddComponent<Rigidbody>();
                rb.mass = s.mass;
                rb.linearDamping = 0.1f; rb.angularDamping = 0.8f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12; rb.solverVelocityIterations = 4;   // (light parts on heavy ones: a stiffer solve, no jitter)
                rb.sleepThreshold = 0.02f;
                made[b] = rb;
                bodies.Add(rb);
                // The player can drag the body by any part (hold E), like the mannequin corpses
                if (b.GetComponent<Alcax.Items.Grabbable>() == null) { var gr = b.gameObject.AddComponent<Alcax.Items.Grabbable>(); gr.label = "BODY"; gr.carryMass = 4f; }
                // Joint to the nearest ragdoll ancestor
                var p = b.parent;
                while (p != null && !made.ContainsKey(p)) p = p.parent;
                if (p == null) continue;
                var j = b.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = made[p];
                j.enableProjection = true;
                j.projectionAngle = 30f; j.projectionDistance = 0.05f;   // (only correct real separation, not every small error)
                j.enablePreprocessing = false;
                // Axis: bend across the body (knees and elbows hinge about the body's side axis)
                j.axis = b.InverseTransformDirection(transform.right);
                j.swingAxis = b.InverseTransformDirection(transform.forward);
                if (s.hinge)
                {
                    j.lowTwistLimit = new SoftJointLimit { limit = -120f, contactDistance = 4f };
                    j.highTwistLimit = new SoftJointLimit { limit = 5f, contactDistance = 4f };
                    j.swing1Limit = new SoftJointLimit { limit = s.swing, contactDistance = 4f };
                    j.swing2Limit = new SoftJointLimit { limit = s.twist, contactDistance = 4f };
                }
                else
                {
                    j.lowTwistLimit = new SoftJointLimit { limit = -s.twist, contactDistance = 4f };
                    j.highTwistLimit = new SoftJointLimit { limit = s.twist, contactDistance = 4f };
                    j.swing1Limit = new SoftJointLimit { limit = s.swing, contactDistance = 4f };
                    j.swing2Limit = new SoftJointLimit { limit = s.swing, contactDistance = 4f };
                }
            }
            // The body's own parts collide with each other (no arm sinking into the chest, no leg through the other),
            // except where that can only fight the joints: parts joined directly to each other (the joint holds
            // them), and colliders already overlapping as it goes limp (the neck's, on the chest, inside the head's:
            // that overlap pushed them apart forever, a head wobbling non-stop). Colliders not on a ragdoll part (the
            // root's) never touch it.
            var own = GetComponentsInChildren<Collider>();
            var partSet = new HashSet<Rigidbody>(bodies);
            // (the torso and the thighs meet across the hips: bending at the hips buries the torso's hit box in them)
            Rigidbody RbOf(HumanBodyBones hb) { var t = anim.GetBoneTransform(hb); return t != null && made.TryGetValue(t, out var r) ? r : null; }
            var chestRb = RbOf(HumanBodyBones.Chest);
            var thighs = new HashSet<Rigidbody> { RbOf(HumanBodyBones.LeftUpperLeg), RbOf(HumanBodyBones.RightUpperLeg) };
            for (int a = 0; a < own.Length; a++)
                for (int b = a + 1; b < own.Length; b++)
                {
                    var ca = own[a]; var cb = own[b];
                    if (ca.isTrigger || cb.isTrigger) continue;
                    var ra = ca.attachedRigidbody; var rb2 = cb.attachedRigidbody;
                    if (ra == rb2) continue;   // (same part: never collide anyway)
                    bool always = ra == null || rb2 == null || !partSet.Contains(ra) || !partSet.Contains(rb2) || Joined(ra, rb2)
                        || (chestRb != null && ((ra == chestRb && thighs.Contains(rb2)) || (rb2 == chestRb && thighs.Contains(ra))));
                    // (overlapping as it goes limp: let go of each other until they come apart, then collide again)
                    bool overlapping = !always && Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb, cb.transform.position, cb.transform.rotation, out _, out _);
                    if (overlapping) parted.Add((ca, cb));
                    bool ignore = always || overlapping;
                    if (ignore) Physics.IgnoreCollision(ca, cb, true);
                }
            // The limbs leave the pose the skinned meshes were bounded in: recompute their bounds, or hair and clothes
            // get culled when you look at the body up close
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            // Nothing else drives the bones now
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb != this && (mb is Alcax.Characters.HumanMotor || mb is Alcax.Characters.HumanWeaponRig)) mb.enabled = false;

            // The killing blow: push the bone nearest the hit, plus a little spin so no two fall alike
            Rigidbody hitRb = null; float best = float.MaxValue;
            foreach (var rb in bodies)
            {
                float d = (rb.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (d < best) { best = d; hitRb = rb; }
            }
            if (hitRb != null)
            {
                hitRb.AddForceAtPosition(hitDir.normalized * impulse, hitPoint, ForceMode.Impulse);
                hitRb.AddTorque(Random.insideUnitSphere * impulse * 0.05f, ForceMode.Impulse);
            }
            StartCoroutine(Settle());
            if (parted.Count > 0) StartCoroutine(Rejoin());
        }

        /// <summary>Parts that overlapped as the body went limp collide again once they've come apart (a hand that
        /// was on the chest can't later sink into it); the ones that never part (the neck in the head) stay ignored.</summary>
        IEnumerator Rejoin()
        {
            var wait = new WaitForSeconds(0.1f);
            float end = Time.time + 8f;
            while (parted.Count > 0 && Time.time < end)
            {
                yield return wait;
                for (int i = parted.Count - 1; i >= 0; i--)
                {
                    var (a, b) = parted[i];
                    if (a == null || b == null) { parted.RemoveAt(i); continue; }
                    // (apart now: from here on they push each other off instead of passing through)
                    var pa = a.transform.position; var qa = a.transform.rotation;
                    bool apart = !Physics.ComputePenetration(a, pa, qa, b, b.transform.position, b.transform.rotation, out _, out _);
                    if (apart) { Physics.IgnoreCollision(a, b, false); parted.RemoveAt(i); }
                }
            }
        }

        static bool Joined(Rigidbody a, Rigidbody b)
        {
            var ja = a.GetComponent<CharacterJoint>(); var jb = b.GetComponent<CharacterJoint>();
            return (ja != null && ja.connectedBody == b) || (jb != null && jb.connectedBody == a);
        }

        IEnumerator Settle()
        {
            // Let it fall loose, then let it rest: heavier damping so it stops twitching, but it stays physical
            // (it can be pushed, dragged, and picked up)
            yield return new WaitForSeconds(3f);
            foreach (var rb in bodies)
            {
                if (rb == null) continue;
                rb.linearDamping = 1.2f;
                rb.angularDamping = 2.5f;
                rb.maxAngularVelocity = 20f;
            }
            // At rest: once the trunk has been still for a second, put every part to sleep together (a joint
            // fighting its limit can't keep a lone head twitching); a push, a shot, or a grab wakes it as usual,
            // and it's put back to sleep when it's still again
            var hipsRb = bodies.Count > 0 ? bodies[0] : null;
            var wait = new WaitForSeconds(0.25f);
            float still = 0f;
            while (hipsRb != null)
            {
                yield return wait;
                if (hipsRb.IsSleeping() || Alcax.Player.PlayerGrabber.Holding) { still = 0f; continue; }   // (never while being dragged)
                bool calm = hipsRb.linearVelocity.sqrMagnitude < 0.06f * 0.06f && hipsRb.angularVelocity.sqrMagnitude < 0.3f * 0.3f;
                still = calm ? still + 0.25f : 0f;
                if (still >= 1f)
                {
                    foreach (var rb in bodies) if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.Sleep(); }
                    still = 0f;
                }
            }
        }
    }
}
