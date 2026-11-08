"""Append actual Higgsfield return motions, preserving whole source frames."""
import importlib.util
import argparse
import json
import os
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'output/hudi-pet-v6.2'
SOURCE = ROOT / 'output/hudi-return-transitions-2026-11-07'
OUT = ROOT / 'output/hudi-pet-v6.3'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--partial', action='store_true')
    args = parser.parse_args()
    target = OUT / 'frames'
    target.mkdir(parents=True, exist_ok=True)
    m = json.loads((BASE / 'manifest.json').read_text(encoding='utf-8'))
    frames = set(m['frames'])
    for name in frames:
        dest = target / (name + '.png')
        if not dest.exists():
            os.link(BASE / 'frames' / dest.name, dest)
    # Restore the safe beginning of the wink so it starts in the common stance.
    m['clips']['wink'] = [{'frame': f'idle-{i:03}', 'seconds': 1/24} for i in range(105)]
    for i in range(42):
        name = f'idle-{i:03}'
        dest = target / (name + '.png')
        if not dest.exists():
            os.link(ROOT / 'output/hudi-pet-v6/frames' / dest.name, dest)
        frames.add(name)
    spec = importlib.util.spec_from_file_location('keyer', ROOT / '.animation-tools/prepare-green-sprites-v6.py')
    keyer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(keyer)
    scale = .46
    matrix = np.array([[scale, 0, 384/2-392*scale], [0, scale, 500-13-1120*scale]], np.float32)
    run = json.loads((SOURCE / 'generation-run.json').read_text(encoding='utf-8'))
    returns = {}
    review = Image.new('RGB', (6*192, len(run['plan'])*270), (80, 80, 80))
    draw = ImageDraw.Draw(review)
    metrics = []
    def premult(name):
        a = np.array(Image.open(target / (name+'.png')).resize((96, 125))).astype(float)/255
        a[:,:,:3] *= a[:,:,3,None]
        return a
    for row, item in enumerate(run['plan']):
        name = item['name']
        video = SOURCE / (name+'.mp4')
        if not video.exists() and args.partial:
            continue
        cap = cv2.VideoCapture(str(video))
        fps = cap.get(cv2.CAP_PROP_FPS)
        count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        steps = []
        for i in range(count):
            ok, bgr = cap.read()
            if not ok or bgr.shape[:2] not in [(1168, 784), (1172, 784)]:
                raise RuntimeError(f'Unexpected or missing video frame: {name}/{i}')
            frame = f'return-{name}-{i:03}'
            if (target / (frame+'.png')).exists():
                frames.add(frame)
                steps.append({'frame': frame, 'seconds': 1/fps})
                continue
            if bgr.shape[0] != 1168:
                # Kling rounds its encoded reference height to a multiple of four.
                # Normalize the entire shot once; do not warp individual body parts.
                bgr = cv2.resize(bgr, (784, 1168), interpolation=cv2.INTER_AREA)
            colors, alpha, _ = keyer.key_frame(bgr)
            rgba = keyer.output_frame(colors, alpha, matrix)
            Image.fromarray(rgba).save(target / (frame+'.png'), compress_level=2)
            frames.add(frame)
            steps.append({'frame': frame, 'seconds': 1/fps})
        cap.release()
        returns[name] = steps
        original_last = m['clips'][name][-1]['frame']
        review_ids = [original_last, steps[0]['frame'], steps[count//3]['frame'],
                      steps[2*count//3]['frame'], steps[-1]['frame'], 'neutral']
        for col, frame in enumerate(review_ids):
            im = Image.open(target / (frame+'.png')).resize((192, 250))
            x, y = col*192, row*270
            review.paste(im, (x, y), im)
            draw.text((x+4, y+251), frame, fill='white')
        metrics.append({'clip': name, 'returnFrames': count,
                        'joinMAE': float(np.mean(abs(premult(original_last)-premult(steps[0]['frame'])))),
                        'restMAE': float(np.mean(abs(premult('neutral')-premult(steps[-1]['frame']))))})
        m['clips'][name] += steps
        print(f'{name}: {count} natural return frames', flush=True)
    if len(returns) != len(run['plan']):
        if args.partial:
            print('Prepared available frames; final manifest waits for all videos.', flush=True)
            return
        raise RuntimeError('Not all transition videos are ready')
    # The original stretch begins midway through the squat/stretch source.
    # Reverse the real crouch-to-rest turn to enter that matching upright stance.
    m['clips']['stretch'] = list(reversed(returns['crouch'])) + m['clips']['stretch']
    m.update(version='6.3', frames=sorted(frames),
             playback='whole Higgsfield performances plus generated physical return motions',
             pocketHold={'sourceStart': 72, 'sourceEnd': 86, 'cycleSeconds': 3.2, 'extraCycles': [0, 2, 3, 4]})
    (OUT/'manifest.json').write_text(json.dumps(m, indent=2), encoding='utf-8')
    (OUT/'return-seams.json').write_text(json.dumps(metrics, indent=2), encoding='utf-8')
    review.save(OUT/'return-motions-review.jpg')
    suspects = []
    for frame in sorted(frames):
        if not frame.startswith('return-'):
            continue
        a = np.array(Image.open(target / (frame+'.png'))).astype(np.int16)
        mask = ((a[:,:,2] > 125) & (a[:,:,2]-a[:,:,0] > 20) &
                (a[:,:,1]-a[:,:,0] > 10) & (a[:,:,3] > 200)).astype(np.uint8)
        solid = np.where(a[:,:,3] > 220)
        y0, y1 = int(solid[0].min()), int(solid[0].max())
        mask[:y0] = 0
        mask[y0+int((y1-y0)*.50):] = 0
        n, _, stats, centers = cv2.connectedComponentsWithStats(mask)
        ids = [i for i in range(1,n) if stats[i,4] > 5 and stats[i,3] < stats[i,2]*7]
        ids.sort(key=lambda i: stats[i,4], reverse=True)
        if len(ids) < 3:
            continue
        eyes = sorted(ids[:2], key=lambda i: centers[i,0])
        middle_y = (centers[eyes[0],1]+centers[eyes[1],1])/2
        if any(centers[eyes[0],0] < centers[i,0] < centers[eyes[1],0] and
               5 < centers[i,1]-middle_y < 28 for i in ids[2:]):
            suspects.append(frame)
    (OUT/'return-face-audit.json').write_text(json.dumps({'possibleThirdLights': suspects}, indent=2), encoding='utf-8')
    print(f'Prepared {len(frames)} intact frames.', flush=True)
    if any(item['joinMAE'] > .015 or item['restMAE'] > .015 for item in metrics):
        raise RuntimeError('A generated transition does not match its source pose; review before packaging.')


if __name__ == '__main__':
    main()
