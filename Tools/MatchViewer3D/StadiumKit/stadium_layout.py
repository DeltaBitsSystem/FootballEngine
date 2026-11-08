"""Reference stadium layout: club facts -> deterministic stadium description.

Pure Python (no bpy) so it is the executable specification of the F# port (`StadiumLayout.fromClub`).
`golden()` writes test vectors the port must reproduce. Coordinates: metres, pitch centre at the origin,
X along the touchlines (length), Y across (width), Z up. Every placed kit piece faces the pitch along its +Y.
"""

import json
import math
import sys
from dataclasses import asdict, dataclass, field

PITCH_LENGTH = 105.0
PITCH_WIDTH = 68.0
SEAT_PITCH = 0.5  # metres of row length per seat
AISLE_EVERY = 14  # seats between aisles
AISLE_WIDTH = 1.2
TRUSS_SPACING = 10.0
BENCH_INNER = 4.0  # metres from the halfway line to each bench's inner end
BENCH_LENGTH = 7.0  # matches the kit dugout
TECH_ZONE_HALF = BENCH_INNER + BENCH_LENGTH + 2.0
ROOF_CLEARANCE = 6.5
TOWER_HEIGHT = 42.0  # authored mast height of the kit's floodlight_tower  # truss root height above the top row: headroom for the back rows and the mast base


class Rng:
    """SplitMix64: tiny, portable and bit-identical in F#; never Python's `random`."""

    def __init__(self, seed):
        self.state = seed & 0xFFFFFFFFFFFFFFFF

    def next_u64(self):
        self.state = (self.state + 0x9E3779B97F4A7C15) & 0xFFFFFFFFFFFFFFFF
        z = self.state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
        return z ^ (z >> 31)

    def unit(self):
        return (self.next_u64() >> 11) / float(1 << 53)

    def chance(self, p):
        return self.unit() < p


@dataclass
class Tier:
    rows: int
    row_depth: float
    riser: float
    front_offset: float  # horizontal distance from the stand's front line to this tier's first row
    base_height: float  # height of the first row's tread
    vip_band: bool  # glazed hospitality band below this tier's front

    def back_offset(self):
        return self.front_offset + self.rows * self.row_depth

    def top_height(self):
        return self.base_height + self.rows * self.riser


@dataclass
class Stand:
    side: str  # north | south | east | west | corner_ne ...
    kind: str  # straight | corner
    # Straight: the front line runs from `start` to `end` (2D); corner: arc around `centre` from a0 to a1.
    start: tuple = (0.0, 0.0)
    end: tuple = (0.0, 0.0)
    centre: tuple = (0.0, 0.0)
    radius: float = 0.0
    a0: float = 0.0
    a1: float = 0.0
    tiers: list = field(default_factory=list)
    roof: str = "none"  # none | truss_20 | truss_30 | truss_40
    seated: bool = True  # False: standing terrace (lower-league realism)
    main: bool = False


@dataclass
class Layout:
    schema: int
    prestige: float
    capacity: int
    shape: str
    lighting: str  # towers | roof
    facade: str
    seat_colours: str  # solid | tiered | blocks
    stands: list
    pieces: list  # {"piece", "x", "y", "z", "yaw", "tint"}


def prestige_of(reputation, league_level):
    # Reputation dominates; the division adds a floor so a top-flight minnow still looks top-flight.
    division = {0: 0.18, 1: 0.07}.get(league_level, 0.0)
    return max(0.0, min(1.0, reputation / 9999.0 * 0.82 + division))


def _tiers_for(p, rng, main, side_scale):
    count = 1 if p < 0.3 else (2 if p < 0.68 else 3)
    if main and p >= 0.22:
        count = min(3, count + (1 if p < 0.68 else 0))
    tiers = []
    front = 0.0
    height = 0.6
    for t in range(count):
        # Lower bowls rake at ~20 degrees for sightlines near the pitch; upper tiers at ~33 so the back still sees.
        rake = math.radians(20.0 + 7.0 * t + 3.0 * rng.unit())
        rows = int(round((8 + 30 * p) * side_scale * (1.0 if t == 0 else 0.85) + rng.unit() * 3))
        rows = max(6, rows)
        depth = 0.8
        tiers.append(Tier(rows, depth, round(depth * math.tan(rake), 3), round(front, 3), round(height, 3),
                          vip_band=(t > 0 and p > 0.55)))
        last = tiers[-1]
        # Upper tiers overhang the one below: it shelters it and puts fans closer to the pitch.
        overhang = 0.0 if t == count - 1 else min(last.rows * depth * 0.45, 3.0 + 6.0 * p)
        front = last.back_offset() - overhang
        height = last.top_height() + (4.2 if p > 0.55 else 3.2)
    return tiers


