"""Original low-frequency anatomical sculpt fields over the project-owned cage.

Metres, Blender Z-up and anterior -Y. Fields are deliberately broad: silhouette
and the transitions between bony landmarks take precedence over skin detail.
"""
import math
import bpy
from mathutils import Vector

def bell(value, centre, radius):
    return math.exp(-((value - centre) / radius) ** 2)

def refine(body, meshes):
    # Diffusion removes the inherited horizontal glute shelf and angular calf contour without resampling UVs or weights.
    group = body.vertex_groups.new(name='Astra anatomical transition smoothing')
    for v in body.data.vertices:
        x, y, z = v.co
        weight = max(bell(z, .87, .085), .7 * bell(z, .34, .09))
        if abs(x) < .24 and z < 1.06:
            group.add([v.index], weight, 'REPLACE')
    bpy.context.view_layer.objects.active = body
    modifier = body.modifiers.new('Anatomical transition diffusion', 'SMOOTH')
    modifier.factor, modifier.iterations, modifier.vertex_group = 1., 18, group.name
    while list(body.modifiers).index(modifier) > 0:
        bpy.ops.object.modifier_move_up(modifier=modifier.name)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    body.vertex_groups.remove(body.vertex_groups['Astra anatomical transition smoothing'])

    for obj in meshes:
        if obj.name == 'FE Boots':
            continue
        for v in obj.data.vertices:
            x, y, z = v.co
            ax = abs(x)
            dx = dy = dz = 0.
            # Acromion/deltoid rises into the shoulder girdle rather than hanging below a swollen trapezius.
            shoulder = bell(ax, .235, .095) * bell(z, 1.425, .075)
            dz += .028 * shoulder
            dx -= math.copysign(.012 * shoulder, x)
            dz -= .014 * bell(ax, .105, .055) * bell(z, 1.485, .035)
            if 1.02 < z < 1.48 and ax < .23:
                centre = bell(ax, 0, .20)
                front, back = max(0., -y / .09), max(0., y / .09)
                # A rib cage has depth and planes, not a continuous barrel tapering into an hourglass waist.
                dx += math.copysign(.012 * bell(z, 1.145, .105) * bell(ax, .14, .08), x)
                dy += .010 * bell(z, 1.32, .14) * front * centre
                dy -= .014 * bell(z, 1.35, .06) * bell(ax, .095, .075) * front
                dy += .004 * bell(ax, 0, .022) * bell(z, 1.34, .09) * front
                dy -= .006 * bell(z, 1.445 - ax * .12, .012) * bell(ax, .10, .085) * front
                dy += .004 * bell(z, 1.285, .016) * bell(ax, .09, .07) * front
                dy -= .003 * bell(z, 1.19, .10) * bell(ax, .045, .035) * front
                dy += .005 * bell(z, 1.365, .08) * bell(ax, .095, .06) * back
                dy -= .004 * bell(ax, 0, .025) * bell(z, 1.29, .13) * back
            if .84 < z < 1.04 and ax < .22:
                back = max(0., y / .10)
                dy += .012 * bell(z, .925, .065) * bell(ax, .085, .060) * back
                dy -= .007 * bell(z, .91, .06) * bell(ax, 0, .018) * back
            if z > 1.49:
                # Broader cranial vault, a tapered mandibular angle and a neck seated under the skull.
                cranial = bell(z, 1.735, .10)
                dx += x * .075 * cranial
                dx -= x * .055 * bell(z, 1.625, .025)
                dy -= .004 * bell(z, 1.61, .02) * bell(ax, 0, .035) * max(0., -y / .07)
                dy += .004 * bell(z, 1.70, .07) * max(0., y / .09)
                dx += math.copysign(.006 * bell(z, 1.55, .045) * bell(ax, .032, .018), x)
                dy -= .003 * bell(z, 1.55, .05) * bell(ax, .03 + (1.6 - z) * .12, .012) * max(0., -y / .04)
                dz -= .005 * bell(z, 1.615, .025)
                if obj.name == 'FINAL_Body_Connected' and y < -.025:
                    anterior = min(1., max(0., (-y - .025) / .025))
                    # Orbital rim, malar plane and nasolabial transition give the face structure without painted shadows.
                    dy += .0035 * bell(ax, .033, .020) * bell(z, 1.690, .016) * anterior
                    dy -= .0035 * bell(ax, .034, .025) * bell(z, 1.711, .009) * anterior
                    dy -= .004 * bell(ax, .048, .020) * bell(z, 1.670, .014) * anterior
                    dy += .0025 * bell(ax, .051, .020) * bell(z, 1.644, .018) * anterior
                    dy += .003 * bell(ax, .013, .006) * bell(z, 1.658, .005) * anterior
                    dy -= .002 * bell(ax, .014, .017) * bell(z, 1.634, .005) * anterior
                    dy -= .0025 * bell(ax, .012, .020) * bell(z, 1.627, .005) * anterior
            if .53 < z < .84 and ax < .23:
                # Vastus lateralis/medialis converge above a narrower bony knee, not a cylindrical thigh.
                dy -= .006 * bell(z, .70, .10) * bell(ax, .12, .06) * max(0., -y / .08)
                dx += math.copysign(.004 * bell(z, .70, .10) * bell(ax, .17, .05), x)
                dx -= math.copysign(.004 * bell(z, .57, .045) * bell(ax, .055, .035), x)
            if .08 < z < .53 and ax < .22:
                # Medial calf is slightly lower; Achilles stays narrow and the tibia keeps its anterior plane.
                dy -= .004 * bell(z, .50, .028) * bell(ax, .10, .04) * max(0., -y / .06)
                dy += .004 * bell(z, .355, .085) * bell(ax, .08, .045) * max(0., y / .06)
                dx += math.copysign(.0025 * bell(z, .115, .018) * bell(ax, .13, .02), x)
            v.co += Vector((dx, dy, dz))
        obj.data.update()
    hair = next(o for o in meshes if o.name == 'GEO_Hair')
    bpy.context.view_layer.objects.active = hair
    modifier = hair.modifiers.new('Rounded cranial hair envelope', 'SMOOTH')
    modifier.factor, modifier.iterations = .8, 10
    bpy.ops.object.modifier_move_up(modifier=modifier.name)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    uv = hair.data.uv_layers.active
    for poly in hair.data.polygons:
        for li in poly.loop_indices:
            p = hair.data.vertices[hair.data.loops[li].vertex_index].co
            u = .5 + math.atan2(p.x, -p.y) / math.tau
            v = max(.01, min(.99, (p.z - 1.67) / .145))
            uv.data[li].uv = (u / 4, .5 + v / 4)
