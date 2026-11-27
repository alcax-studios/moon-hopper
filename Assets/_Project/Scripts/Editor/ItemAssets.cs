using Alcax.Combat;
using Alcax.Core;
using Alcax.Items;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds the items (menu: Alcax > Rebuild Item Assets): one ItemDefinition per item in Data/Items, a visual-only
    /// world model per item in Prefabs/Items (weapons and ammo reuse their pickup models), and Resources/ItemDatabase
    /// with the starting outfit. Run after Rebuild Weapon Assets.
    /// </summary>
    public static class ItemAssets
    {
        const string DataDir = Root + "/Data/Items";
        const string ModelDir = Root + "/Prefabs/Items";

        static Material M(string n) => Mats.Get(n);
        static Material C(string name, Color c, float smooth = 0.15f) => Lit("Item" + name, c, smooth);

        [MenuItem("Alcax/Rebuild Item Assets")]
        public static void BuildAll()
        {
            EnsureFolder(DataDir);
            EnsureFolder(ModelDir);
            EnsureFolder(Root + "/Resources");
            var list = new System.Collections.Generic.List<ItemDefinition>();

            // ---- Weapons (model from the pickup) ----
            foreach (var (file, id, name, desc, mass) in new[]
            {
                ("W_Pistol", "pistol", "Pistol", "A compact station-security sidearm. Light rounds.", 1.0f),
                ("W_Shotgun", "shotgun", "Shotgun", "Pump action. Loads one shell at a time; devastating up close.", 3.4f),
                ("W_Rifle", "rifle", "Rifle", "Automatic rifle on rifle rounds. The insurgents' favorite.", 3.6f),
                ("W_Machete", "machete", "Machete", "A heavy blade. Quiet.", 0.9f),
                ("W_Bat", "bat", "Bat", "Aluminium bat. Blunt and simple.", 1.0f),
            })
            {
                var w = AssetDatabase.LoadAssetAtPath<WeaponData>($"{Root}/Data/Weapons/{file}.asset");
                if (w == null) { Debug.LogWarning($"[ItemAssets] missing {file}; run Rebuild Weapon Assets first"); continue; }
                list.Add(Item(id, name, desc, ItemCategory.Weapon, 1, mass, FromPickup(id, "P_" + file.Substring(2)), d => { d.weapon = w; }));
            }

            // ---- Ammo ----
            list.Add(Item("ammo_light", "Light rounds", "Pistol ammunition.", ItemCategory.Ammo, 120, 0.4f, FromPickup("ammo_light", "P_AmmoLight"), d => d.ammoType = AmmoType.Light));
            list.Add(Item("ammo_shells", "Shells", "12 gauge shotgun shells.", ItemCategory.Ammo, 48, 0.5f, FromPickup("ammo_shells", "P_Shells"), d => d.ammoType = AmmoType.Shells));
            list.Add(Item("ammo_rifle", "Rifle rounds", "Rifle ammunition, boxed.", ItemCategory.Ammo, 150, 0.6f, FromPickup("ammo_rifle", "P_AmmoRifle"), d => d.ammoType = AmmoType.Rifle));

            // ---- Aid and food ----
            list.Add(Item("bandage", "Bandage", "A sterile roll. Stops the bleeding and patches you up a little.", ItemCategory.Aid, 10, 0.1f, Bandage(), d => { d.heal = 20f; d.useTime = 1.5f; d.heldScale = 1.2f; }));
            list.Add(Item("medkit", "Medkit", "A full trauma kit. Takes a while to use.", ItemCategory.Aid, 5, 1.2f, FromPickup("medkit", "P_Medkit"), d => { d.heal = 55f; d.useTime = 3.5f; d.heldScale = 0.8f; }));
            list.Add(Item("ration", "Ration bar", "Dense, dry, and almost food.", ItemCategory.Food, 10, 0.15f, Ration(), d => { d.heal = 4f; d.hunger = 30f; d.useTime = 2f; d.heldScale = 1.2f; }));
            list.Add(Item("water", "Water bottle", "Recycled, filtered, cold.", ItemCategory.Drink, 6, 0.5f, Bottle("water", new Color(0.55f, 0.75f, 0.9f), new Color(0.2f, 0.45f, 0.7f)), d => { d.thirst = 45f; d.useTime = 1.6f; d.heldScale = 1.1f; }));
            list.Add(Item("charge", "Charge", "An energy drink that tastes like a battery. You'll feel great. For a while.", ItemCategory.Drink, 6, 0.3f, Bottle("charge", new Color(0.9f, 0.85f, 0.2f), new Color(0.15f, 0.15f, 0.18f), 0.6f), d => { d.thirst = 15f; d.energy = 15f; d.effect = "energized"; d.useTime = 1.2f; d.heldScale = 1.2f; }));
            list.Add(Item("noodles", "Noodle bowl", "Green broth and hydro greens from the noodle bar.", ItemCategory.Food, 3, 0.5f, Noodles(), d => { d.heal = 8f; d.hunger = 50f; d.thirst = 10f; d.useTime = 3f; d.heldScale = 0.9f; }));
            list.Add(Item("wrap", "Grill wrap", "Grill 03's protein wrap. Mostly real.", ItemCategory.Food, 3, 0.3f, Wrap(), d => { d.heal = 6f; d.hunger = 45f; d.useTime = 2.5f; d.heldScale = 1.1f; }));
            list.Add(Item("coffee", "Coffee", "Station coffee in a paper cup. Still warm.", ItemCategory.Drink, 3, 0.3f, Coffee(), d => { d.thirst = 15f; d.energy = 20f; d.effect = "caffeinated"; d.useTime = 2.5f; d.heldScale = 1.1f; }));

            // ---- Wearables ----
            list.Add(Wear("helmet_security", "Security helmet", "Ballistic helmet, station-security issue. Stops a headshot or two.", EquipSlot.Head, WearStyle.Helmet, new Color(0.16f, 0.22f, 0.32f), 35f, 1.4f, Helmet(new Color(0.16f, 0.22f, 0.32f))));
            list.Add(Wear("cap", "Ball cap", "Keeps the glare off. Nothing else.", EquipSlot.Head, WearStyle.Cap, new Color(0.85f, 0.45f, 0.15f), 0f, 0.1f, Cap(new Color(0.85f, 0.45f, 0.15f))));
            list.Add(Wear("vest_armored", "Armored vest", "Plate carrier with trauma plates. Heavy, worth it.", EquipSlot.Chest, WearStyle.Vest, new Color(0.22f, 0.25f, 0.2f), 40f, 4f, Vest(new Color(0.22f, 0.25f, 0.2f))));
            list.Add(Wear("jacket_work", "Work jacket", "Canvas jacket with the station's maintenance patch.", EquipSlot.Chest, WearStyle.Jacket, Palette.PlayerSleeve, 5f, 1f, Jacket(Palette.PlayerSleeve)));
            list.Add(Wear("pants_cargo", "Cargo pants", "Sand-colored, lots of pockets.", EquipSlot.Pants, WearStyle.Pants, new Color(0.55f, 0.5f, 0.4f), 4f, 0.8f, Pants(new Color(0.55f, 0.5f, 0.4f))));
            list.Add(Wear("boots_work", "Work boots", "Steel toes, grippy soles.", EquipSlot.Shoes, WearStyle.Boots, new Color(0.25f, 0.2f, 0.16f), 3f, 1.2f, Boots(new Color(0.25f, 0.2f, 0.16f))));
            list.Add(Wear("gloves_tactical", "Tactical gloves", "Padded knuckles, touch-screen fingertips.", EquipSlot.Gloves, WearStyle.Gloves, new Color(0.12f, 0.12f, 0.13f), 2f, 0.2f, Gloves(new Color(0.12f, 0.12f, 0.13f))));

// ---- Currency ----
            list.Add(Item("credits", "Credits", "Station credit chips. Every trader takes them.", ItemCategory.Currency, int.MaxValue, 0.002f, Chips(), d => d.heldScale = 1.3f));

            // ---- Misc ----
            list.Add(Item("mug", "Coffee mug", "A ceramic mug from the food court. Empty.", ItemCategory.Misc, 1, 0.35f, Mug(), d => d.heldScale = 1.1f));
            list.Add(Item("keycard", "Keycard", "A blank station keycard. Somebody will want this back.", ItemCategory.Misc, 1, 0.02f, Keycard(), d => d.heldScale = 1.4f));
            list.Add(Item("datapad", "Data pad", "A cracked tablet. It still powers on.", ItemCategory.Misc, 1, 0.5f, Datapad(), d => d.heldScale = 1f));

            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(Root + "/Resources/ItemDatabase.asset");
            if (db == null) { db = ScriptableObject.CreateInstance<ItemDatabase>(); AssetDatabase.CreateAsset(db, Root + "/Resources/ItemDatabase.asset"); }
            // Prices (credits)
            var prices = new System.Collections.Generic.Dictionary<string, int>
            {
                { "pistol", 220 }, { "shotgun", 380 }, { "rifle", 540 }, { "machete", 90 }, { "bat", 50 },
                { "ammo_light", 1 }, { "ammo_shells", 3 }, { "ammo_rifle", 2 },
                { "bandage", 15 }, { "medkit", 65 }, { "ration", 6 }, { "coffee", 4 }, { "noodles", 9 }, { "wrap", 8 }, { "water", 3 }, { "charge", 7 },
                { "helmet_security", 140 }, { "cap", 12 }, { "vest_armored", 280 }, { "jacket_work", 45 }, { "pants_cargo", 35 }, { "boots_work", 40 }, { "gloves_tactical", 30 },
                { "credits", 1 }, { "mug", 3 }, { "keycard", 25 }, { "datapad", 80 },
            };
            foreach (var d in list) { d.value = prices.TryGetValue(d.id, out var v) ? v : 5; EditorUtility.SetDirty(d); }
            db.items = list.ToArray();
            ItemDefinition Find(string id) => list.Find(i => i.id == id);
            db.startWorn = new[] { Find("jacket_work"), Find("pants_cargo"), Find("boots_work") };
            db.startCarried = new[] { Find("bandage"), Find("bandage") };
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ItemAssets] {list.Count} items");
        }

        static ItemDefinition Item(string id, string name, string desc, ItemCategory cat, int stack, float mass, GameObject model, System.Action<ItemDefinition> set = null)
        {
            string path = $"{DataDir}/IT_{id}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (d == null) { d = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(d, path); }
            d.id = id; d.displayName = name; d.description = desc; d.category = cat; d.maxStack = stack; d.mass = mass;
            d.worldModel = model;
            d.weapon = null; d.ammoType = AmmoType.None; d.heal = 0f; d.useTime = 1f; d.heldScale = 1f; d.hunger = d.thirst = d.energy = 0f; d.effect = null;
            d.slot = EquipSlot.None; d.wearStyle = WearStyle.None; d.armor = 0f;
            set?.Invoke(d);
            EditorUtility.SetDirty(d);
            return d;
        }

        static ItemDefinition Wear(string id, string name, string desc, EquipSlot slot, WearStyle style, Color color, float armor, float mass, GameObject model)
            => Item(id, name, desc, ItemCategory.Wearable, 1, mass, model, d => { d.slot = slot; d.wearStyle = style; d.wearColor = color; d.armor = armor; d.heldScale = 0.7f; });

        // ---------- World models (visual only, pivot at the bottom center) ----------

        static GameObject Save(GameObject root, string id) => SaveModel(root, $"{ModelDir}/IM_{id}.prefab");

        static GameObject SaveModel(GameObject go, string path)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>The "Visual" child of a pickup prefab, re-rooted so its bottom sits at the pivot.</summary>
        static GameObject FromPickup(string id, string pickup)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Pickups/{pickup}.prefab");
            var root = new GameObject("IM_" + id);
            if (src == null) { Prim(PrimitiveType.Cube, "Box", root.transform, new Vector3(0, 0.1f, 0), Vector3.one * 0.2f, Vector3.zero, M("MetalDark")); return Save(root, id); }
            var vis = src.transform.Find("Visual");
            var copy = Object.Instantiate(vis != null ? vis.gameObject : src, root.transform);
            copy.name = "Model";
            copy.transform.localPosition = Vector3.zero;
            // Sit it on the ground
            var rs = copy.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                copy.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            }
            return Save(root, id);
        }

        static GameObject Root0(string id) => new GameObject("IM_" + id);

        static GameObject Bandage()
        {
            var r = Root0("bandage");
            Prim(PrimitiveType.Cylinder, "Roll", r.transform, new Vector3(0, 0.04f, 0), new Vector3(0.08f, 0.04f, 0.08f), new Vector3(0, 0, 90f), C("Gauze", new Color(0.93f, 0.92f, 0.88f)));
            Prim(PrimitiveType.Cube, "Tail", r.transform, new Vector3(0.0f, 0.005f, 0.06f), new Vector3(0.075f, 0.004f, 0.06f), Vector3.zero, C("Gauze", new Color(0.93f, 0.92f, 0.88f)));
            Prim(PrimitiveType.Cylinder, "Core", r.transform, new Vector3(0, 0.04f, 0), new Vector3(0.03f, 0.041f, 0.03f), new Vector3(0, 0, 90f), C("Cardboard", new Color(0.6f, 0.5f, 0.36f)));
            return Save(r, "bandage");
        }

        static GameObject Ration()
        {
            var r = Root0("ration");
            Prim(PrimitiveType.Cube, "Bar", r.transform, new Vector3(0, 0.012f, 0), new Vector3(0.13f, 0.024f, 0.045f), Vector3.zero, C("Wrapper", new Color(0.35f, 0.42f, 0.3f)));
            Prim(PrimitiveType.Cube, "Band", r.transform, new Vector3(0, 0.012f, 0), new Vector3(0.04f, 0.026f, 0.047f), Vector3.zero, M("Accent"));
            return Save(r, "ration");
        }

        static GameObject Coffee()
        {
            var r = Root0("coffee");
            Prim(PrimitiveType.Cylinder, "Cup", r.transform, new Vector3(0, 0.055f, 0), new Vector3(0.07f, 0.055f, 0.07f), Vector3.zero, C("Paper", new Color(0.9f, 0.88f, 0.82f)));
            Prim(PrimitiveType.Cylinder, "Sleeve", r.transform, new Vector3(0, 0.05f, 0), new Vector3(0.074f, 0.022f, 0.074f), Vector3.zero, C("Cardboard", new Color(0.6f, 0.5f, 0.36f)));
            Prim(PrimitiveType.Cylinder, "Lid", r.transform, new Vector3(0, 0.113f, 0), new Vector3(0.076f, 0.006f, 0.076f), Vector3.zero, M("Grip"));
            return Save(r, "coffee");
        }