def generate(reputation, league_level, seed):
    rng = Rng(seed * 0x9E37 + 0x51A7)
    p = prestige_of(reputation, league_level)
    side_runoff = 6.5 + 2.5 * rng.unit()
    end_runoff = 8.0 + 3.0 * rng.unit()
    hx = PITCH_LENGTH / 2 + end_runoff
    hy = PITCH_WIDTH / 2 + side_runoff
    shape = "bowl" if p >= 0.7 else ("closed" if p >= 0.4 else "open")
    lighting = "roof" if p >= 0.6 else "towers"  # provisional; settled once roofs are known (below)
    facade = "fins" if p >= 0.6 else "panels"
    seat_colours = ("solid", "tiered", "blocks")[int(rng.unit() * 3)] if p >= 0.3 else "solid"
    stands = []
    # Sides: south is the main stand (cameras, dugouts, tunnel); the TV camera looks at the north stand.
    sides = [("south", (-hx + 4, -hy), (hx - 4, -hy), True, 1.0),
             ("north", (hx - 4, hy), (-hx + 4, hy), False, 1.0),
             ("west", (-hx, hy - 6), (-hx, -hy + 6), False, 0.8),
             ("east", (hx, -hy + 6), (hx, hy - 6), False, 0.8)]
    corner_r = 14.0
    bowl_tiers = _tiers_for(p, rng, False, 1.0) if shape == "bowl" else None
    for name, a, b, main, scale in sides:
        if shape == "bowl":
            # The bowl's straight runs stop where the corner arcs begin.
            if name in ("south", "north"):
                sx = hx - corner_r
                a = (-sx if name == "south" else sx, a[1])
                b = (sx if name == "south" else -sx, b[1])
            else:
                sy = hy - corner_r
                a = (a[0], sy if name == "west" else -sy)
                b = (b[0], -sy if name == "west" else sy)
        elif name in ("west", "east") and shape == "open":
            # Open grounds: shorter end stands with gaps at the corners.
            a = (a[0], a[1] * 0.75)
            b = (b[0], b[1] * 0.75)
        st = Stand(name, "straight", start=a, end=b, main=main)
        # A bowl is one continuous structure: every side shares the tier section so the corners close cleanly.
        st.tiers = bowl_tiers if shape == "bowl" else _tiers_for(p, rng, main, scale)
        st.seated = not (p < 0.3 and not main and rng.chance(0.45))
        stands.append(st)
    if shape == "bowl":
        for name, cx, cy, a0 in (("corner_se", 1, -1, -90.0), ("corner_ne", 1, 1, 0.0),
                                 ("corner_nw", -1, 1, 90.0), ("corner_sw", -1, -1, 180.0)):
            c = (cx * (hx - corner_r), cy * (hy - corner_r))
            st = Stand(name, "corner", centre=c, radius=corner_r, a0=a0, a1=a0 + 90.0)
            st.tiers = bowl_tiers
            stands.append(st)
    elif shape == "closed":
        # Closed grounds infill two diagonal corners with a single low tier; the seed picks the pair.
        pair = (("corner_ne", 1, 1, 0.0), ("corner_sw", -1, -1, 180.0)) if rng.chance(0.5) else \
               (("corner_se", 1, -1, -90.0), ("corner_nw", -1, 1, 90.0))
        for name, cx, cy, a0 in pair:
            c = (cx * (hx - 6), cy * (hy - 6))
            st = Stand(name, "corner", centre=c, radius=6.0, a0=a0, a1=a0 + 90.0)
            st.tiers = _tiers_for(min(p, 0.29), rng, False, 0.7)
            stands.append(st)
    for st in stands:
        if p < 0.2:
            st.roof = "truss_20" if st.main else "none"
        elif p < 0.45:
            st.roof = "truss_20" if (st.main or st.side == "north") else ("truss_20" if rng.chance(0.5) else "none")
        else:
            depth = st.tiers[-1].back_offset() - st.tiers[0].front_offset
            st.roof = "truss_40" if depth > 32 else ("truss_30" if depth > 22 else "truss_20")
    # Deep roofs reach almost to the touchline: no corner mast can light past them, so such grounds light from
    # the roof edge. Towers remain for open grounds with short (20 m) roofs.
    if any(st.roof in ("truss_30", "truss_40") for st in stands):
        lighting = "roof"
    capacity = 0
    for st in stands:
        length = _length(st)
        for t in st.tiers:
            # Two-metre aisles per AISLE_EVERY seats; a standing terrace holds ~1.8 people per seat footprint.
            per_row = int(length / SEAT_PITCH) * AISLE_EVERY // (AISLE_EVERY + 2)
            capacity += int(per_row * t.rows * (1.0 if st.seated else 1.8))
    pieces = _pieces(p, rng, stands, hx, hy, lighting, facade)
    return Layout(1, round(p, 4), capacity, shape, lighting, facade, seat_colours,
                  [asdict(s) for s in stands], pieces)


