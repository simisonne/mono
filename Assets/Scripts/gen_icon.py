import os
import struct
from PIL import Image

src = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "Icons", "audiowaveform.png")
out = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "Icons", "mono.ico")

base = Image.open(src).convert("RGBA")
sizes = [16, 32, 48, 64, 128, 256]
resized = [base.resize((s, s), Image.LANCZOS) for s in sizes]

png_data = []
for img in resized:
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    png_data.append(buf.getvalue())

# ICO header: reserved(2) + type(2) + count(2)
header = struct.pack("<HHH", 0, 1, len(png_data))
offset = 6 + 16 * len(png_data)
entries = []
for i, s in enumerate(sizes):
    w = s if s < 256 else 0
    h = s if s < 256 else 0
    entry = struct.pack("<BBBBHHII",
                        w, h, 0, 0, 1, 32,
                        len(png_data[i]), offset)
    entries.append(entry)
    offset += len(png_data[i])

with open(out, "wb") as f:
    f.write(header)
    for e in entries:
        f.write(e)
    for d in png_data:
        f.write(d)

print(f"Icon saved to {out} with sizes: {sizes}")

import io
