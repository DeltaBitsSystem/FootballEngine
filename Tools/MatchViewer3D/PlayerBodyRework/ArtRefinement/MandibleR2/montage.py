"""Assemble unchanged-camera A/B evidence from the reopened final source."""
from pathlib import Path
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Mandible-R2'
for style in ('clay','pbr'):
    for kind,views in [('head',('head_front','head_profile','head')),('poses',('body','squat','twist'))]:
        page=Image.new('RGB',(1000,1500),(25,26,29)); draw=ImageDraw.Draw(page)
        for row,view in enumerate(views):
            for col,variant in enumerate(('before','final')):
                pic=Image.open(OUT/'comparison'/variant/style/(view+'.png')).convert('RGB').resize((375,475))
                x=col*500+62; y=row*500+23
                page.paste(pic,(x,y)); draw.text((x,y-18),f'{variant} / {view}',fill=(235,235,235))
        page.save(OUT/'comparison'/f'AB-{kind}-{style}.jpg',quality=94)
page=Image.new('RGB',(960,720),(25,26,29)); draw=ImageDraw.Draw(page)
for row,distance in enumerate((10,25,50)):
    for level in range(3):
        im=Image.open(OUT/'lod-distances'/f'lod{level}-{distance}m.png').convert('RGB')
        im=im.crop((360,150,600,390)).resize((220,220),Image.Resampling.NEAREST)
        x=level*320+50;y=row*240+18
        page.paste(im,(x,y));draw.text((x,y-16),f'LOD{level} / {distance}m',fill=(235,235,235))
page.save(OUT/'lod-distances'/'comparison.jpg',quality=94)
