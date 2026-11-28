using Alcax.AI;
using Alcax.Characters;
using Alcax.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Operatives with animated human bodies: a Humanoid character model on AC_Human with HumanMotor (follow drive),
    /// HumanWeaponRig, and HumanOperativeBody, hit colliders on the bones (HitZone multipliers, also the ragdoll's
    /// shapes), the weapon copied from the mannequin prefab of the same archetype (grips and muzzle placed), and the
    /// mind (Operative), agent, health, and dialogue copied from it too. Menu: Alcax > Art > Build Human Operatives.
    /// </summary>
    public static class HumanOperativeAssets
    {
        const string PrefabDir = Root + "/Prefabs/AI";

        [MenuItem("Alcax/Art/Build Human Operatives")]
        public static string BuildAll()
        {
            // Station security: one prefab per look (OP_HumanGuard, OP_HumanGuard02, ...), same mind and gear
            var made = new System.Collections.Generic.List<string>();
            string[] looks = { "SK_Human_Security01", "SK_Human_Security02", "SK_Human_Security03" };
            for (int i = 0; i < looks.Length; i++)
            {
                var p = Build("Guard", $"{Root}/Art/Models/Characters/{looks[i]}/{looks[i]}.fbx", i == 0 ? "" : (i + 1).ToString("00"));
                if (p != null) made.Add(p.name);
            }
            AssetDatabase.SaveAssets();
            return string.Join(", ", made);
        }

        public static GameObject Build(string archetypeName, string modelPath, string variant = "")
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/OP_{archetypeName}.prefab");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null || model == null) { Debug.LogWarning("[HumanOperatives] missing OP_" + archetypeName + " or " + modelPath); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "OP_Human" + archetypeName + variant;
            var anim = go.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HumanAnimationAssets.ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = false;

            HitColliders(anim);

            // Body
            go.AddComponent<HumanMotor>().drive = MotorDrive.Follow;
            var rig = go.AddComponent<HumanWeaponRig>();
            go.AddComponent<HumanOperativeBody>();
            go.AddComponent<CharacterOutfit>().Discover();   // removable clothing, lootable off the body

            // Weapon: the mannequin's, without its colliders
            var srcWeapon = source.transform.Find("Rig/Body/Weapon");
            var srcOp = source.GetComponent<Operative>();
            var arc = new SerializedObject(srcOp).FindProperty("archetype").objectReferenceValue as EnemyArchetype;
            if (srcWeapon != null)
            {
                var w = Object.Instantiate(srcWeapon.gameObject, go.transform).transform;
                w.name = "Weapon";
                foreach (var c in w.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                bool pistol = arc != null && arc.weaponName == "Pistol";
                rig.Kind = pistol ? HandWeapon.Pistol : HandWeapon.Rifle;
                rig.Weapon = w;
            }

            // Physics and navigation like the mannequin's
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = 80f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var agent = go.AddComponent<NavMeshAgent>();
            CopyComponent(source.GetComponent<NavMeshAgent>(), agent);
            CopyComponent(source.GetComponent<Health>(), go.AddComponent<Health>());
            var srcNpc = source.GetComponent<Alcax.Dialogue.Npc>();
            if (srcNpc != null) CopyComponent(srcNpc, go.AddComponent<Alcax.Dialogue.Npc>());
            CopyComponent(srcOp, go.AddComponent<Operative>());
            SetLayerRecursive(go, CharacterRig.ActorLayer);

            EnsureFolder(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/OP_Human{archetypeName}{variant}.prefab");
            Object.DestroyImmediate(go);
            Debug.Log($"[HumanOperatives] OP_Human{archetypeName}{variant}: weapon {rig.Kind}");
            return prefab;
        }

        static void CopyComponent(Component from, Component to)
        {
            if (from == null || to == null) return;
            EditorUtility.CopySerialized(from, to);
        }

        /// <summary>Capsules (and a head sphere) on the bones, each a HitZone: damage multipliers and the ragdoll's shapes.</summary>
        public static void HitColliders(Animator a)
        {
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            void Capsule(HumanBodyBones on, Vector3 from, Vector3 to, float radius, float zone, string name)
            {
                var bone = B(on);
                if (bone == null) return;
                var g = new GameObject("HB_" + name);
                g.transform.SetParent(bone, false);
                g.transform.position = (from + to) * 0.5f;
                g.transform.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
                var c = g.AddComponent<CapsuleCollider>();
                c.direction = 1;
                c.radius = radius;
                c.height = (to - from).magnitude + radius * 2f;
                var hz = g.AddComponent<HitZone>();
                hz.damageMultiplier = zone;
                hz.isHead = false;
            }
            var hips = B(HumanBodyBones.Hips); var spine = B(HumanBodyBones.Spine); var chest = B(HumanBodyBones.Chest);
            var neck = B(HumanBodyBones.Neck); var head = B(HumanBodyBones.Head);
            var lul = B(HumanBodyBones.LeftUpperLeg); var rul = B(HumanBodyBones.RightUpperLeg);
            // Hips across the pelvis, torso from the spine to the neck
            Capsule(HumanBodyBones.Hips, lul.position + (lul.position - rul.position) * 0.15f, rul.position + (rul.position - lul.position) * 0.15f, 0.13f, 0.9f, "Hips");
            Capsule(HumanBodyBones.Chest, spine.position + Vector3.up * 0.05f, neck.position - Vector3.up * 0.06f, 0.16f, 1f, "Torso");
            // Head: a sphere a little above the head bone
            {
                var g = new GameObject("HB_Head");
                g.transform.SetParent(head, false);
                g.transform.position = head.position + Vector3.up * 0.08f + a.transform.forward * 0.02f;
                var s = g.AddComponent<SphereCollider>();
                s.radius = 0.11f;
                var hz = g.AddComponent<HitZone>();
                hz.damageMultiplier = 2.2f; hz.isHead = true;
            }
            foreach (var side in new[] { 0, 1 })
            {
                bool l = side == 0;
                var ua = B(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var la = B(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hd = B(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var ul = B(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var ll = B(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var ft = B(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                string s = l ? "L" : "R";
                Capsule(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, ua.position, la.position, 0.055f, 0.6f, "UpperArm" + s);
                Capsule(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, la.position, hd.position, 0.045f, 0.5f, "ForeArm" + s);
                Capsule(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, ul.position, ll.position, 0.08f, 0.7f, "Thigh" + s);
                Capsule(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, ll.position, ft.position, 0.06f, 0.6f, "Shin" + s);
            }
        }
    }
}
