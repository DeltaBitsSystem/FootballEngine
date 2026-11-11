"""Retarget the pinned CMU trials onto the 60-joint runtime rig and write the binary motion database.

The retarget is rotation-only: every driven rig bone takes the CMU bone's world rotation since rest, applied after
the minimal rotation that lines the rig's rest bone up with CMU's. Segment lengths therefore stay the rig's own, and
only the root translation is scaled, by the ratio of thigh-plus-shin lengths, so stride length matches the body that
walks it. (Rest hip heights are not comparable: CMU's rest legs are splayed 20°, which made a 10% overscale and
visible skating.)
Foot contacts are labelled on the retargeted rig, not on CMU markers, because that is the body the runtime plants.

Usage: python3 -I build_motion_db.py <player.glb> <out motion-db.bin> <out report.json>
"""

import hashlib
import json
import math
import pathlib
import re
import struct
import sys

import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent
SOURCE = ROOT / "source"
OUTPUT_HZ = 60
# CMU ASF lengths are in units where 0.45 corresponds to one inch.
ASF_TO_METRES = (1.0 / 0.45) * 0.0254

# Rig joint ← CMU bone. Helpers, fingers and supports keep their bind pose; the runtime correctives drive supports.
MAPPING = {
    "pelvis": "root",
    "spine": "lowerback",
    "chest": "thorax",
    "neck": "lowerneck",
    "head": "head",
    "clavicle.L": "lclavicle",
    "upper_arm.L": "lhumerus",
    "forearm.L": "lradius",
    "hand.L": "lhand",
    "clavicle.R": "rclavicle",
    "upper_arm.R": "rhumerus",
    "forearm.R": "rradius",
    "hand.R": "rhand",
    "thigh.L": "lfemur",
    "shin.L": "ltibia",
    "foot.L": "lfoot",
    "toe.L": "ltoes",
    "thigh.R": "rfemur",
    "shin.R": "rtibia",
    "foot.R": "rfoot",
    "toe.R": "rtoes",
}
# Rig child whose rest position gives each driven joint's bone direction.
DIRECTION_CHILD = {
    "spine": "chest",
    "chest": "neck",
    "neck": "head",
    "clavicle.L": "upper_arm.L",
    "upper_arm.L": "forearm.L",
    "forearm.L": "hand.L",
    "hand.L": "middle_01.L",
    "clavicle.R": "upper_arm.R",
    "upper_arm.R": "forearm.R",
    "forearm.R": "hand.R",
    "hand.R": "middle_01.R",
    "thigh.L": "shin.L",
    "shin.L": "foot.L",
    "foot.L": "toe.L",
    "thigh.R": "shin.R",
    "shin.R": "foot.R",
    "foot.R": "toe.R",
}
# Root trajectory smoothing: motion matching reads the path the body travels, not the pelvis sway within a stride.
ROOT_SMOOTHING_SECONDS = 0.1
# The pelvis heading swings a few degrees once per stride (~0.7 s at running pace); a 0.35 s Gaussian leaves under
# 1 % of that swing in the root yaw, while the pelvis local takes the rest so every joint keeps its world pose.
# Position keeps the shorter window: these captures start and stop within a second, and a long window would lag
# the root behind the accelerating body.
HEADING_SMOOTHING_SECONDS = 0.35
# A joint stepping further than this between 60 Hz frames (1800 deg/s, beyond a kicking knee) and returning within
# SPIKE_FRAMES is a source flip (Euler wrap or marker swap in the capture), not motion; it is re-interpolated.
SPIKE_STEP_DEGREES = 30.0
SPIKE_FRAMES = 6
# The joint is back on its path once it is within the reach of its pre-spike speed (at least 10 deg/frame, at most
# the spike step), with 2x slack; a flat 30 deg/frame reach accepted a slow joint's frame still inside the flip.
SPIKE_MIN_REACH_DEGREES = 10.0
SPIKE_RETURN_SLACK = 2.0
# A foot is planted when its lowest point is within 5 cm of its standing height and slower than walking pace
# (0.5 m/s); three frames of hysteresis stop single-frame flicker at heel strike.
# Arm markers jitter at the frame rate, retargeted elbows stop dead at full extension and repaired shoulder flips
# leave kinks; all reach the screen as fingertip flicks. A 5-tap binomial removes the 30 Hz jitter and keeps ~87 %
# of 5 Hz motion (arm swing is 1.5-3 Hz). Legs are left raw: their contacts and kicks are the motion that matters.
SMOOTHED_JOINTS = ("upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R")
SMOOTHING_TAPS = np.array([1.0, 4.0, 6.0, 4.0, 1.0]) / 16.0
CONTACT_HEIGHT = 0.05
CONTACT_SPEED = 0.5
CONTACT_MIN_FRAMES = 3


