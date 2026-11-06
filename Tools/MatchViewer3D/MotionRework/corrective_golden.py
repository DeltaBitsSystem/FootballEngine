"""Blender batch reference: invoke with a copied runtime source and output path after --."""
import json
import math
import sys
import bpy
from mathutils import Quaternion

source, output = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=source)
rig = bpy.data.objects['FINAL_Rig']
rig.animation_data_clear()
pairs = [('hip_support', 'thigh'), ('knee_support', 'shin'),
         ('elbow_support', 'forearm'), ('axilla_support', 'upper_arm')]

def reset():
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
        bone.rotation_mode = 'QUATERNION'

def rows(matrix):
    return [list(row) for row in matrix]

cases = []
for side in ('L', 'R'):
    for helper_name, source_name in pairs:
        for parent_angle in (0.0, 0.47):
            for angle in (0.0, 0.8):
                reset()
                child = rig.pose.bones[source_name + '.' + side]
                helper = rig.pose.bones[helper_name + '.' + side]
                child.parent.rotation_quaternion = Quaternion((0.2, 0.7, 0.4), parent_angle)
                child.rotation_quaternion = Quaternion((0.4, 0.2, 0.8), angle)
                helper.rotation_quaternion = Quaternion().slerp(child.rotation_quaternion, 0.5)
                bpy.context.view_layer.update()
                parent = child.parent
                cases.append({'source': child.name, 'support': helper.name,
                              'parentAngle': parent_angle, 'angle': angle,
                              'parentWorld': rows(parent.matrix),
                              'sourceWorld': rows(child.matrix), 'supportWorld': rows(helper.matrix),
                              'sourceBindLocal': rows(parent.bone.matrix_local.inverted() @ child.bone.matrix_local),
                              'supportBindLocal': rows(parent.bone.matrix_local.inverted() @ helper.bone.matrix_local)})
with open(output, 'w') as stream:
    json.dump({'cases': cases}, stream, indent=2)
print('CORRECTIVE_GOLDEN', output, len(cases))
