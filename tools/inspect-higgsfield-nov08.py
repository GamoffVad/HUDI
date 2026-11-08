"""Inspect downloaded source videos without altering the generation outputs."""
from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image, ImageDraw

folder = Path(__file__).resolve().parents[1] / 'assets/higgsfield/2026-11-08-salute-water'
results = []
for path in folder.glob('*.mp4'):
    cap = cv2.VideoCapture(str(path))
    fps = cap.get(cv2.CAP_PROP_FPS)
    count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    width, height = (int(cap.get(k)) for k in (cv2.CAP_PROP_FRAME_WIDTH, cv2.CAP_PROP_FRAME_HEIGHT))
    sheet = Image.new('RGB', (4 * 256, 3 * 280), '#202020')
    draw = ImageDraw.Draw(sheet)
    endpoints = []
    for i, n in enumerate(np.linspace(0, count - 1, 12, dtype=int)):
        cap.set(cv2.CAP_PROP_POS_FRAMES, int(n))
        ok, frame = cap.read()
        if not ok:
            raise RuntimeError(f'Cannot decode {path.name} frame {n}')
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        if i in (0, 11):
            endpoints.append(rgb.astype(float))
        image = Image.fromarray(rgb)
        image.thumbnail((256, 256))
        x, y = (i % 4) * 256, (i // 4) * 280
        sheet.paste(image, (x, y))
        draw.text((x + 6, y + 258), f'{n / fps:.2f}s', fill='white')
    cap.release()
    sheet.save(folder / (path.stem + '-contact.jpg'), quality=92)
    results.append(dict(file=path.name, width=width, height=height, fps=fps, frames=count, duration=count/fps,
                        endpoint_mean_absolute_difference=float(np.mean(np.abs(endpoints[0]-endpoints[1])))))
(folder / 'video-inspection.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results, indent=2))
