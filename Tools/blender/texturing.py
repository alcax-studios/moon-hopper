"""
Texturing pipeline for mid-poly assets (docs/art/asset-pipeline.md):
1. Layered procedural materials (wear on edges, dust in cavities, roughness breakup, scratches), Cycles-only masks.
2. Game meshes: modifiers applied, parts joined into groups (the body, plus one object per moving part).
3. One UV atlas across all of them.
4. Bake to one texture set and write Unity URP Lit maps:
   T_<Asset>_BaseColor (sRGB), _Normal (OpenGL, as Unity), _MetallicSmoothness (R metal, A smoothness),
   _Occlusion, _Emission.
Use: import texturing as tx
"""
import os
import bpy
import numpy as np

# ---------- Materials ----------

def _n(nt, t, loc, **kw):
    n = nt.nodes.new(t)
    n.location = loc
    for k, v in kw.items():
        setattr(n, k, v)
    return n


def wear_material(name, base, rough, metallic=0.0, edge=None, dust=(0.42, 0.33, 0.24), dust_amt=0.6,
                  breakup=0.08, stipple=0.0, scratches=0.0, emission=None, strength=0.0, edge_width=0.0018):
    """A layered material. base: linear RGB; edge: (color, metallic, roughness) revealed on convex edges, or None.
    dust: desert dust color settling in cavities. stipple: fine bump (polymer texture). scratches: 0..1."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = _n(nt, "ShaderNodeOutputMaterial", (1400, 0))
    p = _n(nt, "ShaderNodeBsdfPrincipled", (1100, 0))
    nt.links.new(p.outputs[0], out.inputs["Surface"])
    tc = _n(nt, "ShaderNodeTexCoord", (-1200, 0))
    # Masks: convex edges (AO looking inward) and cavities (AO)
    ao_edge = _n(nt, "ShaderNodeAmbientOcclusion", (-900, 300), inside=True, samples=12)
    ao_edge.inputs["Distance"].default_value = edge_width
    ao_cav = _n(nt, "ShaderNodeAmbientOcclusion", (-900, -50), samples=12)
    ao_cav.inputs["Distance"].default_value = 0.012
    noise = _n(nt, "ShaderNodeTexNoise", (-900, -400))
    noise.inputs["Scale"].default_value = 60.0
    noise.inputs["Detail"].default_value = 8.0
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    big = _n(nt, "ShaderNodeTexNoise", (-900, -650))
    big.inputs["Scale"].default_value = 9.0
    big.inputs["Detail"].default_value = 4.0
    nt.links.new(tc.outputs["Object"], big.inputs["Vector"])
    # edge = 1 - ao_inside, sharpened and broken up by noise
    inv = _n(nt, "ShaderNodeMath", (-650, 300), operation='SUBTRACT')
    inv.inputs[0].default_value = 1.0
    nt.links.new(ao_edge.outputs["AO"], inv.inputs[1])
    emix = _n(nt, "ShaderNodeMath", (-450, 300), operation='MULTIPLY')
    nt.links.new(inv.outputs[0], emix.inputs[0])
    nt.links.new(noise.outputs["Fac"], emix.inputs[1])
    eramp = _n(nt, "ShaderNodeValToRGB", (-250, 300))
    eramp.color_ramp.elements[0].position = 0.18
    eramp.color_ramp.elements[1].position = 0.32
    nt.links.new(emix.outputs[0], eramp.inputs[0])
    # cavity = 1 - ao, with large-scale variation
    cinv = _n(nt, "ShaderNodeMath", (-650, -50), operation='SUBTRACT')
    cinv.inputs[0].default_value = 1.0
    nt.links.new(ao_cav.outputs["AO"], cinv.inputs[1])
    cmul = _n(nt, "ShaderNodeMath", (-450, -50), operation='MULTIPLY')
    nt.links.new(cinv.outputs[0], cmul.inputs[0])
    nt.links.new(big.outputs["Fac"], cmul.inputs[1])
    cramp = _n(nt, "ShaderNodeValToRGB", (-250, -50))
    cramp.color_ramp.elements[0].position = 0.12
    cramp.color_ramp.elements[1].position = 0.5
    nt.links.new(cmul.outputs[0], cramp.inputs[0])

    # Base color: base -> edge color on edges -> dust in cavities
    col = _n(nt, "ShaderNodeMix", (100, 400), data_type='RGBA')
    col.inputs[6].default_value = (*base, 1)
    col.inputs[7].default_value = (*(edge[0] if edge else base), 1)
    nt.links.new(eramp.outputs["Color"], col.inputs[0])
    col2 = _n(nt, "ShaderNodeMix", (350, 400), data_type='RGBA')
    nt.links.new(col.outputs[2], col2.inputs[6])
    col2.inputs[7].default_value = (*dust, 1)
    dfac = _n(nt, "ShaderNodeMath", (100, 150), operation='MULTIPLY')
    nt.links.new(cramp.outputs["Color"], dfac.inputs[0])
    dfac.inputs[1].default_value = dust_amt
    nt.links.new(dfac.outputs[0], col2.inputs[0])
    nt.links.new(col2.outputs[2], p.inputs["Base Color"])

    # Metallic: base -> edge metal on edges, dust is not metal
    met = _n(nt, "ShaderNodeMix", (350, 100), data_type='FLOAT')
    met.inputs[2].default_value = metallic
    met.inputs[3].default_value = edge[1] if edge else metallic
    nt.links.new(eramp.outputs["Color"], met.inputs[0])
    met2 = _n(nt, "ShaderNodeMath", (600, 100), operation='MULTIPLY')
    nt.links.new(met.outputs[0], met2.inputs[0])
    dinv = _n(nt, "ShaderNodeMath", (350, -150), operation='SUBTRACT')
    dinv.inputs[0].default_value = 1.0
    nt.links.new(dfac.outputs[0], dinv.inputs[1])
    nt.links.new(dinv.outputs[0], met2.inputs[1])
    nt.links.new(met2.outputs[0], p.inputs["Metallic"])

    # Roughness: base +- breakup noise -> edge roughness -> dusty is rough; scratches polish lines
    rb = _n(nt, "ShaderNodeMapRange", (100, -300))
    rb.inputs["To Min"].default_value = rough - breakup
    rb.inputs["To Max"].default_value = rough + breakup
    nt.links.new(big.outputs["Fac"], rb.inputs["Value"])
    rmix = _n(nt, "ShaderNodeMix", (350, -350), data_type='FLOAT')
    nt.links.new(eramp.outputs["Color"], rmix.inputs[0])
    nt.links.new(rb.outputs[0], rmix.inputs[2])
    rmix.inputs[3].default_value = edge[2] if edge else rough
    rmix2 = _n(nt, "ShaderNodeMix", (600, -350), data_type='FLOAT')
    nt.links.new(dfac.outputs[0], rmix2.inputs[0])
    nt.links.new(rmix.outputs[0], rmix2.inputs[2])
    rmix2.inputs[3].default_value = 0.85
    rough_out = rmix2.outputs[0]
    bump_h = None
    if scratches > 0:
        # Long thin scratches: noise stretched along the weapon (Y), thresholded
        mp = _n(nt, "ShaderNodeMapping", (-900, -900))
        mp.inputs["Scale"].default_value = (220.0, 6.0, 220.0)
        nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
        sn = _n(nt, "ShaderNodeTexNoise", (-650, -900))
        sn.inputs["Scale"].default_value = 1.0
        sn.inputs["Detail"].default_value = 2.0
        nt.links.new(mp.outputs[0], sn.inputs["Vector"])
        sr = _n(nt, "ShaderNodeValToRGB", (-400, -900))
        sr.color_ramp.elements[0].position = 0.68
        sr.color_ramp.elements[1].position = 0.7
        nt.links.new(sn.outputs["Fac"], sr.inputs[0])
        sm = _n(nt, "ShaderNodeMath", (-150, -900), operation='MULTIPLY')
        nt.links.new(sr.outputs["Color"], sm.inputs[0])
        sm.inputs[1].default_value = scratches
        rs = _n(nt, "ShaderNodeMix", (850, -350), data_type='FLOAT')
        nt.links.new(sm.outputs[0], rs.inputs[0])
        nt.links.new(rough_out, rs.inputs[2])
        rs.inputs[3].default_value = 0.18
        rough_out = rs.outputs[0]
        bump_h = sm.outputs[0]
    nt.links.new(rough_out, p.inputs["Roughness"])

    # Fine surface bump (polymer stipple, scratches)
    if stipple > 0 or bump_h is not None:
        bump = _n(nt, "ShaderNodeBump", (850, -650))
        bump.inputs["Strength"].default_value = 0.25
        bump.inputs["Distance"].default_value = 0.0004
        h = None
        if stipple > 0:
            st = _n(nt, "ShaderNodeTexNoise", (-650, -1150))
            st.inputs["Scale"].default_value = 900.0
            st.inputs["Detail"].default_value = 1.0
            nt.links.new(tc.outputs["Object"], st.inputs["Vector"])
            sts = _n(nt, "ShaderNodeMath", (-400, -1150), operation='MULTIPLY')
            nt.links.new(st.outputs["Fac"], sts.inputs[0])
            sts.inputs[1].default_value = stipple
            h = sts.outputs[0]
        if bump_h is not None:
            if h is None:
                h = bump_h
            else:
                add = _n(nt, "ShaderNodeMath", (-150, -1150), operation='SUBTRACT')
                nt.links.new(h, add.inputs[0])
                nt.links.new(bump_h, add.inputs[1])
                h = add.outputs[0]
        nt.links.new(h, bump.inputs["Height"])
        nt.links.new(bump.outputs[0], p.inputs["Normal"])

    if emission:
        p.inputs["Emission Color"].default_value = (*emission, 1)
        p.inputs["Emission Strength"].default_value = strength
    m["mh_metal_socket"] = met2.name
    m["mh_color_socket"] = col2.name
    return m


# ---------- Game meshes ----------

def bake_meshes(source_coll, groups, out_coll_name):
    """Apply modifiers to copies of the parts and join them per group.
    groups: {"Body": None (everything not listed elsewhere), "Cell": ["Cell", "CellCore", ...], ...}.
    Returns {group: object}."""
    deps = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.collections.get(out_coll_name)
    if out:
        for o in list(out.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        out = bpy.data.collections.new(out_coll_name)
        bpy.context.scene.collection.children.link(out)
    listed = {n for v in groups.values() if v for n in v}
    members = {g: [] for g in groups}
    for o in source_coll.all_objects:
        if o.type != 'MESH' or o.get("cutter") or o.hide_render:
            continue
        g = next((k for k, v in groups.items() if v and o.name in v), None)
        if g is None:
            g = next(k for k, v in groups.items() if v is None)
        me = bpy.data.meshes.new_from_object(o.evaluated_get(deps), preserve_all_data_layers=True, depsgraph=deps)
        me.transform(o.matrix_world)
        # Faces left without a material (boolean cutters have none) take the part's own
        main = next((m for m in me.materials if m is not None), None)
        for i, m in enumerate(me.materials):
            if m is None and main is not None:
                me.materials[i] = main
        c = bpy.data.objects.new(o.name + "_g", me)
        out.objects.link(c)
        members[g].append(c)
    result = {}
    for g, objs in members.items():
        if not objs:
            continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        if len(objs) > 1:
            bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        j.name = g
        j.data.name = g
        # Triangulate before UVs and baking, so the engine shades the same triangles the bake used
        tri = j.modifiers.new("Triangulate", 'TRIANGULATE')
        tri.keep_custom_normals = True
        tri.quad_method = 'BEAUTY'
        tri.ngon_method = 'BEAUTY'
        bpy.ops.object.select_all(action='DESELECT')
        j.select_set(True)
        bpy.context.view_layer.objects.active = j
        bpy.ops.object.modifier_apply(modifier=tri.name)
        result[g] = j
    return result


def uv_atlas(objs, margin=0.003):
    """One UV map across all objects: smart projection, then islands packed together by area."""
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[-1])
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=margin, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(margin=margin, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')


# ---------- Baking ----------

def _image(name, size, data):
    img = bpy.data.images.get(name)
    if img and (img.size[0] != size):
        bpy.data.images.remove(img)
        img = None
    if img is None:
        img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color' if data else 'sRGB'
    return img


def _materials(objs):
    ms = []
    for o in objs:
        for s in o.material_slots:
            if s.material and s.material not in ms:
                ms.append(s.material)
    return ms


def _set_target(mats, img):
    for m in mats:
        nt = m.node_tree
        n = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
        n.name = "BakeTarget"
        n.image = img
        n.location = (1400, 400)
        for x in nt.nodes:
            x.select = False
        n.select = True
        nt.nodes.active = n


def _bake(objs, kind, pass_filter=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    kw = dict(type=kind, margin=16, use_clear=True)
    if pass_filter:
        kw["pass_filter"] = pass_filter
    bpy.ops.object.bake(**kw)


def bake_set(objs, asset, size, outdir, samples=32):
    """Bake all maps for the objects into one atlas and write Unity textures. Returns the file paths."""
    sc = bpy.context.scene
    prev_engine = sc.render.engine
    sc.render.engine = 'CYCLES'
    try:
        sc.cycles.device = 'GPU'
    except Exception:
        pass
    sc.cycles.samples = samples
    sc.render.bake.use_selected_to_active = False
    mats = _materials(objs)
    os.makedirs(outdir, exist_ok=True)
    imgs = {}

    def run(key, kind, data, pass_filter=None):
        img = _image(f"T_{asset}_{key}_bake", size, data)
        _set_target(mats, img)
        _bake(objs, kind, pass_filter)
        imgs[key] = img

    # Base color and metallic: route each material's value into an emission shader, bake, restore.
    # (The diffuse pass can't be used for base color: metals have no diffuse.)
    def routed(key, prop, out_index, principled_input, data):
        saved = []
        for m in mats:
            nt = m.node_tree
            out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            prev = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
            em = nt.nodes.new("ShaderNodeEmission")
            src_node = nt.nodes.get(m.get(prop, ""))
            if src_node is not None:
                nt.links.new(src_node.outputs[out_index], em.inputs["Color"])
            else:
                p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
                v = p.inputs[principled_input].default_value
                em.inputs["Color"].default_value = tuple(v) if hasattr(v, "__len__") else (v, v, v, 1)
            nt.links.new(em.outputs[0], out.inputs["Surface"])
            saved.append((m, prev, em))
        run(key, 'EMIT', data)
        for m, prev, em in saved:
            nt = m.node_tree
            out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
            if prev is not None:
                nt.links.new(prev, out.inputs["Surface"])
            nt.nodes.remove(em)

    routed("BaseColor", "mh_color_socket", 2, "Base Color", False)
    routed("Metallic", "mh_metal_socket", 0, "Metallic", True)
    run("Roughness", 'ROUGHNESS', True)
    run("Normal", 'NORMAL', True)
    run("Occlusion", 'AO', True)
    # Emission as color only (strength 1); the intensity is set on the Unity material
    strengths = []
    for m in mats:
        p = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if p is not None:
            strengths.append((p, p.inputs["Emission Strength"].default_value))
            p.inputs["Emission Strength"].default_value = min(1.0, p.inputs["Emission Strength"].default_value)
    run("Emission", 'EMIT', False)
    for p, v in strengths:
        p.inputs["Emission Strength"].default_value = v
    sc.render.engine = prev_engine

    def px(img):
        a = np.empty(size * size * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(size, size, 4)

    def save(name, arr, data):
        img = _image(name, size, data)
        img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
        path = os.path.join(outdir, name + ".png")
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        return path

    paths = {}
    bc = px(imgs["BaseColor"]); bc[..., 3] = 1
    paths["BaseColor"] = save(f"T_{asset}_BaseColor", bc, False)
    nm = px(imgs["Normal"]); nm[..., 3] = 1
    paths["Normal"] = save(f"T_{asset}_Normal", nm, True)
    ms = np.zeros((size, size, 4), np.float32)
    ms[..., 0] = px(imgs["Metallic"])[..., 0]
    ms[..., 3] = 1.0 - px(imgs["Roughness"])[..., 0]
    paths["MetallicSmoothness"] = save(f"T_{asset}_MetallicSmoothness", ms, True)
    ao = px(imgs["Occlusion"]); ao[..., 3] = 1
    paths["Occlusion"] = save(f"T_{asset}_Occlusion", ao, True)
    em = px(imgs["Emission"]); em[..., 3] = 1
    paths["Emission"] = save(f"T_{asset}_Emission", em, False)
    return paths


def preview_material(name, asset, paths):
    """A material that shows the baked maps on the game meshes (to check the bake in Blender)."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = _n(nt, "ShaderNodeOutputMaterial", (900, 0))
    p = _n(nt, "ShaderNodeBsdfPrincipled", (600, 0))
    nt.links.new(p.outputs[0], out.inputs["Surface"])

    def tex(key, loc, data):
        img = bpy.data.images.load(paths[key], check_existing=True)
        img.reload()
        img.colorspace_settings.name = 'Non-Color' if data else 'sRGB'
        return _n(nt, "ShaderNodeTexImage", loc, image=img)

    bc = tex("BaseColor", (0, 300), False)
    nt.links.new(bc.outputs[0], p.inputs["Base Color"])
    msn = tex("MetallicSmoothness", (0, 0), True)
    sep = _n(nt, "ShaderNodeSeparateColor", (250, 50))
    nt.links.new(msn.outputs[0], sep.inputs[0])
    nt.links.new(sep.outputs[0], p.inputs["Metallic"])
    inv = _n(nt, "ShaderNodeMath", (300, -100), operation='SUBTRACT')
    inv.inputs[0].default_value = 1.0
    nt.links.new(msn.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    nrm = tex("Normal", (0, -300), True)
    nmap = _n(nt, "ShaderNodeNormalMap", (300, -300))
    nt.links.new(nrm.outputs[0], nmap.inputs["Color"])
    nt.links.new(nmap.outputs[0], p.inputs["Normal"])
    emt = tex("Emission", (0, -600), False)
    nt.links.new(emt.outputs[0], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 18.0
    return m
