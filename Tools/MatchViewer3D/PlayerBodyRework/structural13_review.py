"""Produce labelled A/B contact sheet and coordinate table from saved evidence."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Structural-13'
shots=['front','profile','back','three-quarter']
sheet=Image.new('RGB',(640,1720),(35,35,35));draw=ImageDraw.Draw(sheet)
for row,name in enumerate(shots):
    for col,label in enumerate(['A','B']):
        im=Image.open(OUT/'evidence'/(label+'-'+name+'.png')).convert('RGB')
        im=im.resize((320,400),Image.Resampling.LANCZOS)
        y=row*430;sheet.paste(im,(col*320,y+30));draw.text((col*320+10,y+8),label+' / '+name,fill='white')
sheet.save(OUT/'evidence'/'AB-structural.jpg',quality=94)
data=json.loads((OUT/'landmarks.json').read_text())
lines=['# Mediciones comparables','',data['method'],'','| Marcador aproximado | Antes (x, y, z) m | Después (x, y, z) m |','|---|---|---|']
for key in ['ankle lateral','knee anterior','trochanter lateral','pubis','umbilical level','sternum','acromion','neck base','chin']:
    a=data['before'][key]['xyz_m'];b=data['after'][key]['xyz_m']
    lines.append(f'| {key} | {a} | {b} |')
lines+=['',f"Suelo {data['before']['floor']['z_m']:.6f} m; coronilla {data['before']['crown']['z_m']:.6f} m; altura {data['before']['height_m']:.6f} m (iguales en A y B).",'','| Banda transversal | Ancho A → B (m) | Profundidad A → B (m) | Cantidad vértices A → B |','|---|---:|---:|---:|']
for key in ['neck_section_m','chest_section_m','waist_section_m','pelvis_section_m','knee_section_m','calf_section_m']:
    a=data['before'][key];b=data['after'][key]
    lines.append(f"| {key} | {a['width']:.4f} → {b['width']:.4f} | {a['depth']:.4f} → {b['depth']:.4f} | {a['count']} → {b['count']} |")
lines+=['','Las bandas toman vértices dentro de ±6 mm de una altura fija. Cuando cambia el número de vértices, especialmente en pelvis y gemelo, no representan una sección geométrica exacta; no interpretar esas diferencias como medidas antropométricas precisas.']
(OUT/'landmarks-table.md').write_text('\n'.join(lines)+'\n')
