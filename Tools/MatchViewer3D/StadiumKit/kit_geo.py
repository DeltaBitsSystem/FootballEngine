"""Geometry helpers for the stadium kit: primitives with physically scaled UVs, merged per piece."""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector


class MeshBuilder:
    """Accumulates primitives into one bmesh; material slots are by kit material id."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.materials = []

    def _mat_index(self, mid):
        if mid not in self.materials:
            self.materials.append(mid)
        return self.materials.index(mid)

    def _box_uv(self, faces, tile):
        # World-scale box projection: one texture repeat per `tile` metres on every face.
        for f in faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                co = loop.vert.co
                u, v = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
                loop[self.uv].uv = (u / tile, v / tile)

    def box(self, center, size, mat, tile=1.0, rot=None):
        m = Matrix.Translation(Vector(center))
        if rot is not None:
            m = m @ rot.to_4x4()
        m = m @ Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        faces = list({f for v in res["verts"] for f in v.link_faces})
        idx = self._mat_index(mat)
        for f in faces:
            f.material_index = idx
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        self._box_uv(faces, tile)
        return faces

    def tube(self, p0, p1, radius, mat, sides=8, tile=1.0, caps=True):
        p0, p1 = Vector(p0), Vector(p1)
        axis = p1 - p0
        length = axis.length
        if length < 1e-6:
            return []
        z = axis.normalized()
        helper = Vector((0, 0, 1)) if abs(z.z) < 0.9 else Vector((1, 0, 0))
        x = helper.cross(z).normalized()
        y = z.cross(x)
        rings = []
        for end in (p0, p1):
            ring = []
            for i in range(sides):
                a = 2 * math.pi * i / sides
                ring.append(self.bm.verts.new(end + (x * math.cos(a) + y * math.sin(a)) * radius))
            rings.append(ring)
        idx = self._mat_index(mat)
        faces = []
        circumference = 2 * math.pi * radius
        for i in range(sides):
            j = (i + 1) % sides
            f = self.bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
            f.material_index = idx
            f.smooth = True
            u0 = i / sides * circumference / tile
            u1 = (i + 1) / sides * circumference / tile
            for loop, (u, v) in zip(f.loops, ((u0, 0), (u1, 0), (u1, length / tile), (u0, length / tile))):
                loop[self.uv].uv = (u, v)
            faces.append(f)
        if caps:
            for ring, rev in ((rings[0], True), (rings[1], False)):
                f = self.bm.faces.new(list(reversed(ring)) if rev else ring)
                f.material_index = idx
                faces.append(f)
                self._box_uv([f], tile)
        return faces

    def quad(self, corners, mat, uv_rect=None, tile=1.0, double=False):
        vs = [self.bm.verts.new(Vector(c)) for c in corners]
        f = self.bm.faces.new(vs)
        f.material_index = self._mat_index(mat)
        if uv_rect is None:
            self._box_uv([f], tile)
        else:
            for loop, uv in zip(f.loops, uv_rect):
                loop[self.uv].uv = uv
        out = [f]
        if double:
            vs2 = [self.bm.verts.new(Vector(c)) for c in reversed(corners)]
            g = self.bm.faces.new(vs2)
            g.material_index = f.material_index
            for loop, uv in zip(g.loops, list(reversed(uv_rect)) if uv_rect else [(0, 0)] * 4):
                loop[self.uv].uv = uv
            out.append(g)
        return out

    def grid(self, origin, du, dv, nu, nv, mat, uv_scale=(1.0, 1.0)):
        """Planar grid with UV 0..uv_scale; used where vertices must move at runtime (net, flag)."""
        origin, du, dv = Vector(origin), Vector(du), Vector(dv)
        verts = [[self.bm.verts.new(origin + du * (i / nu) + dv * (j / nv)) for i in range(nu + 1)] for j in range(nv + 1)]
        idx = self._mat_index(mat)
        for j in range(nv):
            for i in range(nu):
                f = self.bm.faces.new((verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i]))
                f.material_index = idx
                for loop, (a, b) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                    loop[self.uv].uv = (a / nu * uv_scale[0], b / nv * uv_scale[1])
        return verts

    def merge_object(self, obj):
        """Bake an evaluated object (modifiers applied) into this builder, keeping its UVs and materials."""
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        me = ev.to_mesh()
        me.transform(obj.matrix_world)
        offset = len(self.bm.verts)
        remap = [self._mat_index(s.material["fe_material"] if s.material and "fe_material" in s.material else "painted_steel") for s in obj.material_slots] or [self._mat_index("painted_steel")]
        tmp = bmesh.new()
        tmp.from_mesh(me)
        src_uv = tmp.loops.layers.uv.active
        vmap = [self.bm.verts.new(v.co) for v in tmp.verts]
        for f in tmp.faces:
            nf = self.bm.faces.new([vmap[v.index] for v in f.verts])
            nf.material_index = remap[min(f.material_index, len(remap) - 1)]
            nf.smooth = f.smooth
            for l_new, l_old in zip(nf.loops, f.loops):
                l_new[self.uv].uv = l_old[src_uv].uv if src_uv else (0, 0)
        tmp.free()
        ev.to_mesh_clear()
        return offset

    def build(self, materials_by_id, collection=None):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        self.bm.to_mesh(me)
        self.bm.free()
        for mid in self.materials:
            me.materials.append(materials_by_id[mid])
        obj = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(obj)
        return obj


def shade_auto_smooth(obj, angle_deg=35.0):
    # Hard creases on architecture, soft tubes and shells; the operator writes sharp edges into the mesh,
    # which the glTF exporter turns into split normals.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_deg))
