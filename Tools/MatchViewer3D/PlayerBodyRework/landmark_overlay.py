"""Annotate our own renders only; no reference artwork is copied."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study'
data=json.loads((OUT/'landmark-projection.json').read_text())
for view,file in [('frontal','front'),('perfil','profile')]:
    image=Image.open(BASE/'PhaseA-Sculpt-11/evidence'/(file+'.png')).convert('RGB');d=ImageDraw.Draw(image)
    pts=data['views'][view]
    for first,last in [('coronilla','suelo'),('acromion L','acromion R'),('pelvis ASIS L aprox','pelvis ASIS R aprox'),('menton','esternon superior')]:
        d.line([tuple(pts[first]),tuple(pts[last])],fill=(255,185,20),width=2)
    for name,(x,y) in pts.items():
        d.ellipse((x-3,y-3,x+3,y+3),fill=(255,75,50))
        d.text((min(500,x+8),max(0,y-6)),name,fill=(255,235,180),stroke_width=1,stroke_fill=(20,20,20))
    image.save(OUT/('overlay-'+view+'.png'))
