"""Run Sol's all-frame topology and deformation validator on this copy only."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[4]
source=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_validate.py'
text=source.read_text()
assert text.count('Final-Body/Revision-Sol')==1
text=text.replace('Final-Body/Revision-Sol','Final-Body/Art-Refinement')
assert 'Final-Body/Revision-Sol' not in text
exec(compile(text,str(source),'exec'),{'__file__':str(source),'__name__':'__main__'})
