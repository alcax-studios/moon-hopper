using Alcax.Combat;
using Alcax.Loot;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// The plasma carbine's game assets from its pipeline model (Art/Models/Weapons/SM_PlasmaCarbine): weapon data,
    /// first-person view model (with PlasmaRig), world pickup, and the energy-cell ammo pickup. Called by
    /// Alcax > Rebuild Weapon Assets; needs the FX library (Alcax > Rebuild FX Assets).
    /// </summary>
    public static class PlasmaCarbineAssets
    {
        public const string Model = Root + "/Art/Models/Weapons/SM_PlasmaCarbine/SM_PlasmaCarbine.fbx";
        public static WeaponData Data;

        public static void Build()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (fbx == null) { Debug.LogWarning("[PlasmaCarbine] model missing: " + Model); return; }
            if (AssetDatabase.LoadAssetAtPath<Alcax.FX.FXLibrary>(Root + "/Resources/FXLibrary.asset") == null) FXAssets.BuildAll();

            // The plasma fluid behind the barrel glass and in the cell (animated shader), then the baked set for the rest
            var folder = System.IO.Path.GetDirectoryName(Model).Replace('\\', '/');
            EnsureFolder(folder + "/Materials");
            var fluidPath = folder + "/Materials/M_PlasmaFluid.mat";
            var fluid = AssetDatabase.LoadAssetAtPath<Material>(fluidPath);
            if (fluid == null) { fluid = new Material(Shader.Find("Alcax/Plasma Fluid")); AssetDatabase.CreateAsset(fluid, fluidPath); }
            fluid.shader = Shader.Find("Alcax/Plasma Fluid");
            EditorUtility.SetDirty(fluid);
            AssetDatabase.SaveAssets();
            ArtMaterials.Build(folder);
            fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Model);

            string path = $"{Root}/Data/Weapons/W_PlasmaCarbine.asset";
            Data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (Data == null) { Data = ScriptableObject.CreateInstance<WeaponData>(); AssetDatabase.CreateAsset(Data, path); }
            var w = Data;
            w.displayName = "Plasma Carbine"; w.kind = WeaponKind.Plasma; w.sound = WeaponSound.Rifle;
            w.damage = 24f; w.range = 160f; w.cooldown = 0.13f; w.automatic = true; w.pellets = 1; w.spread = 0.55f;
            w.magazineSize = 32; w.ammoType = AmmoType.Energy; w.startingReserve = 64; w.reloadTime = 2.0f; w.reloadPerShell = false; w.noiseRadius = 55f;
            w.knockback = 1.0f; w.recoil = 1.0f; w.shake = 0.09f; w.hitStop = 0f;
            w.boltSpeed = 120f; w.heatPerShot = 0.075f; w.coolRate = 0.55f; w.overheatTime = 1.8f;

            // View model: held like the rifle (grip at the hand), the model's own pivots drive the parts
            var vm = new GameObject("VM_PlasmaCarbine");
            var grip = Node("Grip", vm.transform, new Vector3(0.19f, -0.205f, 0.33f), new Vector3(0f, -3f, 0f));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.transform.SetParent(grip, false);
            Node("Sight", model.transform, new Vector3(0f, 0.1425f, 0.123f));   // the optic's center: aiming lines it up
            WeaponAssets.Hand(grip, "HandR", new Vector3(0f, -0.03f, -0.005f), new Vector3(16f, 0f, 0f));
            WeaponAssets.Hand(grip, "HandL", new Vector3(-0.014f, 0.005f, 0.4f), new Vector3(-5f, 12f, 65f));
            vm.AddComponent<PlasmaRig>();
            w.viewModel = WeaponAssets.SavePrefab(vm, $"{Root}/Prefabs/ViewModels/VM_PlasmaCarbine.prefab");
            EditorUtility.SetDirty(w);

            // World pickup
            {
                var p = WeaponAssets.PickupBase("P_PlasmaCarbine", out var vis);
                p.kind = PickupKind.Weapon;
                p.weapon = w;
                var holder = Node("Model", vis, Vector3.zero, new Vector3(0f, 30f, 0f), Vector3.one);
                var m = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                m.transform.SetParent(holder, false);
                m.transform.localPosition = new Vector3(0f, 0f, -0.15f);
                WeaponAssets.SavePickup(p, "P_PlasmaCarbine");
            }
            // Energy cells: two of the carbine's own cells
            {
                var p = WeaponAssets.PickupBase("P_EnergyCell", out var vis);
                p.kind = PickupKind.Ammo; p.ammoType = AmmoType.Energy; p.amount = 32;
                Mesh cell = null; Material mat = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Model))
                    if (o is Mesh mesh && mesh.name == "Mag") cell = mesh;
                var mr = fbx.transform.Find("Mag")?.GetComponent<MeshRenderer>();
                if (mr != null) mat = mr.sharedMaterial;
                for (int i = 0; i < 2; i++)
                {
                    var c = Node("Cell" + i, vis, new Vector3(-0.03f + i * 0.06f, 0.05f, -0.12f), new Vector3(0f, 0f, i == 0 ? -6f : 4f));
                    c.gameObject.AddComponent<MeshFilter>().sharedMesh = cell;
                    c.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                WeaponAssets.SavePickup(p, "P_EnergyCell");
            }
            Debug.Log("[PlasmaCarbine] weapon, view model, and pickups built");
        }
    }
}
