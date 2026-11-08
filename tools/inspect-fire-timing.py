"""Produce half-second samples for checking the order of fire-scene actions."""
from pathlib import Path
import sys
import cv2
from PIL import Image, ImageDraw

path = Path(sys.argv[1]).resolve()
cap = cv2.VideoCapture(str(path))
fps = cap.get(cv2.CAP_PROP_FPS)
count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
for part in range(2):
    sheet = Image.new('RGB', (1152, 1248), '#202020')
    draw = ImageDraw.Draw(sheet)
    for i in range(16):
        seconds = part * 8 + i * .5
        n = min(count - 1, round(seconds * fps))
        cap.set(cv2.CAP_PROP_POS_FRAMES, n)
        ok, frame = cap.read()
        if not ok:
            raise RuntimeError(f'Cannot decode frame {n}')
        image = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        image.thumbnail((288, 288))
        x, y = i % 4 * 288, i // 4 * 312
        sheet.paste(image, (x, y))
        draw.text((x + 7, y + 291), f'{n/fps:.2f}s', fill='white')
    dest = path.with_name(f'{path.stem}-timing-{part+1}.jpg')
    sheet.save(dest, quality=94)
    print(dest)
cap.release()
