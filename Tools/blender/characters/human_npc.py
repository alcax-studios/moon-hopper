"""
Human NPC from MPFB (MakeHuman for Blender): body from macro values, the game_engine rig (UE5 mannequin-style
bone names: works as a Unity Humanoid and in Unreal), skin, high-poly eyes, brows, lashes, teeth, tongue, hair,
clothes. Assets come from the CC0 MakeHuman packs (SourceArt/_local/makehuman, installed in MPFB).
Run in its own Blender file. CHARACTERS holds the presets; BUILD = the one to build.
"""
import os
import random
import bpy
from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.assetservice import AssetService
from bl_ext.user_default.mpfb.services.targetservice import TargetService
from bl_ext.user_default.mpfb.services.locationservice import LocationService

# Working build for men (MPFB targets on top of the macros, both sides where l-/r-), so loose clothes don't hang off thin arms
_FIT_M = {"arms/upperarm-muscle-incr": 0.25, "arms/upperarm-shoulder-muscle-incr": 0.15,
          "arms/upperarm-scale-horiz-incr": 0.1, "arms/upperarm-scale-depth-incr": 0.1,
          "arms/lowerarm-muscle-incr": 0.2, "torso/measure-shoulder-dist-incr": 0.1,
          "torso/torso-vshape-incr": 0.12, "torso/torso-muscle-pectoral-incr": 0.15,
          "neck/neck-scale-horiz-incr": 0.25}
_FIT_F = {"arms/upperarm-muscle-incr": 0.35, "arms/upperarm-shoulder-muscle-incr": 0.35,
          "torso/measure-shoulder-dist-incr": 0.15}
# Station security uniform: a suit (dyed dark navy in Unity, CharacterMaterials), shirt and tie, dress shoes
_UNIFORM_M = ["male_elegantsuit01.mhclo", "shoes06.mhclo"]
_UNIFORM_F = ["female_elegantsuit01.mhclo:Chest", "female_casualsuit01.mhclo:Pants", "shoes03.mhclo"]   # (asset:Slot = only that half of a suit; casualsuit02 bottoms are shorts)

