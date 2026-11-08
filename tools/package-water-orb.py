"""Package the approved, chroma-keyed Higgsfield loop as the WPF water material."""
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED

root = Path(__file__).resolve().parents[1]
source = root / 'assets/water-orb/higgsfield-v1'
frames = source / 'frames-alpha'
manifest = json.loads((source / 'manifest.json').read_text(encoding='utf-8'))
paths = [frames / f'frame-{i:03}.png' for i in range(manifest['frames'])]
assert all(path.is_file() for path in paths), 'Run prepare-higgsfield-water.py first.'
target = root / 'src/HudiPet/Assets/water-orb.zip'
with ZipFile(target, 'w', ZIP_STORED) as archive:
    archive.writestr('manifest.json', json.dumps(manifest))
    for path in paths:
        archive.write(path, path.name)
print(target)
