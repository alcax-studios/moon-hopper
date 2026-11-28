"""
Plasma carbine, texturing stage. Run after plasma_carbine.py:
exec(open(r"C:\\Dev\\moon-hopper\\Tools\\blender\\assets\\plasma_carbine_tex.py").read())
Set STAGE before running: "materials" (layered materials and markings), "bake" (game meshes, UVs, bake).
"""
import sys, math, importlib
sys.path.append(r"C:\Dev\moon-hopper\Tools\blender")
import bpy
from mathutils import Matrix, Vector
import hardsurface as hs, texturing as tx
importlib.reload(hs); importlib.reload(tx)

ASSET = "SM_PlasmaCarbine"
SIZE = globals().get("SIZE", 4096)
STAGE = globals().get("STAGE", "materials")
OUT = r"C:\Dev\moon-hopper\Assets\_Project\Art\Models\Weapons\SM_PlasmaCarbine\Textures"
c = hs.srgb
DUST = c("#7A6450")   # Kelxor dust

def materials():
    tx.wear_material("M_PC_Gunmetal", c("#3A3F45"), 0.5, 0.0, edge=(c("#A7ABAF"), 1.0, 0.3), dust=DUST, dust_amt=0.25, scratches=0.6)
    tx.wear_material("M_PC_Aluminum", c("#9AA0A6"), 0.3, 1.0, edge=(c("#C8CCD0"), 1.0, 0.2), dust=DUST, dust_amt=0.18, scratches=0.4)
    tx.wear_material("M_PC_Polymer", c("#2B2D30"), 0.62, 0.0, edge=(c("#4A4D52"), 0.0, 0.45), dust=DUST, dust_amt=0.35, stipple=1.0)
    tx.wear_material("M_PC_Rubber", c("#1F2022"), 0.9, 0.0, dust=DUST, dust_amt=0.45, stipple=0.6, breakup=0.04)
    tx.wear_material("M_PC_Orange", c("#FF6A2B"), 0.45, 0.0, edge=(c("#3A3F45"), 0.0, 0.5), dust=DUST, dust_amt=0.2, scratches=0.3)
    tx.wear_material("M_PC_DarkMetal", c("#24272B"), 0.5, 1.0, dust=DUST, dust_amt=0.15)
    tx.wear_material("M_PC_Energy", c("#6FE3FF"), 0.3, 0.0, dust_amt=0, emission=c("#6FE3FF"), strength=18)
    tx.wear_material("M_PC_Glass", c("#20262B"), 0.06, 0.0, dust=DUST, dust_amt=0.1, breakup=0.02)
    tx.wear_material("M_PC_Stencil", c("#D8D4CC"), 0.5, 0.0, dust=DUST, dust_amt=0.2)
    tx.wear_material("M_PC_Engrave", c("#1A1C1E"), 0.6, 0.0, dust_amt=0)


def text(name, body, size, pos, right, normal, mat):
    """Flat text geometry on a surface: right = reading direction, normal = outward."""
    cu = bpy.data.curves.new(name + "_font", 'FONT')
    cu.body = body
    cu.size = size
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    tmp = bpy.data.objects.new(name + "_tmp", cu)
    bpy.data.collections["PlasmaCarbine"].objects.link(tmp)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(deps))
    bpy.data.objects.remove(tmp)
    bpy.data.curves.remove(cu)
    r, n = Vector(right).normalized(), Vector(normal).normalized()
    up = n.cross(r)
    m = Matrix((r, up, n)).transposed().to_4x4()
    m.translation = Vector(pos) + n * 0.0002
    me.transform(m)
    ob = bpy.data.objects.new(name, me)
    bpy.data.collections["PlasmaCarbine"].objects.link(ob)
    me.materials.append(bpy.data.materials[mat])
    for p in me.polygons:
        p.use_smooth = False
    return ob


