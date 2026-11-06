"""Build an original 4x4 runtime atlas; 2 home and 2 away material rows."""
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
colors=[(190,34,43,255),(28,34,50,255),(196,38,46,255),(170,110,82,255),
        (34,25,21,255),(24,27,32,255),(220,220,210,255),(225,224,216,255)]
away=[(32,76,168,255),(28,34,50,255),(37,78,169,255),(170,110,82,255),
      (34,25,21,255),(24,27,32,255),(220,220,210,255),(225,224,216,255)]
albedo=Image.new('RGBA',(4096,4096));detail=Image.new('RGBA',(4096,4096),(128,128,255,175))
skin=Image.open(OUT/'textures/final-body-albedo-2048.png').convert('RGBA').resize((1024,1024),Image.Resampling.LANCZOS)
normal=Image.open(OUT/'textures/final-body-normal-2048.png').convert('RGB').resize((1024,1024),Image.Resampling.LANCZOS)
rough=Image.open(OUT/'textures/final-body-roughness-2048.png').convert('L').resize((1024,1024),Image.Resampling.LANCZOS)
nr=Image.merge('RGBA',(*normal.split(),rough))
for team,palette in enumerate((colors,away)):
    for slot,color in enumerate(palette):
        x=(slot%4)*1024;y=(team*2+slot//4)*1024
        albedo.paste(skin if slot==3 else Image.new('RGBA',(1024,1024),color),(x,y))
        if slot==7:
            albedo.paste(Image.new('RGBA',(340,1024),(100,70,40,255)),(x+342,y))
            albedo.paste(Image.new('RGBA',(342,1024),(12,10,8,255)),(x+682,y))
        if slot==3:detail.paste(nr,(x,y))
albedo.save(OUT/'player-albedo.png');detail.save(OUT/'player-normal-roughness.png')
print('ATLAS_WRITTEN',flush=True)
