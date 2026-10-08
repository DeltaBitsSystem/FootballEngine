"""Wardrobe meshes are accessories of an unmodified anatomical body, never body materials."""
import math

import bpy
from mathutils import Vector


def garment_uv(point, material):
    if material in ("boots", "trim"):
        return ((point[0] + 0.23) / 0.46, (point[1] + 0.30) / 0.40)
    if material in ("shorts", "socks"):
        centre = 0.10 if point[0] > 0 else -0.10
        angle = math.atan2(point[1], point[0] - centre)
        u = (angle + math.pi) / math.tau
        v = (point[2] - 0.07) / 0.44 if material == "socks" else (point[2] - 0.73) / 0.32
        # Each leg has its own half-island, avoiding mirrored kit painting overwriting the other side.
        return ((u + (1 if point[0] > 0 else 0)) / 2, max(0.005, min(0.995, v)))
    return ((point[0] + 0.40) / 0.80, max(0.005, min(0.995, (point[2] - 1.0) / 0.56)))


def wardrobe(scene, rig):
    from player_authoring import MeshAuthor
    equipment = []
    jersey = MeshAuthor(scene)
    rings = [(1.015, 0.164, 0.124), (1.065, 0.166, 0.128), (1.15, 0.174, 0.138),
             (1.25, 0.190, 0.152), (1.36, 0.213, 0.160), (1.44, 0.216, 0.149),
             (1.49, 0.195, 0.117), (1.53, 0.085, 0.074), (1.542, 0.061, 0.058)]

    def torso_weights(index, count):
        z = rings[index][0]
        if z < 1.14:
            spine = max(0, min(1, (z - 1.015) / 0.125))
            return {"pelvis": 1-spine, "spine": spine}
        chest = max(0, min(1, (z - 1.14) / 0.18))
        return {"spine": 1-chest, "chest": chest}

    jersey.loft([((0, -0.025 if z < 1.5 else -0.015, z), width, depth) for z, width, depth in rings],
                "jersey", torso_weights, segments=48, caps=False)
    for sign, side in [(-1, "R"), (1, "L")]:
        bone = rig.data.bones[f"upper_arm.{side}"]
        direction = (bone.tail_local - bone.head_local).normalized()
        transverse = Vector((0, 1, 0))
        depth = direction.cross(transverse).normalized()
        sleeve = [(0, 0.080), (0.16, 0.083), (0.32, 0.075), (0.48, 0.067)]
        jersey.loft([(bone.head_local.lerp(bone.tail_local, t), radius, radius) for t, radius in sleeve],
                    "jersey", lambda index, _, side=side: {"chest": 0.20 if index == 0 else 0.0,
                        f"upper_arm.{side}": 0.80 if index == 0 else 1.0},
                    (transverse, depth), segments=32, caps=False)
    equipment.append(("FE Jersey", "jersey", "torso,upper-arms", jersey, 0.0016))

    shorts = MeshAuthor(scene)
    shorts.loft([((0, -0.014, z), width, depth) for z, width, depth in
                 [(0.88, 0.174, 0.128), (0.95, 0.171, 0.125), (1.027, 0.164, 0.125)]],
                "shorts", lambda *_: {"pelvis": 1.0}, segments=48, caps=False)
    for sign, side in [(-1, "R"), (1, "L")]:
        leg = [(0.735, 0.080, 0.087), (0.79, 0.088, 0.092), (0.86, 0.094, 0.099), (0.94, 0.096, 0.105)]
        shorts.loft([((sign * 0.10, -0.010, z), width, depth) for z, width, depth in leg],
                    "shorts", lambda i, n, side=side: {"pelvis": (i/(n-1)) * 0.6,
                        f"thigh.{side}": 1 - (i/(n-1)) * 0.6}, segments=32, caps=False)
    equipment.append(("FE Shorts", "shorts", "pelvis,upper-thighs", shorts, 0.0018))

    socks = MeshAuthor(scene)
    for sign, side in [(-1, "R"), (1, "L")]:
        contours = [(0.095, 0.0, 0.036, 0.039), (0.16, 0.0, 0.037, 0.044),
                    (0.25, 0.003, 0.052, 0.059), (0.35, 0.006, 0.063, 0.074),
                    (0.43, 0.002, 0.065, 0.069), (0.505, -0.009, 0.056, 0.054)]
        socks.loft([((sign*0.10, y, z), width, depth) for z, y, width, depth in contours],
                   "socks", lambda i, n, side=side: {f"shin.{side}": 1.0}, segments=32, caps=False)
    equipment.append(("FE Socks", "socks", "lower-legs", socks, 0.0012))

    boots = MeshAuthor(scene)
    for sign, side in [(-1, "R"), (1, "L")]:
        x = sign * 0.10
        boots.loft([((x, y, z), width, height) for y, z, width, height in
                    [(0.075, 0.068, 0.026, 0.042), (0.035, 0.075, 0.045, 0.065),
                     (-0.025, 0.062, 0.054, 0.049), (-0.09, 0.048, 0.055, 0.034),
                     (-0.18, 0.038, 0.050, 0.026), (-0.255, 0.033, 0.030, 0.021),
                     (-0.275, 0.031, 0.005, 0.016)]],
                   "boots", lambda *_, side=side: {f"foot.{side}": 1.0},
                   (Vector((1, 0, 0)), Vector((0, 0, -1))), segments=32)
        boots.loft([((x, y, 0.020), width, 0.008) for y, width in
                    [(0.073, 0.025), (0.025, 0.048), (-0.07, 0.058), (-0.18, 0.052), (-0.264, 0.027)]],
                   "trim", lambda *_, side=side: {f"foot.{side}": 1.0},
                   (Vector((1, 0, 0)), Vector((0, 0, -1))), segments=32)
        for y in [-0.025, -0.042, -0.059, -0.076, -0.093]:
            boots.segment((x-0.021, y, 0.101), (x+0.021, y-0.008, 0.101),
                          [(0, 0.002, 0.002), (1, 0.002, 0.002)], "trim", f"foot.{side}")
        for dx, y in [(-0.025, 0.025), (0.025, 0.025), (-0.03, -0.10), (0.03, -0.10), (-0.02, -0.20), (0.02, -0.20)]:
            boots.segment((x+dx, y, 0.015), (x+dx, y, 0.005),
                          [(0, 0.006, 0.006), (1, 0.004, 0.004)], "boots", f"foot.{side}")
    equipment.append(("FE Boots", "boots", "feet", boots, 0.0))
    result = []
    for name, category, coverage, author, thickness in equipment:
        points = [Vector(point) for point in author.vertices]
        faces = [list(face) for face in author.faces]
        materials = [author.material_names[slot] for slot in author.material_indices]
        uvs = [[garment_uv(points[index], mat) for index in face] for face, mat in zip(faces, materials)]
        # A planar garment island must not interpolate across a cylindrical seam.
        for face_uv in uvs:
            if max(uv[0] for uv in face_uv) - min(uv[0] for uv in face_uv) > 0.4:
                for index, uv in enumerate(face_uv):
                    if uv[0] < 0.25:
                        face_uv[index] = (uv[0] + 0.5, uv[1])
        result.append({"name": name, "category": category, "coverage": coverage, "points": points,
                       "faces": faces, "weights": author.weights, "uvs": uvs, "materials": materials,
                       "thickness": thickness})
    return result


