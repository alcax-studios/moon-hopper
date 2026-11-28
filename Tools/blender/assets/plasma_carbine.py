"""
Golden asset 1: the plasma carbine (docs/art/golden-plasma-weapon.md), hand-modeled mid-poly.
Run in Blender: exec(open(r"C:\\Dev\\moon-hopper\\Tools\\blender\\assets\\plasma_carbine.py").read())
Rebuilds the PlasmaCarbine collection. Blender Z-up, muzzle toward -Y, meters.
Moving parts are separate objects: Cell, VentFront, VentRear, Trigger, Coil1-3.
"""
import sys, math
sys.path.append(r"C:\Dev\moon-hopper\Tools\blender")
import importlib, hardsurface as hs
importlib.reload(hs)

NAME = "PlasmaCarbine"
hs.clear(NAME)
hs.collection(NAME)

# ---------- Materials (flat values for now; the texturing pass layers wear on top) ----------
GUN = hs.material("M_PC_Gunmetal", hs.srgb("#4A5058"), metallic=1.0, roughness=0.38)
ALU = hs.material("M_PC_Aluminum", hs.srgb("#9AA0A6"), metallic=1.0, roughness=0.28)
POLY = hs.material("M_PC_Polymer", hs.srgb("#2B2D30"), roughness=0.62)
RUB = hs.material("M_PC_Rubber", hs.srgb("#1F2022"), roughness=0.9)
ORA = hs.material("M_PC_Orange", hs.srgb("#FF6A2B"), roughness=0.45)
NRG = hs.material("M_PC_Energy", hs.srgb("#6FE3FF"), roughness=0.3, emission=hs.srgb("#6FE3FF"), strength=18)
GLS = hs.material("M_PC_Glass", hs.srgb("#20262B"), roughness=0.05)
DARK = hs.material("M_PC_DarkMetal", hs.srgb("#24272B"), metallic=1.0, roughness=0.5)
# The plasma fluid behind glass: its own animated material in Unity (Alcax/Plasma Fluid), not baked into the atlas
FLUID = hs.material("M_PlasmaFluid", hs.srgb("#2A8FD0"), roughness=0.1, emission=hs.srgb("#6FE3FF"), strength=8)

parts = []
def P(ob, bevel=0.0012, seg=2, angle=35):
    hs.finish(ob, bevel, seg, angle)
    parts.append(ob)
    return ob

SH_Z = 0.058           # shroud axis height
SH_R = 0.034           # shroud radius (octagon)

# ---------- Receiver ----------
upper = hs.profile("Receiver", [(0.07, 0.03), (0.07, 0.074), (0.052, 0.09), (-0.245, 0.09), (-0.30, 0.076), (-0.30, 0.03)], 0.054, mat=GUN, chamfer=0.004)
# Panel grooves along both sides, an ejection-port style recess, and lightening slots
cuts = []
for sx in (-1, 1):
    cuts.append(hs.cutter(f"RecGrooveTop{sx}", (sx * 0.027 - 0.0012, -0.28, 0.0715), (sx * 0.027 + 0.0012, 0.05, 0.0735)))
    cuts.append(hs.cutter(f"RecGrooveLow{sx}", (sx * 0.027 - 0.0012, -0.28, 0.0395), (sx * 0.027 + 0.0012, 0.05, 0.0415)))
    for i in range(3):
        y = -0.285 + i * 0.014
        cuts.append(hs.cutter(f"RecSlot{sx}_{i}", (sx * 0.027 - 0.004, y - 0.004, 0.044), (sx * 0.027 + 0.004, y + 0.004, 0.068)))
hs.cut(upper, cuts)
P(upper, 0.0015)

