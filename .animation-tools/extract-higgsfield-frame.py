"""Decode exact frames for visual review; no retouching or motion generation."""
import sys
from pathlib import Path
import cv2
source = Path(sys.argv[1]).resolve()
output = source.parent / 'review'
output.mkdir(exist_ok=True)
cap = cv2.VideoCapture(str(source))
for index in map(int, sys.argv[2:]):
    cap.set(cv2.CAP_PROP_POS_FRAMES, index)
    ok, frame = cap.read()
    if not ok:
        raise ValueError(f'Frame {index} not found')
    destination = output / f'{source.stem}-{index:03d}.png'
    cv2.imwrite(str(destination), frame)
    print(destination)
cap.release()
