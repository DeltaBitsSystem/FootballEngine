"""Tile review PNGs into one sheet: python3 contact_sheet.py out.png a.png b.png ..."""
import sys
from PIL import Image

out, paths = sys.argv[1], sys.argv[2:]
images = [Image.open(p).convert('RGB') for p in paths]
h = 480
images = [im.resize((round(im.width * h / im.height), h)) for im in images]
cols = min(4, len(images))
rows = (len(images) + cols - 1) // cols
w = max(im.width for im in images)
sheet = Image.new('RGB', (cols * w, rows * h), (20, 20, 20))
for i, im in enumerate(images):
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save(out)
