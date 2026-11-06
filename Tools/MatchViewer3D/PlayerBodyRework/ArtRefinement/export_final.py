"""Use the existing three-LOD roundtrip tool with an explicitly changed root."""
from pathlib import Path

ROOT=Path(__file__).resolve().parents[4]
source=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_export.py'
text=source.read_text()
assert text.count('Final-Body/Revision-Sol')==2
text=text.replace('Final-Body/Revision-Sol','Final-Body/Art-Refinement')
assert 'Final-Body/Revision-Sol' not in text
exec(compile(text,str(source),'exec'),{'__file__':str(source),'__name__':'__main__'})