lower = P(hs.profile("LowerReceiver", [(0.062, 0.031), (0.062, 0.008), (0.03, -0.004), (-0.135, -0.004), (-0.152, 0.012), (-0.152, 0.031)], 0.048, mat=GUN, chamfer=0.003), 0.0015)
grip = hs.profile("Grip", [(0.05, 0.0), (0.014, 0.0), (0.03, -0.06), (0.037, -0.106), (0.072, -0.112), (0.078, -0.1), (0.062, -0.03)], 0.032, mat=RUB, chamfer=0.006)
# Grip texture: horizontal grooves
gcuts = []
for i in range(7):
    z = -0.02 - i * 0.012
    gcuts.append(hs.cutter(f"GripGroove{i}", (-0.02, 0.012 + i * 0.0035, z - 0.0012), (0.02, 0.03 + i * 0.0035, z + 0.0012)))
hs.cut(grip, gcuts)
P(grip, 0.002, 3)
P(hs.box("GuardBottom", (-0.006, -0.048, -0.042), (0.006, 0.016, -0.034), mat=GUN), 0.002)
P(hs.box("GuardFront", (-0.006, -0.048, -0.042), (0.006, -0.04, 0.0), mat=GUN), 0.002)
trigger = P(hs.profile("Trigger", [(-0.008, -0.002), (-0.017, -0.028), (-0.012, -0.031), (-0.002, -0.006)], 0.006, mat=ALU), 0.0008)

# ---------- Energy cell (the magazine) ----------
cell = hs.profile("Cell", [(-0.058, 0.0), (-0.064, -0.098), (-0.136, -0.098), (-0.13, 0.0)], 0.04, mat=POLY, chamfer=0.003)
ccuts = [hs.cutter(f"CellWindow{sx}", (sx * 0.017 - 0.006, -0.115, -0.08), (sx * 0.017 + 0.006, -0.078, -0.02)) for sx in (-1, 1)]
hs.cut(cell, ccuts)
P(cell, 0.002, 3)
P(hs.box("CellCore", (-0.016, -0.118, -0.083), (0.016, -0.075, -0.017), mat=FLUID), 0.0)
P(hs.box("CellBand", (-0.0205, -0.137, -0.098), (0.0205, -0.059, -0.088), mat=ORA), 0.001)
P(hs.box("CellLatch", (-0.008, -0.058, -0.03), (0.008, -0.052, -0.004), mat=ALU), 0.0008)

# ---------- Barrel shroud, coils, emitter ----------
shroud = hs.cyl("Shroud", SH_R, -0.295, -0.53, z=SH_Z, segs=8, mat=GUN, spin=22.5)   # flat on top
scuts = []
for i, y in enumerate((-0.335, -0.375, -0.415)):
    for sx in (-1, 1):
        scuts.append(hs.cutter(f"CoilWindow{i}{sx}", (sx * 0.033 - 0.012, y - 0.012, SH_Z - 0.008), (sx * 0.033 + 0.012, y + 0.012, SH_Z + 0.008)))
for i in range(6):
    y = -0.305 - i * 0.0095
    scuts.append(hs.cutter(f"TopVent{i}", (-0.016, y - 0.003, SH_Z + 0.026), (0.016, y + 0.003, SH_Z + 0.05)))
scuts.append(hs.cutter("Bore", (-0.022, -0.56, SH_Z - 0.022), (0.022, -0.29, SH_Z + 0.022)))
# Glass strips along both upper flanks: a rounded trench in the 45-degree faces, with a fluid tube showing through
APO = SH_R * math.cos(math.radians(22.5))
for sx in (-1, 1):
    a = math.radians(45)
    scuts.append(hs.cyl(f"StripCut{sx}", 0.0056, -0.318, -0.442, x=sx * APO * math.cos(a), z=SH_Z + APO * math.sin(a), segs=20))
hs.cut(shroud, scuts)
P(shroud, 0.0018)
P(hs.cyl("InnerBarrel", 0.013, -0.30, -0.535, z=SH_Z, segs=24, mat=DARK), 0.0)
for sx in (-1, 1):
    a = math.radians(45)
    P(hs.cyl(f"Fluid{sx}", 0.0048, -0.322, -0.438, x=sx * 0.026 * math.cos(a), z=SH_Z + 0.026 * math.sin(a), segs=20, mat=FLUID), 0.0)
    for y in (-0.3205, -0.4395):   # metal end caps holding the tube
        P(hs.cyl(f"FluidCap{sx}_{y}", 0.0062, y - 0.0025, y + 0.0025, x=sx * 0.026 * math.cos(a), z=SH_Z + 0.026 * math.sin(a), segs=20, mat=ALU), 0.0004, 1)
