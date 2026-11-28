using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Puts the test officer (SK_Human_Security01 on AC_Human with HumanNPC) into the open scene with a small patrol
    /// loop snapped to the NavMesh. Replaces an earlier copy. Menu: Alcax > Art > Place Test Officer.
    /// </summary>
    public static class HumanNPCPlacement
    {
        const string Model = Root + "/Art/Models/Characters/SK_Human_Security01/SK_Human_Security01.fbx";

        [MenuItem("Alcax/Art/Place Test Officer")]
        public static string Place() => Place(new Vector3(-6f, 0f, 3f), new Vector3(6f, 0f, 3f), new Vector3(5f, 0f, -6f), new Vector3(-5f, 0f, -6f));

        public static string Place(params Vector3[] points)
        {
            var old = GameObject.Find("_Officer"); if (old) Object.DestroyImmediate(old);
            var root = new GameObject("_Officer");
            var pts = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
                var t = new GameObject("Stop" + i).transform;
                t.SetParent(root.transform);
                t.position = p;
                var nextP = points[(i + 1) % points.Length];
                t.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, nextP - points[i]).normalized);   // face sideways at each stop
                pts[i] = t;
            }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            go.name = "Officer_Security01";
            go.transform.SetPositionAndRotation(pts[0].position, pts[0].rotation);
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HumanAnimationAssets.ControllerPath);
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f); col.height = 1.8f; col.radius = 0.28f;
            var npc = go.AddComponent<Alcax.Characters.HumanNPC>();
            npc.SetPatrol(pts);
            var so = new SerializedObject(npc);
            var arr = so.FindProperty("patrol");
            arr.arraySize = pts.Length;
            for (int i = 0; i < pts.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = pts[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveScene(go.scene);
            return $"officer at {pts[0].position}, avatar {(anim.avatar ? anim.avatar.isHuman.ToString() : "none")}";
        }
    }
}
