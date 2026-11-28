"""
Smoke flipbook from a fluid simulation (docs/art/asset-pipeline.md, VFX): a puff of smoke simulated with Blender's
Mantaflow, rendered frame by frame with transparency and soft studio lighting, and packed into one 8 x 8 flipbook
for Unity (Assets/_Project/Art/FX/Textures/T_FX_Smoke.png). Frames read left to right, top to bottom.
Run in an empty Blender file: exec(open(r"C:\\Dev\\moon-hopper\\Tools\\blender\\fx\\smoke_flipbook.py").read())
Set STAGE = "setup" (scene), "bake" (simulation), "render" (frames and flipbook).
"""
import os, math
import bpy
import numpy as np

STAGE = globals().get("STAGE", "setup")
TILES, FRAME = 8, 256
FRAMES = TILES * TILES
CACHE = r"C:\Dev\moon-hopper\SourceArt\_local\fx_smoke_cache"
FRAMES_DIR = r"C:\Dev\moon-hopper\SourceArt\_local\fx_smoke_frames"
OUT = r"C:\Dev\moon-hopper\Assets\_Project\Art\FX\Textures\T_FX_Smoke.png"
START, STEP = 6, 2     # skip the first frames (the emitter shape), take every second frame


def _mesh_obj(name, kind, size, loc):
    import bmesh
    bm = bmesh.new()
    if kind == "cube":
        bmesh.ops.create_cube(bm, size=size)
    else:
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=size)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return ob


def setup():
    sc = bpy.context.scene
    for o in list(sc.objects):
        bpy.data.objects.remove(o)
    sc.frame_start, sc.frame_end = 1, START + FRAMES * STEP
    # Domain
    dom = _mesh_obj("SmokeDomain", "cube", 2.0, (0, 0, 0.85))
    m = dom.modifiers.new("Fluid", 'FLUID')
    m.fluid_type = 'DOMAIN'
    d = m.domain_settings
    d.domain_type = 'GAS'
    d.resolution_max = 112
    d.use_adaptive_domain = False
    d.vorticity = 0.8
    d.alpha = 0.6          # buoyancy from density (it rises slowly)
    d.beta = 0.25
    d.use_dissolve_smoke = True
    d.dissolve_speed = 70
    d.use_dissolve_smoke_log = True
    d.cache_directory = CACHE
    d.cache_type = 'ALL'
    d.cache_frame_start, d.cache_frame_end = 1, sc.frame_end
    d.use_noise = True
    d.noise_scale = 3
    d.noise_strength = 1.6
    d.noise_pos_scale = 1.5
    # Emitter: a short burst
    em = _mesh_obj("SmokeEmitter", "sphere", 0.14, (0, 0, 0.25))
    fm = em.modifiers.new("Fluid", 'FLUID')
    fm.fluid_type = 'FLOW'
    f = fm.flow_settings
    f.flow_type = 'SMOKE'
    f.flow_behavior = 'INFLOW'
    f.smoke_color = (0.9, 0.9, 0.9)
    f.density = 1.6
    f.use_initial_velocity = True
    f.velocity_normal = 2.6
    f.velocity_coord = (0.0, 0.0, 0.6)
    f.surface_distance = 1.0
    # Emit only for the first frames
    f.use_plane_init = False
    em.hide_render = True
    for frame, on in ((1, 1.6), (5, 1.6), (6, 0.0)):
        f.density = on
        f.keyframe_insert("density", frame=frame)
    # Turbulence breaks the puff into curls and wisps
    win = bpy.context.window_manager.windows[0]
    with bpy.context.temp_override(window=win, area=None, scene=bpy.context.scene):
        bpy.ops.object.effector_add(type='TURBULENCE', location=(0, 0, 0.8))
    tf = next(o for o in bpy.context.scene.objects if o.field and o.field.type == 'TURBULENCE')
    tf.name = "Turbulence"
    tf.field.strength = 2.5
    tf.field.size = 0.35
    tf.field.flow = 0.5
    # Look
    mat = bpy.data.materials.new("M_Smoke")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    vol.inputs["Color"].default_value = (0.85, 0.85, 0.85, 1)
    vol.inputs["Density"].default_value = 16.0
    vol.inputs["Anisotropy"].default_value = 0.25
    nt.links.new(vol.outputs[0], out.inputs["Volume"])
    dom.data.materials.append(mat)
    # Camera (orthographic, framing the domain) and lights
    cam_data = bpy.data.cameras.new("FlipCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 1.4
    cam = bpy.data.objects.new("FlipCam", cam_data)
    sc.collection.objects.link(cam)
    cam.location = (0, -6, 0.5)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    key = bpy.data.lights.new("Key", 'SUN'); key.energy = 3.5; key.angle = math.radians(20)
    k = bpy.data.objects.new("Key", key); sc.collection.objects.link(k)
    k.rotation_euler = (math.radians(35), math.radians(-30), math.radians(25))
    fill = bpy.data.lights.new("Fill", 'SUN'); fill.energy = 0.8
    fo = bpy.data.objects.new("Fill", fill); sc.collection.objects.link(fo)
    fo.rotation_euler = (math.radians(120), math.radians(40), 0)
    w = sc.world or bpy.data.worlds.new("World")
    sc.world = w
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.5, 0.5, 0.52, 1)
    bg.inputs["Strength"].default_value = 0.6
    # Render
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'GPU'
    sc.cycles.samples = 128
    sc.cycles.use_denoising = True
    sc.cycles.volume_step_rate = 1.0
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = FRAME
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    try:
        sc.view_settings.view_transform = 'Standard'
    except TypeError:
        pass
    print("setup done, frames", sc.frame_end)


