"""Chroma-key the approved Higgsfield videos; retain real contact shadows and mirror.

No diffusion, body-part animation or new frames. Keys and rescales existing pixels.
"""
import argparse
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'output/hudi-higgsfield-v2-green-2026-11-07'
OUTPUT = ROOT / 'output/hudi-pet-v6'
W, H = 384, 500
SOURCES = {
    'idle': '01_idle_blink_wink', 'think': '02_think_left',
    'hover': '03_hover_two_eyes', 'squatstretch': '04_squat_stretch',
    'sad': '05_sad_eyes', 'surprise': '06_surprise_eyes',
    'angry': '07_angry_lightning', 'lookaround': '08_natural_left_right',
    'combat': '09_combat_guard', 'dance': '10_dance_two_eyes',
}

def key_frame(bgr, plate_override=None):
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB).astype(np.float32)
    r, g, b = cv2.split(rgb)
    excess = g - np.maximum(r, b)
    # The generated plate contains small red/blue values and varies by clip.
    # Estimate its full RGB, not just G: treating it as pure green leaves a yellow veil.
    samples = np.concatenate([rgb[:, :35], rgb[:, -35:]], axis=1)
    plate = np.median(samples, axis=1)
    plate = cv2.GaussianBlur(plate[:, None, :], (1, 31), 0)
    if plate_override is not None:
        plate = np.asarray(plate_override,dtype=np.float32)
    plate_g = np.maximum(plate[:, :, 1], 180)
    plate_excess = np.maximum(plate_g - np.maximum(plate[:, :, 0], plate[:, :, 2]), 30)
    green = (excess > g * (plate_excess / plate_g) * .65) & (g > 15)
    alpha = np.clip(1 - np.maximum(excess, 0) / plate_excess, 0, 1)
    opaque = ((~green) & (alpha > .75)).astype(np.uint8)
    n, labels, stats, _ = cv2.connectedComponentsWithStats(opaque, 8)
    if n < 2:
        raise RuntimeError('No character detected')
    main = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
    ys, xs = np.where(labels == main)
    floor = int(ys.max())
    feet = xs[ys > floor - 65]
    cx = (float(feet.min()) + float(feet.max())) / 2
    rx = max((float(feet.max()) - float(feet.min())) / 2 + 85, 100)
    yy, xx = np.ogrid[:rgb.shape[0], :rgb.shape[1]]
    # Shadow is original green-floor darkening, represented as translucent black.
    ellipse = ((xx - cx) / rx) ** 2 + ((yy - floor) / 38) ** 2
    support = np.clip((1 - ellipse) * 4, 0, 1)
    shadow = np.clip((1 - g / plate_g - .018) / .982, 0, .75) * support
    presence = np.ones_like(alpha)
    non_plate_light = np.maximum(r - plate[:, :, 0] * g / plate_g,
                                 b - plate[:, :, 2] * g / plate_g)
    presence[green] = np.clip((non_plate_light[green] - 4) / 20, 0, 1)
    foreground_alpha = alpha * presence
    foreground_alpha[foreground_alpha < .008] = 0
    # Unmix the green screen in premultiplied color space. Gray fabric stays gray.
    premult = rgb - (1 - alpha[:, :, None]) * plate
    premult *= presence[:, :, None]
    premult = np.clip(premult, 0, 255 * foreground_alpha[:, :, None])
    alpha = foreground_alpha + shadow * green * (1 - foreground_alpha)
    return premult, alpha, floor

def frame_at(name, index):
    cap = cv2.VideoCapture(str(SOURCE / (SOURCES[name] + '.mp4')))
    cap.set(cv2.CAP_PROP_POS_FRAMES, index)
    ok, frame = cap.read()
    cap.release()
    if not ok:
        raise RuntimeError(f'Missing {name} frame {index}')
    return frame

def output_frame(colors, alpha, matrix):
    colors = cv2.warpAffine(colors, matrix, (W, H), flags=cv2.INTER_AREA)
    alpha = cv2.warpAffine(alpha, matrix, (W, H), flags=cv2.INTER_AREA)
    straight = np.divide(colors, alpha[:, :, None], out=np.zeros_like(colors), where=alpha[:, :, None] > 1e-5)
    rgba = np.dstack([np.clip(straight, 0, 255), np.clip(alpha * 255, 0, 255)]).astype(np.uint8)
    return np.ascontiguousarray(rgba[:, ::-1])

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--sample', action='store_true')
    args = parser.parse_args()
    OUTPUT.mkdir(exist_ok=True, parents=True)
    # One fixed transform for every clip preserves generated left/right movement.
    scale = .46
    matrix = np.array([[scale, 0, W / 2 - 392 * scale], [0, scale, H - 13 - 1120 * scale]], np.float32)
    if args.sample:
        for name, index in [('idle', 0), ('hover', 96), ('squatstretch', 38), ('angry', 40), ('lookaround', 154), ('dance', 115)]:
            c, a, _ = key_frame(frame_at(name, index))
            cutout = Image.fromarray(output_frame(c, a, matrix))
            cutout.save(OUTPUT / f'{name}-sample.png')
            for label, color in [('dark', (26, 29, 35, 255)), ('light', (235, 235, 235, 255))]:
                canvas = Image.new('RGBA', (W, H), color)
                canvas.alpha_composite(cutout)
                canvas.convert('RGB').save(OUTPUT / f'{name}-{label}-review.jpg', quality=95)
        print('Samples ready', flush=True)
        return
    frames_dir = OUTPUT / 'frames'
    frames_dir.mkdir(exist_ok=True)
    clips, frame_ids, metrics = {}, ['neutral'], {}
    for name, filename in SOURCES.items():
        cap = cv2.VideoCapture(str(SOURCE / (filename + '.mp4')))
        fps = cap.get(cv2.CAP_PROP_FPS)
        total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        steps = []
        for index in range(total):
            ok, bgr = cap.read()
            if not ok:
                raise RuntimeError(f'Cannot decode {name} {index}')
            colors, alpha, floor = key_frame(bgr)
            rgba = output_frame(colors, alpha, matrix)
            frame_id = f'{name}-{index:03d}'
            Image.fromarray(rgba).save(frames_dir / (frame_id + '.png'), compress_level=2)
            frame_ids.append(frame_id)
            steps.append({'frame': frame_id, 'seconds': 1 / fps})
            if name == 'idle' and index == 0:
                Image.fromarray(rgba).save(frames_dir / 'neutral.png')
            if index % 96 == 0:
                print(f'{name}: {index + 1}/{total}', flush=True)
        cap.release()
        clips[name] = steps
        metrics[name] = {'source': filename + '.mp4', 'frames': total, 'fps': fps}
    clips['wink'] = clips['idle'][42:105]
    clips['happy'] = clips['idle'][108:174]
    clips['crouch'] = clips['squatstretch'][:96]
    clips['stretch'] = clips['squatstretch'][96:]
    manifest = {'version': 6, 'frameSize': [W, H], 'alphaPrepared': True,
        'rendering': 'chroma-keyed-higgsfield-video', 'horizontalMirrored': True,
        'contactShadow': 'original floor darkening preserved as black alpha',
        'neutral': 'neutral', 'left': ['neutral'], 'right': ['neutral'], 'up': ['neutral'], 'down': ['neutral'],
        'clips': clips, 'frames': frame_ids, 'uniformScale': scale, 'baseline': H - 30,
        'sourceMetrics': metrics}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(f'Prepared {len(frame_ids)} mirrored transparent frames.', flush=True)

if __name__ == '__main__':
    main()