coils = []
for i, y in enumerate((-0.335, -0.375, -0.415)):
    coils.append(P(hs.torus(f"Coil{i + 1}", 0.019, 0.0045, y, z=SH_Z, mat=NRG), 0.0))
# Cooling fins at the front of the shroud
for i in range(7):
    y = -0.452 - i * 0.0105
    fin = hs.cyl(f"Fin{i}", SH_R + 0.005, y - 0.0018, y + 0.0018, z=SH_Z, segs=8, mat=GUN, spin=22.5)
    hs.cut(fin, [hs.cutter(f"FinBore{i}", (-0.03, y - 0.01, SH_Z - 0.03), (0.03, y + 0.01, SH_Z + 0.03))])
    P(fin, 0.0006, 1)
emitter = hs.cyl("Emitter", 0.024, -0.53, -0.556, z=SH_Z, segs=32, mat=ALU, r1=0.021)
hs.cut(emitter, [hs.cutter("EmitterBore", (-0.0145, -0.57, SH_Z - 0.0145), (0.0145, -0.545, SH_Z + 0.0145))])
P(emitter, 0.001)
P(hs.cyl("EmitterGlow", 0.0135, -0.546, -0.548, z=SH_Z, segs=24, mat=NRG), 0.0)

# ---------- Heat vents (hinged flaps over the top vent grille) ----------
vent_r = P(hs.box("VentRear", (-0.019, -0.36, SH_Z + 0.0315), (0.019, -0.3, SH_Z + 0.0345), mat=ALU), 0.0008)
vent_f = P(hs.box("VentFront", (-0.019, -0.43, SH_Z + 0.0315), (0.019, -0.365, SH_Z + 0.0345), mat=ALU), 0.0008)

# ---------- Rail and optic ----------
rail = hs.box("Rail", (-0.011, -0.235, 0.09), (0.011, 0.035, 0.097), mat=GUN)
hs.cut(rail, [hs.cutter(f"RailSlot{i}", (-0.02, -0.228 + i * 0.011, 0.094), (0.02, -0.223 + i * 0.011, 0.1)) for i in range(24)])
P(rail, 0.0006, 1)
P(hs.box("OpticBase", (-0.014, -0.125, 0.097), (0.014, -0.035, 0.106), mat=GUN), 0.0012)
hood = hs.profile("OpticHood", [(-0.038, 0.106), (-0.038, 0.142), (-0.05, 0.15), (-0.118, 0.15), (-0.128, 0.142), (-0.128, 0.106)], 0.034, mat=POLY, chamfer=0.003)
hs.cut(hood, [hs.cutter("OpticTunnel", (-0.0135, -0.14, 0.111), (0.0135, -0.03, 0.144))])
P(hood, 0.0015, 2)
P(hs.box("OpticGlass", (-0.0136, -0.09, 0.111), (0.0136, -0.088, 0.144), mat=GLS), 0.0)
P(hs.box("OpticReticle", (-0.0012, -0.0885, 0.126), (0.0012, -0.0875, 0.13), mat=NRG), 0.0)

# ---------- Stock ----------
stock = hs.profile("Stock", [(0.07, 0.074), (0.29, 0.058), (0.302, 0.046), (0.302, -0.048), (0.288, -0.058), (0.25, -0.04), (0.07, 0.022)], 0.026, mat=POLY, chamfer=0.004)
hs.cut(stock, [hs.cutter("StockHole", (-0.02, 0.11, 0.032), (0.02, 0.255, 0.05)),
               hs.cutter("StockHole2", (-0.02, 0.2, -0.02), (0.02, 0.265, 0.025))])
P(stock, 0.0025, 2)
P(hs.box("ButtPad", (-0.018, 0.302, -0.062), (0.018, 0.322, 0.062), mat=RUB), 0.004, 3)

