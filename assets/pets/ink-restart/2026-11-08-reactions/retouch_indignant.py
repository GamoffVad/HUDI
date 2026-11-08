"""Local video retouch of the generated mouth; preserve the original source."""
from pathlib import Path
import cv2
import numpy as np
import subprocess
import imageio_ffmpeg
import json
from PIL import Image, ImageDraw

P = Path(__file__).resolve().parent
cap = cv2.VideoCapture(str(P / 'indignant-v2.mp4'))
fps = cap.get(cv2.CAP_PROP_FPS)
w, h = int(cap.get(3)), int(cap.get(4))
out = P / 'indignant-mouthless'
out.mkdir(exist_ok=True)
# Mouth centres, measured in the source video; interpolation follows head motion.
anchors = np.array([
    [39, 468, 443], [42, 477, 446], [45, 495, 444],
    [48, 503, 450], [50, 505, 449], [53, 490, 455],
    [55, 480, 460], [57, 495, 457], [60, 511, 455],
    [63, 500, 454], [65, 491, 456], [68, 492, 457],
    [70, 492, 457], [73, 500, 455], [76, 503, 455],
    [80, 500, 455], [83, 491, 455], [86, 477, 454],
    [89, 456, 450], [90, 447, 449], [93, 444, 453],
    [95, 440, 455], [100, 440, 456], [105, 440, 456],
    [110, 440, 456], [115, 444, 454], [120, 450, 449]
])
ff = subprocess.Popen([
    imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', f'{w}x{h}', '-r', str(fps),
    '-i', '-', '-an', '-c:v', 'libx264', '-crf', '14', '-preset', 'slow',
    '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
    str(P / 'indignant-mouthless.mp4')
], stdin=subprocess.PIPE)
tiles, report, i = [], [], 0
while True:
    ok, bgr = cap.read()
    if not ok:
        break
    result = bgr.copy()
    if 39 <= i <= 120:
        x = round(float(np.interp(i, anchors[:, 0], anchors[:, 1])))
        y = round(float(np.interp(i, anchors[:, 0], anchors[:, 2])))
        mask = np.zeros((h, w), np.uint8)
        angle = -15 if 41 <= i <= 84 else 0
        cv2.ellipse(mask, (x, y), (31, 11), angle, 0, 360, 255, -1)
        filled = cv2.inpaint(bgr, mask, 5, cv2.INPAINT_NS)
        # Restrict the replacement to the small mouth region, with a soft inner join.
        mix = cv2.GaussianBlur(mask.astype(np.float32) / 255, (9, 9), 2)
        mix *= min(1, (i - 38) / 3, (121 - i) / 4)
        result = np.uint8(np.clip(bgr * (1 - mix[:, :, None]) + filled * mix[:, :, None], 0, 255))
        changed = np.any(result != bgr, axis=2)
        yy, xx = np.where(changed)
        report.append({'frame': i, 'centre': [x, y], 'changed_pixels': len(xx), 'bounds': [int(xx.min()), int(yy.min()), int(xx.max()), int(yy.max())]})
    ff.stdin.write(result.tobytes())
    im = Image.fromarray(cv2.cvtColor(result, cv2.COLOR_BGR2RGB)).crop((300, 230, 680, 485)).resize((228, 153))
    ImageDraw.Draw(im).text((4, 4), str(i), fill='red')
    tiles.append(im)
    i += 1
ff.stdin.close()
assert ff.wait() == 0
cap.release()
for start in range(0, len(tiles), 30):
    sheet = Image.new('RGB', (1368, 765))
    for j, im in enumerate(tiles[start:start + 30]):
        sheet.paste(im, ((j % 6) * 228, (j // 6) * 153))
    sheet.save(out / f'faces-{start // 30}.jpg', quality=95)
(out / 'retouch.json').write_text(json.dumps({'frames': i, 'source': 'indignant-v2.mp4', 'method': 'Small tracked lower-face mask with spatial inpainting, no regeneration', 'edits': report}, indent=2), encoding='utf8')
print(f'Wrote {i} frames; retouched {len(report)} frames')