static GameObject Noodles()
        {
            var r = Root0("noodles");
            Prim(PrimitiveType.Cylinder, "Bowl", r.transform, new Vector3(0, 0.035f, 0), new Vector3(0.15f, 0.035f, 0.15f), Vector3.zero, C("Bowl", new Color(0.75f, 0.2f, 0.15f), 0.5f));
            Prim(PrimitiveType.Cylinder, "Broth", r.transform, new Vector3(0, 0.068f, 0), new Vector3(0.135f, 0.004f, 0.135f), Vector3.zero, C("Broth", new Color(0.55f, 0.62f, 0.3f), 0.6f));
            Prim(PrimitiveType.Cube, "Sticks", r.transform, new Vector3(0.02f, 0.09f, 0), new Vector3(0.2f, 0.006f, 0.012f), new Vector3(0, 20f, 8f), C("Wood2", new Color(0.7f, 0.55f, 0.35f)));
            return Save(r, "noodles");
        }

        static GameObject Wrap()
        {
            var r = Root0("wrap");
            Prim(PrimitiveType.Capsule, "Wrap", r.transform, new Vector3(0, 0.03f, 0), new Vector3(0.06f, 0.07f, 0.06f), new Vector3(90f, 0, 0), C("Tortilla", new Color(0.85f, 0.72f, 0.5f)));
            Prim(PrimitiveType.Cube, "Paper", r.transform, new Vector3(0, 0.03f, -0.035f), new Vector3(0.065f, 0.065f, 0.07f), Vector3.zero, C("Paper", new Color(0.9f, 0.88f, 0.82f)));
            return Save(r, "wrap");
        }

