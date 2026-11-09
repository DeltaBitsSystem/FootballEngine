"""Join review tiles into one labelled sheet. Usage: python3 -I contact_sheet.py <out.png> <tile=label> ..."""

import sys

from PIL import Image, ImageDraw

output, items = sys.argv[1], [item.split("=", 1) for item in sys.argv[2:]]
tiles = [(Image.open(path).convert("RGB"), label) for path, label in items]
width, height = tiles[0][0].size
sheet = Image.new("RGB", (width * len(tiles), height + 20), "white")
for index, (tile, label) in enumerate(tiles):
    sheet.paste(tile, (width * index, 20))
    ImageDraw.Draw(sheet).text((width * index + 6, 4), label, fill="black")
sheet.save(output)
print(f"wrote {output}")
