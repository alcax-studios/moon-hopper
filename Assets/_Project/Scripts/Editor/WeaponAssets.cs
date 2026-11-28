using Alcax.Combat;
using Alcax.Core;
using Alcax.Loot;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>Builds the weapon assets: weapon data, first-person view models, and world pickups (menu: Alcax > Rebuild Weapon Assets).</summary>
    public static class WeaponAssets
    {
        public static WeaponData Fists, Bat, Machete, Pistol, Shotgun, Rifle;
        public static Pickup PBat, PMachete, PPistol, PShotgun, PRifle, PAmmoLight, PShells, PAmmoRifle, PMedkit;

        static Material M(string n) => Mats.Get(n);

        [MenuItem("Alcax/Rebuild Weapon Assets")]
        public static void BuildAll()
        {
            BuildWeaponData();
            BuildPickups();
            PlasmaCarbineAssets.Build();   // the first asset from the art pipeline (Art/Models/Weapons/SM_PlasmaCarbine)
            AssetDatabase.SaveAssets();
        }

        // ---------- Weapons ----------

        static WeaponData Weapon(string file, System.Action<WeaponData> set)
        {
            string path = $"{Root}/Data/Weapons/{file}.asset";
            EnsureFolder($"{Root}/Data/Weapons");
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (w == null) { w = ScriptableObject.CreateInstance<WeaponData>(); AssetDatabase.CreateAsset(w, path); }
            set(w);
            EditorUtility.SetDirty(w);
            return w;
        }

        static void BuildWeaponData()
        {
            Fists = Weapon("W_Fists", w =>
            {
                w.displayName = "Fists"; w.kind = WeaponKind.Melee; w.sound = WeaponSound.Fists;
                w.damage = 12f; w.range = 1.7f; w.cooldown = 0.45f; w.windup = 0.07f; w.swingAngle = 70f; w.maxTargets = 1;
                w.knockback = 1.6f; w.shake = 0.15f; w.hitStop = 0.035f; w.ammoType = AmmoType.None; w.magazineSize = 0;
            });
            Bat = Weapon("W_Bat", w =>
            {
                w.displayName = "Baseball Bat"; w.kind = WeaponKind.Melee; w.sound = WeaponSound.Blunt;
                w.damage = 34f; w.range = 2.2f; w.cooldown = 0.7f; w.windup = 0.12f; w.swingAngle = 100f; w.maxTargets = 2;
                w.knockback = 3.2f; w.shake = 0.35f; w.hitStop = 0.065f; w.ammoType = AmmoType.None; w.magazineSize = 0;
            });
            Machete = Weapon("W_Machete", w =>
            {
                w.displayName = "Machete"; w.kind = WeaponKind.Melee; w.sound = WeaponSound.Blade;
                w.damage = 46f; w.range = 2.0f; w.cooldown = 0.55f; w.windup = 0.08f; w.swingAngle = 85f; w.maxTargets = 1;
                w.knockback = 1.4f; w.shake = 0.25f; w.hitStop = 0.05f; w.ammoType = AmmoType.None; w.magazineSize = 0;
            });
            Pistol = Weapon("W_Pistol", w =>
            {
                w.displayName = "Pistol"; w.kind = WeaponKind.Hitscan; w.sound = WeaponSound.Pistol;
                w.damage = 28f; w.range = 70f; w.cooldown = 0.17f; w.automatic = false; w.pellets = 1; w.spread = 0.8f;
                w.magazineSize = 9; w.ammoType = AmmoType.Light; w.startingReserve = 18; w.reloadTime = 1.35f; w.reloadPerShell = false; w.noiseRadius = 35f;
                w.knockback = 0.8f; w.recoil = 2.2f; w.shake = 0.12f; w.hitStop = 0.04f;
            });
            Shotgun = Weapon("W_Shotgun", w =>
            {
                w.displayName = "Shotgun"; w.kind = WeaponKind.Hitscan; w.sound = WeaponSound.Shotgun;
                w.damage = 14f; w.range = 28f; w.cooldown = 0.85f; w.automatic = false; w.pellets = 8; w.spread = 6.5f;
                w.magazineSize = 5; w.ammoType = AmmoType.Shells; w.startingReserve = 10; w.reloadTime = 0.5f; w.reloadPerShell = true; w.noiseRadius = 45f;
                w.knockback = 1.1f; w.recoil = 7f; w.shake = 0.45f; w.hitStop = 0.06f;
            });
            Rifle = Weapon("W_Rifle", w =>
            {
                // Automatic: hold to fire. Same family as the enemy rifles (13-15 damage, 0.1 s cycle, 30 rounds).
                w.displayName = "Rifle"; w.kind = WeaponKind.Hitscan; w.sound = WeaponSound.Rifle;
                w.damage = 20f; w.range = 110f; w.cooldown = 0.1f; w.automatic = true; w.pellets = 1; w.spread = 1.1f;
                w.magazineSize = 30; w.ammoType = AmmoType.Rifle; w.startingReserve = 60; w.reloadTime = 2.1f; w.reloadPerShell = false; w.noiseRadius = 65f;
                w.knockback = 0.6f; w.recoil = 1.15f; w.shake = 0.08f; w.hitStop = 0f;
            });
        }

        /// <summary>Weapon geometry with no hands, pointing along +Z (guns) or +Y (melee), grip at the origin.</summary>
        static void WeaponGeo(WeaponData w, Transform p)
        {
            if (w == Bat)
            {
                Prim(PrimitiveType.Cylinder, "Handle", p, new Vector3(0, 0.06f, 0), new Vector3(0.035f, 0.13f, 0.035f), Vector3.zero, M("Grip"));
                Prim(PrimitiveType.Cylinder, "Taper", p, new Vector3(0, 0.3f, 0), new Vector3(0.05f, 0.14f, 0.05f), Vector3.zero, M("WoodLight"));
                Prim(PrimitiveType.Cylinder, "Barrel", p, new Vector3(0, 0.6f, 0), new Vector3(0.075f, 0.2f, 0.075f), Vector3.zero, M("WoodLight"));
                Prim(PrimitiveType.Sphere, "Cap", p, new Vector3(0, 0.8f, 0), new Vector3(0.075f, 0.03f, 0.075f), Vector3.zero, M("WoodLight"));
                Node("Tip", p, new Vector3(0, 0.8f, 0));
                Prim(PrimitiveType.Cylinder, "Knob", p, new Vector3(0, -0.075f, 0), new Vector3(0.05f, 0.012f, 0.05f), Vector3.zero, M("Grip"));
                // A few nails: this bat has seen things.
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f + 20f;
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    Prim(PrimitiveType.Cube, "Nail" + i, p, new Vector3(0, 0.62f + i * 0.04f, 0) + dir * 0.04f,
                        new Vector3(0.008f, 0.008f, 0.05f), new Vector3(0, a, 0), M("Metal"));
                }
            }
            else if (w == Machete)
            {
                Prim(PrimitiveType.Cube, "Handle", p, new Vector3(0, 0.04f, 0), new Vector3(0.032f, 0.15f, 0.042f), Vector3.zero, M("CabinDark"));
                Prim(PrimitiveType.Cube, "Guard", p, new Vector3(0, 0.125f, 0), new Vector3(0.04f, 0.02f, 0.07f), Vector3.zero, M("MetalDark"));
                Prim(PrimitiveType.Cube, "Blade", p, new Vector3(0, 0.38f, 0.008f), new Vector3(0.012f, 0.48f, 0.065f), Vector3.zero, M("Metal"));
                Prim(PrimitiveType.Cube, "Tip", p, new Vector3(0, 0.63f, 0.0f), new Vector3(0.012f, 0.06f, 0.06f), new Vector3(35f, 0, 0), M("Metal"));
                Node("Tip", p, new Vector3(0, 0.64f, 0.02f));
                Prim(PrimitiveType.Cube, "Edge", p, new Vector3(0, 0.38f, 0.043f), new Vector3(0.006f, 0.46f, 0.012f), Vector3.zero, M("UIBone"));
            }
            else if (w == Pistol)
            {
                Prim(PrimitiveType.Cube, "Slide", p, new Vector3(0, 0.085f, 0.06f), new Vector3(0.042f, 0.05f, 0.2f), Vector3.zero, M("MetalDark"));
                Prim(PrimitiveType.Cube, "Frame", p, new Vector3(0, 0.05f, 0.04f), new Vector3(0.038f, 0.03f, 0.16f), Vector3.zero, M("Grip"));
                Prim(PrimitiveType.Cube, "Grip", p, new Vector3(0, -0.01f, -0.01f), new Vector3(0.038f, 0.11f, 0.05f), new Vector3(14f, 0, 0), M("Grip"));
                Prim(PrimitiveType.Cube, "Sight", p, new Vector3(0, 0.115f, 0.15f), new Vector3(0.01f, 0.012f, 0.01f), Vector3.zero, M("Accent"));
                Node("Muzzle", p, new Vector3(0, 0.085f, 0.17f));
                // Magazine: sits inside the grip, base plate showing. It drops out on reload.
                var mag = Node("Mag", p, new Vector3(0, -0.03f, -0.012f), new Vector3(14f, 0, 0));
                Prim(PrimitiveType.Cube, "MagBody", mag, Vector3.zero, new Vector3(0.03f, 0.1f, 0.038f), Vector3.zero, M("MetalDark"));
                Prim(PrimitiveType.Cube, "MagBase", mag, new Vector3(0, -0.056f, 0.002f), new Vector3(0.042f, 0.014f, 0.048f), Vector3.zero, M("Grip"));
                Prim(PrimitiveType.Cube, "MagRound", mag, new Vector3(0, 0.052f, 0.004f), new Vector3(0.012f, 0.01f, 0.024f), Vector3.zero, M("Brass"));
                // Spent casing template (hidden): cloned and ejected per shot.
                var casing = Node("CasingVis", p, new Vector3(0.026f, 0.09f, 0.05f));
                Prim(PrimitiveType.Cylinder, "Brass", casing, Vector3.zero, new Vector3(0.011f, 0.012f, 0.011f), new Vector3(0, 0, 90f), M("Brass"));
                casing.gameObject.SetActive(false);
            }
            else if (w == Rifle) RifleGeo(p);
            else if (w == Shotgun)
            {
                Prim(PrimitiveType.Cylinder, "Barrel", p, new Vector3(0, 0.07f, 0.32f), new Vector3(0.036f, 0.3f, 0.036f), new Vector3(90f, 0, 0), M("MetalDark"));
                Prim(PrimitiveType.Cylinder, "Tube", p, new Vector3(0, 0.03f, 0.28f), new Vector3(0.03f, 0.25f, 0.03f), new Vector3(90f, 0, 0), M("MetalDark"));
                Prim(PrimitiveType.Cube, "Pump", p, new Vector3(0, 0.03f, 0.3f), new Vector3(0.055f, 0.05f, 0.16f), Vector3.zero, M("WoodLight"));
                Prim(PrimitiveType.Cube, "Receiver", p, new Vector3(0, 0.055f, 0.02f), new Vector3(0.055f, 0.085f, 0.18f), Vector3.zero, M("MetalDark"));
                Prim(PrimitiveType.Cube, "Grip", p, new Vector3(0, -0.02f, -0.07f), new Vector3(0.045f, 0.09f, 0.05f), new Vector3(20f, 0, 0), M("WoodLight"));
                Prim(PrimitiveType.Cube, "Stock", p, new Vector3(0, -0.01f, -0.24f), new Vector3(0.05f, 0.1f, 0.26f), new Vector3(6f, 0, 0), M("WoodLight"));
                Prim(PrimitiveType.Cube, "Bead", p, new Vector3(0, 0.095f, 0.6f), new Vector3(0.01f, 0.012f, 0.01f), Vector3.zero, M("Accent"));
                Node("Muzzle", p, new Vector3(0, 0.07f, 0.63f));
                // Shell template (hidden): loaded into the port under the receiver, and ejected on each pump.
                var shell = Node("ShellVis", p, new Vector3(0, 0.0f, 0.06f), new Vector3(90f, 0, 0));
                Prim(PrimitiveType.Cylinder, "Hull", shell, Vector3.zero, new Vector3(0.024f, 0.03f, 0.024f), Vector3.zero, M("Medkit"));
                Prim(PrimitiveType.Cylinder, "Head", shell, new Vector3(0, -0.032f, 0), new Vector3(0.026f, 0.006f, 0.026f), Vector3.zero, M("Brass"));
                shell.gameObject.SetActive(false);
            }
        }

        static void RifleGeo(Transform p)
        {
            // Grip at the origin, +Z forward: stock behind, receiver, handguard, barrel. Box magazine drops on reload.
            Prim(PrimitiveType.Cube, "Grip", p, new Vector3(0, -0.02f, -0.005f), new Vector3(0.036f, 0.1f, 0.045f), new Vector3(16f, 0, 0), M("Grip"));
            Prim(PrimitiveType.Cube, "Receiver", p, new Vector3(0, 0.055f, 0.08f), new Vector3(0.05f, 0.075f, 0.3f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "Stock", p, new Vector3(0, 0.035f, -0.2f), new Vector3(0.045f, 0.085f, 0.24f), new Vector3(3f, 0, 0), M("Grip"));
            Prim(PrimitiveType.Cube, "ButtPad", p, new Vector3(0, 0.03f, -0.325f), new Vector3(0.048f, 0.1f, 0.015f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "Handguard", p, new Vector3(0, 0.055f, 0.36f), new Vector3(0.052f, 0.06f, 0.26f), Vector3.zero, M("Grip"));
            Prim(PrimitiveType.Cylinder, "Barrel", p, new Vector3(0, 0.06f, 0.57f), new Vector3(0.02f, 0.1f, 0.02f), new Vector3(90f, 0, 0), M("MetalDark"));
            Prim(PrimitiveType.Cylinder, "Brake", p, new Vector3(0, 0.06f, 0.685f), new Vector3(0.03f, 0.025f, 0.03f), new Vector3(90f, 0, 0), M("Metal"));
            Prim(PrimitiveType.Cube, "Rail", p, new Vector3(0, 0.097f, 0.16f), new Vector3(0.026f, 0.01f, 0.42f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "RearSight", p, new Vector3(0, 0.113f, -0.03f), new Vector3(0.03f, 0.022f, 0.02f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "FrontPost", p, new Vector3(0, 0.112f, 0.46f), new Vector3(0.008f, 0.024f, 0.01f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "Sight", p, new Vector3(0, 0.126f, 0.46f), new Vector3(0.006f, 0.006f, 0.006f), Vector3.zero, M("Accent"));
            Prim(PrimitiveType.Cube, "ChargingHandle", p, new Vector3(0.03f, 0.07f, 0.0f), new Vector3(0.012f, 0.014f, 0.035f), Vector3.zero, M("Metal"));
            Node("Muzzle", p, new Vector3(0, 0.06f, 0.71f));
            var mag = Node("Mag", p, new Vector3(0, -0.035f, 0.13f), new Vector3(10f, 0, 0));
            Prim(PrimitiveType.Cube, "MagBody", mag, new Vector3(0, -0.02f, 0), new Vector3(0.032f, 0.14f, 0.07f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "MagBase", mag, new Vector3(0, -0.095f, 0.004f), new Vector3(0.038f, 0.012f, 0.076f), Vector3.zero, M("Grip"));
            var casing = Node("CasingVis", p, new Vector3(0.03f, 0.065f, 0.07f));
            Prim(PrimitiveType.Cylinder, "Brass", casing, Vector3.zero, new Vector3(0.01f, 0.018f, 0.01f), new Vector3(0, 0, 90f), M("Brass"));
            casing.gameObject.SetActive(false);
        }

        internal static GameObject Hand(Transform p, string name, Vector3 pos, Vector3 euler)
        {
            var h = Node(name, p, pos, euler);
            Prim(PrimitiveType.Cube, "Fist", h, Vector3.zero, new Vector3(0.07f, 0.075f, 0.085f), Vector3.zero, M("PlayerSkin"));
            Prim(PrimitiveType.Cube, "Thumb", h, new Vector3(-0.038f, 0.018f, 0.015f), new Vector3(0.025f, 0.025f, 0.05f), new Vector3(0, -20f, 0), M("PlayerSkin"));
            Prim(PrimitiveType.Cube, "Sleeve", h, new Vector3(0, -0.012f, -0.14f), new Vector3(0.08f, 0.08f, 0.2f), new Vector3(-4f, 0, 0), M("PlayerSleeve"));
            Prim(PrimitiveType.Cube, "Cuff", h, new Vector3(0, -0.004f, -0.055f), new Vector3(0.085f, 0.085f, 0.03f), Vector3.zero, M("Grip"));
            return h.gameObject;
        }

        internal static GameObject SavePrefab(GameObject go, string path)
        {
            // Guard: every behaviour must ship enabled (a disabled behaviour once froze every NPC).
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (!mb.enabled) { Debug.LogWarning($"[WeaponAssets] {mb.GetType().Name} on {mb.name} was disabled before saving {path}; re-enabling."); mb.enabled = true; }
            EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void BuildViewModels()
        {
            string dir = $"{Root}/Prefabs/ViewModels";

            var fists = new GameObject("VM_Fists");
            var fr = Hand(fists.transform, "HandR", new Vector3(0.17f, -0.17f, 0.56f), new Vector3(-28f, -12f, -14f));
            fr.transform.localScale = Vector3.one * 0.85f;
            var fl = Hand(fists.transform, "HandL", new Vector3(-0.18f, -0.2f, 0.52f), new Vector3(-24f, 14f, 16f));
            fl.transform.localScale = Vector3.one * 0.85f;
            Fists.viewModel = SavePrefab(fists, $"{dir}/VM_Fists.prefab");

            GameObject Melee(string name, WeaponData w)
            {
                var root = new GameObject(name);
                var grip = Node("Grip", root.transform, new Vector3(0.26f, -0.32f, 0.5f), new Vector3(-38f, -12f, -18f));
                Hand(grip, "HandR", new Vector3(0, 0.03f, 0), new Vector3(-90f, 0, 0));
                WeaponGeo(w, grip);
                return root;
            }
            Bat.viewModel = SavePrefab(Melee("VM_Bat", Bat), $"{dir}/VM_Bat.prefab");
            Machete.viewModel = SavePrefab(Melee("VM_Machete", Machete), $"{dir}/VM_Machete.prefab");

            var pistol = new GameObject("VM_Pistol");
            var pg = Node("Grip", pistol.transform, new Vector3(0.19f, -0.2f, 0.42f), new Vector3(0f, -3f, 0f));
            WeaponGeo(Pistol, pg);
            Hand(pg, "HandR", new Vector3(0, -0.02f, -0.015f), new Vector3(14f, 0, 0));
            Hand(pg, "HandL", new Vector3(-0.045f, -0.035f, 0.0f), new Vector3(14f, 25f, 20f));
            Pistol.viewModel = SavePrefab(pistol, $"{dir}/VM_Pistol.prefab");

            var shotgun = new GameObject("VM_Shotgun");
            var sg = Node("Grip", shotgun.transform, new Vector3(0.2f, -0.24f, 0.36f), new Vector3(0f, -4f, 0f));
            WeaponGeo(Shotgun, sg);
            Hand(sg, "HandR", new Vector3(0, -0.03f, -0.08f), new Vector3(20f, 0, 0));
            Hand(sg, "HandL", new Vector3(-0.01f, -0.01f, 0.3f), new Vector3(0f, 15f, 70f));
            Shotgun.viewModel = SavePrefab(shotgun, $"{dir}/VM_Shotgun.prefab");

            var rifle = new GameObject("VM_Rifle");
            var rg = Node("Grip", rifle.transform, new Vector3(0.19f, -0.2f, 0.34f), new Vector3(0f, -3f, 0f));
            WeaponGeo(Rifle, rg);
            Hand(rg, "HandR", new Vector3(0, -0.025f, -0.01f), new Vector3(16f, 0, 0));
            Hand(rg, "HandL", new Vector3(-0.012f, 0.0f, 0.34f), new Vector3(-5f, 12f, 65f));
            Rifle.viewModel = SavePrefab(rifle, $"{dir}/VM_Rifle.prefab");

            foreach (var w in new[] { Fists, Bat, Machete, Pistol, Shotgun, Rifle }) EditorUtility.SetDirty(w);
        }

        // ---------- Pickups ----------

        internal static Pickup PickupBase(string name, out Transform visual)
        {
            var root = new GameObject(name);
            visual = Node("Visual", root.transform, new Vector3(0, 0.35f, 0));
            var ring = Prim(PrimitiveType.Cylinder, "Beacon", root.transform, new Vector3(0, 0.02f, 0), new Vector3(0.55f, 0.004f, 0.55f), Vector3.zero, M("Beacon"), false, false);
            var light = Node("Glow", root.transform, new Vector3(0, 0.6f, 0)).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Palette.Accent;
            light.range = 3.2f;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            var p = root.AddComponent<Pickup>();
            SetRef(p, "visual", visual);
            SetRef(p, "glow", light);
            return p;
        }

        internal static Pickup SavePickup(Pickup p, string file)
        {
            var go = SavePrefab(p.gameObject, $"{Root}/Prefabs/Pickups/{file}.prefab");
            return go.GetComponent<Pickup>();
        }

        static void BuildPickups()
        {
            BuildViewModels();

            Pickup WeaponPickup(string file, WeaponData w, Vector3 euler, float scale)
            {
                var p = PickupBase(file, out var vis);
                p.kind = PickupKind.Weapon;
                p.weapon = w;
                var holder = Node("Model", vis, Vector3.zero, euler, Vector3.one * scale);
                WeaponGeo(w, holder);
                return SavePickup(p, file);
            }
            PBat = WeaponPickup("P_Bat", Bat, new Vector3(0, 0, 80f), 1.3f);
            PMachete = WeaponPickup("P_Machete", Machete, new Vector3(0, 0, 80f), 1.4f);
            PPistol = WeaponPickup("P_Pistol", Pistol, new Vector3(0, 30f, 0), 2.2f);
            PShotgun = WeaponPickup("P_Shotgun", Shotgun, new Vector3(0, 30f, 0), 1.5f);
            PRifle = WeaponPickup("P_Rifle", Rifle, new Vector3(0, 30f, 0), 1.4f);

            {
                var p = PickupBase("P_AmmoLight", out var vis);
                p.kind = PickupKind.Ammo; p.ammoType = AmmoType.Light; p.amount = 12;
                Prim(PrimitiveType.Cube, "Box", vis, Vector3.zero, new Vector3(0.3f, 0.16f, 0.2f), Vector3.zero, M("Moss"));
                Prim(PrimitiveType.Cube, "Band", vis, Vector3.zero, new Vector3(0.305f, 0.04f, 0.205f), Vector3.zero, M("Accent"));
                Prim(PrimitiveType.Cube, "Lid", vis, new Vector3(0, 0.085f, 0), new Vector3(0.31f, 0.02f, 0.21f), Vector3.zero, M("MetalDark"));
                PAmmoLight = SavePickup(p, "P_AmmoLight");
            }
            {
                var p = PickupBase("P_Shells", out var vis);
                p.kind = PickupKind.Ammo; p.ammoType = AmmoType.Shells; p.amount = 6;
                Prim(PrimitiveType.Cube, "Box", vis, Vector3.zero, new Vector3(0.26f, 0.12f, 0.18f), Vector3.zero, M("CabinWood"));
                for (int i = 0; i < 5; i++)
                    Prim(PrimitiveType.Cylinder, "Shell" + i, vis, new Vector3(-0.1f + i * 0.05f, 0.1f, 0f), new Vector3(0.035f, 0.05f, 0.035f), Vector3.zero, M(i % 2 == 0 ? "Medkit" : "BloodBright"));
                PShells = SavePickup(p, "P_Shells");
            }
            {
                var p = PickupBase("P_AmmoRifle", out var vis);
                p.kind = PickupKind.Ammo; p.ammoType = AmmoType.Rifle; p.amount = 30;
                Prim(PrimitiveType.Cube, "Box", vis, Vector3.zero, new Vector3(0.32f, 0.15f, 0.2f), Vector3.zero, M("MetalDark"));
                Prim(PrimitiveType.Cube, "Band", vis, Vector3.zero, new Vector3(0.325f, 0.035f, 0.205f), Vector3.zero, M("Brass"));
                for (int i = 0; i < 2; i++)
                    Prim(PrimitiveType.Cube, "Mag" + i, vis, new Vector3(-0.06f + i * 0.12f, 0.13f, 0f), new Vector3(0.03f, 0.12f, 0.06f), new Vector3(0, 0, 8f), M("MetalDark"));
                PAmmoRifle = SavePickup(p, "P_AmmoRifle");
            }
            {
                var p = PickupBase("P_Medkit", out var vis);
                p.kind = PickupKind.Medkit; p.heal = 40f;
                Prim(PrimitiveType.Cube, "Case", vis, Vector3.zero, new Vector3(0.34f, 0.22f, 0.12f), Vector3.zero, M("UIBone"));
                Prim(PrimitiveType.Cube, "CrossV", vis, new Vector3(0, 0, -0.062f), new Vector3(0.05f, 0.15f, 0.01f), Vector3.zero, M("Medkit"));
                Prim(PrimitiveType.Cube, "CrossH", vis, new Vector3(0, 0, -0.062f), new Vector3(0.15f, 0.05f, 0.01f), Vector3.zero, M("Medkit"));
                Prim(PrimitiveType.Cube, "Handle", vis, new Vector3(0, 0.13f, 0), new Vector3(0.12f, 0.03f, 0.03f), Vector3.zero, M("Grip"));
                PMedkit = SavePickup(p, "P_Medkit");
            }
        }
    }

    /// <summary>The material set, created from the palette.</summary>
    public static class Mats
    {
        public static Material Get(string name) => name switch
        {
            "Moss" => Lit("Moss", Palette.Moss),
            "CabinWood" => Lit("CabinWood", Palette.CabinWood),
            "CabinDark" => Lit("CabinDark", Palette.CabinDark),
            "PlayerSkin" => Lit("PlayerSkin", Palette.PlayerSkin, 0.15f),
            "PlayerSleeve" => Lit("PlayerSleeve", Palette.PlayerSleeve),
            "Metal" => Lit("Metal", Palette.Metal, 0.6f),
            "MetalDark" => Lit("MetalDark", Palette.MetalDark, 0.45f),
            "WoodLight" => Lit("WoodLight", Palette.WoodLight, 0.2f),
            "Grip" => Lit("Grip", Palette.Grip),
            "Brass" => Lit("Brass", Palette.Brass, 0.55f),
            "Medkit" => Lit("Medkit", Palette.Medkit),
            "Blood" => Lit("Blood", Palette.Blood, 0.5f),
            "BloodBright" => Lit("BloodBright", Palette.BloodBright, 0.4f),
            "UIBone" => Lit("UIBone", Palette.UIBone, 0.2f),
            "Accent" => Lit("Accent", Palette.Accent, 0.3f, Palette.Accent, 1.2f),
            "Beacon" => Lit("Beacon", Palette.Accent, 0f, Palette.Accent, 0.55f),
            _ => throw new System.Exception("Unknown material " + name),
        };
    }
}
