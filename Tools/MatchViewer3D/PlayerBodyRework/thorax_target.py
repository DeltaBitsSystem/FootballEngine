"""Original drawing of intended planes over our own reference renders only."""
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Branched-Cage/Anatomy-Refined/Thorax'
target=OUT/'Targets';target.mkdir(exist_ok=True)
curves={
 'front':[[[320,177],[289,185],[256,200],[217,211]],[[320,177],[351,185],[384,200],[423,211]],[[255,220],[238,245],[235,282]],[[385,220],[402,245],[405,282]],[[320,190],[320,289]],[[235,274],[260,302],[295,310],[320,296]],[[405,274],[380,302],[345,310],[320,296]],[[260,313],[290,340],[320,350],[350,340],[380,313]]],
 'back':[[[320,175],[282,192],[255,208],[225,218]],[[320,175],[358,192],[385,208],[415,218]],[[258,225],[242,248],[260,295],[291,314]],[[382,225],[398,248],[380,295],[349,314]],[[320,193],[320,330]]],
 'profile':[[[313,170],[305,195],[291,215]],[[291,215],[275,240],[270,274],[279,298],[292,319]],[[344,207],[361,230],[373,269],[365,304],[347,323]]],
}
for view,paths in curves.items():
    img=Image.open(OUT/'evidence'/('A-'+view+'.png')).convert('RGB');draw=ImageDraw.Draw(img)
    for p in paths:draw.line([tuple(q) for q in p],fill=(255,181,44),width=3,joint='curve')
    draw.rectangle((6,6,390,30),fill=(15,15,15));draw.text((12,12),'NARANJA: objetivo de planos, no contorno existente',fill=(255,205,110))
    img.save(target/('target-'+view+'.png'))
