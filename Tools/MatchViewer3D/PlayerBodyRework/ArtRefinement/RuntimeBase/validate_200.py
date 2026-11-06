"""Run original all-frame pose validator on the consolidated selected body."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[5]
source=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_validate.py'
text=source.read_text()
assert text.count('Final-Body/Revision-Sol')==1
text=text.replace('Final-Body/Revision-Sol','Final-Body/Art-Refinement/Runtime-Base')
text=text.replace("OUT / 'final-body-source.blend'","OUT / 'body-consolidated.blend'")
assert 'Final-Body/Revision-Sol' not in text
exec(compile(text,str(source),'exec'),{'__file__':str(source),'__name__':'__main__'})
