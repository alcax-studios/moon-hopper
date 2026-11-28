"""
Desert plants for Kelxor (docs/tech/pipeline-test-plan.md, test 2): generated meshes with the wind weights the
foliage shader reads, painted into vertex colors:
    R = main bend (0 at the base, rising to 1 at the tips)   G = branch sway (per branch, with a phase)
    B = leaf flutter (leaves and blade tips)
Species (several seeded variants each):
    Scrub     a creosote-like bush: thin branching stems, small leaves on cards (alpha texture)
    Succulent an alien agave: a spiral rosette of thick fleshy leaves, teal shading to orange tips
    Grass     a dry tuft of thin blades
Textures are generated here too. Run in its own Blender file; STAGE = "build" or "export".
"""
import os, math, random, sys
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix

STAGE = globals().get("STAGE", "build")
OUT_TEX = r"C:\Dev\moon-hopper\SourceArt\Environment\Kelxor_Plants\textures"
COLL = "Plants"


def coll():
    c = bpy.data.collections.get(COLL)
    if c is None:
        c = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(c)
    return c


def clear():
    c = coll()
    for o in list(c.objects):
        bpy.data.objects.remove(o, do_unlink=True)


# ---------- Textures ----------

def save(name, arr):
    os.makedirs(OUT_TEX, exist_ok=True)
    h, w = arr.shape[:2]
    img = bpy.data.images.get(name) or bpy.data.images.new(name, w, h, alpha=True)
    if img.size[0] != w:
        img.scale(w, h)
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    path = os.path.join(OUT_TEX, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    return path


def textures():
    rng = np.random.default_rng(3)
    n = 512
    y, x = np.mgrid[0:n, 0:n] / (n - 1.0)
    # Scrub leaf atlas: 4 x 4 small oval leaves with a midrib, olive to dusty green
    leaf = np.zeros((n, n, 4), np.float32)
    t = n // 4
    for i in range(16):
        cx, cy = (i % 4) * t, (i // 4) * t
        u = (np.mgrid[0:t, 0:t][1] / (t - 1.0)) * 2 - 1
        v = (np.mgrid[0:t, 0:t][0] / (t - 1.0))
        width = 0.42 * np.sin(np.clip(v, 0, 1) * np.pi) ** 0.8 * (0.85 + 0.3 * rng.random())
        a = np.clip((width - np.abs(u)) * 40, 0, 1) * (v > 0.04)
        base = np.array([0.27, 0.31, 0.14]) * (0.85 + 0.3 * rng.random())
        tip = np.array([0.44, 0.44, 0.22])
        col = base[None, None, :] * (1 - v[..., None]) + tip[None, None, :] * v[..., None]
        col *= (1 - 0.35 * np.exp(-(u * 14) ** 2))[..., None]          # midrib
        col *= (0.9 + 0.2 * rng.random((t, t)))[..., None]
        leaf[cy:cy + t, cx:cx + t, :3] = col
        leaf[cy:cy + t, cx:cx + t, 3] = a
    save("T_Plant_ScrubLeaf_BaseColor", leaf)
    # Stems and bark: grey-brown with streaks
    bark = np.zeros((256, 256, 4), np.float32)
    s = 0.25 + 0.08 * np.sin(np.mgrid[0:256, 0:256][1] / 6.0 + rng.random((256, 256)) * 2)
    bark[..., 0], bark[..., 1], bark[..., 2], bark[..., 3] = s * 1.15, s * 0.95, s * 0.75, 1
    save("T_Plant_Bark_BaseColor", bark)
    # Succulent: along V from a pale base through teal to burnt-orange tips, fine lengthwise ridges
    v = y
    base = np.array([0.55, 0.6, 0.45]); mid = np.array([0.18, 0.38, 0.36]); tip = np.array([0.62, 0.28, 0.1])
    k1 = np.clip(v / 0.5, 0, 1)[..., None]; k2 = np.clip((v - 0.6) / 0.4, 0, 1)[..., None]
    col = base * (1 - k1) + mid * k1
    col = col * (1 - k2) + tip * k2
    col *= (0.88 + 0.12 * np.sin(x * 90))[..., None]
    col *= (0.9 + 0.1 * rng.random((n, n)))[..., None]
    suc = np.concatenate([col, np.ones((n, n, 1))], axis=2)
    save("T_Plant_Succulent_BaseColor", suc)
    # Dry grass blade: straw with darker base, alpha tapering to a point
    g = np.zeros((n, 64, 4), np.float32)
    vv, uu = np.mgrid[0:n, 0:64].astype(np.float32)
    vv /= n - 1.0
    uu /= 63.0
    w = 0.5 * (1 - vv) ** 0.7 + 0.02
    g[..., 3] = np.clip((w - np.abs(uu * 2 - 1) * 0.5) * 60, 0, 1)
    straw = np.array([0.72, 0.6, 0.38]); dark = np.array([0.38, 0.32, 0.2])
    g[..., :3] = dark * (1 - vv[..., None]) + straw * vv[..., None]
    g[..., :3] *= (0.85 + 0.15 * np.sin(uu * 30))[..., None]
    save("T_Plant_Grass_BaseColor", g)


# ---------- Mesh helpers ----------

class Builder:
    def __init__(self, name):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.col = self.bm.loops.layers.color.new("Col")
        self.name = name

    def quad(self, p, uv, c):
        vs = [self.bm.verts.new(x) for x in p]
        f = self.bm.faces.new(vs)
        for l, t, cc in zip(f.loops, uv, c):
            l[self.uv].uv = t
            l[self.col] = cc
        return f

    def tube(self, pts, radii, sides, color_fn, uv_scale=1.0):
        """A tapered tube along points (a stem). color_fn(t) -> RGBA."""
        rings = []
        for i, p in enumerate(pts):
            d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            a = d.orthogonal().normalized()
            b = d.cross(a)
            ring = []
            for s in range(sides):
                ang = 2 * math.pi * s / sides
                ring.append(self.bm.verts.new(p + (a * math.cos(ang) + b * math.sin(ang)) * radii[i]))
            rings.append(ring)
        for i in range(len(rings) - 1):
            t0, t1 = i / (len(rings) - 1), (i + 1) / (len(rings) - 1)
            for s in range(sides):
                s1 = (s + 1) % sides
                f = self.bm.faces.new((rings[i][s], rings[i][s1], rings[i + 1][s1], rings[i + 1][s]))
                for l, (u, v, t) in zip(f.loops, ((s / sides, t0, t0), ((s + 1) / sides, t0, t0), ((s + 1) / sides, t1, t1), (s / sides, t1, t1))):
                    l[self.uv].uv = (u * uv_scale, v * uv_scale * 4)
                    l[self.col] = color_fn(t)

    def object(self, mats):
        me = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(me)
        self.bm.free()
        ob = bpy.data.objects.new(self.name, me)
        coll().objects.link(ob)
        for m in mats:
            me.materials.append(m)
        for p in me.polygons:
            p.use_smooth = True
        return ob


def mat(name, tex, alpha):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    img = bpy.data.images.load(os.path.join(OUT_TEX, tex + ".png"), check_existing=True)
    t = nt.nodes.get("Tex") or nt.nodes.new("ShaderNodeTexImage")
    t.name = "Tex"
    t.image = img
    nt.links.new(t.outputs[0], p.inputs["Base Color"])
    if alpha:
        nt.links.new(t.outputs[1], p.inputs["Alpha"])
        try:
            m.surface_render_method = 'DITHERED'
        except Exception:
            pass
    p.inputs["Roughness"].default_value = 0.7
    return m


# ---------- Species ----------

def scrub(seed):
    rnd = random.Random(seed)
    b = Builder(f"SM_Plant_Scrub{seed:02d}")
    height = rnd.uniform(0.7, 1.3)
    stems = rnd.randint(16, 22)
    leaves = []
    for s in range(stems):
        ang = rnd.uniform(0, 2 * math.pi)
        lean = rnd.uniform(0.25, 0.65)
        phase = rnd.random()
        segs = 6
        pts = []
        p = Vector((math.cos(ang) * 0.03, math.sin(ang) * 0.03, 0))
        d = Vector((math.cos(ang) * lean, math.sin(ang) * lean, 1)).normalized()
        length = height * rnd.uniform(0.6, 1.0)
        for i in range(segs + 1):
            pts.append(p.copy())
            d = (d + Vector((rnd.uniform(-0.25, 0.25), rnd.uniform(-0.25, 0.25), 0.05))).normalized()
            p += d * length / segs
        r = [0.012 * (1 - i / (segs + 1)) + 0.002 for i in range(segs + 1)]
        b.tube(pts, r, 4, lambda t, ph=phase: (min(1, (t * pts[-1].z / height) ** 1.5), ph * 0.5 + 0.5 * t, 0.0, 1))
        # Leaf cards along the outer half of the stem
        for i in range(1, segs + 1):
            for k in range(5):
                leaves.append((pts[i], rnd, phase, (pts[i].z / height)))
    # Leaves: small crossed cards with one leaf from the atlas each
    leaf_faces = []
    for (p, r, ph, t) in leaves:
        size = r.uniform(0.05, 0.09)
        tile = r.randint(0, 15)
        u0, v0 = (tile % 4) / 4, (tile // 4) / 4
        a = r.uniform(0, 2 * math.pi)
        for rot in (0, math.pi / 2):
            dx = Vector((math.cos(a + rot), math.sin(a + rot), 0)) * size * 0.5
            up = Vector((r.uniform(-0.3, 0.3), r.uniform(-0.3, 0.3), 1)).normalized() * size
            c = (min(1, t ** 1.5), ph * 0.5 + 0.5, 1.0, 1)
            f = b.quad([p - dx, p + dx, p + dx + up, p - dx + up],
                       [(u0, v0), (u0 + 0.25, v0), (u0 + 0.25, v0 + 0.25), (u0, v0 + 0.25)], [c, c, c, c])
            f.material_index = 1
            leaf_faces.append(f)
    return b.object([mat("M_Plant_Bark", "T_Plant_Bark_BaseColor", False), mat("M_Plant_ScrubLeaf", "T_Plant_ScrubLeaf_BaseColor", True)])


def succulent(seed):
    rnd = random.Random(seed + 100)
    b = Builder(f"SM_Plant_Succulent{seed:02d}")
    n = rnd.randint(16, 24)
    golden = math.radians(137.5)
    size = rnd.uniform(0.6, 1.1)
    for i in range(n):
        ang = i * golden
        t = i / n
        length = size * (0.55 + 0.45 * (1 - t) ** 0.5) * rnd.uniform(0.85, 1.1)
        tilt = math.radians(20 + 55 * (1 - t))        # outer leaves lie lower
        width = 0.07 * size * rnd.uniform(0.8, 1.2)
        thick = 0.025 * size
        phase = rnd.random()
        segs = 7
        d = Vector((math.cos(ang) * math.sin(tilt), math.sin(ang) * math.sin(tilt), math.cos(tilt)))
        side = Vector((-math.sin(ang), math.cos(ang), 0))
        rows = []
        for k in range(segs + 1):
            s = k / segs
            curl = s * s * 0.5 * (1 - t)          # tips curl outward and down
            dd = (d + Vector((math.cos(ang), math.sin(ang), -1.2)) * curl).normalized()
            center = dd * length * s + Vector((0, 0, 0.02))
            center.z = max(center.z, 0.015 + 0.01 * s)   # outer leaves rest on the ground instead of piercing it
            w = width * math.sin(math.pi * min(1.0, s * 1.05 + 0.08)) ** 0.6
            normal = dd.cross(side).normalized()
            rows.append((center - side * w, center, center + side * w, center + normal * thick * (1 - s), s))
        for k in range(segs):
            r0, r1 = rows[k], rows[k + 1]
            s0, s1 = r0[4], r1[4]
            c0 = (min(1, (s0 * length / size) ** 1.4), phase, 0.2 * s0, 1)
            c1 = (min(1, (s1 * length / size) ** 1.4), phase, 0.2 * s1, 1)
            # Top face (two quads across the leaf) and the ridge underneath
            b.quad([r0[0], r0[1], r1[1], r1[0]], [(0, s0), (0.5, s0), (0.5, s1), (0, s1)], [c0, c0, c1, c1])
            b.quad([r0[1], r0[2], r1[2], r1[1]], [(0.5, s0), (1, s0), (1, s1), (0.5, s1)], [c0, c0, c1, c1])
            b.quad([r1[0], r1[3], r0[3], r0[0]], [(0, s1), (0.5, s1), (0.5, s0), (0, s0)], [c1, c1, c0, c0])
            b.quad([r0[2], r0[3], r1[3], r1[2]], [(1, s0), (0.5, s0), (0.5, s1), (1, s1)], [c0, c0, c1, c1])
    return b.object([mat("M_Plant_Succulent", "T_Plant_Succulent_BaseColor", False)])


def grass(seed):
    rnd = random.Random(seed + 200)
    b = Builder(f"SM_Plant_Grass{seed:02d}")
    blades = rnd.randint(28, 44)
    for i in range(blades):
        ang = rnd.uniform(0, 2 * math.pi)
        r0 = rnd.uniform(0, 0.08)
        base = Vector((math.cos(ang) * r0, math.sin(ang) * r0, 0))
        h = rnd.uniform(0.25, 0.55)
        lean = rnd.uniform(0.15, 0.6)
        d = Vector((math.cos(ang) * lean, math.sin(ang) * lean, 1)).normalized()
        side = Vector((-math.sin(ang), math.cos(ang), 0)) * rnd.uniform(0.006, 0.011)
        phase = rnd.random()
        segs = 4
        prev = None
        for k in range(segs + 1):
            s = k / segs
            droop = Vector((math.cos(ang), math.sin(ang), -0.8)) * s * s * lean * 0.6
            p = base + (d + droop).normalized() * h * s
            w = side * (1 - s * 0.85)
            row = (p - w, p + w, s)
            if prev:
                c0 = (prev[2] ** 1.3, phase, prev[2], 1); c1 = (s ** 1.3, phase, s, 1)
                b.quad([prev[0], prev[1], row[1], row[0]], [(0, prev[2]), (1, prev[2]), (1, s), (0, s)], [c0, c0, c1, c1])
            prev = row
    return b.object([mat("M_Plant_Grass", "T_Plant_Grass_BaseColor", True)])


SPECIES = {"Scrub": scrub, "Succulent": succulent, "Grass": grass}
VARIANTS = 3


def build():
    clear()
    textures()
    x = 0.0
    for name, fn in SPECIES.items():
        for v in range(VARIANTS):
            ob = fn(v + 1)
            ob.location = (x, 0, 0)
            x += 1.6
            tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            print(ob.name, tris, "tris")


def export():
    sys.path.append(r"C:\Dev\moon-hopper\Tools\blender")
    import importlib, shutil, moon_hopper_export as mh
    importlib.reload(mh)
    for ob in list(coll().objects):
        if not ob.name.startswith("SM_Plant_"):
            continue
        loc = ob.location.copy()
        ob.location = (0, 0, 0)
        bpy.context.view_layer.update()
        objs = [ob]
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        temp = []
        if tris > 1500:
            # LODs: fewer leaves further away (cards removed at random), stems kept
            root = bpy.data.objects.new(ob.name + "_root", None)
            coll().objects.link(root)
            temp.append(root)
            objs = [root]
            for i, keep in ((0, 1.0), (1, 0.45), (2, 0.15)):
                l = ob.copy(); l.data = ob.data.copy(); l.name = f"{ob.name}_LOD{i}"
                coll().objects.link(l)
                l.parent = root
                temp.append(l)
                if keep < 1.0:
                    bm = bmesh.new(); bm.from_mesh(l.data)
                    r = random.Random(i)
                    kill = [f for f in bm.faces if f.material_index == 1 and r.random() > keep]
                    bmesh.ops.delete(bm, geom=kill, context='FACES')
                    bm.to_mesh(l.data); bm.free()
                objs.append(l)
            root.name = ob.name + "__tmp"
        fbx, problems = mh.export_asset("Foliage", ob.name, objs, force=True)
        for t in temp:
            bpy.data.objects.remove(t, do_unlink=True)
        ob.location = loc
        tdir = os.path.join(os.path.dirname(fbx), "Textures")
        os.makedirs(tdir, exist_ok=True)
        for m in ob.data.materials:
            img = m.node_tree.nodes["Tex"].image
            shutil.copy2(bpy.path.abspath(img.filepath), os.path.join(tdir, os.path.basename(img.filepath)))
        print("exported", ob.name, problems)


if STAGE == "build":
    build()
elif STAGE == "export":
    export()
