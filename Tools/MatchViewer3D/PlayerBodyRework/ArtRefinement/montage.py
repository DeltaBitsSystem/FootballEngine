"""Build side-by-side review sheets from the disk-rendered views."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[4]
out = root/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/comparison'
for style in ('clay', 'pbr'):
    canvas = Image.new('RGB', (1200, 1560), (25, 26, 29))
    draw = ImageDraw.Draw(canvas)
    for row, view in enumerate(('head_front', 'head_profile', 'head')):
        for col, variant in enumerate(('before', 'final')):
            source = out/variant/style/(view+'.png')
            image = Image.open(source).convert('RGB').resize((600, 760))
            y = row*520+22
            image = image.resize((390, 494))
            x = col*600+105
            canvas.paste(image,(x,y))
            draw.text((x,y-19),f'{variant} / {view}',fill=(235,235,235))
    canvas.save(out/('AB-'+style+'.jpg'),quality=92)

asset=out.parent
for style in ('clay','pbr'):
    sheet=Image.new('RGB',(1200,1560),(25,26,29))
    draw=ImageDraw.Draw(sheet)
    for row,view in enumerate(('front','profile','body')):
        for col,variant in enumerate(('before','final')):
            folder=(asset/('before-body-'+style) if variant=='before' else asset/('final-review-clay' if style=='clay' else 'final-review'))
            image=Image.open(folder/(view+'.png')).convert('RGB').resize((390,494))
            x=col*600+105; y=row*520+22
            sheet.paste(image,(x,y))
            draw.text((x,y-19),f'{variant} / {view}',fill=(235,235,235))
    sheet.save(out/('AB-body-'+style+'.jpg'),quality=92)

sheet=Image.new('RGB',(960,720),(25,26,29))
draw=ImageDraw.Draw(sheet)
for row,distance in enumerate((10,25,50)):
    for level in range(3):
        im=Image.open(asset/'lod-distances'/f'lod{level}-{distance}m.png').convert('RGB')
        im=im.crop((360,150,600,390)).resize((220,220),Image.Resampling.NEAREST)
        x=level*320+50;y=row*240+18
        sheet.paste(im,(x,y))
        draw.text((x,y-16),f'LOD{level} / {distance}m (crop x0.92)',fill=(235,235,235))
sheet.save(asset/'lod-distances'/'comparison.jpg',quality=94)
