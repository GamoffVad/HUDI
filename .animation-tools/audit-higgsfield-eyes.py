"""Flag face-light artifacts in decoded source frames; never alters animation."""
import json
import sys
from pathlib import Path
import cv2
import numpy as np

root = Path(sys.argv[1]).resolve()
run = json.loads((root / 'generation-run.json').read_text(encoding='utf-8-sig'))
reports = []
for clip in run['clips']:
    source = root / (clip['name'] + '.mp4')
    if not source.exists():
        continue
    cap = cv2.VideoCapture(str(source))
    fps = cap.get(cv2.CAP_PROP_FPS)
    counts, gaps, suspicious = [], [], []
    frame_index = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        h, w = frame.shape[:2]
        # Restrict to the screen-height region, above the hoodie drawstrings.
        y0, y1, x0, x1 = int(h * .20), int(h * .47), int(w * .10), int(w * .90)
        roi8 = frame[y0:y1, x0:x1]
        roi = roi8.astype(np.int16)
        b, g, r = cv2.split(roi)
        # The black visor separates the eye lights from blue strings below it.
        dark = ((b < 115) & (g < 115) & (r < 115)).astype(np.uint8)
        dark = cv2.morphologyEx(dark, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
        contours, _ = cv2.findContours(dark, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        screen = np.zeros_like(dark)
        if contours:
            cv2.drawContours(screen, [max(contours, key=cv2.contourArea)], -1, 1, cv2.FILLED)
            screen = cv2.erode(screen, np.ones((9, 9), np.uint8))
        mask = ((b > r + 25) & (g > r + 15) & (b >= g - 10) & (g > 120)).astype(np.uint8)
        mask &= screen
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
        n, labels, stats, centers = cv2.connectedComponentsWithStats(mask, 8)
        parts = [(stats[i], centers[i]) for i in range(1, n) if stats[i, cv2.CC_STAT_AREA] >= 24]
        counts.append(len(parts))
        if len(parts) > 2:
            suspicious.append(frame_index)
        if len(parts) == 2:
            parts.sort(key=lambda p: p[1][0])
            gaps.append({'frame': frame_index, 'seconds': round(frame_index / fps, 3),
                         'center_distance_px': round(float(parts[1][1][0] - parts[0][1][0]), 2)})
        frame_index += 1
    cap.release()
    distribution = {str(n): counts.count(n) for n in sorted(set(counts))}
    reports.append({'name': clip['name'], 'decoded_frames': frame_index,
        'face_light_component_counts': distribution,
        'frames_with_more_than_two_lights': suspicious,
        'first_two_eye_distance': gaps[0] if gaps else None,
        'smallest_two_eye_distance': min(gaps, key=lambda p: p['center_distance_px']) if gaps else None,
        'last_two_eye_distance': gaps[-1] if gaps else None,
        'scope': 'Heuristic cyan-component detector in fixed face ROI. Needs visual review; does not certify absence of a dark mouth or correct motion.'})
(root / 'eye-inspection.json').write_text(json.dumps(reports, indent=2), encoding='utf-8')
print(json.dumps([{'name': p['name'], 'counts': p['face_light_component_counts'], 'flagged_frames': len(p['frames_with_more_than_two_lights']), 'minimum_eye_distance': p['smallest_two_eye_distance']} for p in reports], indent=2))