def front_frames(st, step):
    """Sample a stand's front line: list of ((x, y), (nx, ny)) with n pointing away from the pitch.

    Straight runs go counter-clockwise around the pitch so the pitch is on their left; corners are arcs.
    """
    out = []
    if st["kind"] == "straight":
        (ax, ay), (bx, by) = st["start"], st["end"]
        length = math.hypot(bx - ax, by - ay)
        dx, dy = (bx - ax) / length, (by - ay) / length
        n = max(1, int(length / step))
        for i in range(n + 1):
            t = i / n
            out.append(((ax + (bx - ax) * t, ay + (by - ay) * t), (dy, -dx)))
    else:
        cx, cy = st["centre"]
        sweep = math.radians(st["a1"] - st["a0"])
        n = max(2, int(sweep * st["radius"] / step) + 4)
        for i in range(n + 1):
            a = math.radians(st["a0"]) + sweep * i / n
            out.append(((cx + math.cos(a) * st["radius"], cy + math.sin(a) * st["radius"]), (math.cos(a), math.sin(a))))
    return out


def tier_profile(t, upper):
    """Section of one tier as (offset outward, height) points: front wall/soffit, steps, back wall."""
    fo, h, d, r, rows = t["front_offset"], t["base_height"], t["row_depth"], t["riser"], t["rows"]
    pts = [(fo + rows * d, h - 1.4), (fo, h - 1.4)] if upper else [(fo, 0.0)]
    pts.append((fo, h + 0.25))
    for i in range(rows):
        z = h + i * r
        pts += [(fo + i * d, z), (fo + (i + 1) * d, z), (fo + (i + 1) * d, z + r)]
    pts.append((fo + rows * d + 0.6, h + rows * r + 1.1))
    pts.append((fo + rows * d + 0.6, h - 1.4 if upper else 0.0))
    return [(round(o, 4), round(z, 4)) for o, z in pts]


def vomitory_block(block):
    # Every fourth aisle block hosts a vomitory in the middle rows of the tier.
    return block % 4 == 2


def seat_positions(st, t):
    """Seats of one tier as (x, y, z, yaw_degrees, row). Aisles every AISLE_EVERY seats, vomitory gaps."""
    if not st["seated"]:
        return []
    fr = front_frames(st, SEAT_PITCH)
    out = []
    mid = t["rows"] // 2
    for row in range(t["rows"]):
        o = t["front_offset"] + row * t["row_depth"] + 0.45
        z = t["base_height"] + row * t["riser"]
        for i in range(len(fr) - 1):
            k = i % (AISLE_EVERY + 2)
            if k >= AISLE_EVERY:
                continue
            block = i // (AISLE_EVERY + 2)
            if t["rows"] >= 12 and vomitory_block(block) and mid - 2 <= row <= mid + 1:
                continue
            (px, py), (nx, ny) = fr[i]
            (qx, qy), _ = fr[i + 1]
            cx, cy = (px + qx) / 2, (py + qy) / 2
            yaw = math.degrees(math.atan2(-ny, -nx)) - 90.0
            out.append((round(cx + nx * o, 4), round(cy + ny * o, 4), round(z, 4), round(yaw, 3), row))
    return out