# face: a seed for the face shaping (a few nose, mouth, chin, cheek, eye, jaw, and brow targets at moderate strength)
CHARACTERS = {
    # Station security
    "SK_Human_Security01": dict(   # late 30s, athletic
        macro={"gender": 1.0, "age": 0.78, "muscle": 0.68, "weight": 0.6, "height": 0.62, "proportions": 0.6,
               "race": {"african": 0.15, "asian": 0.2, "caucasian": 0.65}},
        skin="middleage_caucasian_male.mhmat", eyebrows="eyebrow010.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short02.mhclo", clothes=_UNIFORM_M, targets=_FIT_M, face=11),
    "SK_Human_Security02": dict(   # early 30s, broad
        macro={"gender": 1.0, "age": 0.62, "muscle": 0.7, "weight": 0.63, "height": 0.555, "proportions": 0.6,
               "race": {"african": 0.85, "asian": 0.0, "caucasian": 0.15}},
        skin="middleage_african_male.mhmat", eyebrows="eyebrow005.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short04.mhclo", clothes=_UNIFORM_M, targets=_FIT_M, face=23),
    "SK_Human_Security03": dict(   # late 20s, wiry
        macro={"gender": 0.0, "age": 0.55, "muscle": 0.7, "weight": 0.5, "height": 0.7, "proportions": 0.6,
               "race": {"african": 0.0, "asian": 0.85, "caucasian": 0.15}},
        skin="young_asian_female.mhmat", eyebrows="eyebrow002.mhclo", eyelashes="eyelashes01.mhclo",
        hair="ponytail01.mhclo", clothes=_UNIFORM_F, targets=_FIT_F, face=37),
    # Civilians (travelers, staff, guests)
    "SK_Human_Civ01": dict(        # young woman
        macro={"gender": 0.0, "age": 0.5, "muscle": 0.5, "weight": 0.5, "height": 0.62, "proportions": 0.6,
               "race": {"african": 0.0, "asian": 0.1, "caucasian": 0.9}},
        skin="young_caucasian_female.mhmat", eyebrows="eyebrow001.mhclo", eyelashes="eyelashes01.mhclo",
        hair="bob01.mhclo", clothes=["female_casualsuit01.mhclo", "shoes03.mhclo"], targets=_FIT_F, face=41),
    "SK_Human_Civ02": dict(        # middle-aged man
        macro={"gender": 1.0, "age": 0.82, "muscle": 0.55, "weight": 0.62, "height": 0.59, "proportions": 0.55,
               "race": {"african": 0.0, "asian": 0.9, "caucasian": 0.1}},
        skin="middleage_asian_male.mhmat", eyebrows="eyebrow007.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short03.mhclo", clothes=["male_casualsuit02.mhclo", "shoes02.mhclo"], targets=_FIT_M, face=53),
    "SK_Human_Civ03": dict(        # older man, heavier
        macro={"gender": 1.0, "age": 0.92, "muscle": 0.45, "weight": 0.75, "height": 0.57, "proportions": 0.5,
               "race": {"african": 0.9, "asian": 0.0, "caucasian": 0.1}},
        skin="old_african_male.mhmat", eyebrows="eyebrow004.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short01.mhclo", clothes=["male_casualsuit04.mhclo", "shoes01.mhclo"], targets={}, face=67),
    "SK_Human_Civ04": dict(        # woman, 40s
        macro={"gender": 0.0, "age": 0.72, "muscle": 0.5, "weight": 0.55, "height": 0.64, "proportions": 0.6,
               "race": {"african": 0.75, "asian": 0.0, "caucasian": 0.25}},
        skin="middleage_african_female.mhmat", eyebrows="eyebrow003.mhclo", eyelashes="eyelashes01.mhclo",
        hair="afro01.mhclo", clothes=["female_casualsuit02.mhclo", "shoes04.mhclo"], targets=_FIT_F, face=79),
    "SK_Human_Civ05": dict(        # young man, striped shirt
        macro={"gender": 1.0, "age": 0.5, "muscle": 0.6, "weight": 0.5, "height": 0.53, "proportions": 0.6,
               "race": {"african": 0.8, "asian": 0.0, "caucasian": 0.2}},
        skin="young_african_male.mhmat", eyebrows="eyebrow006.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short04.mhclo", clothes=["male_casualsuit03.mhclo", "shoes05.mhclo"], targets=_FIT_M, face=83),
    "SK_Human_Civ06": dict(        # businesswoman
        macro={"gender": 0.0, "age": 0.62, "muscle": 0.5, "weight": 0.48, "height": 0.66, "proportions": 0.6,
               "race": {"african": 0.0, "asian": 0.8, "caucasian": 0.2}},
        skin="middleage_asian_female.mhmat", eyebrows="eyebrow009.mhclo", eyelashes="eyelashes03.mhclo",
        hair="long01.mhclo", clothes=["female_elegantsuit01.mhclo", "shoes03.mhclo"], targets=_FIT_F, face=89),
    "SK_Human_Civ07": dict(        # older woman
        macro={"gender": 0.0, "age": 0.9, "muscle": 0.4, "weight": 0.62, "height": 0.55, "proportions": 0.55,
               "race": {"african": 0.0, "asian": 0.0, "caucasian": 1.0}},
        skin="old_caucasian_female.mhmat", eyebrows="eyebrow011.mhclo", eyelashes="eyelashes01.mhclo",
        hair="bob02.mhclo", clothes=["female_casualsuit01.mhclo", "shoes04.mhclo"], targets={}, face=97),
    "SK_Human_Civ08": dict(        # man, 40s, T-shirt and jeans
        macro={"gender": 1.0, "age": 0.75, "muscle": 0.55, "weight": 0.68, "height": 0.6, "proportions": 0.55,
               "race": {"african": 0.0, "asian": 0.1, "caucasian": 0.9}},
        skin="middleage_caucasian_male.mhmat", eyebrows="eyebrow008.mhclo", eyelashes="eyelashes02.mhclo",
        hair="short02.mhclo", clothes=["male_casualsuit06.mhclo", "shoes01.mhclo"], targets={}, face=101),
}
BUILD = globals().get("BUILD", "SK_Human_Security01")


def find(kind, name):
    for p in AssetService.list_mhclo_assets(kind) if kind != "skins" else AssetService.list_mhmat_assets(kind):
        if os.path.basename(str(p)) == name:
            return str(p)
    raise FileNotFoundError(f"{kind}: {name}")


