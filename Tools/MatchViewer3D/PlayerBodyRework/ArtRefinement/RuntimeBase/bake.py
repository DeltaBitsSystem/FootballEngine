"""Run established original body texture baking only inside Runtime-Base."""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[5]
source=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/ArtRefinement/bake_final.py'
text=source.read_text();old='Final-Body/Art-Refinement'
assert old in text
text=text.replace(old,old+'/Runtime-Base')
assert "Final-Body/Art-Refinement'" not in text
exec(compile(text,str(source),'exec'),{'__file__':str(source),'__name__':'__main__'})
