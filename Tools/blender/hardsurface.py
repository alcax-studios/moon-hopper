"""
Hard-surface helpers for the mid-poly workflow (docs/art/asset-pipeline.md):
profiles extruded to a width, primitives, boolean cutters, then a bevel (by angle, hardened normals)
and weighted normals on every part, so edges catch light without a high-to-low bake.

Blender is Z-up. Weapons point down -Y (Blender's front), 1 unit = 1 m.
Use: import sys; sys.path.append(r"C:\\Dev\\moon-hopper\\Tools\\blender"); import hardsurface as hs
"""
import math
import bmesh
import bpy
from mathutils import Vector, Matrix

COLL = None


def collection(name):
    global COLL
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    COLL = c
    return c


def clear(name):
    c = bpy.data.collections.get(name)
    if c:
        for o in list(c.all_objects):
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def _obj(name, bm, mat=None, parent=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    (COLL or bpy.context.scene.collection).objects.link(ob)
    if mat:
        me.materials.append(mat)
    if parent:
        ob.parent = parent
    return ob


def profile(name, pts_yz, width, x=0.0, mat=None, chamfer=0.0):
    """Extrude a side profile (points in Y, Z) to a width along X, centered on x.
    chamfer: a large flat chamfer around both side outlines (the machined look), in meters."""
    bm = bmesh.new()
    vs = [bm.verts.new((x - width / 2, y, z)) for y, z in pts_yz]
    f = bm.faces.new(vs)
    bm.normal_update()
    if f.normal.x > 0:
        f.normal_flip()
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.x += width
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if chamfer > 0:
        caps = [fc for fc in bm.faces if abs(fc.normal.x) > 0.99]
        edges = list({e for fc in caps for e in fc.edges})
        bmesh.ops.bevel(bm, geom=edges, offset=chamfer, segments=1, affect='EDGES', profile=0.5, clamp_overlap=True)
    return _obj(name, bm, mat)


def tube(name, points, radius, mat=None, segs=10):
    """A cable or conduit through 3D points (a curve with a round bevel, converted to mesh)."""
    cu = bpy.data.curves.new(name + "_curve", 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = radius
    cu.bevel_resolution = max(1, segs // 4)
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = p
        bp.handle_left_type = bp.handle_right_type = 'AUTO'
    cu.resolution_u = 8
    ob = bpy.data.objects.new(name + "_tmp", cu)
    (COLL or bpy.context.scene.collection).objects.link(ob)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    out = bpy.data.objects.new(name, me)
    (COLL or bpy.context.scene.collection).objects.link(out)
    if mat:
        me.materials.append(mat)
    return out


def box(name, mn, mx, mat=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    c = [(a + b) / 2 for a, b in zip(mn, mx)]
    s = [b - a for a, b in zip(mn, mx)]
    for v in bm.verts:
        v.co = Vector((c[0] + v.co.x * s[0], c[1] + v.co.y * s[1], c[2] + v.co.z * s[2]))
    return _obj(name, bm, mat)


def cyl(name, radius, y0, y1, x=0.0, z=0.0, segs=32, mat=None, r1=None, spin=0.0):
    """A cylinder along Y from y0 to y1 (r1: radius at y1 for a cone; spin: degrees around its own axis)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=r1 if r1 is not None else radius, depth=abs(y1 - y0))
    rot = Matrix.Rotation(math.radians(90), 4, 'X')
    bmesh.ops.transform(bm, matrix=rot, verts=bm.verts)
    if spin:
        bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(spin), 4, 'Y'), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector((x, (y0 + y1) / 2, z)), verts=bm.verts)
    return _obj(name, bm, mat)


def cylz(name, radius, z0, z1, x=0.0, y=0.0, segs=24, mat=None):
    """A cylinder along Z (screws seen from above)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=abs(z1 - z0))
    bmesh.ops.translate(bm, vec=Vector((x, y, (z0 + z1) / 2)), verts=bm.verts)
    return _obj(name, bm, mat)


def cylx(name, radius, x0, x1, y=0.0, z=0.0, segs=24, mat=None):
    """A cylinder along X (screws on the sides)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=abs(x1 - x0))
    bmesh.ops.transform(bm, matrix=Matrix.Rotation(math.radians(90), 4, 'Y'), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(((x0 + x1) / 2, y, z)), verts=bm.verts)
    return _obj(name, bm, mat)


def torus(name, major, minor, y, x=0.0, z=0.0, mat=None, segs=48, minor_segs=12):
    bm = bmesh.new()
    for i in range(segs):
        a = 2 * math.pi * i / segs
        for j in range(minor_segs):
            b = 2 * math.pi * j / minor_segs
            r = major + minor * math.cos(b)
            bm.verts.new((x + r * math.cos(a), y + minor * math.sin(b), z + r * math.sin(a)))
    bm.verts.ensure_lookup_table()
    for i in range(segs):
        for j in range(minor_segs):
            a = i * minor_segs + j
            b = ((i + 1) % segs) * minor_segs + j
            c = ((i + 1) % segs) * minor_segs + (j + 1) % minor_segs
            d = i * minor_segs + (j + 1) % minor_segs
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[c], bm.verts[d]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj(name, bm, mat)


def cutter(name, mn, mx):
    """An invisible box used to cut (boolean difference)."""
    ob = box(name, mn, mx)
    ob.display_type = 'WIRE'
    ob.hide_render = True
    ob["cutter"] = True
    return ob


def cut(target, cutters):
    """Boolean-difference cutters out of the target (non-destructive; applied at export)."""
    for c in cutters:
        c["cutter"] = True
        c.hide_render = True
        c.display_type = 'WIRE'
        m = target.modifiers.new("Cut_" + c.name, 'BOOLEAN')
        m.operation = 'DIFFERENCE'
        m.object = c
        try:
            m.material_mode = 'INDEX'   # cut walls keep the part's material
        except (AttributeError, TypeError):
            pass
        m.solver = 'EXACT'
        c.parent = target
        c.matrix_parent_inverse = target.matrix_world.inverted()
        c.hide_viewport = True


def finish(ob, bevel=0.0012, segments=2, angle=35):
    """Mid-poly finish: angle-limited bevel with hardened normals, then weighted normals."""
    if bevel > 0:
        b = ob.modifiers.new("Bevel", 'BEVEL')
        b.width = bevel
        b.segments = segments
        b.limit_method = 'ANGLE'
        b.angle_limit = math.radians(angle)
        b.harden_normals = True
        b.miter_outer = 'MITER_ARC'
        b.use_clamp_overlap = True
    w = ob.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL')
    w.keep_sharp = True
    w.weight = 50
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def tris(objs):
    deps = bpy.context.evaluated_depsgraph_get()
    n = 0
    for o in objs:
        if o.type != 'MESH' or o.get("cutter"):
            continue
        e = o.evaluated_get(deps)
        m = e.to_mesh()
        n += sum(len(p.vertices) - 2 for p in m.polygons)
        e.to_mesh_clear()
    return n


def material(name, color, metallic=0.0, roughness=0.5, emission=None, strength=0.0, alpha=1.0):
    """A simple Principled material (detailed wear comes in the texturing pass)."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = roughness
    if emission:
        p.inputs["Emission Color"].default_value = (*emission, 1)
        p.inputs["Emission Strength"].default_value = strength
    if alpha < 1:
        p.inputs["Alpha"].default_value = alpha
    return m


def srgb(hexstr):
    """'#RRGGBB' to linear RGB."""
    h = hexstr.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