def build(name):
    c = CHARACTERS[name]
    for o in list(bpy.context.scene.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    macro = dict(c["macro"])
    race = macro.pop("race")
    detail = {k: v for k, v in macro.items()}
    detail["race"] = race
    basemesh = HumanService.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True,
                                         feet_on_ground=True, scale=0.1, macro_detail_dict=detail)
    basemesh.name = name + "_Body"
    # Body detail targets before the rig and clothes, so the skeleton and the garments fit the final shape
    tdir = LocationService.get_mpfb_data("targets")
    for t, w in c.get("targets", {}).items():
        sub, tname = t.split("/")
        for side in (("l-", "r-") if sub == "arms" else ("",)):
            TargetService.load_target(basemesh, os.path.join(tdir, sub, side + tname + ".target.gz"), weight=w)
    # Face: a seeded handful of shape targets per region at moderate strength (both sides alike), so every
    # character has its own face and rebuilds the same. No moves, tilts, or an open jaw: shapes only.
    if c.get("face") is not None:
        rng = random.Random(c["face"])
        skip = ("trans-", "-angle", "angle-", "jaw-drop", "open", "-age-")
        for region, n in (("nose", 3), ("mouth", 2), ("chin", 2), ("cheek", 2), ("eyes", 2), ("head", 2), ("forehead", 1), ("eyebrows", 1)):
            d = os.path.join(tdir, region)
            if not os.path.isdir(d):
                continue
            files = sorted(f for f in os.listdir(d) if f.endswith(".target.gz") and not f.startswith("r-") and not any(s in f for s in skip))
            for f in rng.sample(files, min(n, len(files))):
                w = rng.uniform(0.2, 0.6)
                TargetService.load_target(basemesh, os.path.join(d, f), weight=w)
                if f.startswith("l-") and os.path.exists(os.path.join(d, "r-" + f[2:])):
                    TargetService.load_target(basemesh, os.path.join(d, "r-" + f[2:]), weight=w)
    rig = HumanService.add_builtin_rig(basemesh, "game_engine", import_weights=True)
    rig.name = name
    HumanService.set_character_skin(find("skins", c["skin"]), basemesh, skin_type="ENHANCED_SSS")
    for kind, asset in (("eyes", "high-poly.mhclo"), ("eyebrows", c["eyebrows"]), ("eyelashes", c["eyelashes"]),
                        ("teeth", "teeth_base.mhclo"), ("tongue", "tongue01.mhclo"), ("hair", c["hair"])):
        HumanService.add_mhclo_asset(find(kind, asset), basemesh, asset_type=kind.capitalize().rstrip("s") if kind != "eyes" else "Eyes",
                                     subdiv_levels=0, material_type="MAKESKIN")
    for cl in c["clothes"]:
        HumanService.add_mhclo_asset(find("clothes", cl.split(":")[0]), basemesh, asset_type="Clothes", subdiv_levels=0, material_type="MAKESKIN")
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs)
    print(name, "rig bones", len(rig.data.bones), "meshes", len(objs), "tris", tris, "height", round(basemesh.dimensions.z, 2))
    return rig, basemesh


if globals().get("STAGE", "build") == "build":
    build(BUILD)


# ---------- Export: game meshes, textures, FBX ----------

def textures_of(mat):
    """The image textures a MakeHuman material uses, by role, looking inside node groups (the skin keeps them in a
    group) and naming roles from the node label or the image file name."""
    out = {}
    if not mat or not mat.use_nodes:
        return out
    def scan(tree):
        for n in tree.nodes:
            if n.type == 'GROUP' and n.node_tree is not None:
                scan(n.node_tree)
                continue
            if n.type != 'TEX_IMAGE' or n.image is None:
                continue
            key = ((n.label or n.name) + " " + n.image.name + " " + bpy.path.basename(n.image.filepath)).lower()
            role = ("Normal" if ("normal" in key or "_nm" in key) else
                    "Roughness" if "rough" in key else
                    "Specular" if "spec" in key else
                    "Bump" if "bump" in key else
                    "SSS" if ("sss" in key or "subsurf" in key) else
                    "BaseColor" if ("diffuse" in key or "color" in key or "albedo" in key or "skin" in key) else None)
            if role and role not in out:
                out[role] = n.image
    scan(mat.node_tree)
    return out


