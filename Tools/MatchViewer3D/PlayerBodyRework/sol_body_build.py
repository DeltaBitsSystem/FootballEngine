"""One-command isolated authoring, baking, validation, export and review pipeline."""
import hashlib
import argparse
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
LOGS = Path('/tmp/opencode/sol-body-build')
STAGES = [
    ('author','sol_body_author.py',[]),
    ('textures','sol_body_textures.py',[]),
    ('validate','sol_body_validate.py',[]),
    ('export','sol_body_export.py',[]),
    ('review','sol_body_review.py',['--','--revision','Revision-Sol','--samples','32']),
    ('comparison','sol_body_comparison.py',[]),
    ('lod-review','sol_body_lod_review.py',[]),
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--from-stage',choices=[name for name,_,_ in STAGES],default='author')
    args=parser.parse_args()
    LOGS.mkdir(parents=True,exist_ok=True)
    start=next(i for i,stage in enumerate(STAGES) if stage[0]==args.from_stage)
    for name,script,stage_args in STAGES[start:]:
        print(f'Blender isolated stage: {name}',flush=True)
        with (LOGS/(name+'.log')).open('w') as log:
            command=(['python3',str(HERE/script)] if name=='comparison' else
                     ['blender','--background','--factory-startup','--python-exit-code','1','--python',str(HERE/script),*stage_args])
            subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT,check=True)
    validation=json.loads((OUT/'sol-validation.json').read_text())
    topology=validation['topology']
    if topology['components']!=1 or topology['boundaryEdges'] or topology['nonManifoldEdges'] or topology['unweighted'] or topology['maxInfluences']>4:
        raise RuntimeError('Body topology/skin checks failed; inspect sol-validation.json.')
    if topology['maxWeightSumError']>1e-5:
        raise RuntimeError('Body weight normalization failed.')
    uv=json.loads((OUT/'uv-atlas-audit.json').read_text())
    if uv['degenerateUVTriangles'] or uv['triangleCentroidBlackMisses']:
        raise RuntimeError('UV raster validation failed.')
    exports=json.loads((OUT/'sol-export-roundtrip.json').read_text())
    if len(exports['exports'])!=3:
        raise RuntimeError('Missing exported LOD.')
    from PIL import Image,ImageDraw
    sheet=Image.new('RGB',(1200,1080),(28,31,36))
    draw=ImageDraw.Draw(sheet)
    for i,(name,label) in enumerate([('body','Full body'),('head','Head'),('hand','Hand'),('foot','Foot'),('grip','Digit articulation'),('squat','Foot-planted squat')]):
        image=Image.open(OUT/'sol-review'/(name+'.png')).convert('RGB')
        image.thumbnail((390,500))
        x=(i%3)*400+(400-image.width)//2
        y=(i//3)*540+28
        sheet.paste(image,(x,y))
        draw.text(((i%3)*400+14,(i//3)*540+8),label,fill=(230,230,230))
    sheet.save(OUT/'model-review.jpg',quality=94)
    compare=Image.new('RGB',(1200,810),(28,31,36))
    draw=ImageDraw.Draw(compare)
    for row,label in enumerate(['before-E','after-Sol']):
        for col,view in enumerate(['body','head','hand','hip']):
            image=Image.open(OUT/'comparison'/label/(view+'.png')).convert('RGB')
            image.thumbnail((295,375))
            compare.paste(image,(col*300+(300-image.width)//2,row*405+25))
            draw.text((col*300+8,row*405+6),label+' / '+view,fill=(230,230,230))
    compare.save(OUT/'comparison'/'AB-clay.jpg',quality=94)
    lod_sheet=Image.new('RGB',(1080,1000),(28,31,36))
    draw=ImageDraw.Draw(lod_sheet)
    for level in range(3):
        for row,view in enumerate(['body','head']):
            image=Image.open(OUT/'lod-review'/f'lod{level}-{view}.png').convert('RGB')
            image.thumbnail((350,465))
            lod_sheet.paste(image,(level*360+(360-image.width)//2,row*500+25))
            draw.text((level*360+10,row*500+7),f'LOD{level} / {view}',fill=(230,230,230))
    lod_sheet.save(OUT/'lod-review'/'comparison.jpg',quality=94)
    input_paths=[HERE/script for _,script,_ in STAGES]+[
        HERE/'sol_body_build.py',HERE/'final_body_author_v6.py',HERE/'final_body_atlas.py',HERE/'final_body_export.py',
        ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Iteration1/athlete-body-editable.blend',
    ]
    provenance={
        'originalContent':'Project-authored geometry, rig, validation poses and procedural texture pixels. No stock meshes, photographs or motion-capture clips incorporated.',
        'tools':'Blender native bmesh, subdivision, UV unwrap/pack, Cycles baking, decimation and glTF exporter/importer.',
        'referenceStudy':['https://anatomy4sculptors.com/blog/forming-the-head-simple-to-complex/'],
        'runtimeAcceptance':'Standard GLB roundtrip validated. The game atlas/motion contract is not installed or tested by this authoring pipeline.',
        'inputs':[{'path':str(p.relative_to(ROOT)),'sha256':digest(p)} for p in input_paths],
        'outputs':[{'path':str(p.relative_to(OUT)),'bytes':p.stat().st_size,'sha256':digest(p)} for p in sorted(OUT.rglob('*'))
                   if p.is_file() and p.suffix in {'.blend','.glb','.png','.jpg','.json','.md'} and p.name!='provenance.json'],
    }
    (OUT/'provenance.json').write_text(json.dumps(provenance,indent=2))
    print('Complete authoring pipeline verified. Visual acceptance remains a review of the actual images, not these checks.',flush=True)
