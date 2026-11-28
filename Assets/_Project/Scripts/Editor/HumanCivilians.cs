using Alcax.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Gives the talkable people in the open scene human bodies: each one built on the primitive rig (a "Rig" child
    /// with an Npc) keeps its root and all its logic (Npc, AmbientNpc, Civilian, Health, loot, agent), and its rig is
    /// swapped for a human model (a woman's for the rigs with a bun, else a man's; the models alternate so neighbours
    /// differ) with AC_Human, hit colliders, HumanMotor, HumanWeaponRig, HumanNpcBody, and CharacterOutfit (lootable
    /// clothes). The Atrium builder runs it after placing everyone. Menu: Alcax > Art > Humanize Civilians.
    /// </summary>
    public static class HumanCivilians
    {
        static readonly string[] Women = { "SK_Human_Civ01", "SK_Human_Civ04", "SK_Human_Civ06", "SK_Human_Civ07" };
        static readonly string[] Men = { "SK_Human_Civ02", "SK_Human_Civ03", "SK_Human_Civ05", "SK_Human_Civ08" };
        // Who's who by first name (the station's people); anyone else: a bun on the old rig means a woman
        static readonly System.Collections.Generic.HashSet<string> WomenNames = new() { "Ama", "Lena", "Mei", "Dana", "Amara", "Yara", "Priya", "Selin", "Rhea", "Noor", "Zofia", "Ines", "Sefa" };
        static readonly System.Collections.Generic.HashSet<string> MenNames = new() { "Oskar", "Malik", "Tariq", "Ken", "Ivo", "Hugo", "Tomas", "Jun", "Elias", "Marco", "Diego", "Bayo" };

        [MenuItem("Alcax/Art/Humanize Civilians")]
        public static string HumanizeScene()
        {
            int n = 0, w = 0, m = 0;
            foreach (var npc in Object.FindObjectsByType<Alcax.Dialogue.Npc>(FindObjectsInactive.Include))
            {
                var rig = npc.transform.Find("Rig");
                bool human = npc.GetComponent<HumanNpcBody>() != null;
                if ((rig == null && !human) || npc.GetComponent<Alcax.AI.Operative>() != null) continue;
                var first = npc.displayName.Replace("Dr. ", "").Split(' ')[0];
                bool woman = WomenNames.Contains(first) || (!MenNames.Contains(first) && rig != null && FindDeep(rig, "Bun") != null);
                if (human && !WomenNames.Contains(first) && !MenNames.Contains(first)) continue;   // (already human and nothing better to go on)
                var model = woman ? Women[w++ % Women.Length] : Men[m++ % Men.Length];
                if (Humanize(npc.gameObject, model)) n++;
            }
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"{n} people given human bodies";
        }

        public static bool Humanize(GameObject root, string modelName)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Art/Models/Characters/{modelName}/{modelName}.fbx");
            if (model == null) { Debug.LogWarning("[HumanCivilians] missing " + modelName); return false; }
            int layer = root.layer;
            var oldRig = root.transform.Find("Rig");
            if (oldRig != null) Object.DestroyImmediate(oldRig.gameObject);
            // (re-run: an earlier human model's skeleton and meshes go too)
            var drop = new System.Collections.Generic.List<GameObject>();
            foreach (Transform c in root.transform) if (c.name.StartsWith("SK_Human_") || c.name.StartsWith("Human_") || string.Equals(c.name, "root", System.StringComparison.OrdinalIgnoreCase)) drop.Add(c.gameObject);   // (the skeleton is named after its model)
            foreach (var d in drop) Object.DestroyImmediate(d);

            // The model's skeleton and meshes go straight under the person's root (the avatar's paths stay the same)
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var avatar = inst.GetComponent<Animator>().avatar;
            var kids = new System.Collections.Generic.List<Transform>();
            foreach (Transform k in inst.transform) kids.Add(k);
            foreach (var k in kids) k.SetParent(root.transform, false);
            Object.DestroyImmediate(inst);

            var anim = root.GetComponent<Animator>();
            if (anim == null) anim = root.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HumanAnimationAssets.ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            anim.Rebind();

            HumanOperativeAssets.HitColliders(anim);
            if (root.GetComponent<HumanMotor>() == null) root.AddComponent<HumanMotor>().drive = MotorDrive.Follow;
            if (root.GetComponent<HumanWeaponRig>() == null) root.AddComponent<HumanWeaponRig>();
            if (root.GetComponent<HumanNpcBody>() == null) root.AddComponent<HumanNpcBody>();
            var outfit = root.GetComponent<CharacterOutfit>();
            if (outfit == null) outfit = root.AddComponent<CharacterOutfit>();
            outfit.Discover();
            var oso = new SerializedObject(outfit); oso.FindProperty("varyColours").boolValue = true; oso.ApplyModifiedPropertiesWithoutUndo();   // (a crowd not in matching outfits)
            foreach (var k in kids) SetLayerRecursive(k.gameObject, layer);
            EditorUtility.SetDirty(root);
            return true;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