# ---------- Outfit slots (removable clothing) ----------
# Each garment is its own mesh named <Name>_Outfit_<Slot>; the body is kept whole under them, with a per-vertex mask
# (UV map "OutfitMask": u = the slots that cover the vertex, v = 1 in the underwear) so the game hides the skin under
# worn pieces (no poke-through) and draws underwear where nothing is worn. Unity: CharacterOutfit, Alcax/Skin Lit.
SLOT_BITS = {"Chest": 1, "Pants": 2, "Shoes": 4, "Gloves": 8, "Head": 16}


def slot_of(asset):
    a = asset.lower()
    if "shoe" in a or "boot" in a:
        return "Shoes"
    if "glove" in a:
        return "Gloves"
    if any(k in a for k in ("fedora", "hat", "helmet", "cap", "hood")):
        return "Head"
    if "suit" in a:
        return "Split"          # a top and a bottom in one asset: split at the waist
    if any(k in a for k in ("pant", "trouser", "skirt", "short", "jean")):
        return "Pants"
    if any(k in a for k in ("shirt", "top", "jacket", "coat", "sweater", "vest")):
        return "Chest"
    return None


def body_heights(rig):
    """Waist (splits a suit), the underwear's band, and the bust (for a bra), from the skeleton."""
    def z(b):
        bb = rig.data.bones.get(b)
        return (rig.matrix_world @ bb.head_local).z if bb else None
    waist = z("spine_01") - 0.02
    crotch = z("thigh_l") - 0.085
    top = z("pelvis") + 0.07
    clav = z("clavicle_l")
    return waist, crotch, top, (clav - 0.3, clav - 0.13)


def components(bm):
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            comp.append(x.index)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        out.append(comp)
    return out


def keep_verts(obj, keep):
    import bmesh
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
    bm.to_mesh(obj.data); bm.free()


def split_suit(g, waist):
    """A suit is two loose parts (top and bottom): one object each. One connected mesh: cut by height."""
    import bmesh
    bm = bmesh.new(); bm.from_mesh(g.data); bm.verts.ensure_lookup_table()
    zs = [(g.matrix_world @ v.co).z for v in bm.verts]
    comps = components(bm)
    top, bottom = set(), set()
    if len(comps) >= 2:
        for c in comps:
            (top if sum(zs[i] for i in c) / len(c) > waist else bottom).update(c)
    if not top or not bottom:
        top = {i for i, zz in enumerate(zs) if zz > waist}
        bottom = set(range(len(zs))) - top
    bm.free()
    pants = g.copy(); pants.data = g.data.copy()
    bpy.context.scene.collection.objects.link(pants)
    keep_verts(g, top)
    keep_verts(pants, bottom)
    return g, pants


def outfit_mask(body, rig, clothes, female):
    """Write the OutfitMask UV map on the body: which slots cover each vertex, and the underwear."""
    waist, crotch, top, bust = body_heights(rig)
    groups = {}
    for asset in clothes:
        stem, _, only = asset.partition(":")
        stem = stem.replace(".mhclo", "")
        vg = body.vertex_groups.get("Delete." + stem)
        if vg is not None:
            groups[vg.index] = (slot_of(stem), only or None)
    mesh = body.data
    bits, under = [0] * len(mesh.vertices), [0.0] * len(mesh.vertices)
    mw = body.matrix_world
    for v in mesh.vertices:
        z = (mw @ v.co).z
        for ge in v.groups:
            entry = groups.get(ge.group)
            if entry is None or entry[0] is None or ge.weight < 0.5:
                continue
            slot, only = entry
            if slot == "Split":
                half = "Chest" if z >= waist else "Pants"
                if only is None or only == half:      # (only one half of the suit worn: the other covers nothing)
                    bits[v.index] |= SLOT_BITS[half]
            else:
                bits[v.index] |= SLOT_BITS[slot]
        if crotch <= z <= top or (female and bust[0] <= z <= bust[1]):
            under[v.index] = 1.0
    uv = mesh.uv_layers.new(name="OutfitMask")
    for loop in mesh.loops:
        uv.data[loop.index].uv = (float(bits[loop.vertex_index]), under[loop.vertex_index])
    return sum(1 for b in bits if b)