ROOF_SPAN = {"truss_20": 20.0, "truss_30": 30.0, "truss_40": 40.0}


def _roof_edge(st):
    """Truss roots along the back of the top tier: list of ((x, y, z) root, (ix, iy) inward unit vector)."""
    top = st["tiers"][-1]
    back = top["front_offset"] + top["rows"] * top["row_depth"] + 0.6
    z = top["base_height"] + top["rows"] * top["riser"] + ROOF_CLEARANCE
    return [((x + nx * back, y + ny * back, z), (-nx, -ny)) for (x, y), (nx, ny) in front_frames(st, TRUSS_SPACING)]


def roof_sheets(st):
    """Roof cladding quads between consecutive trusses, following the top chord's 2% rise towards the pitch."""
    if st["roof"] == "none":
        return []
    span = ROOF_SPAN[st["roof"]]

    def ring(root, inward):
        (rx, ry, rz), (ix, iy) = root, inward
        return [(rx, ry, rz + 0.25), (rx + ix * span, ry + iy * span, rz + 0.02 * span + 0.25)]

    edge = _roof_edge(st)
    quads = []
    for (r0, i0), (r1, i1) in zip(edge, edge[1:]):
        a, b = ring(r0, i0), ring(r1, i1)
        quads.append([tuple(round(c, 4) for c in v) for v in (a[0], b[0], b[1], a[1])])
    return quads


def lamp_positions(pieces, kit_emitters):
    """World positions of every floodlight lamp: kit emitters through each placement's scale, yaw and offset."""
    out = []
    for pc in pieces:
        for e in kit_emitters.get(pc["piece"], []):
            ex, ey, ez = (e["position"][k] * pc["scale"][k] for k in range(3))
            a = math.radians(pc["yaw"])
            out.append((round(pc["x"] + ex * math.cos(a) - ey * math.sin(a), 4),
                        round(pc["y"] + ex * math.sin(a) + ey * math.cos(a), 4),
                        round(pc["z"] + ez, 4)))
    return out


def aiming_targets(lamps):
    """Floodlight aiming: each luminaire points at its own spot of a grid over the playing area.

    `lamps` are (x, y, z) world positions. Lamps are ordered by bearing around the pitch centre and assigned grid
    points with a co-prime stride, so neighbouring lamps light different, mostly far-side spots: uniform light on
    the pitch and on players' faces, never a beam into the stands. Returns (x, y, z) targets at pitch level.
    """
    cols, rows = 13, 7
    grid = []
    for j in range(rows):
        for i in range(cols):
            grid.append((-PITCH_LENGTH / 2 + 4 + (PITCH_LENGTH - 8) * i / (cols - 1),
                         -PITCH_WIDTH / 2 + 4 + (PITCH_WIDTH - 8) * j / (rows - 1)))
    order = sorted(range(len(lamps)), key=lambda k: (math.atan2(lamps[k][1], lamps[k][0]), k))
    stride = 11  # co-prime with the 91 grid points
    targets = [None] * len(lamps)
    for rank, k in enumerate(order):
        lx, ly, _ = lamps[k]
        # Prefer the far half: walk the stride sequence until a point on the opposite side of the centre line.
        for step in range(len(grid)):
            gx, gy = grid[(rank * stride + step) % len(grid)]
            if gx * lx + gy * ly <= 0 or step == len(grid) - 1:
                targets[k] = (round(gx, 3), round(gy, 3), 0.0)
                break
    return targets