static GameObject Bottle(string id, Color body, Color cap, float scale = 1f)
        {
            var r = Root0(id);
            Prim(PrimitiveType.Cylinder, "Body", r.transform, new Vector3(0, 0.1f * scale, 0), new Vector3(0.065f, 0.1f * scale, 0.065f), Vector3.zero, Lit("ItemBottle_" + id, body, 0.8f));
            Prim(PrimitiveType.Cylinder, "Label", r.transform, new Vector3(0, 0.1f * scale, 0), new Vector3(0.067f, 0.04f * scale, 0.067f), Vector3.zero, M("UIBone"));
            Prim(PrimitiveType.Cylinder, "Cap", r.transform, new Vector3(0, 0.205f * scale + 0.01f, 0), new Vector3(0.035f, 0.012f, 0.035f), Vector3.zero, Lit("ItemCap_" + id, cap, 0.4f));
            return Save(r, id);
        }

        static GameObject Mug()
        {
            var r = Root0("mug");
            var white = C("Ceramic", new Color(0.92f, 0.93f, 0.95f), 0.6f);
            Prim(PrimitiveType.Cylinder, "Body", r.transform, new Vector3(0, 0.05f, 0), new Vector3(0.085f, 0.05f, 0.085f), Vector3.zero, white);
            Prim(PrimitiveType.Cylinder, "Coffee", r.transform, new Vector3(0, 0.098f, 0), new Vector3(0.075f, 0.003f, 0.075f), Vector3.zero, C("Dark", new Color(0.12f, 0.08f, 0.06f)));
            Prim(PrimitiveType.Cube, "HandleTop", r.transform, new Vector3(0.055f, 0.075f, 0), new Vector3(0.03f, 0.012f, 0.012f), Vector3.zero, white);
            Prim(PrimitiveType.Cube, "HandleSide", r.transform, new Vector3(0.068f, 0.05f, 0), new Vector3(0.012f, 0.05f, 0.012f), Vector3.zero, white);
            Prim(PrimitiveType.Cube, "HandleBottom", r.transform, new Vector3(0.055f, 0.025f, 0), new Vector3(0.03f, 0.012f, 0.012f), Vector3.zero, white);
            Prim(PrimitiveType.Cylinder, "Stripe", r.transform, new Vector3(0, 0.07f, 0), new Vector3(0.087f, 0.006f, 0.087f), Vector3.zero, M("Accent"));
            return Save(r, "mug");
        }

