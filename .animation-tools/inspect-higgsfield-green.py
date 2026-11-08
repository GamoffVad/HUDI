"""Inspect source videos without altering their animation or chroma background."""
import json
import sys
from pathlib import Path
import cv2
import numpy as np

root = Path(sys.argv[1]).resolve()
run = json.loads((root / 'generation-run.json').read_text(encoding='utf-8-sig'))
review = root / 'review'
review.mkdir(exist_ok=True)
reports = []
for clip in run['clips']:
    path = root / (clip['name'] + '.mp4')
    if not path.exists():
        continue
    cap = cv2.VideoCapture(str(path))
    fps = cap.get(cv2.CAP_PROP_FPS)
    count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    samples = set(round((count - 1) * x) for x in [0, .2, .4, .6, .8, 1])
    border_green = []
    foreground_bounds = []
    decoded = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        h, w = frame.shape[:2]
        b, g, r = cv2.split(frame.astype(np.int16))
        green = (g > r + 35) & (g > b + 35) & (g > 80)
        border = np.concatenate([green[:8].ravel(), green[-8:].ravel(), green[:, :8].ravel(), green[:, -8:].ravel()])
        border_green.append(float(border.mean()))
        n, labels, stats, _ = cv2.connectedComponentsWithStats((~green).astype(np.uint8), 8)
        if n > 1:
            x, y, bw, bh, area = stats[1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])].tolist()
            foreground_bounds.append([x, y, x + bw, y + bh])
        if decoded in samples:
            cv2.imwrite(str(review / f"{clip['name']}-{decoded:03d}.png"), frame)
        decoded += 1
    cap.release()
    report = {
        'name': clip['name'], 'width': w, 'height': h, 'fps': fps,
        'frames': decoded, 'duration_seconds': decoded / fps,
        'min_green_border_fraction': min(border_green),
        'foreground_union': [min(v[0] for v in foreground_bounds), min(v[1] for v in foreground_bounds), max(v[2] for v in foreground_bounds), max(v[3] for v in foreground_bounds)],
        'decode_complete': decoded == count, 'bytes': path.stat().st_size,
    }
    reports.append(report)
    print(json.dumps(report), flush=True)
(root / 'video-inspection.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')
