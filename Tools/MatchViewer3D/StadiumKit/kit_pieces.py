"""Stadium kit pieces. Blender metres, Z up, every piece faces the pitch along +Y.

Each builder takes `lod` (0 = full, 1 = reduced, 2 = distant) and returns a MeshBuilder; the runtime assembles
stands, roofs and terraces from these pieces (StadiumLayout), so dimensions here are real construction sizes.
"""

import math

from mathutils import Matrix, Vector

from kit_geo import MeshBuilder

DARK = "painted_steel:charcoal"
WHITE = "painted_steel:white"
CLUB = "painted_steel:club_primary"


def _sides(lod, near=10, mid=6, far=4):
    return (near, mid, far)[lod]


# ---- seat -------------------------------------------------------------------------------------------


def _seat_profile():
    # Side profile of the shell centreline (y, z): lip, pan, lumbar curve, backrest.
    pts = [(0.205, 0.405), (0.19, 0.425), (0.12, 0.432), (0.0, 0.425), (-0.12, 0.428), (-0.18, 0.445),
           (-0.205, 0.49), (-0.22, 0.56), (-0.24, 0.66), (-0.26, 0.76), (-0.265, 0.80)]
    return pts


def seat(lod=0, tint="seat_plastic:club"):
    """Moulded bucket seat on a riser bracket; 0.50 m pitch along the row, origin on the tread floor."""
    import bmesh
    import bpy

    b = MeshBuilder("seat")
    prof = _seat_profile()
    if lod == 2:
        b.quad([(-0.22, 0.2, 0.42), (0.22, 0.2, 0.42), (0.22, -0.18, 0.43), (-0.22, -0.18, 0.43)], tint, tile=0.5)
        b.quad([(-0.22, -0.2, 0.45), (0.22, -0.2, 0.45), (0.22, -0.265, 0.80), (-0.22, -0.265, 0.80)], tint, tile=0.5)
        return b
    nu = 8 if lod == 0 else 4
    if lod == 1:
        prof = prof[::2] + [prof[-1]]
    half_w = 0.22
    shell = bmesh.new()
    uv = shell.loops.layers.uv.new("UVMap")
    rows = []
    for (y, z) in prof:
        row = []
        for i in range(nu + 1):
            t = -1 + 2 * i / nu
            x = t * half_w * (0.94 if z > 0.5 else 1.0)
            # Raised pan sides and a backrest that wraps forward hold the sitter: the silhouette that reads
            # as a modern stadium seat rather than a bench.
            cup = 0.018 * t * t if z < 0.47 else 0.0
            wrap = 0.035 * t * t if z >= 0.47 else 0.0
            row.append(shell.verts.new((x, y + wrap, z + cup)))
        rows.append(row)
    for j in range(len(rows) - 1):
        for i in range(nu):
            f = shell.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
            f.smooth = True
            for loop in f.loops:
                loop[uv].uv = (loop.vert.co.x / 0.5, (loop.vert.co.z - loop.vert.co.y) / 0.5)
    me = bpy.data.meshes.new("seat_shell")
    shell.to_mesh(me)
    shell.free()
    me.materials.append(bpy.data.materials.get("kit." + tint) or _placeholder(tint))
    obj = bpy.data.objects.new("seat_shell", me)
    bpy.context.scene.collection.objects.link(obj)
    # A real shell has thickness and rounded rims; Solidify + one subdivision gives both without hand topology.
    sol = obj.modifiers.new("solid", "SOLIDIFY")
    sol.thickness = 0.007
    sol.offset = -1.0
    if lod == 0:
        sub = obj.modifiers.new("sub", "SUBSURF")
        sub.levels = 1
        sub.render_levels = 1
    b.merge_object(obj)
    bpy.data.objects.remove(obj)
    # Bracket: the shell sits on a steel arm bolted to the riser, so it never floats over the concrete.
    s = _sides(lod, 8, 5, 4)
    b.box((0, -0.16, 0.20), (0.06, 0.05, 0.40), DARK, tile=0.5)
    b.tube((0, -0.16, 0.40), (0, 0.10, 0.418), 0.018, DARK, sides=s)
    if lod == 0:
        b.box((0, -0.16, 0.01), (0.14, 0.10, 0.02), DARK, tile=0.5)
    return b