def beam_half_angles(lamps, targets):
    """Per-lamp beam half-angle in degrees: a ~24 m spot at the target, clamped to real luminaire optics (6-20).

    Far targets need narrow beams; a wide beam from a corner mast would spill its lower edge onto the stands.
    """
    out = []
    for (lx, ly, lz), (tx, ty, tz) in zip(lamps, targets):
        d = math.dist((lx, ly, lz), (tx, ty, tz))
        out.append(round(min(20.0, max(6.0, math.degrees(math.atan(12.0 / max(d, 1.0))))), 3))
    return out


def _length(st):
    if st.kind == "straight":
        return math.dist(st.start, st.end)
    return math.radians(st.a1 - st.a0) * st.radius


def _yaw_towards_pitch(st):
    if st.kind == "corner":
        mid = math.radians((st.a0 + st.a1) / 2)
        return math.degrees(math.atan2(-math.sin(mid), -math.cos(mid))) - 90.0
    dx, dy = st.end[0] - st.start[0], st.end[1] - st.start[1]
    # Straight runs go counter-clockwise around the pitch, so the pitch is on their left.
    return math.degrees(math.atan2(dy, dx))


def _pieces(p, rng, stands, hx, hy, lighting, facade):
    out = []

    def put(piece, x, y, z, yaw, tint="none", scale=(1.0, 1.0, 1.0)):
        out.append({"piece": piece, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yaw": round(yaw, 2),
                    "tint": tint, "scale": [round(c, 4) for c in scale]})

    # The goal's net hangs behind its local -Y; yaw +90 sends it to +X, so each net points away from the pitch.
    for sx in (-1, 1):
        put("goal", sx * PITCH_LENGTH / 2, 0.0, 0.0, 90.0 * sx)
        put("goal_net", sx * PITCH_LENGTH / 2, 0.0, 0.0, 90.0 * sx)
        for sy in (-1, 1):
            put("corner_flag", sx * PITCH_LENGTH / 2, sy * PITCH_WIDTH / 2, 0.0, 0.0, "clubPrimary")
    # Perimeter boards: 4 m off the touchlines and 5 m behind the goal lines. On the main side they stop at the
    # technical zone, where the two benches and the tunnel meet the pitch.
    board_y = PITCH_WIDTH / 2 + 4.0
    for side in (-1, 1):
        x = -PITCH_LENGTH / 2
        while x + 6.0 <= PITCH_LENGTH / 2 + 0.01:
            if not (side < 0 and x + 6.0 > -TECH_ZONE_HALF and x < TECH_ZONE_HALF):
                yaw = 0.0 if side < 0 else 180.0
                px = x if side < 0 else x + 6.0
                put("adboard", px, side * board_y, 0.0, yaw)
            x += 6.0
    board_x = PITCH_LENGTH / 2 + 5.0
    for side in (-1, 1):
        y = -PITCH_WIDTH / 2 + 4.0
        while y + 6.0 <= PITCH_WIDTH / 2 - 4.0 + 0.01:
            yaw = -90.0 if side < 0 else 90.0
            py = y + 6.0 if side < 0 else y
            put("adboard", side * board_x, py, 0.0, yaw)
            y += 6.0
    for st in stands:
        d = asdict(st)
        for ti, t in enumerate(d["tiers"]):
            fo = t["front_offset"]
            # Tier fronts: glass balustrades in premium upper tiers, tubular rails elsewhere.
            rail = "railing_glass" if (ti > 0 and p > 0.55) else "railing"
            for (x, y), (nx, ny) in front_frames(d, 2.0)[:-1]:
                put(rail, x + nx * (fo - 0.05), y + ny * (fo - 0.05), t["base_height"] + 0.25,
                    math.degrees(math.atan2(-ny, -nx)) - 90.0)
            if ti > 0 and p > 0.5:
                for (x, y), (nx, ny) in front_frames(d, 10.0)[:-1]:
                    put("led_ribbon", x + nx * (fo - 0.02), y + ny * (fo - 0.02), t["base_height"] - 1.35,
                        math.degrees(math.atan2(-ny, -nx)) - 90.0)
            if st.seated and t["rows"] >= 12:
                fr = front_frames(d, SEAT_PITCH)
                gap_row = t["rows"] // 2 - 2
                block = 0
                while True:
                    i0 = block * (AISLE_EVERY + 2) + AISLE_EVERY // 2
                    if i0 + 1 >= len(fr):
                        break
                    if vomitory_block(block):
                        (x, y), (nx, ny) = fr[i0]
                        o = fo + gap_row * t["row_depth"]
                        put("vomitory", x + nx * o, y + ny * o, t["base_height"] + gap_row * t["riser"],
                            math.degrees(math.atan2(-ny, -nx)) - 90.0)
                    block += 1
        # Roof: cantilever trusses every TRUSS_SPACING from the back of the top tier; lit roofs carry a lamp
        # gantry along the front edge between consecutive trusses.
        if st.roof != "none":
            span = ROOF_SPAN[st.roof]
            edge = _roof_edge(d)
            for (rx, ry, rz), (ix, iy) in edge:
                put(f"roof_{st.roof}", rx, ry, rz, math.degrees(math.atan2(iy, ix)) - 90.0, "clubPrimary")
            if lighting == "roof":
                for ((rx, ry, rz), (ix, iy)), ((qx, qy, _), _) in zip(edge, edge[1:]):
                    length = math.hypot(qx - rx, qy - ry)
                    put("roof_light_bar", rx + ix * (span - 0.5), ry + iy * (span - 0.5), rz + 0.02 * span - 0.6,
                        math.degrees(math.atan2(iy, ix)) - 90.0, "none", (length / 10.0, 1.0, 1.0))
        # Exterior skin: 10 m facade modules stacked in 5 m levels behind the top tier, facing outwards.
        top = d["tiers"][-1]
        back = top["front_offset"] + top["rows"] * top["row_depth"] + 1.4
        height = top["base_height"] + top["rows"] * top["riser"] + 1.0
        fr10 = front_frames(d, 10.0)
        for ((px, py), (pnx, pny)), ((qx, qy), (qnx, qny)) in zip(fr10, fr10[1:]):
            # Measure on the facade line itself: around a bowl corner the facade, `back` metres outside the
            # front line, runs a much longer chord than the front; modules sized on the front leave gaps.
            ax, ay = px + pnx * back, py + pny * back
            bx, by = qx + qnx * back, qy + qny * back
            length = math.hypot(bx - ax, by - ay)
            # Facing outwards: the chord runs counter-clockwise, so its outward normal is (dy, -dx).
            ox, oy = (by - ay) / length, -(bx - ax) / length
            for level in range(max(1, int(height / 5.0))):
                put(f"facade_{facade}", bx, by, level * 5.0,
                    math.degrees(math.atan2(oy, ox)) - 90.0, "none", (length / 10.0, 1.0, 1.0))
    main = next(s for s in stands if s.main)
    my = main.start[1]
    # One bench per club, symmetric about the halfway line, fronts 3 m off the touchline (technical areas).
    bench_front = -PITCH_WIDTH / 2 - 3.0
    put("dugout", -BENCH_INNER - BENCH_LENGTH, bench_front, 0.0, 0.0)
    put("dugout", BENCH_INNER, bench_front, 0.0, 0.0)
    # The players' tunnel runs from the main stand's front to just behind the bench line, never onto the
    # pitch; its 8 m authored length is scaled to the actual gap.
    tunnel_end = bench_front - 2.0
    put("tunnel", 0.0, my, 0.0, 0.0, "clubPrimary", (1.0, max(0.3, (tunnel_end - my) / 8.0), 1.0))
    gx, gy, gz = broadcast_gantry(main, p)
    put("camera_gantry", gx, gy, gz, 0.0)
    if lighting == "towers":
        # Masts must clear the highest roof with margin, or their beams land on the cladding instead of the pitch.
        highest = max(st.tiers[-1].top_height() + (ROOF_CLEARANCE + 6.0 if st.roof != "none" else 2.0) for st in stands)
        z_scale = max(1.0, (highest + 10.0) / TOWER_HEIGHT)
        for sx in (-1, 1):
            for sy in (-1, 1):
                yaw = math.degrees(math.atan2(-sy, -sx)) - 90.0
                put("floodlight_tower", sx * (hx + 8.0), sy * (hy + 8.0), 0.0, yaw, "none", (1.0, 1.0, z_scale))
    if p >= 0.4:
        ends = [s for s in stands if s.side in ("west", "east")]
        for st in ends[: 1 if p < 0.7 else 2]:
            yaw = _yaw_towards_pitch(st)
            x = st.start[0] + (-1 if st.side == "west" else 1) * (st.tiers[-1].back_offset() + 2.0)
            put("scoreboard", x, 0.0, st.tiers[-1].top_height() + 4.0, yaw)
    return out


