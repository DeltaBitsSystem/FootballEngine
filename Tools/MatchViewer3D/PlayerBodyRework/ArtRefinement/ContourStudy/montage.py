from pathlib import Path
from PIL import Image,ImageDraw

root=Path(__file__).resolve().parents[5]
out=root/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Contour-Study'
page=Image.new('RGB',(1200,1600),(25,26,29));draw=ImageDraw.Draw(page)
for row,view in enumerate(('head_profile','head','head_front','body')):
    for col,label in enumerate(('before','study')):
        img=Image.open(out/'comparison'/label/(view+'.png')).convert('RGB').resize((280,355))
        x=col*600+160;y=row*400+25
        page.paste(img,(x,y));draw.text((x,y-18),label+' / '+view,fill=(240,240,240))
page.save(out/'AB-form.jpg',quality=95)