def _placeholder(mid):
    import bpy

    m = bpy.data.materials.new("kit." + mid)
    m["fe_material"] = mid
    return m


# ---- railings ---------------------------------------------------------------------------------------


def railing(lod=0, glass=False, length=2.0, height=1.1):
    """One bay of front/aisle railing; posts at x=0 only so bays chain without doubled posts."""
    b = MeshBuilder("railing_glass" if glass else "railing")
    s = _sides(lod, 10, 6, 4)
    b.tube((0, 0, 0), (0, 0, height), 0.024, "galvanized", sides=s)
    b.tube((0, 0, height), (length, 0, height), 0.024, "galvanized", sides=s, caps=False)
    if glass:
        b.box((length / 2, 0.0, height * 0.48), (length - 0.1, 0.012, height * 0.86), "glass", tile=2.0)
    else:
        b.tube((0, 0, height * 0.5), (length, 0, height * 0.5), 0.016, "galvanized", sides=s, caps=False)
        if lod == 0:
            b.box((0, 0, 0.005), (0.12, 0.12, 0.01), "galvanized", tile=0.5)
    return b


# ---- perimeter advertising --------------------------------------------------------------------------


def adboard(lod=0, length=6.0, height=0.9):
    """Pitch-side LED board leaning back 10 degrees; the face UV spans 0..1 for runtime content."""
    b = MeshBuilder("adboard")
    lean = Matrix.Rotation(math.radians(-10), 3, "X")
    depth = 0.14
    centre = Vector((length / 2, 0.0, height / 2 + 0.05))
    b.box(tuple(centre), (length, depth, height), DARK, tile=1.0, rot=lean)
    face = []
    for (x, z) in ((0.04, 0.06), (length - 0.04, 0.06), (length - 0.04, height - 0.04), (0.04, height - 0.04)):
        local = Vector((x - length / 2, depth / 2 + 0.002, z - height / 2))
        face.append(tuple(centre + lean @ local))
    b.quad(face, "led_screen", uv_rect=[(0, 0), (1, 0), (1, 1), (0, 1)])
    b.screens = [(length - 0.08, height - 0.1)]
    if lod < 2:
        for x in (0.4, length / 2, length - 0.4):
            b.tube((x, -0.45, 0.0), (x, -0.06, height * 0.7), 0.02, DARK, sides=_sides(lod, 6, 4, 4))
            b.box((x, -0.45, 0.01), (0.25, 0.25, 0.02), "rubber_floor", tile=0.5)
    return b


def led_ribbon(lod=0, length=10.0, height=0.8):
    """Tier-front LED fascia; sits on the balcony edge between bandejas."""
    b = MeshBuilder("led_ribbon")
    b.box((length / 2, -0.15, height / 2), (length, 0.3, height + 0.1), DARK, tile=1.0)
    b.quad([(0, 0.002, 0.05), (length, 0.002, 0.05), (length, 0.002, height), (0, 0.002, height)],
           "led_screen", uv_rect=[(0, 0), (1, 0), (1, 1), (0, 1)])
    b.screens = [(length, height - 0.05)]
    return b


# ---- lighting -------------------------------------------------------------------------------------


def _lamp(b, centre, frame, lod):
    """One sports floodlight: housing, cooling fins and an emissive lens facing the frame's +Y."""
    c = Vector(centre)
    b.box(tuple(c), (0.62, 0.32, 0.52), DARK, tile=0.5, rot=frame)
    lens = [Vector(p) for p in ((-0.27, 0.165, -0.22), (0.27, 0.165, -0.22), (0.27, 0.165, 0.22), (-0.27, 0.165, 0.22))]
    b.quad([tuple(c + frame @ p) for p in lens], "lamp_lens", uv_rect=[(0, 0), (1, 0), (1, 1), (0, 1)])
    if lod == 0:
        for k in range(5):
            x = -0.24 + k * 0.12
            b.box(tuple(c + frame @ Vector((x, -0.22, 0.0))), (0.02, 0.12, 0.44), DARK, tile=0.5, rot=frame)