def markings():
    for o in [o for o in bpy.data.collections["PlasmaCarbine"].objects if o.name.startswith("Mark_")]:
        bpy.data.objects.remove(o)
    SH_Z = 0.058
    text("Mark_ModelL", "PC-1  PLASMA", 0.0062, (-0.0300, -0.128, 0.066), (0, -1, 0), (-1, 0, 0), "M_PC_Stencil")
    text("Mark_RnD", "R&D DIVISION  //  KELXOR", 0.0034, (-0.0300, -0.128, 0.056), (0, -1, 0), (-1, 0, 0), "M_PC_Stencil")
    text("Mark_Serial", "SN 5071-0042", 0.0042, (0.0306, -0.135, 0.062), (0, 1, 0), (1, 0, 0), "M_PC_Engrave")
    text("Mark_SafeR", "SAFE   SEMI", 0.0032, (0.0282, 0.025, 0.059), (0, 1, 0), (1, 0, 0), "M_PC_Stencil")
    text("Mark_CellR", "9.6 MJ", 0.0042, (0.0202, -0.097, -0.008), (0, 1, 0), (1, 0, 0), "M_PC_Stencil")
    text("Mark_CellL", "9.6 MJ", 0.0042, (-0.0202, -0.097, -0.008), (0, -1, 0), (-1, 0, 0), "M_PC_Stencil")
    side = 0.034 * math.cos(math.radians(22.5))
    text("Mark_HotR", "HOT", 0.0052, (side, -0.44, SH_Z), (0, 1, 0), (1, 0, 0), "M_PC_Orange")
    text("Mark_HotL", "HOT", 0.0052, (-side, -0.44, SH_Z), (0, -1, 0), (-1, 0, 0), "M_PC_Orange")


def fluid_uvs(o):
    """UVs for the fluid shader, in meters x 20 so blobs stay round. V runs along the flow: up through the cell,
    toward the muzzle along the barrel tubes; U runs across (around the tube)."""
    import math as m
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    SH_Z = 0.058
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = o.matrix_world @ me.vertices[me.loops[li].vertex_index].co
            if o.name.startswith("CellFluid"):
                uv[li].uv = (co.y * 20.0, co.z * 20.0)
            else:
                sx = 1.0 if co.x > 0 else -1.0
                cx, cz = sx * 0.026 * m.cos(m.radians(45)), SH_Z + 0.026 * m.sin(m.radians(45))
                ang = m.atan2(co.z - cz, co.x - cx)
                uv[li].uv = (ang * 0.0048 * 20.0, -co.y * 20.0)


GROUPS = {
    "Body": None,
    "Cell": ["Cell", "CellBand", "CellLatch", "Mark_CellR", "Mark_CellL"],
    "CellFluid": ["CellCore"],
    "BarrelFluid": ["Fluid-1", "Fluid1"],
    "VentFront": ["VentFront"],
    "VentRear": ["VentRear"],
    "Trigger": ["Trigger"],
    "Coil1": ["Coil1"], "Coil2": ["Coil2"], "Coil3": ["Coil3"],
}

if STAGE == "materials":
    materials()
    markings()
    print("materials and markings done")
elif STAGE == "bake":
    src = bpy.data.collections["PlasmaCarbine"]
    # An earlier export renamed the parts P_<name>; the groups use the plain names
    for o in src.all_objects:
        if o.name.startswith("P_"):
            o.name = o.name[2:]
    g = tx.bake_meshes(src, GROUPS, "PlasmaCarbine_Game")
    fluid = [g[k] for k in ("CellFluid", "BarrelFluid") if k in g]
    for o in fluid:
        fluid_uvs(o)
    objs = [o for o in g.values() if o not in fluid]
    tx.uv_atlas(objs)
    paths = tx.bake_set(objs, ASSET.replace("SM_", ""), SIZE, OUT)
    prev = tx.preview_material("M_PlasmaCarbine_Baked", ASSET, paths)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(prev)
    src.hide_viewport = True
    src.hide_render = True
    print({k: o.name for k, o in g.items()}, paths)

