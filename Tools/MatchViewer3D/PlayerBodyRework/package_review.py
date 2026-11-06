"""Package already rendered original Blender evidence, without editing live assets."""
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
sets=[('inspection-body',['front','profile','back','three-quarter']),('inspection-details',['head-front','head-profile','hand-closeup','feet-closeup']),('inspection-poses',['arms-raised','elbows-flexed','hip-knee-flexed','running-stride'])]
for name,shots in sets:
    sheet=Image.new('RGB',(1280,430),(25,30,37));draw=ImageDraw.Draw(sheet)
    for i,shot in enumerate(shots):
        image=Image.open(OUT/'evidence'/f'{shot}.png').convert('RGB')
        image.thumbnail((320,400));sheet.paste(image,(i*320,25))
        draw.text((i*320+8,6),shot,fill='white')
    sheet.save(OUT/'evidence'/f'{name}.jpg',quality=92)
files=[p for p in OUT.rglob('*') if p.is_file() and p.suffix in {'.blend','.glb','.png','.jpg','.md'}]
scripts=list(Path(__file__).parent.glob('*.py'))
manifest={'provenance':'Original numeric anatomy, pigment pixels and test poses. No external body mesh, photographs, clips or generator service. Existing project joint naming/layout reused as an integration contract only.','tools':'Blender 5.2.2 LTS Voxel Remesh, QuadriFlow exploration, collapse decimation, bone heat, smoothing, UV projection, emission bake, glTF export/import; Pillow contact sheets.','files':{str(p.relative_to(ROOT)):{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in files+scripts}}
(OUT/'package-manifest.json').write_text(json.dumps(manifest,indent=2))
print('Packaged',len(files),'asset/evidence files and',len(scripts),'authoring scripts.')