def floodlight_tower(lod=0, height=42.0, cols=6, rows=4):
    """Corner lattice mast for clubs without a lit roof; the head tilts 28 degrees down towards the pitch."""
    b = MeshBuilder("floodlight_tower")
    base, top = 2.6, 1.1
    s = _sides(lod, 8, 5, 4)
    levels = int(height / (2.5 if lod == 0 else 5.0))

    def corner(k, z):
        w = base + (top - base) * z / height
        sx = (-1, 1, 1, -1)[k]
        sy = (-1, -1, 1, 1)[k]
        return Vector((sx * w / 2, sy * w / 2, z))

    for k in range(4):
        b.tube(tuple(corner(k, 0)), tuple(corner(k, height)), 0.09, "galvanized", sides=s)
    if lod < 2:
        for i in range(levels):
            z0 = height * i / levels
            z1 = height * (i + 1) / levels
            for k in range(4):
                n = (k + 1) % 4
                b.tube(tuple(corner(k, z1)), tuple(corner(n, z1)), 0.04, "galvanized", sides=max(4, s - 2), caps=False)
                a, c = (corner(k, z0), corner(n, z1)) if i % 2 == 0 else (corner(n, z0), corner(k, z1))
                b.tube(tuple(a), tuple(c), 0.035, "galvanized", sides=max(4, s - 2), caps=False)
    b.box((0, 0, 0.6), (base + 1.2, base + 1.2, 1.2), "concrete", tile=2.0)
    # Head frame: its own coordinate frame so lamps aim along the tilted +Y.
    tilt = Matrix.Rotation(math.radians(-28), 3, "X")
    head_c = Vector((0, 0.8, height + 2.2))
    w, h = cols * 0.8, rows * 0.7
    for (x0, z0, x1, z1) in ((-w / 2, -h / 2, w / 2, -h / 2), (-w / 2, h / 2, w / 2, h / 2),
                             (-w / 2, -h / 2, -w / 2, h / 2), (w / 2, -h / 2, w / 2, h / 2)):
        p0 = head_c + tilt @ Vector((x0, -0.3, z0))
        p1 = head_c + tilt @ Vector((x1, -0.3, z1))
        b.tube(tuple(p0), tuple(p1), 0.07, "galvanized", sides=s)
    for r in range(rows):
        for c in range(cols):
            local = Vector((-w / 2 + 0.4 + c * 0.8, 0.0, -h / 2 + 0.35 + r * 0.7))
            _lamp(b, tuple(head_c + tilt @ local), tilt, lod)
    if lod == 0:
        b.box(tuple(head_c + Vector((0, -1.4, -h / 2 - 0.6))), (w + 0.8, 1.6, 0.08), "galvanized", tile=1.0)
    return b


def roof_light_bar(lod=0, length=10.0, lamps=8):
    """Roof-edge lighting gantry for big stadiums: a catwalk with lamps aimed 35 degrees down."""
    b = MeshBuilder("roof_light_bar")
    s = _sides(lod, 8, 5, 4)
    b.tube((0, 0, 0), (length, 0, 0), 0.08, "galvanized", sides=s)
    b.tube((0, -0.8, 0), (length, -0.8, 0), 0.06, "galvanized", sides=s)
    b.box((length / 2, -0.4, -0.05), (length, 0.8, 0.04), "galvanized", tile=1.0)
    tilt = Matrix.Rotation(math.radians(-35), 3, "X")
    for i in range(lamps):
        _lamp(b, (length * (i + 0.5) / lamps, 0.35, -0.35), tilt, lod)
    return b


# ---- roof -----------------------------------------------------------------------------------------


def roof_truss(lod=0, span=30.0, root_depth=4.0, tip_depth=1.2):
    """Cantilever Warren truss from the back of the stand (y=0) towards the pitch (+Y); top chord at z=0."""
    b = MeshBuilder(f"roof_truss_{int(span)}")
    s = _sides(lod, 10, 6, 4)
    panels = max(4, int(span / (2.5 if lod == 0 else 5.0)))

    def top(t):
        return Vector((0, span * t, 0.02 * span * t))

    def bottom(t):
        d = root_depth + (tip_depth - root_depth) * t
        return Vector((0, span * t, -d))

    b.tube(tuple(top(0)), tuple(top(1)), 0.22, CLUB, sides=s)
    b.tube(tuple(bottom(0)), tuple(bottom(1)), 0.18, CLUB, sides=s)
    for i in range(panels + 1):
        t = i / panels
        b.tube(tuple(top(t)), tuple(bottom(t)), 0.08, WHITE, sides=max(4, s - 2), caps=False)
        if i < panels:
            t1 = (i + 1) / panels
            a, c = (top(t), bottom(t1)) if i % 2 == 0 else (bottom(t), top(t1))
            b.tube(tuple(a), tuple(c), 0.09, WHITE, sides=max(4, s - 2), caps=False)
    # Back-stay mast: the cantilever is held by a tie from a mast above the stand's back wall.
    mast_top = Vector((0, 0.0, 9.0))
    b.tube((0, 0, -root_depth), tuple(mast_top), 0.3, CLUB, sides=s)
    b.tube(tuple(mast_top), tuple(top(0.55)), 0.06, "galvanized", sides=max(4, s - 2), caps=False)
    b.tube(tuple(mast_top), (0, -8.0, -root_depth - 6.0), 0.06, "galvanized", sides=max(4, s - 2), caps=False)
    return b


