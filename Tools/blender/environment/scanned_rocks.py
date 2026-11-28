"""
Scanned rocks to game assets (docs/art/asset-pipeline.md): for each imported scan (Poly Haven glTF), make a
low-poly game mesh, bake the scan's full detail (geometry and its normal map) into a new normal map on it, build
LOD1 and LOD2, and convert the textures to our map set (BaseColor, Normal, MetallicSmoothness, Occlusion).
Run in SourceArt/Environment/Kelxor_Rocks/Kelxor_Rocks.blend after the scans are imported.
STAGE = "process" (meshes and bakes, written to SourceArt) or "export" (into the Unity project).
"""
import os, shutil, sys
import bpy, bmesh
import numpy as np

STAGE = globals().get("STAGE", "process")
ROCKS = {   # scan object material name -> (asset name, LOD0 triangle target)
    "boulder_02": ("SM_Rock_Boulder02", 9000),
    "boulder_03": ("SM_Rock_Boulder03", 10000),
    "boulder_04": ("SM_Rock_Boulder04", 10000),
    "namaqualand_boulder_05": ("SM_Rock_Boulder05", 6000),
    "namaqualand_boulder_06": ("SM_Rock_Boulder06", 6000),
    "namaqualand_rocks_01": ("SM_Rock_Cluster01", 5000),
    "mountainside": ("SM_Rock_Cliff01", 20000),
}
TEX = 2048
WORK = r"C:\Dev\moon-hopper\SourceArt\Environment\Kelxor_Rocks\baked"


def scans():
    """Group the imported scan meshes by their material (one asset each)."""
    out = {}
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.get("mh_game") or not o.material_slots or o.material_slots[0].material is None:
            continue
        key = o.material_slots[0].material.name
        if key in ROCKS:
            out.setdefault(key, []).append(o)
    return out


def image_from_material(mat, kind):
    """The scan's texture feeding a Principled input (glTF import wires base color, normal, and the ARM image)."""
    nt = mat.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    def walk(sock):
        if not sock.links:
            return None
        n = sock.links[0].from_node
        if n.type == "TEX_IMAGE":
            return n.image
        for s in n.inputs:
            r = walk(s)
            if r:
                return r
        return None
    return walk(p.inputs[{"base": "Base Color", "rough": "Roughness", "normal": "Normal"}[kind]])


def pixels(img, size):
    if img.size[0] != size:
        img = img.copy()
        img.scale(size, size)
    a = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(size, size, 4)


