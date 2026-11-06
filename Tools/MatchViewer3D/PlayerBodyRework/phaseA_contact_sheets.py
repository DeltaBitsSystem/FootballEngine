"""Same-camera, uncropped clay comparisons; no image retouching."""
from pathlib import Path
import hashlib
import json
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Connected-07'
old=BASE/'PhaseA-Connected/evidence'
def sheet(name,shots):
    width,height=320,400
    result=Image.new('RGB',(width*2,(height+30)*len(shots)),(35,35,35));draw=ImageDraw.Draw(result)
    for row,shot in enumerate(shots):
        a=(OUT/'evidence/A-hand.png') if shot=='hand' else old/('A-'+shot+'.png')
        b=OUT/'evidence'/(shot+'.png')
        for column,path in enumerate([a,b]):
            image=Image.open(path).convert('RGB');assert image.size==(640,800)
            image=image.resize((width,height),Image.Resampling.LANCZOS)
            y=row*(height+30);result.paste(image,(column*width,y+30))
            draw.text((column*width+10,y+8),('A - anterior' if column==0 else 'B - revision 07')+' / '+shot,fill=(235,235,235))
    result.save(OUT/'evidence'/(name+'.jpg'),quality=94)
sheet('AB-body',['front','profile','back','three-quarter'])
sheet('AB-face',['head-front','head-profile','head-three-quarter'])
sheet('AB-hands-feet',['hand','feet'])
files=[p for p in OUT.rglob('*') if p.is_file() and p.suffix!='.blend1' and p.name!='manifest.json']
scripts=list(Path(__file__).parent.glob('phaseA_*edit.py'))+[Path(__file__).parent/n for n in ['phaseA_body_finish.py','phaseA_review_package.py','phaseA_contact_sheets.py']]
manifest={'kind':'Intermediate original form-authoring evidence, not production release','files':[{'path':str(p.relative_to(ROOT)),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in files+scripts]}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2))