# ---- pitch furniture ------------------------------------------------------------------------------


def goal(lod=0):
    """FIFA-size goal (7.32 x 2.44, 0.12 m round posts). Origin at the goal-line centre, net behind (-Y)."""
    b = MeshBuilder("goal")
    s = _sides(lod, 16, 10, 6)
    w, h, r = 7.32, 2.44, 0.06
    xs = (-w / 2 - r, w / 2 + r)
    for x in xs:
        b.tube((x, 0, 0), (x, 0, h + r), r, WHITE, sides=s)
    b.tube((xs[0], 0, h + r), (xs[1], 0, h + r), r, WHITE, sides=s)
    for x in xs:
        b.tube((x, 0, 0.02), (x, -2.0, 0.02), 0.025, WHITE, sides=max(4, s // 2))
        b.tube((x, -2.0, 0.02), (x, -0.9, h), 0.022, WHITE, sides=max(4, s // 2))
        b.tube((x, -0.9, h), (x, 0, h + r), 0.022, WHITE, sides=max(4, s // 2))
    b.tube((xs[0], -2.0, 0.02), (xs[1], -2.0, 0.02), 0.025, WHITE, sides=max(4, s // 2))
    b.tube((xs[0], -0.9, h), (xs[1], -0.9, h), 0.022, WHITE, sides=max(4, s // 2))
    return b


def goal_net(lod=0):
    """Net as a vertex grid the runtime cloth pass deforms; 0.24 m simulation spacing, 0.12 m drawn cells."""
    b = MeshBuilder("goal_net")
    w, h = 7.32, 2.44
    step = 0.24 if lod == 0 else 0.6
    nu = max(2, round(w / step))
    nv = max(2, round(h / step))
    nd = max(2, round(2.0 / step))
    # Back: from the bottom rear bar up to the upper rear bar.
    b.grid((-w / 2, -2.0, 0.02), (w, 0, 0), (0, 1.1, h - 0.02), nu, nv, "net", uv_scale=(w / 0.48, 2.7 / 0.48))
    b.grid((-w / 2, -0.9, h), (w, 0, 0), (0, 0.9, 0), nu, max(2, nd // 2), "net", uv_scale=(w / 0.48, 0.9 / 0.48))
    for x in (-w / 2, w / 2):
        b.grid((x, 0, 0.02), (0, -2.0, 0), (0, 0, h), nd, nv, "net", uv_scale=(2.0 / 0.48, h / 0.48))
    return b


def corner_flag(lod=0):
    b = MeshBuilder("corner_flag")
    b.tube((0, 0, 0), (0, 0, 1.55), 0.016, "painted_steel:white", sides=_sides(lod, 8, 5, 4))
    b.grid((0, 0, 1.55), (0.0, 0.45, 0.0), (0, 0, -0.35), 6 if lod == 0 else 2, 4 if lod == 0 else 1, "flag_cloth")
    return b


def dugout(lod=0, length=7.0, seats=12):
    """Team shelter: curved polycarbonate canopy on a steel frame over a row of padded seats."""
    b = MeshBuilder("dugout")
    s = _sides(lod, 8, 5, 4)
    b.box((length / 2, -0.9, 0.05), (length + 0.2, 2.0, 0.1), "concrete", tile=2.0)
    segs = 10 if lod == 0 else 4
    arcs = []
    for i in range(segs + 1):
        a = math.pi * 0.5 * i / segs
        arcs.append((math.cos(a) * 1.8 - 1.8 + 0.1, math.sin(a) * 2.1 + 0.15))
    for i in range(segs):
        (y0, z0), (y1, z1) = arcs[i], arcs[i + 1]
        b.quad([(0, y0, z0), (length, y0, z0), (length, y1, z1), (0, y1, z1)], "polycarbonate", double=True, tile=1.0)
    for x in (0.0, length / 2, length):
        for i in range(segs):
            (y0, z0), (y1, z1) = arcs[i], arcs[i + 1]
            b.tube((x, y0, z0), (x, y1, z1), 0.035, DARK, sides=s, caps=False)
    for x in (0.0, length):
        b.quad([(x, 0.1, 0.1), (x, -1.7, 0.1), (x, -1.7, 2.25), (x, 0.1, 0.4)], "polycarbonate", double=True, tile=1.0)
    for i in range(seats):
        x = length * (i + 0.5) / seats
        b.box((x, -1.2, 0.45), (0.46, 0.46, 0.1), "seat_plastic:charcoal", tile=0.5)
        b.box((x, -1.43, 0.8), (0.46, 0.08, 0.62), "seat_plastic:charcoal", tile=0.5)
        b.box((x, -1.25, 0.22), (0.06, 0.3, 0.38), DARK, tile=0.5)
    return b


def tunnel(lod=0, length=8.0):
    """Telescopic players' tunnel: steel hoops under a polycarbonate skin, open towards the pitch (+Y)."""
    b = MeshBuilder("tunnel")
    hoops = 6 if lod == 0 else 3
    segs = 12 if lod == 0 else 5
    width, height = 3.0, 2.6
    arc = [(math.cos(math.pi * i / segs) * width / 2, height - 0.6 + math.sin(math.pi * i / segs) * 0.6) for i in range(segs + 1)]
    profile = [(width / 2, 0.0)] + arc + [(-width / 2, 0.0)]
    for k in range(len(profile) - 1):
        (x0, z0), (x1, z1) = profile[k], profile[k + 1]
        b.quad([(x0, 0, z0), (x0, length, z0), (x1, length, z1), (x1, 0, z1)], "polycarbonate", double=True, tile=1.0)
    for i in range(hoops + 1):
        y = length * i / hoops
        for k in range(len(profile) - 1):
            (x0, z0), (x1, z1) = profile[k], profile[k + 1]
            b.tube((x0, y, z0), (x1, y, z1), 0.04, CLUB, sides=_sides(lod, 6, 4, 4), caps=False)
    b.box((0, length / 2, 0.01), (width, length, 0.02), "rubber_floor", tile=1.0)
    return b


def camera_gantry(lod=0):
    b = MeshBuilder("camera_gantry")
    b.box((0, 0, 0), (4.0, 2.2, 0.12), "galvanized", tile=1.0)
    for x in (-2.0, 2.0):
        b.tube((x, -1.1, 0), (x, -1.1, -3.0), 0.06, "galvanized", sides=_sides(lod, 6, 4, 4))
    for x in (-2.0, 0.0):
        r = railing(lod, length=2.0)
        _graft(b, r, Matrix.Translation((x, 1.1, 0.06)))
    for x in (-1.0, 0.8):
        b.box((x, 0.3, 0.85), (0.45, 0.7, 0.4), DARK, tile=0.5)
        b.tube((x, 0.3, 0.06), (x, 0.3, 0.65), 0.04, DARK, sides=_sides(lod, 6, 4, 4))
    return b


def scoreboard(lod=0, width=16.0, height=7.0):
    b = MeshBuilder("scoreboard")
    b.box((0, -0.6, height / 2), (width + 0.6, 1.2, height + 0.6), DARK, tile=1.0)
    b.quad([(-width / 2, 0.003, 0.3), (width / 2, 0.003, 0.3), (width / 2, 0.003, height + 0.3 - 0.6), (-width / 2, 0.003, height + 0.3 - 0.6)],
           "led_screen", uv_rect=[(0, 0), (1, 0), (1, 1), (0, 1)])
    b.screens = [(width, height - 0.6)]
    for x in (-width / 3, width / 3):
        b.tube((x, -0.6, 0), (x, -0.6, -6.0), 0.25, DARK, sides=_sides(lod, 10, 6, 4))
    return b


def facade(lod=0, kind="fins", width=10.0, height=5.0):
    """Exterior skin module stacked by the layout. `fins` reads as a modern arena, `panels` as a club ground."""
    b = MeshBuilder(f"facade_{kind}")
    s = _sides(lod, 8, 5, 4)
    b.box((0, -0.1, height / 2), (0.5, 0.5, height), "concrete", tile=2.0)
    b.box((width / 2, 0.15, height - 0.15), (width, 0.3, 0.3), "concrete", tile=2.0)
    if kind == "fins":
        b.box((width / 2, 0.3, height / 2), (width, 0.05, height), "glass", tile=2.0)
        n = int(width / 0.5) if lod == 0 else int(width / 1.0)
        for i in range(n):
            x = width * (i + 0.5) / n
            twist = Matrix.Rotation(math.radians(15 * math.sin(i * 0.45)), 3, "Z")
            b.box((x, -0.45, height / 2), (0.08, 0.6, height), WHITE, tile=1.0, rot=twist)
    else:
        for i in range(4):
            x = width * (i + 0.5) / 4
            b.box((x, 0.0, height / 2), (width / 4 - 0.03, 0.22, height - 0.04), "concrete", tile=2.0)
        if lod == 0:
            b.box((width / 2, -0.12, 2.3), (1.6, 0.05, 1.2), "glass", tile=1.0)
    return b


def vomitory(lod=0, width=2.4, depth=4.0, height=2.3):
    """Portal cut into a stand: side walls, lintel, dark passage and handrails; origin at the opening floor."""
    b = MeshBuilder("vomitory")
    for x in (-width / 2 - 0.15, width / 2 + 0.15):
        b.box((x, -depth / 2, height / 2 + 0.25), (0.3, depth, height + 0.5), "concrete", tile=2.0)
    b.box((0, -0.15, height + 0.25), (width + 0.6, 0.3, 0.5), "concrete", tile=2.0)
    b.box((0, -depth / 2, 0.02), (width, depth, 0.04), "rubber_floor", tile=1.0)
    b.box((0, -depth, height / 2), (width, 0.1, height), "painted_steel:charcoal", tile=1.0)
    b.box((0, -depth / 2, height + 0.02), (width, depth, 0.04), "painted_steel:charcoal", tile=1.0)
    if lod < 2:
        for x in (-width / 2 - 0.15, width / 2 + 0.15):
            b.tube((x, 0, height + 0.5), (x, 0, height + 1.5), 0.024, "galvanized", sides=_sides(lod, 8, 5, 4))
            b.tube((x, 0, height + 1.5), (x, -depth, height + 1.5), 0.024, "galvanized", sides=_sides(lod, 8, 5, 4))
    return b


def _graft(dst, src, matrix):
    """Copy another builder's geometry into `dst` under `matrix`, keeping material ids and UVs."""
    vmap = {}
    for v in src.bm.verts:
        vmap[v] = dst.bm.verts.new(matrix @ v.co)
    for f in src.bm.faces:
        nf = dst.bm.faces.new([vmap[v] for v in f.verts])
        nf.material_index = dst._mat_index(src.materials[f.material_index])
        nf.smooth = f.smooth
        for ln, lo in zip(nf.loops, f.loops):
            ln[dst.uv].uv = lo[src.uv].uv
    src.bm.free()


PIECES = {
    "seat": seat,
    "railing": railing,
    "railing_glass": lambda lod=0: railing(lod, glass=True),
    "adboard": adboard,
    "led_ribbon": led_ribbon,
    "floodlight_tower": floodlight_tower,
    "roof_light_bar": roof_light_bar,
    "roof_truss_20": lambda lod=0: roof_truss(lod, 20.0, 3.0, 1.0),
    "roof_truss_30": lambda lod=0: roof_truss(lod, 30.0, 4.0, 1.2),
    "roof_truss_40": lambda lod=0: roof_truss(lod, 40.0, 5.0, 1.4),
    "goal": goal,
    "goal_net": goal_net,
    "corner_flag": corner_flag,
    "dugout": dugout,
    "tunnel": tunnel,
    "camera_gantry": camera_gantry,
    "scoreboard": scoreboard,
    "facade_fins": lambda lod=0: facade(lod, "fins"),
    "facade_panels": lambda lod=0: facade(lod, "panels"),
    "vomitory": vomitory,
}