def bake():
    dom = bpy.data.objects["SmokeDomain"]
    bpy.ops.object.select_all(action='DESELECT')
    dom.select_set(True)
    bpy.context.view_layer.objects.active = dom
    bpy.ops.fluid.free_all()
    bpy.ops.fluid.bake_all()
    print("bake done")


def render():
    sc = bpy.context.scene
    os.makedirs(FRAMES_DIR, exist_ok=True)
    paths = []
    for i in range(FRAMES):
        sc.frame_set(START + i * STEP)
        p = os.path.join(FRAMES_DIR, f"f{i:02d}.png")
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)
    size = TILES * FRAME
    atlas = np.zeros((size, size, 4), np.float32)
    for i, p in enumerate(paths):
        img = bpy.data.images.load(p)
        a = np.empty(FRAME * FRAME * 4, np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(FRAME, FRAME, 4)
        bpy.data.images.remove(img)
        # Unpremultiply so the color is the smoke's own shade (alpha carries the density). Where it's thin the
        # division is noise, so blend toward a neutral grey there (no bright rims, no noisy background)
        alpha = a[..., 3:4]
        rgb = np.clip(a[..., :3] / np.maximum(alpha, 1e-3), 0, 1)
        k = np.clip((alpha - 0.02) / 0.25, 0, 1)
        a[..., :3] = rgb * k + 0.62 * (1 - k)
        a[..., 3:4] = np.where(alpha < 0.01, 0.0, alpha)
        tx, ty = i % TILES, i // TILES
        y0 = size - (ty + 1) * FRAME          # Blender images start at the bottom row
        atlas[y0:y0 + FRAME, tx * FRAME:(tx + 1) * FRAME] = a
    out = bpy.data.images.new("T_FX_Smoke", size, size, alpha=True)
    out.pixels.foreach_set(atlas.ravel())
    out.filepath_raw = OUT
    out.file_format = 'PNG'
    out.save()
    print("flipbook written", OUT)


if STAGE == "setup":
    setup()
elif STAGE == "bake":
    bake()
elif STAGE == "render":
    render()
