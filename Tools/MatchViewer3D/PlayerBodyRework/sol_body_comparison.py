"""Controlled clay A/B against the preserved delivery, without editing it."""
from pathlib import Path
import subprocess

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[2]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol/comparison'
for revision,label in [('Revision-E','before-E'),('Revision-Sol','after-Sol')]:
    # Repeated file reloads after Cycles renders can crash Blender's dependency
    # graph. A fresh process per source also makes the A/B isolation explicit.
    subprocess.run(['blender','--background','--factory-startup','--python-exit-code','1',
                    '--python',str(HERE/'sol_body_review.py'),'--','--revision',revision,
                    '--samples','24','--clay','--views','body','head','hand','hip',
                    '--output',str(OUT/label)],cwd=ROOT,check=True)
print('SOL_COMPARISON_COMPLETE',flush=True)
