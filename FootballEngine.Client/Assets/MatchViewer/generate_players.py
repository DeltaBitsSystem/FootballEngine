"""Run in Blender (including through Blender MCP) to regenerate the match sprites.

Creates a separate scene; the user's currently open scene and its objects survive.
Proportions: https://www.thedrawingsource.com/figure-drawing-proportions.html
Football action references: https://posemy.art/soccer-poses/
"""
import bpy
from pathlib import Path
from math import cos, sin, pi
from mathutils import Vector

output = Path(__file__).resolve().parent
original = bpy.context.window.scene
scene = bpy.data.scenes.new("FootballEngine • player sprites")
bpy.context.window.scene = scene


def material(name, rgb):
    mat = bpy.data.materials.new("FE • " + name)
    mat.use_nodes = True
    shader = next(node for node in mat.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (*rgb, 1)
    shader.inputs["Roughness"].default_value = 0.82
    return mat


shirt = material("shirt", (0.86, 0.88, 0.82))
shorts = material("shorts", (0.09, 0.10, 0.12))
socks = material("socks", (0.86, 0.88, 0.82))
skin = material("skin", (0.56, 0.30, 0.18))
hair = material("hair", (0.045, 0.029, 0.023))
boots = material("boots", (0.025, 0.032, 0.045))


def ellipsoid(name, location, scale, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, location=location)
    obj = bpy.context.object
    obj.name = "FE • " + name
    obj.scale = scale
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def jersey_mesh():
    rings = [(1.02, 0.13, 0.105), (1.18, 0.16, 0.11),
             (1.44, 0.21, 0.12), (1.56, 0.155, 0.10), (1.59, 0.07, 0.07)]
    vertices = [(rx * cos(2 * pi * i / 12), ry * sin(2 * pi * i / 12), z)
                for z, rx, ry in rings for i in range(12)]
    faces = [(j * 12 + i, j * 12 + (i + 1) % 12,
              (j + 1) * 12 + (i + 1) % 12, (j + 1) * 12 + i)
             for j in range(len(rings) - 1) for i in range(12)]
    mesh = bpy.data.meshes.new("FE • tailored jersey")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("FE • tailored jersey", mesh)
    scene.collection.objects.link(obj)
    obj.data.materials.append(shirt)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


jersey_mesh()
ellipsoid("pelvis", (0, 0, 1.035), (0.16, 0.11, 0.075), shorts)
ellipsoid("neck", (0, 0, 1.66), (0.059, 0.058, 0.07), skin)
ellipsoid("head", (0, 0, 1.80), (0.106, 0.103, 0.135), skin)
ellipsoid("hair cap", (0, 0.02, 1.91), (0.108, 0.103, 0.048), hair)


def limb(name, start, end, radius, mat, end_radius=None):
    direction = Vector(end) - Vector(start)
    midpoint = (Vector(start) + Vector(end)) / 2
    bpy.ops.mesh.primitive_cone_add(vertices=16, radius1=radius,
                                    radius2=radius if end_radius is None else end_radius,
                                    depth=direction.length, location=midpoint)
    obj = bpy.context.object
    obj.name = "FE • " + name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True


for side in (-1, 1):
    # Contralateral arms and legs give a readable football stride at match scale.
    shoulder = (side * 0.17, 0, 1.48)
    elbow = (side * 0.27, side * 0.13, 1.20)
    wrist = (side * 0.23, side * 0.22, 1.04)
    knee = (side * 0.14, -side * 0.19, 0.53 + (0.06 if side > 0 else 0))
    ankle = (side * 0.14, -side * 0.25, 0.12 + (0.04 if side > 0 else 0))
    limb("sleeve", shoulder, (side * 0.23, side * 0.045, 1.36), 0.063, shirt, 0.078)
    limb("upper arm", (side * 0.23, side * 0.045, 1.36), elbow, 0.043, skin, 0.052)
    ellipsoid("elbow", elbow, (0.049, 0.049, 0.049), skin)
    limb("forearm", elbow, wrist, 0.037, skin, 0.047)
    ellipsoid("hand", (wrist[0], wrist[1], wrist[2] - 0.04), (0.04, 0.043, 0.065), skin)
    hip = (side * 0.09, 0, 1.0)
    shorts_end = (side * 0.11, -side * 0.09, 0.78)
    limb("shorts leg", hip, shorts_end, 0.076, shorts, 0.084)
    limb("thigh", shorts_end, knee, 0.058, skin, 0.072)
    ellipsoid("knee", knee, (0.059, 0.061, 0.059), skin)
    limb("sock", knee, ankle, 0.052, socks, 0.065)
    ellipsoid("boot", (ankle[0], ankle[1] - 0.065, ankle[2] - 0.04),
              (0.074, 0.145, 0.057), boots)

bpy.ops.object.camera_add(location=(3.5, -7.5, 4.1))
camera = bpy.context.object
camera.name = "FE • sprite camera"
target = Vector((0, 0, 1.00))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 2.35
scene.camera = camera

for name, position, power, size in [
    ("key", (1, -3, 5), 650, 4),
    ("fill", (-3, -1, 3), 350, 4),
]:
    bpy.ops.object.light_add(type="AREA", location=position)
    light = bpy.context.object
    light.name = "FE • " + name
    light.data.energy = power
    light.data.shape = "DISK"
    light.data.size = size

scene.render.resolution_x = 160
scene.render.resolution_y = 224
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
scene.render.film_transparent = True

for name, jersey, pants, stockings in [
    ("home", (0.86, 0.88, 0.82), (0.09, 0.10, 0.12), (0.86, 0.88, 0.82)),
    ("away", (0.015, 0.14, 0.52), (0.015, 0.10, 0.35), (0.015, 0.14, 0.52)),
    ("keeper", (0.02, 0.57, 0.40), (0.02, 0.30, 0.25), (0.02, 0.57, 0.40)),
    ("official", (0.95, 0.67, 0.08), (0.09, 0.10, 0.12), (0.09, 0.10, 0.12)),
]:
    for mat, color in ((shirt, jersey), (shorts, pants), (socks, stockings)):
        shader = next(node for node in mat.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
        shader.inputs["Base Color"].default_value = (*color, 1)
    scene.render.filepath = str(output / ("player-" + name + ".png"))
    bpy.ops.render.render(write_still=True, scene=scene.name)

bpy.ops.wm.save_as_mainfile(filepath=str(output / "players.blend"))
bpy.context.window.scene = original
print("FootballEngine sprites saved in", output)