# ---------- Side armor plates, bolts, markings ----------
for sx in (-1, 1):
    x = sx * 0.0285
    P(hs.profile(f"SidePlate{sx}", [(-0.04, 0.046), (-0.04, 0.077), (-0.05, 0.083), (-0.2, 0.083), (-0.215, 0.07), (-0.215, 0.046)], 0.003, x=x, mat=GUN, chamfer=0.0012), 0.0005, 1)
    xo = sx * 0.030
    for y, z in ((-0.05, 0.052), (-0.2, 0.052), (-0.05, 0.076), (-0.195, 0.076)):
        P(hs.cylx(f"Bolt{sx}_{y}_{z}", 0.0026, xo, xo + sx * 0.0014, y=y, z=z, segs=6, mat=ALU), 0.0003, 1)
    for y, z in ((0.04, 0.06), (-0.28, 0.055), (0.05, 0.015)):
        P(hs.cylx(f"Screw{sx}_{y}_{z}", 0.0024, sx * 0.027, sx * 0.0282, y=y, z=z, segs=12, mat=ALU), 0.0003, 1)
P(hs.box("SerialPlate", (0.030, -0.17, 0.056), (0.0306, -0.1, 0.068), mat=ALU), 0.0003, 1)
P(hs.box("OrangeStripe", (-0.0272, -0.29, 0.082), (0.0272, -0.27, 0.0875), mat=ORA), 0.0004, 1)
P(hs.box("Selector", (0.024, 0.015, 0.045), (0.031, 0.035, 0.052), mat=ALU), 0.0006, 1)
P(hs.cylx("SelectorHub", 0.006, 0.027, 0.03, y=0.025, z=0.048, segs=16, mat=ALU), 0.0005, 1)
P(hs.box("ChargeHandle", (-0.034, 0.0, 0.07), (-0.027, 0.03, 0.08), mat=ALU), 0.0008, 1)
hg = hs.profile("HandguardLower", [(-0.3, 0.027), (-0.45, 0.027), (-0.455, 0.022), (-0.44, 0.014), (-0.31, 0.014)], 0.05, mat=POLY, chamfer=0.003)
hs.cut(hg, [hs.cutter(f"HGSlot{i}", (-0.03, -0.43 + i * 0.022, 0.016), (0.03, -0.42 + i * 0.022, 0.02)) for i in range(5)])
P(hg, 0.0015, 2)

# ---------- Shroud collar and the emitter cage ----------
P(hs.cyl("Collar", 0.039, -0.296, -0.306, z=SH_Z, segs=32, mat=ALU), 0.0008, 1)
ring = hs.cyl("CageRing", 0.027, -0.575, -0.582, z=SH_Z, segs=32, mat=GUN)
hs.cut(ring, [hs.cyl("CageRingBore", 0.019, -0.57, -0.59, z=SH_Z, segs=32)])
P(ring, 0.0008, 1)
for i in range(3):
    a = math.radians(90 + i * 120)
    cx, cz = 0.024 * math.cos(a), SH_Z + 0.024 * math.sin(a)
    prong = hs.box(f"Prong{i}", (cx - 0.003, -0.58, cz - 0.003), (cx + 0.003, -0.548, cz + 0.003), mat=GUN)
    P(prong, 0.0008, 1)

# ---------- Power conduits from the cell to the shroud ----------
for sx in (-1, 1):
    x = sx * 0.021
    pts = [(x, -0.128, 0.004), (sx * 0.027, -0.19, 0.012), (sx * 0.03, -0.26, 0.022), (sx * 0.024, -0.312, 0.03)]
    P(hs.tube(f"Conduit{sx}", pts, 0.0032, mat=RUB, segs=12), 0.0)
    P(hs.cyl(f"ConduitSleeve{sx}", 0.0048, -0.305, -0.318, x=sx * 0.024, z=0.03, segs=16, mat=ALU), 0.0005, 1)
    P(hs.cyl(f"ConduitPlug{sx}", 0.0048, -0.122, -0.134, x=x, z=0.004, segs=16, mat=ALU), 0.0005, 1)

n = hs.tris(parts)
print(f"{NAME}: {len(parts)} parts, {n} triangles")