# ---------------------------------------------------------------- maths


def euler_xyz(rx, ry, rz):
    """Static-axis XYZ Euler, as Acclaim defines bone axes and AMC rotations (Rz · Ry · Rx)."""
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    x = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    y = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    z = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return z @ y @ x


def quat_to_matrix(q):
    x, y, z, w = q
    return np.array(
        [
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
        ]
    )


def matrix_to_quat(m):
    trace = m[0, 0] + m[1, 1] + m[2, 2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2
        q = [(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        q = [0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s]
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        q = [(m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s]
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        q = [(m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s, (m[1, 0] - m[0, 1]) / s]
    q = np.array(q)
    return q / np.linalg.norm(q)


def align(a, b):
    """Minimal rotation taking unit direction a onto unit direction b."""
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    v = np.cross(a, b)
    c = float(np.dot(a, b))
    if c < -0.999999:
        axis = np.cross(a, [1.0, 0.0, 0.0])
        if np.linalg.norm(axis) < 1e-6:
            axis = np.cross(a, [0.0, 1.0, 0.0])
        axis /= np.linalg.norm(axis)
        return 2 * np.outer(axis, axis) - np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k / (1 + c)


def quat_angle(a, b):
    return 2.0 * math.degrees(math.acos(min(1.0, abs(float(np.dot(a, b))))))


def quat_slerp(a, b, t):
    if np.dot(a, b) < 0:
        b = -b
    dot = min(1.0, float(np.dot(a, b)))
    if dot > 0.9995:
        q = a + (b - a) * t
    else:
        theta = math.acos(dot)
        q = (math.sin((1 - t) * theta) * a + math.sin(t * theta) * b) / math.sin(theta)
    return q / np.linalg.norm(q)


def quat_mul(a, b):
    x1, y1, z1, w1 = a
    x2, y2, z2, w2 = b
    return np.array([w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
                     w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2])


def quat_conj(q):
    return np.array([-q[0], -q[1], -q[2], q[3]])


def quat_log(q):
    q = -q if q[3] < 0 else q
    length = float(np.linalg.norm(q[:3]))
    return q[:3] * (2.0 * math.atan2(length, q[3]) / length) if length > 1e-9 else 2.0 * q[:3]


def quat_exp(v):
    angle = float(np.linalg.norm(v))
    if angle < 1e-9:
        q = np.array([v[0] * 0.5, v[1] * 0.5, v[2] * 0.5, 1.0])
        return q / np.linalg.norm(q)
    axis = v / angle
    return np.array([*(axis * math.sin(angle / 2)), math.cos(angle / 2)])


def hermite_fill(local, joint, first, last):
    """Replace frames first+1..last-1 with a cubic that leaves `first` and arrives at `last` at their own speeds,
    so the fill joins the sound path on both sides without the velocity kink a straight slerp leaves."""
    count = local.shape[0]
    base = local[first, joint]
    inverse = quat_conj(base)
    tangent = lambda f: quat_log(quat_mul(inverse, local[f, joint]))
    span = last - first
    end = tangent(last)
    m0 = -tangent(first - 1) if first > 0 else np.zeros(3)
    m1 = tangent(last + 1) - end if last + 1 < count else np.zeros(3)
    # Edge velocities may already belong to the flip; capped at the chord speed the fill cannot overshoot it.
    chord = float(np.linalg.norm(end)) / span
    m0, m1 = (m * min(1.0, chord / max(float(np.linalg.norm(m)), 1e-9)) for m in (m0, m1))
    for i in range(1, span):
        t = i / span
        h10, h01, h11 = t ** 3 - 2 * t ** 2 + t, -2 * t ** 3 + 3 * t ** 2, t ** 3 - t ** 2
        v = h10 * span * m0 + h01 * end + h11 * span * m1
        local[first + i, joint] = quat_mul(base, quat_exp(v))
    # Uneven edge speeds bunch a cubic's motion into the middle; a fill that would itself step like a flip falls back
    # to constant speed along the chord.
    if any(quat_angle(local[f - 1, joint], local[f, joint]) > SPIKE_STEP_DEGREES for f in range(first + 1, last + 1)):
        for i in range(1, span):
            local[first + i, joint] = quat_mul(base, quat_exp(end * (i / span)))


def align_hemispheres(local):
    for f in range(1, local.shape[0]):
        flip = np.einsum("ij,ij->i", local[f], local[f - 1]) < 0
        local[f, flip] = -local[f, flip]


def repair_spikes(local):
    """Re-interpolate isolated bursts where a joint leaves its path and comes back; returns the repaired count.

    A burst is a step above SPIKE_STEP_DEGREES after which, within SPIKE_FRAMES, the joint is back within the
    distance it could have covered at that speed. At a clip edge there is no frame on the far side, so the burst is
    replaced by the nearest sound frame instead.
    """
    repaired = 0
    count = local.shape[0]
    for joint in range(local.shape[1]):
        f = 1
        while f < count:
            if quat_angle(local[f - 1, joint], local[f, joint]) > SPIKE_STEP_DEGREES:
                good = local[f - 1, joint]
                before = quat_angle(local[f - 2, joint], good) if f >= 2 else 0.0
                reach = min(SPIKE_STEP_DEGREES, SPIKE_RETURN_SLACK * max(before, SPIKE_MIN_REACH_DEGREES))
                back = next(
                    (k for k in range(1, SPIKE_FRAMES + 1)
                     if f + k < count and quat_angle(good, local[f + k, joint]) <= reach * (k + 1)),
                    None,
                )
                if back is not None:
                    hermite_fill(local, joint, f - 1, f + back)
                    repaired += back
                    f += back
                elif count - f <= SPIKE_FRAMES:
                    for i in range(f, count):
                        local[i, joint] = good if np.dot(good, local[i, joint]) >= 0 else -good
                    repaired += count - f
                    f = count
                elif f <= SPIKE_FRAMES:
                    # The opening frames are the outliers: hold the first frame after them.
                    for i in range(f):
                        local[i, joint] = local[f, joint]
                    repaired += f
            f += 1
    return repaired


def smooth_joints(local, names):
    """Low-pass the listed joints; consecutive rotations share a hemisphere, so a normalised average is a blend."""
    radius = len(SMOOTHING_TAPS) // 2
    for d, name in enumerate(names):
        if name in SMOOTHED_JOINTS:
            padded = np.pad(local[:, d], [(radius, radius), (0, 0)], mode="edge")
            blended = sum(w * padded[k:k + local.shape[0]] for k, w in enumerate(SMOOTHING_TAPS))
            local[:, d] = blended / np.linalg.norm(blended, axis=1, keepdims=True)


def gaussian_smooth(values, sigma_frames):
    radius = max(1, int(3 * sigma_frames))
    kernel = np.exp(-0.5 * (np.arange(-radius, radius + 1) / sigma_frames) ** 2)
    kernel /= kernel.sum()
    padded = np.pad(values, [(radius, radius)] + [(0, 0)] * (values.ndim - 1), mode="edge")
    return np.stack([np.convolve(padded[:, i], kernel, mode="valid") for i in range(values.shape[1])], axis=1)


# ---------------------------------------------------------------- rig


class Rig:
    def __init__(self, glb_path):
        data = pathlib.Path(glb_path).read_bytes()
        length = struct.unpack("<I", data[12:16])[0]
        gltf = json.loads(data[20 : 20 + length])
        nodes = gltf["nodes"]
        joints = gltf["skins"][0]["joints"]
        node_parent = {}
        for index, node in enumerate(nodes):
            for child in node.get("children", []):
                node_parent[child] = index

        self.names = [nodes[j]["name"] for j in joints]
        self.index = {name: i for i, name in enumerate(self.names)}
        joint_of_node = {node: i for i, node in enumerate(joints)}
        self.parent = [joint_of_node.get(node_parent.get(j), -1) for j in joints]
        self.bind_rotation = [quat_to_matrix(nodes[j].get("rotation", [0, 0, 0, 1])) for j in joints]
        self.bind_translation = [np.array(nodes[j].get("translation", [0, 0, 0]), float) for j in joints]

        def world(node):
            matrix = np.eye(4)
            matrix[:3, :3] = quat_to_matrix(nodes[node].get("rotation", [0, 0, 0, 1]))
            matrix[:3, 3] = nodes[node].get("translation", [0, 0, 0])
            matrix[:3, :3] *= np.array(nodes[node].get("scale", [1, 1, 1]))
            return world(node_parent[node]) @ matrix if node in node_parent else matrix

        rest = [world(j) for j in joints]
        self.rest_rotation = [m[:3, :3] / np.linalg.norm(m[:3, :3], axis=0) for m in rest]
        self.rest_position = [m[:3, 3] for m in rest]
        self.order = sorted(range(len(joints)), key=self.depth)
        self.driven = [self.index[name] for name in MAPPING]

    def depth(self, joint):
        depth = 0
        while self.parent[joint] >= 0:
            joint = self.parent[joint]
            depth += 1
        return depth

    def leg_length(self):
        point = lambda name: self.rest_position[self.index[name]]
        return float(np.linalg.norm(point("shin.L") - point("thigh.L")) + np.linalg.norm(point("foot.L") - point("shin.L")))

    def forward(self, world_rotation, pelvis_position):
        """World positions of every joint from world rotations and the pelvis position (rig bind lengths)."""
        positions = [None] * len(self.names)
        for joint in self.order:
            parent = self.parent[joint]
            if parent < 0:
                positions[joint] = self.rest_position[joint].copy()
            elif self.names[joint] == "pelvis":
                positions[joint] = pelvis_position
            else:
                positions[joint] = positions[parent] + world_rotation[parent] @ self.bind_translation[joint]
        return positions


# ---------------------------------------------------------------- acclaim


class Skeleton:
    def __init__(self, path):
        self.bones = {}
        self.children = {}
        section = None
        current = None
        for raw in pathlib.Path(path).read_text(errors="replace").splitlines():
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith(":"):
                section = line.split()[0]
                continue
            words = line.split()
            if section == ":bonedata":
                if words[0] == "begin":
                    current = {"dof": [], "axis": np.zeros(3), "direction": np.zeros(3), "length": 0.0}
                elif words[0] == "end":
                    self.bones[current["name"]] = current
                elif words[0] == "name":
                    current["name"] = words[1]
                elif words[0] == "direction":
                    current["direction"] = np.array([float(v) for v in words[1:4]])
                elif words[0] == "length":
                    current["length"] = float(words[1]) * ASF_TO_METRES
                elif words[0] == "axis":
                    current["axis"] = np.radians([float(v) for v in words[1:4]])
                elif words[0] == "dof":
                    current["dof"] = [w.lower() for w in words[1:]]
            elif section == ":hierarchy" and words[0] not in ("begin", "end"):
                self.children[words[0]] = words[1:]
        for bone in self.bones.values():
            bone["C"] = euler_xyz(*bone["axis"])
        self.order = []
        stack = ["root"]
        while stack:
            name = stack.pop(0)
            self.order.append(name)
            stack.extend(self.children.get(name, []))
        self.parent = {child: parent for parent, kids in self.children.items() for child in kids}

    def leg_length(self):
        return self.bones["lfemur"]["length"] + self.bones["ltibia"]["length"]

    def pose(self, values):
        """World rotation (since rest) and start/end positions of each bone for one AMC frame."""
        rotation = {}
        start = {}
        end = {}
        root = values["root"]
        rotation["root"] = euler_xyz(*np.radians(root[3:6]))
        end["root"] = np.array(root[0:3]) * ASF_TO_METRES
        start["root"] = end["root"]
        for name in self.order[1:]:
            bone = self.bones[name]
            angles = dict(zip(bone["dof"], values.get(name, [])))
            local = euler_xyz(*np.radians([angles.get("rx", 0.0), angles.get("ry", 0.0), angles.get("rz", 0.0)]))
            parent = self.parent[name]
            rotation[name] = rotation[parent] @ bone["C"] @ local @ bone["C"].T
            start[name] = end[parent]
            end[name] = start[name] + rotation[name] @ bone["direction"] * bone["length"]
        return rotation, start, end


def read_amc(path):
    frames = []
    current = None
    for raw in pathlib.Path(path).read_text(errors="replace").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or line.startswith(":"):
            continue
        words = line.split()
        if len(words) == 1 and words[0].isdigit():
            current = {}
            frames.append(current)
        elif current is not None:
            current[words[0]] = [float(v) for v in words[1:]]
    return frames


# ---------------------------------------------------------------- retarget


def retarget(rig, skeleton, frames, step):
    rest_rotation, rest_start, rest_end = skeleton.pose({"root": [0.0] * 6})
    alignment = {}
    for joint_name, bone_name in MAPPING.items():
        joint = rig.index[joint_name]
        if joint_name in DIRECTION_CHILD:
            child = rig.index[DIRECTION_CHILD[joint_name]]
            rig_direction = rig.rest_position[child] - rig.rest_position[joint]
            cmu_direction = rest_end[bone_name] - rest_start[bone_name]
            alignment[joint] = align(rig_direction, cmu_direction)
        else:
            alignment[joint] = np.eye(3)

    scale = rig.leg_length() / skeleton.leg_length()
    sampled = frames[::step]
    count = len(sampled)
    world = np.zeros((count, len(rig.names), 3, 3))
    pelvis = np.zeros((count, 3))
    direction_error = 0.0

    for f, values in enumerate(sampled):
        rotation, start, end = skeleton.pose(values)
        for joint in rig.order:
            name = rig.names[joint]
            parent = rig.parent[joint]
            if joint in alignment:
                world[f, joint] = rotation[MAPPING[name]] @ alignment[joint] @ rig.rest_rotation[joint]
            elif parent < 0:
                world[f, joint] = rig.rest_rotation[joint]
            else:
                world[f, joint] = world[f, parent] @ rig.bind_rotation[joint]
        pelvis[f] = end["root"] * scale
        for joint_name, child_name in DIRECTION_CHILD.items():
            joint, child = rig.index[joint_name], rig.index[child_name]
            rig_rest = rig.rest_position[child] - rig.rest_position[joint]
            moved = world[f, joint] @ np.linalg.inv(rig.rest_rotation[joint]) @ rig_rest
            bone = MAPPING[joint_name]
            target = end[bone] - start[bone]
            cosine = np.dot(moved, target) / (np.linalg.norm(moved) * np.linalg.norm(target))
            direction_error = max(direction_error, math.degrees(math.acos(min(1.0, max(-1.0, cosine)))))
    return world, pelvis, scale, direction_error


def build_clip(rig, skeleton, trial, family, fps):
    frames = read_amc(SOURCE / trial.split("_")[0] / f"{trial}.amc")
    step = fps // OUTPUT_HZ
    world, pelvis, scale, direction_error = retarget(rig, skeleton, frames, step)
    count = len(world)

    positions = np.array([rig.forward(world[f], pelvis[f]) for f in range(count)])
    feet = {side: [rig.index[f"foot.{side}"], rig.index[f"toe.{side}"]] for side in "LR"}
    standing = {side: [rig.rest_position[j][1] for j in feet[side]] for side in "LR"}
    lowest = min(
        float(np.min(positions[:, j, 1] - standing[side][k])) for side in "LR" for k, j in enumerate(feet[side])
    )
    floor = float(np.percentile([
        min(positions[f, j, 1] - standing[side][k] for side in "LR" for k, j in enumerate(feet[side]))
        for f in range(count)
    ], 5))
    positions[:, :, 1] -= floor
    pelvis[:, 1] -= floor

    contacts = np.zeros((count, 2), dtype=bool)
    for s, side in enumerate("LR"):
        # Each foot's own floor: CMU marker placement leaves one foot a couple of centimetres higher than the other,
        # which a shared floor would read as that foot never touching down.
        own_floor = float(np.percentile([
            min(positions[f, joint, 1] - standing[side][k] for k, joint in enumerate(feet[side])) for f in range(count)
        ], 5))
        raw = np.zeros(count, dtype=bool)
        for f in range(count):
            for k, joint in enumerate(feet[side]):
                height = positions[f, joint, 1] - standing[side][k] - own_floor
                previous = positions[max(0, f - 1), joint]
                following = positions[min(count - 1, f + 1), joint]
                speed = np.linalg.norm((following - previous)[[0, 2]]) * OUTPUT_HZ / max(1, min(f + 1, count - 1) - max(0, f - 1))
                if height < CONTACT_HEIGHT and speed < CONTACT_SPEED:
                    raw[f] = True
        f = 0
        while f < count:
            if raw[f]:
                end = f
                while end < count and raw[end]:
                    end += 1
                if end - f >= CONTACT_MIN_FRAMES:
                    contacts[f:end, s] = True
                f = end
            else:
                f += 1

    ground = gaussian_smooth(pelvis[:, [0, 2]], ROOT_SMOOTHING_SECONDS * OUTPUT_HZ)
    pelvis_forward = np.array([world[f, rig.index["pelvis"]] @ np.linalg.inv(rig.rest_rotation[rig.index["pelvis"]]) @ [0, 0, 1] for f in range(count)])
    yaw = np.unwrap(np.arctan2(pelvis_forward[:, 0], pelvis_forward[:, 2]))
    yaw = gaussian_smooth(yaw[:, None], HEADING_SMOOTHING_SECONDS * OUTPUT_HZ)[:, 0]

    local = np.zeros((count, len(rig.driven), 4))
    pelvis_local = np.zeros((count, 3))
    for f in range(count):
        root_rotation = euler_xyz(0.0, yaw[f], 0.0)
        for d, joint in enumerate(rig.driven):
            parent = rig.parent[joint]
            parent_world = root_rotation if rig.names[joint] == "pelvis" else world[f, parent]
            local[f, d] = matrix_to_quat(parent_world.T @ world[f, joint])
            if f > 0 and np.dot(local[f, d], local[f - 1, d]) < 0:
                local[f, d] = -local[f, d]
        offset = pelvis[f] - np.array([ground[f, 0], 0.0, ground[f, 1]])
        pelvis_local[f] = root_rotation.T @ offset
    spikes = repair_spikes(local)
    align_hemispheres(local)
    smooth_joints(local, [rig.names[joint] for joint in rig.driven])

    stats = {
        "trial": trial,
        "family": family,
        "frames": count,
        "seconds": round(count / OUTPUT_HZ, 3),
        "rootScale": round(scale, 4),
        "maxDirectionErrorDegrees": round(direction_error, 3),
        "floorOffsetMetres": round(floor, 4),
        "lowestFootMetres": round(lowest - floor, 4),
        "contactFraction": [round(float(contacts[:, 0].mean()), 3), round(float(contacts[:, 1].mean()), 3)],
        "repairedSpikeFrames": spikes,
        "meanSpeed": round(float(np.mean(np.linalg.norm(np.diff(ground, axis=0), axis=1)) * OUTPUT_HZ), 3),
    }
    return {"ground": ground, "yaw": yaw, "pelvis": pelvis_local, "local": local, "contacts": contacts}, stats


# ---------------------------------------------------------------- output


def write_string(out, text):
    data = text.encode("utf-8")
    out += struct.pack("<H", len(data)) + data


def main():
    glb, output, report_path = sys.argv[1:4]
    rig = Rig(glb)
    trials = json.loads((ROOT / "cmu-trials.json").read_text())
    manifest = json.loads((ROOT / "cmu-manifest.json").read_text())["files"]

    selection = []
    family = "unassigned"
    for line in (ROOT / "cmu-selection.txt").read_text().splitlines():
        heading = re.match(r"#\s*([a-z ,]+):", line)
        if heading:
            family = heading.group(1).split(",")[0].split()[0]
        elif not line.startswith("#"):
            selection.extend((trial, family) for trial in line.split())

    out = bytearray(b"FEMODB01")
    out += struct.pack("<IHH", 1, OUTPUT_HZ, len(rig.driven))
    for joint in rig.driven:
        write_string(out, rig.names[joint])
    out += struct.pack("<I", len(selection))

    skeletons = {}
    clips = []
    for trial, group in selection:
        subject = trial.split("_")[0]
        for relative in (f"{subject}/{subject}.asf", f"{subject}/{trial}.amc"):
            if hashlib.sha256((SOURCE / relative).read_bytes()).hexdigest() != manifest[relative]:
                raise SystemExit(f"{relative} does not match the pinned manifest; run fetch_cmu.py --verify-only")
        skeleton = skeletons.setdefault(subject, Skeleton(SOURCE / subject / f"{subject}.asf"))
        clip, stats = build_clip(rig, skeleton, trial, group, trials[trial]["fps"])
        stats["description"] = trials[trial]["description"]
        clips.append(stats)
        write_string(out, trial)
        write_string(out, f"CMU {trial}: {trials[trial]['description']}")
        write_string(out, group)
        out += struct.pack("<I", stats["frames"])
        for f in range(stats["frames"]):
            out += struct.pack(
                "<6fB",
                clip["ground"][f, 0],
                clip["ground"][f, 1],
                clip["yaw"][f],
                *clip["pelvis"][f],
                int(clip["contacts"][f, 0]) | (int(clip["contacts"][f, 1]) << 1),
            )
            quantized = np.clip(np.round(clip["local"][f] * 32767.0), -32767, 32767).astype("<i2")
            out += quantized.tobytes()
        print(f"{trial:7s} {group:12s} {stats['frames']:5d} frames  dir-err {stats['maxDirectionErrorDegrees']:.2f}°  contact {stats['contactFraction']}")

    pathlib.Path(output).write_bytes(bytes(out))
    families = {}
    for clip in clips:
        entry = families.setdefault(clip["family"], {"clips": 0, "frames": 0})
        entry["clips"] += 1
        entry["frames"] += clip["frames"]
    report = {
        "schema": 1,
        "rateHz": OUTPUT_HZ,
        "drivenJoints": [rig.names[j] for j in rig.driven],
        "bytes": len(out),
        "sha256": hashlib.sha256(out).hexdigest(),
        "frames": sum(c["frames"] for c in clips),
        "families": families,
        "clips": clips,
    }
    pathlib.Path(report_path).write_text(json.dumps(report, indent=1) + "\n")
    print(f"{report['frames']} frames, {len(out) / 1e6:.1f} MB, sha256 {report['sha256']}")


if __name__ == "__main__":
    main()