def broadcast_gantry(main, p):
    """Main TV position on the halfway line: hung in front of the main stand at 14-22 m (higher in big grounds).

    The broadcast camera is mounted on this gantry, so framing follows each stadium's real geometry.
    """
    target = 14.0 + 8.0 * p
    my = main.start[1]
    for t in main.tiers:
        if t.base_height <= target <= t.top_height():
            # The gantry projects 1.5 m in front of the tier's front so balustrades never cross the lens.
            return 0.0, my - (t.front_offset - 1.5), target
    top = main.tiers[-1]
    if main.roof != "none":
        # Low roofed stand: hang under the roof's front edge so the trusses stay behind the lens.
        span = {"truss_20": 20.0, "truss_30": 30.0, "truss_40": 40.0}[main.roof]
        o = max(main.tiers[0].front_offset + 1.0, top.back_offset() + 0.6 - span + 2.0)
        return 0.0, my - o, top.top_height() + ROOF_CLEARANCE - 4.5
    return 0.0, my - top.back_offset(), top.top_height() + 2.5


GOLDEN = [
    ("giant", 9600, 0, 7),
    ("big", 7800, 0, 11),
    ("mid_top", 5200, 0, 19),
    ("small_top", 3300, 0, 23),
    ("second_div", 2800, 1, 31),
    ("third_div", 1200, 2, 41),
]