def export(name):
    import os, shutil, sys
    sys.path.append(r"C:\Dev\moon-hopper\Tools\blender")
    import importlib, moon_hopper_export as mh
    importlib.reload(mh)
    c = CHARACTERS[name]
    rig = bpy.data.objects[name]
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.parent == rig or (o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers))]
    out_dir = os.path.join(r"C:\Dev\moon-hopper\Assets\_Project\Art\Models\Characters", name, "Textures")
    os.makedirs(out_dir, exist_ok=True)
    win = bpy.context.window_manager.windows[0]
    waist = body_heights(rig)[0]
    female = c["macro"]["gender"] < 0.5
    game = []
    for o in meshes:
        part = o.name.split(".")[-1] if "." in o.name else "Body"
        g = o.copy(); g.data = o.data.copy(); g.name = f"{name[3:]}_{part}"   # e.g. Human_Security01_Body
        # Own copies of the materials (renaming shared ones renamed the source file's materials)
        for i, m in enumerate(g.data.materials):
            if m is not None:
                g.data.materials[i] = m.copy()
        bpy.context.scene.collection.objects.link(g)
        # Bake the body shape (MPFB macro shape keys) into the mesh first
        if g.data.shape_keys:
            with bpy.context.temp_override(window=win, object=g, active_object=g):
                bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
        if part == "Body":
            # The whole body stays (clothes come off): what each garment covers becomes the outfit mask instead
            covered = outfit_mask(g, rig, c["clothes"], female)
            for md in list(g.modifiers):
                if md.type == 'MASK' and md.name.startswith("Delete."):
                    g.modifiers.remove(md)
            print(name, "body verts under clothes:", covered)
        for md in list(g.modifiers):
            if md.type == 'MASK':
                with bpy.context.temp_override(window=win, object=g, active_object=g):
                    bpy.ops.object.modifier_apply(modifier=md.name)
        # Drop MPFB's helper geometry from the body
        hg = g.vertex_groups.get("HelperGeometry")
        if hg is not None:
            import bmesh
            bm = bmesh.new(); bm.from_mesh(g.data)
            deform = bm.verts.layers.deform.active
            kill = [v for v in bm.verts if deform and hg.index in v[deform] and v[deform][hg.index] > 0.5]
            bmesh.ops.delete(bm, geom=kill, context='VERTS')
            bm.to_mesh(g.data); bm.free()
        # The skin comes as several region slots of one material: merge them into one
        if part == "Body" and len(g.data.materials) > 1:
            for p in g.data.polygons:
                p.material_index = 0
            while len(g.data.materials) > 1:
                g.data.materials.pop(index=len(g.data.materials) - 1)
        # Textures under our names
        for mi, m in enumerate(g.data.materials):
            for role, img in textures_of(m).items():
                src = bpy.path.abspath(img.filepath)
                if os.path.exists(src):
                    ext = os.path.splitext(src)[1]
                    shutil.copy2(src, os.path.join(out_dir, f"T_{name[3:]}_{part}_{role}{ext}"))
            new = f"M_{name[3:]}_{part}" if len(g.data.materials) == 1 else f"M_{name[3:]}_{part}_{mi}"
            old = bpy.data.materials.get(new)
            if old is not None and old != m:
                old.name = new + "_old"   # free the name so Blender does not add .001
            m.name = new
        # Garments: one mesh per slot (a suit splits into its top and bottom)
        stems = {cl.partition(":")[0].replace(".mhclo", "") for cl in c["clothes"]}
        slot = slot_of(part) if part in stems else None   # (only the outfit: "short03" is a haircut, not shorts)
        if slot == "Split":
            top, bottom = split_suit(g, waist)
            only = next((cl.partition(":")[2] for cl in c["clothes"] if cl.partition(":")[0].replace(".mhclo", "") == part), "")
            for piece, half in ((top, "Chest"), (bottom, "Pants")):
                if only and only != half:
                    bpy.data.objects.remove(piece, do_unlink=True)   # this half isn't part of the outfit
                    continue
                piece.name = f"{name[3:]}_Outfit_{half}"
                game.append(piece)
            continue
        if slot is not None:
            g.name = f"{name[3:]}_Outfit_{slot}"
        game.append(g)
    pieces = [g.name for g in game if "_Outfit_" in g.name]
    fbx, problems = mh.export_asset("Characters", name, [rig] + game, force=True)
    for g in game:
        bpy.data.objects.remove(g, do_unlink=True)
    print("exported", fbx, problems, "outfit pieces:", pieces)


if globals().get("STAGE") == "export":
    export(BUILD)
