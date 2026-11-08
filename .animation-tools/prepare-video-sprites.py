"""Extract whole-video frames and create offline transparent desktop-pet sprites.

BiRefNet General Lite ONNX model distributed by rembg; model inference follows
rembg's BiRefNet preprocessing. No image synthesis or body-part deformation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request

import cv2
import numpy as np
import onnxruntime as ort
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'output/hudi-higgsfield-2026-11-07'
OUTPUT = ROOT / 'output/hudi-pet-v5'
MODEL = ROOT / '.animation-tools/models/birefnet-general-lite.onnx'
MODEL_URL = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx'
MODEL_MD5 = '4fab47adc4ff364be1713e97b7e66334'
MODEL_KIND = 'birefnet'
W, H = 384, 500
CLIPS = {
    'idle': '01_idle_breathe_blink_wink',
    'think': '02_think',
    'crouch': '03_squat',
    'stretch': '07_hands_behind_head_refined',
    'angry': '05_surprise_angry',
    'combat': '06_combat_guard',
}

def session():
    MODEL.parent.mkdir(parents=True, exist_ok=True)
    if not MODEL.exists():
        temporary = MODEL.with_suffix('.download')
        print('Downloading segmentation model: ' + MODEL.name, flush=True)
        with urllib.request.urlopen(MODEL_URL) as response, temporary.open('wb') as output:
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
        if hashlib.md5(temporary.read_bytes()).hexdigest() != MODEL_MD5:
            raise RuntimeError('Model checksum mismatch')
        temporary.replace(MODEL)
    options = ort.SessionOptions()
    options.enable_mem_pattern = False
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    options.intra_op_num_threads = 4
    providers = ['DmlExecutionProvider', 'CPUExecutionProvider'] if 'DmlExecutionProvider' in ort.get_available_providers() else ['CPUExecutionProvider']
    result = ort.InferenceSession(str(MODEL), sess_options=options, providers=providers)
    print('Model ready: ' + ', '.join(result.get_providers()), flush=True)
    return result

def mask_for(rgb, model):
    size = 320 if MODEL_KIND == 'u2netp' else 1024
    sample = np.asarray(Image.fromarray(rgb).resize((size, size), Image.Resampling.LANCZOS), dtype=np.float32)
    sample /= max(float(sample.max()), 1)
    deviation = [1, 1, 1] if MODEL_KIND == 'isnet' else [.229, .224, .225]
    sample = (sample - np.array([.485, .456, .406], np.float32)) / np.array(deviation, np.float32)
    logits = model.run(None, {model.get_inputs()[0].name: sample.transpose(2, 0, 1)[None]})[0][0, 0]
    mask = 1 / (1 + np.exp(-np.clip(logits, -50, 50))) if MODEL_KIND == 'birefnet' else logits
    mask = (mask - mask.min()) / max(float(mask.max() - mask.min()), 1e-6)
    mask = cv2.resize(mask, (rgb.shape[1], rgb.shape[0]), interpolation=cv2.INTER_LINEAR)
    # Remove isolated floor shadows and specks, retaining the character component.
    count, labels, stats, _ = cv2.connectedComponentsWithStats((mask > .45).astype(np.uint8), 8)
    if count < 2:
        raise RuntimeError('Empty segmentation')
    main = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    support = cv2.dilate((labels == main).astype(np.uint8), np.ones((5, 5), np.uint8))
    mask *= support
    mask = np.clip((mask - .10) / .80, 0, 1)
    return mask * mask * (3 - 2 * mask)

def transform_from(mask):
    ys, xs = np.where(mask > .5)
    scale = 426 / (ys.max() - ys.min() + 1)
    floor = ys.max()
    feet = xs[ys >= floor - 70]
    center = (float(feet.min()) + float(feet.max())) / 2
    return np.array([[scale, 0, W / 2 - center * scale], [0, scale, H - 15 - floor * scale]], dtype=np.float32)

def cutout(rgb, mask, matrix):
    # Resize premultiplied colors and coverage together to avoid a gray fringe.
    premult = rgb.astype(np.float32) * mask[:, :, None]
    alpha = cv2.warpAffine(mask, matrix, (W, H), flags=cv2.INTER_LINEAR)
    colors = cv2.warpAffine(premult, matrix, (W, H), flags=cv2.INTER_LINEAR)
    straight = np.divide(colors, alpha[:, :, None], out=np.zeros_like(colors), where=alpha[:, :, None] > 1e-5)
    return np.dstack([np.clip(straight, 0, 255).astype(np.uint8), np.clip(alpha * 255, 0, 255).astype(np.uint8)])

def frame_at(video, index):
    reader = cv2.VideoCapture(str(video))
    reader.set(cv2.CAP_PROP_POS_FRAMES, index)
    ok, frame = reader.read()
    reader.release()
    if not ok:
        raise RuntimeError(f'Cannot read frame {index} of {video}')
    return cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

def main():
    global MODEL, MODEL_URL, MODEL_MD5, MODEL_KIND
    parser = argparse.ArgumentParser()
    parser.add_argument('--sample', action='store_true')
    parser.add_argument('--model', choices=['birefnet', 'isnet', 'u2netp'], default='isnet')
    args = parser.parse_args()
    MODEL_KIND = args.model
    if args.model != 'birefnet':
        filename, MODEL_MD5 = ('isnet-general-use.onnx', 'fc16ebd8b0c10d971d3513d564d01e29') if args.model == 'isnet' else ('u2netp.onnx', '8e83ca70e441ab06c318d82300c84806')
        MODEL = MODEL.parent / filename
        MODEL_URL = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/' + filename
    OUTPUT.mkdir(parents=True, exist_ok=True)
    model = session()
    neutral = frame_at(SOURCE / (CLIPS['idle'] + '.mp4'), 0)
    neutral_mask = mask_for(neutral, model)
    matrix = transform_from(neutral_mask)
    Image.fromarray(cutout(neutral, neutral_mask, matrix)).save(OUTPUT / 'neutral-sample.png')
    if args.sample:
        previews = []
        for key, frame in [('idle', 0), ('think', 65), ('crouch', 70), ('stretch', 65), ('angry', 75), ('combat', 65)]:
            t = time.perf_counter()
            rgb = frame_at(SOURCE / (CLIPS[key] + '.mp4'), frame)
            rgba = Image.fromarray(cutout(rgb, mask_for(rgb, model), matrix))
            rgba.save(OUTPUT / (key + '-sample.png'))
            back = Image.new('RGBA', (W, H), (30, 34, 42, 255))
            back.alpha_composite(rgba)
            previews.append(back.convert('RGB'))
            print(f'Sample {key}: {time.perf_counter() - t:.2f}s', flush=True)
        sheet = Image.new('RGB', (W * 3, H * 2), (30, 34, 42))
        for i, preview in enumerate(previews):
            sheet.paste(preview, ((i % 3) * W, (i // 3) * H))
        sheet.save(OUTPUT / ('segmentation-review-' + args.model + '.jpg'), quality=93)
        return
    frames_dir = OUTPUT / 'frames'
    frames_dir.mkdir(exist_ok=True)
    masks_dir = OUTPUT / ('masks-' + args.model)
    masks_dir.mkdir(exist_ok=True)
    metrics, clips, all_ids, geometry = {}, {}, ['neutral'], {}
    for key, source in CLIPS.items():
        reader = cv2.VideoCapture(str(SOURCE / (source + '.mp4')))
        fps = reader.get(cv2.CAP_PROP_FPS)
        total = int(reader.get(cv2.CAP_PROP_FRAME_COUNT))
        records = []
        started = time.perf_counter()
        for index in range(total):
            ok, bgr = reader.read()
            if not ok:
                raise RuntimeError(f'Missing frame {key} {index}')
            target = masks_dir / f'{key}-{index:03d}.png'
            if target.exists():
                mask = np.asarray(Image.open(target), dtype=np.float32) / 255
            else:
                rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
                mask = mask_for(rgb, model)
                Image.fromarray(np.uint8(mask * 255)).save(target, compress_level=3)
            ys, xs = np.where(mask > .5)
            floor = int(ys.max())
            feet = xs[ys >= floor - 70]
            records.append({'x0': int(xs.min()), 'x1': int(xs.max()), 'y0': int(ys.min()),
                            'y1': floor, 'floor': floor, 'center': (int(feet.min()) + int(feet.max())) / 2})
            if index % 48 == 0:
                print(f'Masks {key}: {index + 1}/{total}, {time.perf_counter() - started:.1f}s', flush=True)
        reader.release()
        # Filter segmentation noise in the anchor, without deforming the generated body.
        for index, record in enumerate(records):
            nearby = records[max(0, index - 2):min(total, index + 3)]
            record['anchorX'] = float(np.median([r['center'] for r in nearby]))
            record['anchorY'] = float(np.median([r['floor'] for r in nearby]))
        geometry[key] = records
        metrics[key] = {'frames': total, 'fps': fps, 'seconds': total / fps, 'source': source + '.mp4'}
    all_records = [record for records in geometry.values() for record in records]
    max_side = max(max(r['anchorX'] - r['x0'], r['x1'] - r['anchorX']) for r in all_records)
    max_up = max(r['anchorY'] - r['y0'] for r in all_records)
    max_down = max(r['y1'] - r['anchorY'] for r in all_records)
    scale = min((W / 2 - 15) / max_side, (H - 45) / (max_up + max_down))
    baseline = H - 15 - max_down * scale
    for key, source in CLIPS.items():
        reader = cv2.VideoCapture(str(SOURCE / (source + '.mp4')))
        steps = []
        for index, record in enumerate(geometry[key]):
            ok, bgr = reader.read()
            if not ok:
                raise RuntimeError(f'Export failed: {key} {index}')
            rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
            mask = np.asarray(Image.open(masks_dir / f'{key}-{index:03d}.png'), dtype=np.float32) / 255
            matrix = np.array([[scale, 0, W / 2 - record['anchorX'] * scale],
                               [0, scale, baseline - record['anchorY'] * scale]], np.float32)
            rgba = cutout(rgb, mask, matrix)
            frame_id = f'{key}-{index:03d}'
            Image.fromarray(rgba).save(frames_dir / (frame_id + '.png'), compress_level=3)
            if key == 'idle' and index == 0:
                Image.fromarray(rgba).save(frames_dir / 'neutral.png')
            steps.append({'frame': frame_id, 'seconds': 1 / metrics[key]['fps']})
            all_ids.append(frame_id)
        reader.release()
        clips[key] = steps
        print('Exported ' + key, flush=True)
    clips['wink'] = list(clips['idle'])
    breathing = clips['idle'][:30]
    clips['idle'] = breathing + list(reversed(breathing[1:-1]))
    manifest = {'version': 5, 'frameSize': [W, H], 'rendering': 'segmented-higgsfield-video', 'alphaPrepared': True,
                'neutral': 'neutral', 'left': ['neutral'], 'right': ['neutral'], 'up': ['neutral'], 'down': ['neutral'],
                'clips': clips, 'frames': all_ids, 'uniformScale': scale, 'baseline': baseline,
                'sourceMetrics': metrics, 'segmentationModel': MODEL_KIND}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    (OUTPUT / 'segmentation-geometry.json').write_text(json.dumps(geometry, indent=2), encoding='utf-8')
    print('Sprites ready: ' + str(len(all_ids)), flush=True)

if __name__ == '__main__':
    main()