# ---------- Export stage: pivots, hierarchy, FBX ----------
GRIP = (0.0, 0.035, -0.015)        # where the hand holds it: the weapon's origin in Unity
SH_Z = 0.058
PIVOTS = {                          # moving parts rotate or slide around these
    "Trigger": (0.0, -0.005, -0.002),
    "VentRear": (0.0, -0.30, SH_Z + 0.0345),
    "VentFront": (0.0, -0.365, SH_Z + 0.0345),
    "Coil1": (0.0, -0.335, SH_Z), "Coil2": (0.0, -0.375, SH_Z), "Coil3": (0.0, -0.415, SH_Z),
}
NAMES = {"Cell": "Mag"}             # WeaponHolder animates a child called Mag on reload

def export():
    from mathutils import Vector, Matrix
    import moon_hopper_export as mh
    importlib.reload(mh)
    src = bpy.data.collections["PlasmaCarbine"]
    for o in src.all_objects:
        if not o.name.startswith("P_"):
            o.name = "P_" + o.name
    game = bpy.data.collections["PlasmaCarbine_Game"]
    for o in list(game.objects):
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o)
    root = bpy.data.objects.new(ASSET, None)
    game.objects.link(root)
    root.empty_display_size = 0.05
    root.location = GRIP
    bpy.context.view_layer.update()          # matrix_world must be current before parenting
    for o in list(game.objects):
        if o.type != 'MESH':
            continue
        base = o.name.split(".")[0].replace("_LOD0", "")
        if "_LOD" in o.name:                 # LOD meshes keep their names (Body_LOD0, _LOD1, _LOD2)
            base = "Body"
        else:
            name = NAMES.get(base, base)
            o.name = name
            o.data.name = name
        piv = Vector(PIVOTS.get(base, GRIP))
        if not o.get("mh_pivoted"):          # only shift the mesh once, however often this runs
            o.data.transform(Matrix.Translation(-piv))
            o["mh_pivoted"] = True
        o.parent = None
        o.location = piv
        o.parent = root
        o.matrix_parent_inverse = root.matrix_world.inverted()
    # LODs for the body (the moving parts are small): LOD1 about 50%, LOD2 about 25% of the triangles
    body = bpy.data.objects.get("Body") or bpy.data.objects.get("Body_LOD0")
    if body and body.name == "Body":
        body.name = "Body_LOD0"
        for i, ratio in ((1, 0.5), (2, 0.25)):
            lod = body.copy()
            lod.data = body.data.copy()
            lod.name = f"Body_LOD{i}"
            lod.data.name = lod.name
            game.objects.link(lod)
            dec = lod.modifiers.new("Decimate", 'DECIMATE')
            dec.ratio = ratio
            dec.use_collapse_triangulate = True
            bpy.ops.object.select_all(action='DESELECT')
            lod.select_set(True)
            bpy.context.view_layer.objects.active = lod
            bpy.ops.object.modifier_apply(modifier=dec.name)
            lod.parent = root
            lod.matrix_parent_inverse = root.matrix_world.inverted()
    # The cell's fluid rides with the cell (it drops out with it on reload)
    bpy.context.view_layer.update()
    mag, cf = bpy.data.objects.get("Mag"), bpy.data.objects.get("CellFluid")
    if mag and cf:
        cf.parent = mag
        cf.matrix_parent_inverse = mag.matrix_world.inverted()
    muzzle = bpy.data.objects.new("Muzzle", None)
    game.objects.link(muzzle)
    muzzle.location = (0.0, -0.585, SH_Z)
    muzzle.parent = root
    muzzle.matrix_parent_inverse = root.matrix_world.inverted()
    # Export with the root at the world origin so the grip is the FBX origin
    root.location = (0, 0, 0)
    bpy.context.view_layer.update()
    objs = [root] + list(root.children_recursive)   # nested parts too (the cell fluid rides under Mag)
    fbx, problems = mh.export_asset("Weapons", ASSET, objs, force=False)
    root.location = GRIP
    bpy.context.view_layer.update()
    print("export:", fbx, problems)

if STAGE == "export":
    export()
