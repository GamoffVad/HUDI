"""Extract the requested green-screen sprite sheet and assemble a seamless loop.

The generated source is kept intact. All resampling/interpolation operates on
premultiplied RGBA to retain the translucent water rim without dark fringes.
"""
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "assets/water-orb/v1"
SIZE = 256
STEPS = 8
DURATION_MS = 4000


def premultiply(rgba):
    result = rgba.astype(np.float32) / 255
    result[:, :, :3] *= result[:, :, 3:4]
    return result


def straight(image):
    alpha = np.clip(image[:, :, 3:4], 0, 1)
    color = np.divide(image[:, :, :3], alpha, out=np.zeros_like(image[:, :, :3]), where=alpha > 1e-5)
    rgba = np.concatenate((np.clip(color, 0, 1), alpha), axis=2)
    return np.round(rgba * 255).astype(np.uint8)


def extract(tile):
    rgb = tile.astype(np.float32) / 255
    r, g, b = rgb.transpose(2, 0, 1)
    background = (np.maximum(r, b) < .13) & (g > .8)
    key = np.median(rgb[background], axis=0)
    difference = max(float(key[1] - max(key[0], key[2])), .5)
    alpha = np.clip(1 - (g - np.maximum(r, b)) / difference, 0, 1)
    alpha = np.clip((alpha - .06) / .94, 0, 1)
    # Confine the matte to the water silhouette, avoiding generated chroma noise
    # in the empty margins; its clear center remains transparent.
    seed = (alpha > .2).astype(np.uint8) * 255
    seed = cv2.morphologyEx(seed, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    contours, _ = cv2.findContours(seed, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        raise RuntimeError("No water contour in a sprite-sheet cell")
    contour = max(contours, key=cv2.contourArea)
    mask = np.zeros(seed.shape, np.uint8)
    cv2.drawContours(mask, [contour], -1, 255, cv2.FILLED)
    mask = cv2.dilate(mask, np.ones((5, 5), np.uint8))
    mask = cv2.GaussianBlur(mask, (5, 5), .8).astype(np.float32) / 255
    alpha *= mask
    alpha[alpha < .015] = 0
    premul = np.clip(rgb - (1 - alpha[:, :, None]) * key, 0, alpha[:, :, None])
    premul[:, :, 1] = np.minimum(premul[:, :, 1], np.maximum(premul[:, :, 0], premul[:, :, 2]))
    premul[alpha == 0] = 0
    result = np.dstack((premul, alpha)).astype(np.float32)
    moments = cv2.moments(contour)
    center = (moments['m10'] / moments['m00'], moments['m01'] / moments['m00'])
    return result, center


def composite(frame, background):
    a = frame[:, :, 3:4].astype(np.float32) / 255
    return np.round(frame[:, :, :3] * a + background * (1 - a)).astype(np.uint8)


def save_gif(frames, path):
    # A shared palette prevents highlights from changing color between frames.
    samples = np.vstack(frames[::STEPS])
    palette = Image.fromarray(samples).quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    indexed = [Image.fromarray(frame).quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    duration = [30, 30, 30, 40, 30, 30, 30, 30] * 16
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=duration, loop=0, disposal=2, optimize=False)


def main():
    source = np.array(Image.open(DEST / 'water-orb-green-sheet.png').convert('RGB'))
    h, w = source.shape[:2]
    keys = []
    for row in range(4):
        for column in range(4):
            tile = source[round(row * h / 4):round((row + 1) * h / 4), round(column * w / 4):round((column + 1) * w / 4)]
            tile = cv2.resize(tile, (320, 320), interpolation=cv2.INTER_CUBIC)
            frame, (cx, cy) = extract(tile)
            frame = cv2.warpAffine(frame, np.array([[1, 0, 160 - cx], [0, 1, 160 - cy]], np.float32), (320, 320), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT)
            # One shared crop/scale for every frame; never resize each tight bbox.
            frame = cv2.resize(frame[32:288, 32:288], (SIZE, SIZE), interpolation=cv2.INTER_AREA)
            keys.append(np.clip(frame, 0, 1))

    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    loop = []
    for i, first in enumerate(keys):
        second = keys[(i + 1) % len(keys)]
        gray_a = np.uint8(np.clip(first[:, :, 3] * 255, 0, 255))
        gray_b = np.uint8(np.clip(second[:, :, 3] * 255, 0, 255))
        forward = cv2.calcOpticalFlowFarneback(gray_a, gray_b, None, .5, 4, 31, 4, 7, 1.5, 0)
        backward = cv2.calcOpticalFlowFarneback(gray_b, gray_a, None, .5, 4, 31, 4, 7, 1.5, 0)
        for step in range(STEPS):
            t = step / STEPS
            a = cv2.remap(first, xx - t * forward[:, :, 0], yy - t * forward[:, :, 1], cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
            b = cv2.remap(second, xx - (1 - t) * backward[:, :, 0], yy - (1 - t) * backward[:, :, 1], cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
            loop.append(straight(a * (1 - t) + b * t))

    alpha_folder = DEST / 'frames-alpha'
    green_folder = DEST / 'frames-green'
    for folder in (alpha_folder, green_folder):
        folder.mkdir(parents=True, exist_ok=True)
    green = np.full((SIZE, SIZE, 3), (0, 255, 0), np.float32)
    dark = np.full((SIZE, SIZE, 3), (24, 28, 34), np.float32)
    previews, greens = [], []
    for i, frame in enumerate(loop):
        Image.fromarray(frame).save(alpha_folder / f'frame-{i:03}.png')
        on_green = composite(frame, green)
        Image.fromarray(on_green).save(green_folder / f'frame-{i:03}.png')
        greens.append(on_green)
        previews.append(composite(frame, dark))
    save_gif(greens, DEST / 'water-orb-loop-green.gif')
    save_gif(previews, DEST / 'water-orb-preview.gif')
    images = [Image.fromarray(frame) for frame in loop]
    images[0].save(DEST / 'water-orb-loop.apng', format='PNG', save_all=True, append_images=images[1:], duration=DURATION_MS / len(images), loop=0, disposal=0, blend=0)
    sheet = Image.new('RGBA', (SIZE * 4, SIZE * 4))
    for i in range(16):
        sheet.paste(images[i * STEPS], ((i % 4) * SIZE, (i // 4) * SIZE))
    sheet.save(DEST / 'water-orb-alpha-sheet.png')

    # Show the same genuine alpha against three contrasting backgrounds.
    comparison = Image.new('RGB', (SIZE * 3, SIZE + 28))
    backgrounds = [dark, np.full_like(dark, (235, 236, 238)), np.where((((xx // 24 + yy // 24) % 2)[:, :, None] == 0), np.array([75, 94, 113]), np.array([188, 199, 211])).astype(np.float32)]
    draw = ImageDraw.Draw(comparison)
    for i, background in enumerate(backgrounds):
        comparison.paste(Image.fromarray(composite(loop[0], background)), (i * SIZE, 0))
        draw.text((i * SIZE + 10, SIZE + 6), ['Dark background', 'Light background', 'Alpha check'][i], fill='white')
    comparison.save(DEST / 'alpha-check.png')

    premult = [premultiply(frame) for frame in loop]
    steps = [float(np.abs(premult[(i + 1) % len(loop)] - premult[i]).mean()) for i in range(len(loop))]
    alpha_stack = np.stack([frame[:, :, 3] for frame in loop])
    report = {
        'generated_keyframes': 16, 'frames': len(loop), 'frame_size': [SIZE, SIZE], 'duration_ms': DURATION_MS,
        'loop': 'forward optical-flow interpolation, including frame 15 back to frame 0',
        'gif': 'green-screen loop and opaque dark-background preview; full partial alpha is in APNG and PNG frames',
        'alpha_zero_pct': float((alpha_stack == 0).mean() * 100),
        'partial_alpha_pct': float(((alpha_stack > 0) & (alpha_stack < 255)).mean() * 100),
        'center_alpha_max': int(alpha_stack[:, SIZE//2-12:SIZE//2+12, SIZE//2-12:SIZE//2+12].max()),
        'edge_alpha_max': int(max(alpha_stack[:, :4].max(), alpha_stack[:, -4:].max(), alpha_stack[:, :, :4].max(), alpha_stack[:, :, -4:].max())),
        'seam_mean_difference': steps[-1], 'largest_adjacent_difference': max(steps),
        'seam_is_normal_step': bool(steps[-1] <= np.percentile(steps[:-1], 95) * 1.5),
    }
    report['success'] = report['edge_alpha_max'] == 0 and report['center_alpha_max'] < 45 and report['seam_is_normal_step'] and report['partial_alpha_pct'] > 10
    (DEST / 'manifest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if not report['success']:
        raise RuntimeError('Water animation validation failed')


if __name__ == '__main__':
    main()
