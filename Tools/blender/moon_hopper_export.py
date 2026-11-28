bl_info = {
    "name": "Moon Hopper Export",
    "author": "alcax_studios",
    "version": (1, 0),
    "blender": (4, 2, 0),
    "location": "3D View > Sidebar > Moon Hopper",
    "description": "Checks and exports an asset to the Unity project with the pipeline's fixed FBX settings",
    "category": "Import-Export",
}

# Exports the selected objects as one asset:
#   Unity:  <project>/Assets/_Project/Art/Models/<Class>/<Asset>/<Asset>.fbx  (+ textures it uses)
#   Source: <project>/SourceArt/<Class>/<Asset>/<Asset>.blend                 (a copy of the open file)
# Rules (docs/art/asset-pipeline.md): 1 unit = 1 m, origin at the base, transforms applied, every mesh UV'd,
# LODs named <Asset>_LOD0, _LOD1, ... The Unity import validator enforces the budgets.
# Scriptable: import moon_hopper_export as mh; mh.export_asset("Props", "SM_Crate")

import os
import shutil
import bpy

PROJECT = r"C:\Dev\moon-hopper"
CLASSES = ["Props", "Weapons", "Architecture", "Environment", "Vehicles", "Characters", "Creatures", "Foliage"]
PREFIX = {"Characters": "SK_", "Creatures": "SK_"}


def check(objects, asset_class, asset_name):  # noqa: C901
    """Problems that would break the import. Returns a list of strings (empty when clean)."""
    problems = []
    prefix = PREFIX.get(asset_class, "SM_")
    if not asset_name.startswith(prefix):
        problems.append(f"name should start with {prefix}")
    meshes = [o for o in objects if o.type == 'MESH']
    if not meshes:
        problems.append("no mesh selected")
    low = None
    for o in meshes:
        if any(abs(s - 1.0) > 1e-4 for s in o.scale):
            problems.append(f"{o.name}: scale not applied (Ctrl+A > Scale)")
        if o.parent is None and o.type == 'MESH' and any(abs(r) > 1e-4 for r in o.rotation_euler):
            problems.append(f"{o.name}: rotation not applied (Ctrl+A > Rotation)")
        if not o.data.uv_layers:
            problems.append(f"{o.name}: no UV map")
        z = min((o.matrix_world @ v.co).z for v in o.data.vertices) if o.data.vertices else 0.0
        low = z if low is None else min(low, z)
    if low is not None and abs(low) > 0.02 and asset_class != "Weapons":   # weapons: origin is the grip
        problems.append(f"lowest point is at {low:.3f} m: put the base at z = 0")
    return problems


def triangles(objects):
    deps = bpy.context.evaluated_depsgraph_get()
    n = 0
    for o in objects:
        if o.type != 'MESH' or "_LOD" in o.name and not o.name.endswith("_LOD0"):
            continue
        m = o.evaluated_get(deps).to_mesh()
        n += sum(len(p.vertices) - 2 for p in m.polygons)
        o.evaluated_get(deps).to_mesh_clear()
    return n


def export_asset(asset_class, asset_name, objects=None, force=False):
    """Export the objects (default: selection). Returns (fbx_path, problems)."""
    if asset_class not in CLASSES:
        raise ValueError(f"class must be one of {CLASSES}")
    objects = list(objects or bpy.context.selected_objects)
    problems = check(objects, asset_class, asset_name)
    if problems and not force:
        return None, problems
    out = os.path.join(PROJECT, "Assets", "_Project", "Art", "Models", asset_class, asset_name)
    os.makedirs(out, exist_ok=True)
    fbx = os.path.join(out, asset_name + ".fbx")
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    rigged = asset_class in PREFIX
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True,
        object_types={'MESH', 'ARMATURE', 'EMPTY'} if rigged else {'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', bake_space_transform=not rigged,
        mesh_smooth_type='FACE', use_tspace=True, add_leaf_bones=False,
        bake_anim=rigged, path_mode='STRIP', embed_textures=False)   # textures are collected separately under our names
    # Keep the editable source next to the project (outside Assets/, so Unity doesn't import it)
    if bpy.data.filepath:
        src = os.path.join(PROJECT, "SourceArt", asset_class, asset_name)
        os.makedirs(src, exist_ok=True)
        bpy.ops.wm.save_mainfile()
        dst = os.path.join(src, asset_name + ".blend")
        if os.path.normcase(os.path.abspath(dst)) != os.path.normcase(os.path.abspath(bpy.data.filepath)):
            shutil.copy2(bpy.data.filepath, dst)
    print(f"[MoonHopper] exported {fbx} ({triangles(objects)} tris)")
    return fbx, problems


class MH_OT_export(bpy.types.Operator):
    bl_idname = "moonhopper.export"
    bl_label = "Check and Export"
    bl_description = "Check the selection and export it to the Unity project"

    def execute(self, context):
        s = context.scene
        fbx, problems = export_asset(s.mh_class, s.mh_asset)
        for p in problems:
            self.report({'WARNING'}, p)
        if fbx is None:
            self.report({'ERROR'}, f"Not exported: {len(problems)} problem(s), see the messages")
            return {'CANCELLED'}
        self.report({'INFO'}, f"Exported {os.path.basename(fbx)} ({triangles(context.selected_objects)} tris)")
        return {'FINISHED'}


class MH_PT_panel(bpy.types.Panel):
    bl_label = "Moon Hopper Export"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Moon Hopper"

    def draw(self, context):
        s = context.scene
        col = self.layout.column()
        col.prop(s, "mh_class")
        col.prop(s, "mh_asset")
        sel = context.selected_objects
        col.label(text=f"{len(sel)} selected, {triangles(sel)} tris")
        for p in check(sel, s.mh_class, s.mh_asset)[:6]:
            col.label(text=p, icon='ERROR')
        col.operator("moonhopper.export", icon='EXPORT')


def register():
    bpy.types.Scene.mh_class = bpy.props.EnumProperty(name="Class", items=[(c, c, "") for c in CLASSES], default="Props")
    bpy.types.Scene.mh_asset = bpy.props.StringProperty(name="Asset", default="SM_NewAsset")
    bpy.utils.register_class(MH_OT_export)
    bpy.utils.register_class(MH_PT_panel)


def unregister():
    bpy.utils.unregister_class(MH_PT_panel)
    bpy.utils.unregister_class(MH_OT_export)
    del bpy.types.Scene.mh_class
    del bpy.types.Scene.mh_asset
