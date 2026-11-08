"""Build a calm water loop with a locked silhouette and continuous inner flow.

One generated base image replaces the unrelated keyframes used by v1. Only
internal texture coordinates move; the circular rim never changes geometry.
"""
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'assets/water-orb/v2'
VERSION = 2
SIZE = 256
RADIUS = 105.0
AMPLITUDE = 3.0
FPS = 50
SECONDS = 6
COUNT = FPS * SECONDS


def smoothstep(low, high, value):
    t = np.clip((value - low) / (high - low), 0, 1)
    return t * t * (3 - 2 * t)


def straight(premult):
    a = np.clip(premult[:, :, 3:4], 0, 1)
    rgb = np.divide(premult[:, :, :3], a, out=np.zeros_like(premult[:, :, :3]), where=a > 1e-6)
    return np.round(np.concatenate((np.clip(rgb, 0, 1), a), axis=2) * 255).astype(np.uint8)


def composite(frame, background):
    a = frame[:, :, 3:4].astype(np.float32) / 255
    return np.round(frame[:, :, :3] * a + np.asarray(background) * (1 - a)).astype(np.uint8)


def extract_base():
    rgb = np.asarray(Image.open(DEST / 'water-orb-green.png').convert('RGB'), dtype=np.float32) / 255
    margins = np.concatenate((rgb[:40].reshape(-1, 3), rgb[-40:].reshape(-1, 3)))
    key = np.median(margins, axis=0)
    r, g, b = rgb.transpose(2, 0, 1)
    contrast = float(key[1] - max(key[0], key[2]))
    alpha = np.clip(1 - (g - np.maximum(r, b)) / contrast, 0, 1)
    alpha = np.clip((alpha - .012) / .988, 0, 1)
    seed = np.uint8(alpha > .12) * 255
    seed = cv2.morphologyEx(seed, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    contours, _ = cv2.findContours(seed, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contour = max(contours, key=cv2.contourArea)
    (cx, cy), (width, height), angle = cv2.fitEllipse(contour)
    radius = max(width, height) / 2
    yy, xx = np.mgrid[:rgb.shape[0], :rgb.shape[1]].astype(np.float32)
    # Keep the generated, almost circular rim. Limit chroma noise to its mask.
    mask = np.zeros(seed.shape, np.uint8)
    cv2.drawContours(mask, [contour], -1, 255, cv2.FILLED)
    mask = cv2.dilate(mask, np.ones((5, 5), np.uint8))
    mask = cv2.GaussianBlur(mask, (5, 5), .8).astype(np.float32) / 255
    alpha *= mask
    alpha[alpha < .006] = 0
    color = np.clip(rgb - (1 - alpha[:, :, None]) * key, 0, alpha[:, :, None])
    color[:, :, 1] = np.minimum(color[:, :, 1], np.maximum(color[:, :, 0], color[:, :, 2]))
    color[alpha == 0] = 0
    premult = np.dstack((color, alpha)).astype(np.float32)
    scale = RADIUS / radius
    matrix = np.array([[scale, 0, SIZE / 2 - cx * scale], [0, scale, SIZE / 2 - cy * scale]], np.float32)
    base = cv2.warpAffine(premult, matrix, (SIZE, SIZE), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT)
    base = np.clip(base, 0, 1)
    return base, {'source_circle_axes': [float(width), float(height)], 'source_axis_ratio': float(max(width, height) / min(width, height))}


def save_gif(frames, path):
    # Uniform 20 ms cadence: no alternating frame lengths or palette flicker.
    samples = np.vstack(frames[::25])
    palette = Image.fromarray(samples).quantize(colors=256, method=Image.Quantize.MEDIANCUT)
    indexed = [Image.fromarray(frame).quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
    indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=20, loop=0, disposal=2, optimize=False)


def main():
    base, shape = extract_base()
    yy, xx = np.mgrid[:SIZE, :SIZE].astype(np.float32)
    u, v = (xx - SIZE / 2) / RADIUS, (yy - SIZE / 2) / RADIUS
    radial = np.hypot(u, v)
    envelope = 1 - smoothstep(.72, .93, radial)
    # Keep the main studio reflection still too; movement belongs to the water.
    reflection_lock = np.exp(-(((u + .55) / .28) ** 2 + ((v + .55) / .35) ** 2))
    envelope *= 1 - .90 * reflection_lock

    def render(phase):
        # Periodic analytic motion: position AND velocity agree at the seam.
        dx = AMPLITUDE * envelope * (.70 * np.sin(phase + 1.8 * v + .8 * u) + .30 * np.sin(2 * phase - 1.1 * u + 1.6 * v))
        dy = AMPLITUDE * envelope * (.65 * np.cos(phase + 1.4 * u - .9 * v) + .35 * np.cos(2 * phase + 1.3 * v + .8 * u))
        water = cv2.remap(base, xx + dx, yy + dy, cv2.INTER_CUBIC, borderMode=cv2.BORDER_CONSTANT)
        # Assign the rim exactly, so resampling can never move its pixels.
        water[radial >= .93] = base[radial >= .93]
        return straight(water)

    frames = [render(2 * np.pi * index / COUNT) for index in range(COUNT)]
    alpha_folder = DEST / 'frames-alpha'
    alpha_folder.mkdir(parents=True, exist_ok=True)
    for index, frame in enumerate(frames):
        Image.fromarray(frame).save(alpha_folder / f'frame-{index:03}.png')
    Image.fromarray(straight(base)).save(DEST / 'water-orb-base-alpha.png')

    dark = (24, 28, 34)
    previews = [composite(frame, dark) for frame in frames]
    green = [composite(frame, (0, 255, 0)) for frame in frames]
    save_gif(previews, DEST / 'water-orb-preview.gif')
    save_gif(green, DEST / 'water-orb-loop-green.gif')
    images = [Image.fromarray(frame) for frame in frames]
    images[0].save(DEST / 'water-orb-loop.apng', format='PNG', save_all=True, append_images=images[1:], duration=20, loop=0, disposal=0, blend=0)

    checker = np.where((((xx // 24 + yy // 24) % 2)[:, :, None] == 0), np.array([75, 94, 113]), np.array([188, 199, 211]))
    check = Image.new('RGB', (SIZE * 3, SIZE + 24), dark)
    draw = ImageDraw.Draw(check)
    for index, background in enumerate((dark, (235, 236, 238), checker)):
        check.paste(Image.fromarray(composite(frames[0], background)), (SIZE * index, 0))
        draw.text((SIZE * index + 8, SIZE + 5), ['Dark background', 'Light background', 'Alpha check'][index], fill='white')
    check.save(DEST / 'alpha-check.png')
    contact = Image.new('RGB', (SIZE * 4, SIZE * 2), dark)
    for index in range(8):
        frame = previews[index * COUNT // 8]
        contact.paste(Image.fromarray(frame), ((index % 4) * SIZE, (index // 4) * SIZE))
    contact.save(DEST / 'motion-contact.png')

    alpha_stack = np.stack([frame[:, :, 3] for frame in frames])
    ring = radial >= .93
    border_delta = max(int(np.abs(frame[ring].astype(int) - frames[0][ring].astype(int)).max()) for frame in frames)
    rgb_frames = np.stack(previews).astype(np.float32) / 255
    deltas = np.abs(np.roll(rgb_frames, -1, axis=0) - rgb_frames).mean(axis=(1, 2, 3))
    closure = int(np.abs(render(0).astype(int) - render(2 * np.pi).astype(int)).max())
    report = {
        'version': VERSION, 'source': 'one generated base; continuous periodic internal texture displacement',
        'reference': 'pic/a7ce4524a4aaa4934e9289dba304499e.mp4',
        'frames': COUNT, 'fps': FPS, 'duration_ms': SECONDS * 1000, 'frame_size': [SIZE, SIZE],
        'maximum_internal_displacement_per_axis_px': AMPLITUDE,
        'outer_rim_pixel_change': border_delta, 'exact_loop_closure_max_channel_delta': closure,
        'max_adjacent_mean_rgb_delta': float(deltas.max()), 'seam_mean_rgb_delta': float(deltas[-1]),
        'partial_alpha_pct': float(((alpha_stack > 0) & (alpha_stack < 255)).mean() * 100),
        'edge_alpha_max': int(max(alpha_stack[:, :4].max(), alpha_stack[:, -4:].max(), alpha_stack[:, :, :4].max(), alpha_stack[:, :, -4:].max())),
        **shape,
    }
    report['success'] = border_delta == 0 and closure <= 1 and report['edge_alpha_max'] == 0 and shape['source_axis_ratio'] < 1.03 and float(deltas.max()) < .002
    (DEST / 'manifest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    if not report['success']:
        raise RuntimeError(f'Water v{VERSION} validation failed')


if __name__ == '__main__':
    main()