def golden(path):
    """Test vectors for the F# port: the full layout plus derived geometry digests per stand and tier."""
    data = {}
    for name, rep, lvl, seed in GOLDEN:
        lay = asdict(generate(rep, lvl, seed))
        geometry = []
        for st in lay["stands"]:
            for ti, t in enumerate(st["tiers"]):
                seats = seat_positions(st, t)
                geometry.append({
                    "stand": st["side"], "tier": ti,
                    "profile": tier_profile(t, ti > 0),
                    "seatCount": len(seats),
                    "firstSeats": seats[:3],
                    "seatSum": [round(sum(s[k] for s in seats), 2) for k in range(4)],
                })
        lamps = lamp_positions(lay["pieces"], KIT_EMITTERS())
        targets = aiming_targets(lamps)
        beams = beam_half_angles(lamps, targets)
        sheets = [q for st in lay["stands"] for q in roof_sheets(st)]
        lighting = {"lampCount": len(lamps), "firstLamps": lamps[:3], "firstTargets": targets[:3],
                    "lampSum": [round(sum(l[k] for l in lamps), 2) for k in range(3)],
                    "targetSum": [round(sum(t[k] for t in targets), 2) for k in range(3)],
                    "beamHalfAngleSum": round(sum(beams), 2), "firstBeams": beams[:3],
                    "roofSheetCount": len(sheets),
                    "roofSheetSum": round(sum(c for q in sheets for v in q for c in v), 2)}
        data[name] = lay | {"input": {"reputation": rep, "leagueLevel": lvl, "seed": seed}, "geometry": geometry,
                            "lighting": lighting}
    json.dump(data, open(path, "w"), indent=1)


def KIT_EMITTERS():
    import os
    kit = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "FootballEngine.Client", "Assets",
                       "MatchViewer", "StadiumKit", "kit.json")
    return {p["id"]: p["emitters"] for p in json.load(open(kit))["pieces"] if p["emitters"]}


def asdict_layout(layout):
    return asdict(layout)


if __name__ == "__main__":
    if len(sys.argv) > 1:
        golden(sys.argv[1])
    for name, rep, lvl, seed in GOLDEN:
        lay = generate(rep, lvl, seed)
        print(name, lay.prestige, lay.shape, lay.lighting, lay.capacity,
              [(s["side"], len(s["tiers"]), s["roof"]) for s in lay.stands][:4])
