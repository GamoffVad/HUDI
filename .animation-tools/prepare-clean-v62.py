"""Build a clean whole-frame library: no eye processing, hover or synthetic morphs."""
import importlib.util
import json
import os
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'output/hudi-pet-v6'
NEW = ROOT / 'output/hudi-hood-pockets-2026-11-07'
OUT = ROOT / 'output/hudi-pet-v6.2'
ALLOWED = ['wink', 'think', 'crouch', 'stretch', 'angry', 'sad', 'surprise', 'lookaround', 'dance', 'combat']


def main():
    target = OUT / 'frames'
    target.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((BASE / 'manifest.json').read_text(encoding='utf-8'))
    manifest['clips'] = {name: manifest['clips'][name] for name in ALLOWED}
    used = {'neutral'}
    for clip in manifest['clips'].values():
        used.update(step['frame'] for step in clip)
    for frame in used:
        destination = target / (frame + '.png')
        if not destination.exists():
            os.link(BASE / 'frames' / destination.name, destination)
    spec = importlib.util.spec_from_file_location('keyer', ROOT / '.animation-tools/prepare-green-sprites-v6.py')
    keyer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(keyer)
    scale = .46
    matrix = np.array([[scale, 0, 384 / 2 - 392 * scale], [0, scale, 500 - 13 - 1120 * scale]], np.float32)
    for name, filename in [('hood', '11_hood.mp4'), ('pockets', '12_pockets.mp4')]:
        cap = cv2.VideoCapture(str(NEW / filename))
        fps = cap.get(cv2.CAP_PROP_FPS)
        count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        steps = []
        for i in range(count):
            ok, bgr = cap.read()
            if not ok:
                raise RuntimeError(f'Missing {name} frame {i}')
            if bgr.shape[:2] != (1168, 784):
                raise RuntimeError('Unexpected source dimensions; recalibrate before keying')
            colors, alpha, _ = keyer.key_frame(bgr)
            rgba = keyer.output_frame(colors, alpha, matrix)
            frame = f'{name}-{i:03d}'
            Image.fromarray(rgba).save(target / (frame + '.png'), compress_level=2)
            used.add(frame)
            steps.append({'frame': frame, 'seconds': 1 / fps})
            if i % 48 == 0:
                print(f'{name}: {i + 1}/{count}', flush=True)
        cap.release()
        manifest['clips'][name] = steps
    manifest.update(version='6.2', frames=sorted(used), cursorTracking=False,
                    hoverReaction=False, eyeReconstruction=False,
                    playback='unaltered whole Higgsfield frames, no synthesized poses')
    (OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')

    # Flag possible third eye-screen lights for manual inspection; never retouch faces.
    suspects = []
    for frame in sorted(used):
        a = np.array(Image.open(target / (frame + '.png'))).astype(np.int16)
        mask = ((a[:, :, 2] > 125) & (a[:, :, 2] - a[:, :, 0] > 20) &
                (a[:, :, 1] - a[:, :, 0] > 10) & (a[:, :, 3] > 200)).astype(np.uint8)
        solid = np.where(a[:, :, 3] > 220)
        y0, y1 = int(solid[0].min()), int(solid[0].max())
        mask[:y0] = 0
        mask[y0 + int((y1 - y0) * .50):] = 0
        n, _, stats, centers = cv2.connectedComponentsWithStats(mask)
        ids = [i for i in range(1, n) if stats[i, 4] > 5 and stats[i, 3] < stats[i, 2] * 7]
        ids.sort(key=lambda i: stats[i, 4], reverse=True)
        if len(ids) < 3:
            continue
        eyes = sorted(ids[:2], key=lambda i: centers[i, 0])
        middle_y = (centers[eyes[0], 1] + centers[eyes[1], 1]) / 2
        if any(centers[eyes[0], 0] < centers[i, 0] < centers[eyes[1], 0] and
               5 < centers[i, 1] - middle_y < 28 for i in ids[2:]):
            suspects.append(frame)
    (OUT / 'face-audit.json').write_text(json.dumps({'frames': len(used), 'possible_third_lights': suspects}, indent=2), encoding='utf-8')
    print(f'Prepared {len(used)} source frames; possible third face lights: {suspects}', flush=True)


if __name__ == '__main__':
    main()