def build_layered(scene, rig, output):
    from human_authoring import read_anatomy, adapt_anatomy, NAMES, make_atlases
    source_points, source_uvs, source_faces, skeleton, source_weights = read_anatomy()
    points, weights = adapt_anatomy(rig, source_points, skeleton, source_weights)
    surfaces = {name: {"name": "FE " + name.title(), "category": name, "coverage": "none", "points": [],
                       "faces": [], "weights": [], "uvs": [], "materials": [], "thickness": 0.0}
                for name in ["body", "eyes", "hair"]}
    mappings = {name: {} for name in surfaces}

    def append(name, face, uv, mat):
        target = surfaces[name]
        mapped = []
        for index in face:
            if index not in mappings[name]:
                mappings[name][index] = len(target["points"])
                point = points[index].copy()
                if name == "hair":
                    point += (point - Vector((0, -0.065, 1.73))).normalized() * 0.004
                target["points"].append(point)
                target["weights"].append(weights[index].copy())
            mapped.append(mappings[name][index])
        target["faces"].append(mapped)
        target["uvs"].append(uv)
        target["materials"].append(mat)

    for group, corners in source_faces:
        face = [index for index, _ in corners]
        centre = sum((points[index] for index in face), Vector()) / len(face)
        if "eye" in group:
            sign = 1 if centre.x > 0 else -1
            uv = [((points[index].x-sign*0.0294+0.016)/0.032, (points[index].z-1.6745)/0.032) for index in face]
            append("eyes", face, uv, "eyes")
        else:
            uv = [source_uvs[index] for _, index in corners]
            append("body", face, uv, "skin")
            if centre.z > 1.746-0.080*max(0, min(1, (centre.y+0.11)/0.12)):
                append("hair", face, uv, "hair")
    hair = surfaces["hair"]
    edges = {}
    for face in hair["faces"]:
        for a, b in zip(face, face[1:]+face[:1]):
            edge = tuple(sorted((a, b)))
            edges[edge] = edges.get(edge, 0)+1
    for index in {v for edge, count in edges.items() if count == 1 for v in edge}:
        point = hair["points"][index]
        point.z = 1.746-0.080*max(0, min(1, (point.y+0.11)/0.12))
    layers = list(surfaces.values()) + wardrobe(scene, rig)
    all_points, all_faces, all_uvs, all_materials = [], [], [], []
    for layer in layers:
        offset = len(all_points)
        all_points.extend(tuple(point) for point in layer["points"])
        all_faces.extend([[index+offset for index in face] for face in layer["faces"]])
        all_uvs.extend(layer["uvs"])
        all_materials.extend(layer["materials"])
    albedo_path, detail_path = make_atlases(output, all_points, all_faces, all_uvs, all_materials)
    albedo = bpy.data.images.load(str(albedo_path), check_existing=False)
    detail = bpy.data.images.load(str(detail_path), check_existing=False)
    detail.colorspace_settings.name = "Non-Color"
    albedo.pack()
    detail.pack()
    material_cache = {}
    for category in [layer["category"] for layer in layers]:
        mat = bpy.data.materials.new("FE " + category.title() + " PBR")
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        shader = next(node for node in nodes if node.type == "BSDF_PRINCIPLED")
        base_node, detail_node = nodes.new("ShaderNodeTexImage"), nodes.new("ShaderNodeTexImage")
        base_node.image, detail_node.image = albedo, detail
        links.new(base_node.outputs["Color"], shader.inputs["Base Color"])
        links.new(detail_node.outputs["Alpha"], shader.inputs["Roughness"])
        normal_node = nodes.new("ShaderNodeNormalMap")
        links.new(detail_node.outputs["Color"], normal_node.inputs["Color"])
        links.new(normal_node.outputs["Normal"], shader.inputs["Normal"])
        if category == "body" and "Subsurface Weight" in shader.inputs:
            shader.inputs["Subsurface Weight"].default_value = 0.055
        material_cache[category] = mat
    objects = []
    for layer in layers:
        data = bpy.data.meshes.new(layer["name"] + " Mesh")
        data.from_pydata(layer["points"], [], layer["faces"])
        data.update()
        obj = bpy.data.objects.new(layer["name"], data)
        scene.collection.objects.link(obj)
        data.materials.append(material_cache[layer["category"]])
        uv_layer = data.uv_layers.new(name="FE Atlas UV")
        for polygon, uv, name in zip(data.polygons, layer["uvs"], layer["materials"]):
            polygon.use_smooth = True
            slot = NAMES.index(name)
            for loop, coordinate in zip(polygon.loop_indices, uv):
                uv_layer.data[loop].uv = ((slot%4+coordinate[0])/4, 1-(slot//4+1-coordinate[1])/4)
        groups = {bone.name: obj.vertex_groups.new(name=bone.name) for bone in rig.data.bones}
        for index, influences in enumerate(layer["weights"]):
            total = sum(influences.values())
            for name, value in influences.items():
                if value > 0:
                    groups[name].add([index], value/total, "REPLACE")
        bpy.context.view_layer.objects.active = obj
        if layer["thickness"]:
            # Baked thickness leaves independent, inspectable cloth edges in the GLB without runtime cloth simulation.
            modifier = obj.modifiers.new("FE Garment Thickness", "SOLIDIFY")
            modifier.thickness = layer["thickness"]
            modifier.offset = 0.0
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        modifier = obj.modifiers.new("FE Skin", "ARMATURE")
        modifier.object = rig
        obj.parent = rig
        obj["fe_category"] = layer["category"]
        obj["fe_coverage"] = layer["coverage"]
        obj["fe_independent_equipment"] = layer["category"] in ("jersey", "shorts", "socks", "boots")
        obj["fe_body_mutation"] = False
        objects.append(obj)
    return objects[0]
