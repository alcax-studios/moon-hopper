using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Human animation setup: imports the Quaternius Universal Animation Library clips (Art/Animations/UAL, CC0) as
    /// Humanoid (clean names, loops on *_Loop, in-place), and builds AC_Human: a speed-driven locomotion blend (idle,
    /// walk, jog, sprint), idle poses (talking, folded arms, phone, rail), sitting, interact, hit reactions, and death.
    /// Any Humanoid character can use it (retargeting). Menu: Alcax > Art > Build Human Animation.
    /// </summary>
    public static class HumanAnimationAssets
    {
        const string Dir = Root + "/Art/Animations";
        public const string ControllerPath = Dir + "/AC_Human.controller";

        [MenuItem("Alcax/Art/Build Human Animation")]
        public static void BuildAll()
        {
            foreach (var f in new[] { "A_UAL1", "A_UAL2" }) ImportLibrary($"{Dir}/UAL/{f}.fbx");
            BuildController();
            AssetDatabase.SaveAssets();
        }

        static readonly Dictionary<string, float> HeadingFix = new Dictionary<string, float>
        {
            { "Jog_Fwd_Loop", 29f }, { "Sprint_Loop", 20f }, { "Walk_Formal_Loop", 5f },
        };

        static void ImportLibrary(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) { Debug.LogWarning("[HumanAnimation] missing " + path); return; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importCameras = false; mi.importLights = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.name.Split('|').Last();
                c.name = n;
                c.loopTime = n.EndsWith("_Loop") || n == "Sword_Idle" || n == "Pistol_Aim_Neutral";
                c.loopPose = c.loopTime;
                // In place: the character controller and the NavMesh agent move the body
                c.lockRootRotation = true; c.keepOriginalOrientation = false;   // based on the body: the library's root faces backwards
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                // Some gaits run with the pelvis turned off their line (the jog ~29 deg, the sprint ~20): turn the
                // whole clip back so the body faces where it goes (measured on a bare Animator, hips heading)
                if (HeadingFix.TryGetValue(n, out var fix)) c.rotationOffset = fix;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
            Debug.Log($"[HumanAnimation] {System.IO.Path.GetFileName(path)}: {clips.Length} clips");
        }

        static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        static AnimationClip Clip(string name)
        {
            if (clips.Count == 0)
                foreach (var f in new[] { "A_UAL1", "A_UAL2" })
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/UAL/{f}.fbx"))
                        if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clips[c.name] = c;
            clips.TryGetValue(name, out var clip);
            if (clip == null) Debug.LogWarning("[HumanAnimation] no clip " + name);
            return clip;
        }

        static void BuildController()
        {
            clips.Clear();
            // Rebuild in place (keeps the GUID, so characters already using it stay hooked up)
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in ac.parameters) ac.RemoveParameter(p);
            while (ac.layers.Length > 0) ac.RemoveLayer(0);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != ac && sub != null && !(sub is AnimatorController)) Object.DestroyImmediate(sub, true);   // orphaned states, trees
            ac.AddLayer("Base Layer");
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Pose", AnimatorControllerParameterType.Int);       // 0 none, 1 talk, 2 fold arms, 3 phone, 4 rail
            ac.AddParameter("Sitting", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Interact", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("HitHead", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Crouch", AnimatorControllerParameterType.Float);
            ac.AddParameter("HitReact", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("HitKind", AnimatorControllerParameterType.Int);
            ac.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("PunchKind", AnimatorControllerParameterType.Int);   // 0 jab, 1 cross, 2 hook
            ac.AddParameter("PunchBody", AnimatorControllerParameterType.Trigger);   // (the torso layer's copy of Punch: a trigger is used up by one layer)
            ac.AddParameter("Fists", AnimatorControllerParameterType.Bool);
            ac.AddParameter(new AnimatorControllerParameter { name = "Dir", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            var layer = ac.layers[0];
            layer.iKPass = true;    // look-at and foot IK (HumanMotor.OnAnimatorIK)
            var layers = ac.layers; layers[0] = layer; ac.layers = layers;
            var sm = ac.layers[0].stateMachine;

            // Locomotion: standing (idle, walk, jog, sprint by Speed) and crouched (idle, crouch walk), blended by
            // Crouch. Dir (1 or -1) multiplies the playback: -1 backpedals (HumanMotor, follow drive).
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Crouch";
            tree.useAutomaticThresholds = false;
            var standTree = tree.CreateBlendTreeChild(0f);
            standTree.name = "Stand";
            standTree.blendType = BlendTreeType.Simple1D;
            standTree.blendParameter = "Speed";
            standTree.useAutomaticThresholds = false;
            standTree.AddChild(Clip("Idle_Loop"), 0f);
            standTree.AddChild(Clip("Walk_Formal_Loop"), 1.4f);   // the plain Walk_Loop swaggers
            standTree.AddChild(Clip("Jog_Fwd_Loop"), 3.4f);
            standTree.AddChild(Clip("Sprint_Loop"), 6.2f);
            var crouchTree = tree.CreateBlendTreeChild(1f);
            crouchTree.name = "Crouch";
            crouchTree.blendType = BlendTreeType.Simple1D;
            crouchTree.blendParameter = "Speed";
            crouchTree.useAutomaticThresholds = false;
            crouchTree.AddChild(Clip("Crouch_Idle_Loop"), 0f);
            crouchTree.AddChild(Clip("Crouch_Fwd_Loop"), 1.4f);
            loco.speedParameterActive = true;
            loco.speedParameter = "Dir";
            sm.defaultState = loco;

            AnimatorState State(string name, string clip, float speed = 1f)
            {
                var s = sm.AddState(name);
                s.motion = Clip(clip);
                s.speed = speed;
                return s;
            }
            void Trans(AnimatorState from, AnimatorState to, float dur, params (AnimatorConditionMode mode, float th, string p)[] conds)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = false; t.duration = dur;
                foreach (var c in conds) t.AddCondition(c.mode, c.th, c.p);
            }

            // Idle poses (only while standing still)
            var poses = new[] { ("Talk", "Idle_Talking_Loop", 1), ("FoldArms", "Idle_FoldArms_Loop", 2), ("Phone", "Idle_TalkingPhone_Loop", 3), ("Rail", "Idle_Rail_Loop", 4) };
            foreach (var (name, clip, id) in poses)
            {
                var s = State(name, clip);
                Trans(loco, s, 0.35f, (AnimatorConditionMode.Equals, id, "Pose"), (AnimatorConditionMode.Less, 0.1f, "Speed"));
                Trans(s, loco, 0.3f, (AnimatorConditionMode.NotEqual, id, "Pose"));
                Trans(s, loco, 0.25f, (AnimatorConditionMode.Greater, 0.1f, "Speed"));
            }

            // Sitting: enter, idle, exit
            var sitIn = State("SitDown", "Sitting_Enter");
            var sit = State("Sit", "Sitting_Idle_Loop");
            var sitTalk = State("SitTalk", "Sitting_Talking_Loop");
            var sitOut = State("StandUp", "Sitting_Exit");
            Trans(loco, sitIn, 0.25f, (AnimatorConditionMode.If, 0, "Sitting"));
            var t1 = sitIn.AddTransition(sit); t1.hasExitTime = true; t1.exitTime = 0.95f; t1.duration = 0.1f;
            Trans(sit, sitTalk, 0.4f, (AnimatorConditionMode.Equals, 1, "Pose"));
            Trans(sitTalk, sit, 0.4f, (AnimatorConditionMode.NotEqual, 1, "Pose"));
            Trans(sit, sitOut, 0.2f, (AnimatorConditionMode.IfNot, 0, "Sitting"));
            Trans(sitTalk, sitOut, 0.2f, (AnimatorConditionMode.IfNot, 0, "Sitting"));
            var t2 = sitOut.AddTransition(loco); t2.hasExitTime = true; t2.exitTime = 0.9f; t2.duration = 0.2f;

            // Interact (press a button, take something), then back
            var interact = State("Interact", "Interact");
            Trans(loco, interact, 0.15f, (AnimatorConditionMode.If, 0, "Interact"));
            var t3 = interact.AddTransition(loco); t3.hasExitTime = true; t3.exitTime = 0.85f; t3.duration = 0.25f;

            // Hit reactions from anywhere upright, then back to moving
            var hitChest = State("HitChest", "Hit_Chest");
            var hitHead = State("HitHead", "Hit_Head");
            foreach (var (s, head) in new[] { (hitChest, false), (hitHead, true) })
            {
                var t = sm.AddAnyStateTransition(s);
                t.hasExitTime = false; t.duration = 0.08f; t.canTransitionToSelf = true;
                t.AddCondition(AnimatorConditionMode.If, 0, "Hit");
                t.AddCondition(head ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "HitHead");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                var back = s.AddTransition(loco); back.hasExitTime = true; back.exitTime = 0.8f; back.duration = 0.2f;
            }

            // Death (HumanNPC hands off to the ragdoll partway through)
            var death = State("Death", "Death01");
            var td = sm.AddAnyStateTransition(death);
            td.hasExitTime = false; td.duration = 0.1f; td.canTransitionToSelf = false;
            td.AddCondition(AnimatorConditionMode.If, 0, "Dead");

            // Upper-body hit reactions on their own additive layer (spine, head, arms; the legs keep walking): HumanMotor
            // fires HitReact with HitKind (0 chest, 1 head, 2 knocked back) and drives the layer's weight
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Dir + "/AM_UpperBody.mask");
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, Dir + "/AM_UpperBody.mask"); }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                    || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm);
            EditorUtility.SetDirty(mask);
            ac.AddLayer("Upper");
            var ls = ac.layers;
            ls[1].avatarMask = mask;
            ls[1].blendingMode = AnimatorLayerBlendingMode.Additive;   // (only the change from the clip's first frame: a recoil on top of whatever the body is doing)
            ls[1].defaultWeight = 0f;
            ac.layers = ls;
            var usm = ac.layers[1].stateMachine;
            var empty = usm.AddState("Empty");
            usm.defaultState = empty;
            foreach (var (name, clip, kind) in new[] { ("HitChest", "Hit_Chest", 0), ("HitHead", "Hit_Head", 1), ("HitKnock", "Hit_Knockback", 2) })
            {
                var s = usm.AddState(name);
                s.motion = Clip(clip);
                var t = usm.AddAnyStateTransition(s);
                t.hasExitTime = false; t.duration = 0.04f; t.canTransitionToSelf = true;
                t.AddCondition(AnimatorConditionMode.If, 0, "HitReact");
                t.AddCondition(AnimatorConditionMode.Equals, kind, "HitKind");
                var back = s.AddTransition(empty); back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.12f;
            }

            // Fists: the library's boxing on two layers. Arms (override, arms and fingers): the guard (the jab's first
            // frame, held) while Fists is on, and the jab, cross and hook themselves. Torso (additive, spine and head):
            // only the change each punch makes from its first frame (the shoulder turning through, the weight going
            // forward), so the boxing stance's crouch the clips start from never squats the body. HumanMotor.Punch
            // fires Punch and PunchBody with PunchKind; HumanMotor.Fists sets Fists and fades the arms layer in.
            var armsMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Dir + "/AM_Arms.mask");
            if (armsMask == null) { armsMask = new AvatarMask(); AssetDatabase.CreateAsset(armsMask, Dir + "/AM_Arms.mask"); }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                armsMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers);
            EditorUtility.SetDirty(armsMask);
            ac.AddLayer("Arms");
            var al = ac.layers;
            al[2].avatarMask = armsMask;
            al[2].blendingMode = AnimatorLayerBlendingMode.Override;
            al[2].defaultWeight = 0f;
            ac.layers = al;
            var asm = ac.layers[2].stateMachine;
            var armsIdle = asm.AddState("Empty");
            asm.defaultState = armsIdle;
            var guard = asm.AddState("Guard");
            guard.motion = Clip("Punch_Jab");
            guard.speed = 0f;   // (held on its first frame: the boxing guard)
            var tg = armsIdle.AddTransition(guard); tg.hasExitTime = false; tg.duration = 0.2f; tg.AddCondition(AnimatorConditionMode.If, 0, "Fists");
            var te = guard.AddTransition(armsIdle); te.hasExitTime = false; te.duration = 0.25f; te.AddCondition(AnimatorConditionMode.IfNot, 0, "Fists");
            var punches = new[] { ("Jab", "Punch_Jab", 0), ("Cross", "Punch_Cross", 1), ("Hook", "Melee_Hook", 2) };
            foreach (var (name, clip, kind) in punches)
            {
                var s = asm.AddState(name);
                s.motion = Clip(clip);
                s.speed = 1.25f;   // (snappier than the library's)
                var t = asm.AddAnyStateTransition(s);
                t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = true;
                t.AddCondition(AnimatorConditionMode.If, 0, "Punch");
                t.AddCondition(AnimatorConditionMode.Equals, kind, "PunchKind");
                var back = s.AddTransition(guard); back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.15f;
            }

            var torsoMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Dir + "/AM_Torso.mask");
            if (torsoMask == null) { torsoMask = new AvatarMask(); AssetDatabase.CreateAsset(torsoMask, Dir + "/AM_Torso.mask"); }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                torsoMask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head);
            EditorUtility.SetDirty(torsoMask);
            ac.AddLayer("Torso");
            var tl = ac.layers;
            tl[3].avatarMask = torsoMask;
            tl[3].blendingMode = AnimatorLayerBlendingMode.Additive;
            tl[3].defaultWeight = 1f;
            ac.layers = tl;
            var tsm = ac.layers[3].stateMachine;
            var torsoIdle = tsm.AddState("Empty");
            tsm.defaultState = torsoIdle;
            foreach (var (name, clip, kind) in punches)
            {
                var s = tsm.AddState(name);
                s.motion = Clip(clip);
                s.speed = 1.25f;
                var t = tsm.AddAnyStateTransition(s);
                t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = true;
                t.AddCondition(AnimatorConditionMode.If, 0, "PunchBody");
                t.AddCondition(AnimatorConditionMode.Equals, kind, "PunchKind");
                var back = s.AddTransition(torsoIdle); back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.15f;
            }
            EditorUtility.SetDirty(ac);
            Debug.Log("[HumanAnimation] AC_Human built");
        }
    }
}
