"""Reuse Sol's original procedural bake stages with an isolated output root."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[4]
HERE=ROOT/'Tools/MatchViewer3D/PlayerBodyRework'
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT.mkdir(exist_ok=True)
source=HERE/'final_body_atlas.py'
text=source.read_text().split('# UV checker evidence with the real camera and lights.')[0]
assert text.count('Final-Body/Revision-E')==1
text=text.replace('Final-Body/Revision-E','Final-Body/Art-Refinement')
assert 'Revision-E' not in text and 'Revision-Sol' not in text
ns={'__file__':str(source),'__name__':'__main__'}
exec(compile(text,str(source),'exec'),ns)
# The albedo and UV audit above used the promoted mesh; normal and roughness
# come from Sol's native tangent/position-field bake, without changing UVs.
source=HERE/'sol_body_textures.py'
text=source.read_text()
text=text[text.index('scene = bpy.context.scene\nscene.render.threads_mode'):]
assert 'Revision-Sol' not in text
ns.update({'OUT':OUT})
exec(compile(text,str(source),'exec'),ns)