static GameObject Chips()
        {
            var r = Root0("credits");
            var gold = Lit("ItemChipGold", new Color(0.85f, 0.68f, 0.3f), 0.7f);
            var teal = Lit("ItemChipTeal", new Color(0.2f, 0.6f, 0.62f), 0.6f);
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cylinder, "Chip" + i, r.transform, new Vector3(0.004f * (i % 2), 0.004f + i * 0.0085f, 0.003f * (i % 3)), new Vector3(0.05f, 0.004f, 0.05f), Vector3.zero, i % 2 == 0 ? gold : teal);
            Prim(PrimitiveType.Cylinder, "Loose", r.transform, new Vector3(0.05f, 0.004f, -0.02f), new Vector3(0.05f, 0.004f, 0.05f), Vector3.zero, gold);
            return Save(r, "credits");
        }

        static GameObject Keycard()
        {
            var r = Root0("keycard");
            Prim(PrimitiveType.Cube, "Card", r.transform, new Vector3(0, 0.002f, 0), new Vector3(0.054f, 0.003f, 0.086f), Vector3.zero, C("CardWhite", new Color(0.85f, 0.87f, 0.9f), 0.5f));
            Prim(PrimitiveType.Cube, "Stripe", r.transform, new Vector3(0, 0.004f, 0.02f), new Vector3(0.055f, 0.002f, 0.015f), Vector3.zero, M("Accent"));
            Prim(PrimitiveType.Cube, "Chip", r.transform, new Vector3(0.012f, 0.004f, -0.02f), new Vector3(0.014f, 0.002f, 0.012f), Vector3.zero, M("Brass"));
            return Save(r, "keycard");
        }

        static GameObject Datapad()
        {
            var r = Root0("datapad");
            Prim(PrimitiveType.Cube, "Body", r.transform, new Vector3(0, 0.006f, 0), new Vector3(0.16f, 0.012f, 0.23f), Vector3.zero, M("MetalDark"));
            Prim(PrimitiveType.Cube, "Screen", r.transform, new Vector3(0, 0.0125f, 0), new Vector3(0.14f, 0.002f, 0.2f), Vector3.zero, Lit("ItemScreen", new Color(0.1f, 0.3f, 0.35f), 0.8f, new Color(0.2f, 0.7f, 0.8f), 0.6f));
            return Save(r, "datapad");
        }

        // Wearables, as they look lying on a shelf
        static GameObject Helmet(Color c)
        {
            var r = Root0("helmet_security");
            Prim(PrimitiveType.Sphere, "Shell", r.transform, new Vector3(0, 0.06f, 0), new Vector3(0.24f, 0.2f, 0.27f), Vector3.zero, C("Helmet", c, 0.35f));
            Prim(PrimitiveType.Cube, "Visor", r.transform, new Vector3(0, 0.08f, 0.11f), new Vector3(0.2f, 0.05f, 0.04f), new Vector3(-15f, 0, 0), Lit("ItemVisor", new Color(0.1f, 0.12f, 0.14f), 0.9f));
            return Save(r, "helmet_security");
        }

        static GameObject Cap(Color c)
        {
            var r = Root0("cap");
            Prim(PrimitiveType.Sphere, "Crown", r.transform, new Vector3(0, 0.03f, 0), new Vector3(0.2f, 0.12f, 0.21f), Vector3.zero, C("Cap", c));
            Prim(PrimitiveType.Cube, "Brim", r.transform, new Vector3(0, 0.01f, 0.12f), new Vector3(0.17f, 0.012f, 0.09f), Vector3.zero, C("Cap", c));
            return Save(r, "cap");
        }

        static GameObject Vest(Color c)
        {
            var r = Root0("vest_armored");
            Prim(PrimitiveType.Cube, "Front", r.transform, new Vector3(0, 0.04f, 0), new Vector3(0.38f, 0.08f, 0.46f), Vector3.zero, C("Vest", c));
            for (int i = 0; i < 3; i++) Prim(PrimitiveType.Cube, "Pouch" + i, r.transform, new Vector3(-0.11f + i * 0.11f, 0.09f, -0.12f), new Vector3(0.09f, 0.03f, 0.12f), Vector3.zero, C("VestDark", c * 0.7f));
            return Save(r, "vest_armored");
        }

        static GameObject Jacket(Color c)
        {
            var r = Root0("jacket_work");
            Prim(PrimitiveType.Cube, "Folded", r.transform, new Vector3(0, 0.04f, 0), new Vector3(0.36f, 0.08f, 0.3f), Vector3.zero, C("Jacket", c));
            Prim(PrimitiveType.Cube, "Patch", r.transform, new Vector3(0.08f, 0.081f, 0.05f), new Vector3(0.06f, 0.003f, 0.06f), Vector3.zero, M("Accent"));
            return Save(r, "jacket_work");
        }

        static GameObject Pants(Color c)
        {
            var r = Root0("pants_cargo");
            Prim(PrimitiveType.Cube, "Folded", r.transform, new Vector3(0, 0.035f, 0), new Vector3(0.32f, 0.07f, 0.26f), Vector3.zero, C("Pants", c));
            return Save(r, "pants_cargo");
        }

        static GameObject Boots(Color c)
        {
            var r = Root0("boots_work");
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.06f : 0.06f;
                Prim(PrimitiveType.Cube, "Foot" + i, r.transform, new Vector3(x, 0.035f, 0.02f), new Vector3(0.09f, 0.07f, 0.26f), Vector3.zero, C("Boots", c));
                Prim(PrimitiveType.Cube, "Shaft" + i, r.transform, new Vector3(x, 0.11f, -0.06f), new Vector3(0.09f, 0.14f, 0.1f), Vector3.zero, C("Boots", c));
            }
            return Save(r, "boots_work");
        }

        static GameObject Gloves(Color c)
        {
            var r = Root0("gloves_tactical");
            for (int i = 0; i < 2; i++)
                Prim(PrimitiveType.Cube, "Glove" + i, r.transform, new Vector3(i == 0 ? -0.05f : 0.05f, 0.015f, 0), new Vector3(0.08f, 0.03f, 0.17f), new Vector3(0, i == 0 ? 8f : -8f, 0), C("Gloves", c));
            return Save(r, "gloves_tactical");
        }
    }
}