def save_png(name, arr, data):
    img = bpy.data.images.get(name) or bpy.data.images.new(name, TEX, TEX, alpha=True)
    if img.size[0] != TEX:
        img.scale(TEX, TEX)
    img.colorspace_settings.name = 'Non-Color' if data else 'sRGB'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    path = os.path.join(WORK, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    return path


def process():
    os.makedirs(WORK, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'GPU'
    sc.cycles.samples = 8
    win = bpy.context.window_manager.windows[0]
    for key, objs in scans().items():
        name, target = ROCKS[key]
        mat = objs[0].material_slots[0].material
        # High: the scan, transforms applied, origin at the bottom center
        for o in objs:
            o.data.transform(o.matrix_world)
            o.matrix_world.identity()
        if len(objs) > 1:
            with bpy.context.temp_override(window=win, active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
                bpy.ops.object.join()
        high = objs[0]
        high.name = name + "_high"
        vs = np.array([v.co[:] for v in high.data.vertices])
        lo, hi_ = vs.min(0), vs.max(0)
        base = np.array([(lo[0] + hi_[0]) / 2, (lo[1] + hi_[1]) / 2, lo[2]])
        from mathutils import Matrix, Vector
        high.data.transform(Matrix.Translation(-Vector(base)))
        high.location = (0, 0, 0)
        tris = sum(len(p.vertices) - 2 for p in high.data.polygons)
        # Low: decimated copy (UVs kept, so the scan's color and roughness still map)
        low = high.copy()
        low.data = high.data.copy()
        low.name = name
        low["mh_game"] = True
        bpy.context.scene.collection.objects.link(low)
        dec = low.modifiers.new("Decimate", 'DECIMATE')
        dec.ratio = min(1.0, target / max(tris, 1))
        dec.use_collapse_triangulate = True
        with bpy.context.temp_override(window=win, object=low, active_object=low):
            bpy.ops.object.modifier_apply(modifier=dec.name)
        for p in low.data.polygons:
            p.use_smooth = True
        # Bake the high scan's shading normal (its geometry plus its own normal map) onto the low mesh
        nimg = bpy.data.images.new(name + "_nbake", TEX, TEX, alpha=False, float_buffer=False)
        nimg.colorspace_settings.name = 'Non-Color'
        bake_mat = bpy.data.materials.new(name + "_bakeTarget")
        bake_mat.use_nodes = True
        tn = bake_mat.node_tree.nodes.new("ShaderNodeTexImage")
        tn.image = nimg
        bake_mat.node_tree.nodes.active = tn
        low.data.materials.clear()
        low.data.materials.append(bake_mat)
        size = float(np.max(hi_ - lo))
        sc.render.bake.use_selected_to_active = True
        sc.render.bake.cage_extrusion = 0.02 * size
        sc.render.bake.max_ray_distance = 0.05 * size
        sc.render.bake.margin = 16
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        high.select_set(True)
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        with bpy.context.temp_override(window=win, active_object=low, object=low, selected_objects=[high, low]):
            bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT')
        # Textures in our set
        bc = pixels(image_from_material(mat, "base"), TEX); bc[..., 3] = 1
        arm = pixels(image_from_material(mat, "rough"), TEX)      # glTF ARM: R occlusion, G roughness, B metal
        nm = pixels(nimg, TEX); nm[..., 3] = 1
        ms = np.zeros_like(arm); ms[..., 0] = arm[..., 2]; ms[..., 3] = 1 - arm[..., 1]
        ao = np.repeat(arm[..., 0:1], 4, axis=2); ao[..., 3] = 1
        stem = "T_" + name[3:]
        save_png(stem + "_BaseColor", bc, False)
        save_png(stem + "_Normal", nm, True)
        save_png(stem + "_MetallicSmoothness", ms, True)
        save_png(stem + "_Occlusion", ao, True)
        # A plain material on the low mesh for the export (Unity builds the real one from the textures)
        game_mat = bpy.data.materials.get("M_" + name[3:]) or bpy.data.materials.new("M_" + name[3:])
        low.data.materials.clear()
        low.data.materials.append(game_mat)
        # LODs
        lod0 = low
        lod0.name = name + "_LOD0"
        lods = [lod0]
        for i, r in ((1, 0.5), (2, 0.22)):
            l = lod0.copy(); l.data = lod0.data.copy(); l.name = f"{name}_LOD{i}"
            bpy.context.scene.collection.objects.link(l)
            d = l.modifiers.new("Decimate", 'DECIMATE'); d.ratio = r; d.use_collapse_triangulate = True
            with bpy.context.temp_override(window=win, object=l, active_object=l):
                bpy.ops.object.modifier_apply(modifier=d.name)
            lods.append(l)
        root = bpy.data.objects.new(name, None)
        bpy.context.scene.collection.objects.link(root)
        root["mh_game"] = True
        for l in lods:
            l.parent = root
        high.hide_set(True)
        high.hide_render = True
        print(f"{name}: scan {tris} tris -> LOD0 {sum(len(p.vertices) - 2 for p in lod0.data.polygons)}, size {size:.2f} m")


def export():
    sys.path.append(r"C:\Dev\moon-hopper\Tools\blender")
    import importlib, moon_hopper_export as mh
    importlib.reload(mh)
    for key, (name, _) in ROCKS.items():
        root = bpy.data.objects.get(name)
        if root is None:
            continue
        objs = [root] + list(root.children)
        fbx, problems = mh.export_asset("Environment", name, objs, force=True)
        tdir = os.path.join(os.path.dirname(fbx), "Textures")
        os.makedirs(tdir, exist_ok=True)
        stem = "T_" + name[3:]
        for m in ("BaseColor", "Normal", "MetallicSmoothness", "Occlusion"):
            shutil.copy2(os.path.join(WORK, f"{stem}_{m}.png"), os.path.join(tdir, f"{stem}_{m}.png"))
        print("exported", name, problems)


if STAGE == "process":
    process()
elif STAGE == "export":
    export()
