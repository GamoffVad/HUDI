"""Motion-compensated joins between the existing Higgsfield clips and rest pose.

Keeps original source frames unchanged. Interpolates their pixels in premultiplied
RGBA so that the character never becomes translucent against the desktop.
"""
import json
import os
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'output/hudi-pet-v6'
OUTPUT = ROOT / 'output/hudi-pet-v6.1'
NAMES = ['wink', 'happy', 'think', 'crouch', 'stretch', 'angry', 'sad',
         'surprise', 'lookaround', 'dance', 'combat']


def load(frame):
    rgba = np.array(Image.open(SOURCE / 'frames' / (frame + '.png')), np.float32) / 255
    rgba[:, :, :3] *= rgba[:, :, 3:]
    return rgba


def flow_image(rgba):
    gray = cv2.cvtColor(rgba[:, :, :3], cv2.COLOR_RGB2GRAY)
    return np.uint8(np.clip((gray + (1 - rgba[:, :, 3]) * .36) * 255, 0, 255))


def warp(pixels, flow, fraction, grid):
    coords = grid - flow * fraction
    for _ in range(3):
        displacement = cv2.remap(flow, coords[:, :, 0], coords[:, :, 1], cv2.INTER_LINEAR)
        coords = grid - displacement * fraction
    return cv2.remap(pixels, coords[:, :, 0], coords[:, :, 1], cv2.INTER_LINEAR)


def save(pixels, path):
    alpha = pixels[:, :, 3:]
    rgb = np.divide(pixels[:, :, :3], alpha, out=np.zeros_like(pixels[:, :, :3]), where=alpha > 1e-5)
    rgba = np.uint8(np.clip(np.dstack([rgb, alpha]) * 255, 0, 255))
    Image.fromarray(rgba).save(path, compress_level=3)


def main():
    frames_dir = OUTPUT / 'frames'
    frames_dir.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((SOURCE / 'manifest.json').read_text(encoding='utf-8'))
    for frame in manifest['frames']:
        target = frames_dir / (frame + '.png')
        if not target.exists():
            os.link(SOURCE / 'frames' / target.name, target)
    h, w = load('neutral').shape[:2]
    xx, yy = np.meshgrid(np.arange(w), np.arange(h))
    grid = np.stack([xx, yy], axis=-1).astype(np.float32)
    transitions = {}
    reviews = []
    for name in NAMES:
        transitions[name] = {}
        original = manifest['clips'][name]
        for direction, start, end, count in [
            ('enter', 'neutral', original[0]['frame'], 10),
            ('exit', original[-1]['frame'], 'neutral', 12),
        ]:
            a, b = load(start), load(end)
            optical = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
            forward = optical.calc(flow_image(a), flow_image(b), None)
            backward = optical.calc(flow_image(b), flow_image(a), None)
            steps = [{'frame': start, 'seconds': 1 / 24}] if direction == 'enter' else []
            for i in range(1, count):
                t = i / count
                t = t * t * t * (10 + t * (-15 + 6 * t))
                middle = warp(a, forward, t, grid) * (1 - t) + warp(b, backward, 1 - t, grid) * t
                frame = f'bridge-{name}-{direction}-{i:02d}'
                save(middle, frames_dir / (frame + '.png'))
                manifest['frames'].append(frame)
                steps.append({'frame': frame, 'seconds': 1 / 24})
                if i == count // 2 and direction == 'exit':
                    reviews.append((name, start, frame, end))
            if direction == 'exit':
                steps.append({'frame': end, 'seconds': 1 / 24})
            transitions[name][direction] = steps
        print(name + ': smooth entry and return prepared', flush=True)
    manifest['version'] = '6.1'
    manifest['transitions'] = transitions
    manifest['transitionMethod'] = 'bidirectional optical flow, eased premultiplied RGBA'
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    sheet = Image.new('RGB', (576, len(reviews) * 250), (220, 220, 220))
    draw = ImageDraw.Draw(sheet)
    for row, (name, *frames) in enumerate(reviews):
        for col, frame in enumerate(frames):
            canvas = Image.new('RGBA', (w, h), (220, 220, 220, 255))
            canvas.alpha_composite(Image.open(frames_dir / (frame + '.png')))
            sheet.paste(canvas.resize((192, 250)).convert('RGB'), (col * 192, row * 250))
        draw.text((4, row * 250 + 4), name, fill='black')
    sheet.save(OUTPUT / 'transition-review.jpg', quality=94)


if __name__ == '__main__':
    main()
