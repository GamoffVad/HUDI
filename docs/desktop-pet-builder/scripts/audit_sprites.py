#!/usr/bin/env python3
"""Read-only sprite QA with optional report/contact sheet. Requires Pillow."""
import argparse
import json
import math
import sys
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageStat


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def frame_path(root, name):
    if not isinstance(name, str) or not name:
        raise ValueError('Frame ID must be a non-empty string')
    item = Path(name if Path(name).suffix else name + '.png')
    if item.is_absolute():
        raise ValueError('Frame paths must be relative')
    candidate = (root / item).resolve()
    if not candidate.is_relative_to(root):
        raise ValueError('Frame path escapes the frames directory')
    return candidate


def contact_sheet(clips, paths, output):
    rows = [(name, steps) for name, steps in clips.items() if isinstance(steps, list) and steps]
    if not rows:
        return
    cell_w, cell_h = 180, 230
    sheet = Image.new('RGB', (cell_w * 6, cell_h * len(rows)), (70, 70, 70))
    draw = ImageDraw.Draw(sheet)
    for row, (name, steps) in enumerate(rows):
        chosen = [steps[0], steps[len(steps)//2], steps[-1]]
        for col in range(6):
            item = chosen[col // 2]
            ident = item.get('frame') if isinstance(item, dict) else None
            if not isinstance(ident, str) or ident not in paths:
                continue
            color = (238, 238, 238, 255) if col % 2 == 0 else (30, 33, 40, 255)
            background = Image.new('RGBA', (cell_w, cell_h - 30), color)
            with Image.open(paths[ident]) as source:
                image = source.convert('RGBA')
            image.thumbnail((cell_w - 12, cell_h - 38), Image.Resampling.LANCZOS)
            background.alpha_composite(image, ((cell_w-image.width)//2, (cell_h-30-image.height)//2))
            x, y = col * cell_w, row * cell_h
            sheet.paste(background.convert('RGB'), (x, y))
            label = f'{name}: {ident}'
            draw.text((x+4, y+cell_h-26), label[:28], fill='white')
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output)


def audit(manifest, root, sheet=None):
    errors, warnings = [], []
    clips = manifest.get('clips')
    if not isinstance(clips, dict) or not clips:
        return {'success': False, 'errors': ['clips must be a non-empty object'], 'warnings': []}
    size = manifest.get('frameSize')
    if not (isinstance(size, list) and len(size) == 2 and all(type(n) is int and n > 0 for n in size)):
        errors.append('frameSize must contain two positive integers')
        size = None
    required, durations = set(), {}
    for name, steps in clips.items():
        if not isinstance(steps, list) or not steps:
            errors.append(f'{name}: empty or invalid sequence')
            continue
        total = 0
        for i, step in enumerate(steps):
            if not isinstance(step, dict):
                errors.append(f'{name}[{i}]: invalid step')
                continue
            frame, seconds = step.get('frame'), step.get('seconds')
            if not isinstance(frame, str) or not frame:
                errors.append(f'{name}[{i}]: missing frame ID')
            else:
                required.add(frame)
            if not number(seconds) or seconds <= 0:
                errors.append(f'{name}[{i}]: duration must be finite and positive')
            else:
                total += seconds
        durations[name] = {'steps': len(steps), 'seconds': total}
    declared = manifest.get('frames', sorted(required))
    if not isinstance(declared, list) or any(not isinstance(x, str) or not x for x in declared):
        errors.append('frames must be an array of non-empty frame IDs')
        declared = []
    if required - set(declared):
        errors.append('Referenced frames absent from frames: ' + ', '.join(sorted(required-set(declared))))
    all_ids = set(declared) | required
    paths, transparent, soft_alpha = {}, 0, 0
    for ident in sorted(all_ids):
        try:
            path = frame_path(root, ident)
            with Image.open(path) as source:
                image = source.convert('RGBA')
            if size and image.size != tuple(size):
                errors.append(f'{ident}: size {image.size} differs from {tuple(size)}')
                continue
            alpha = image.getchannel('A')
            histogram = alpha.histogram()
            if histogram[0]:
                transparent += 1
            if sum(histogram[1:255]):
                soft_alpha += 1
            if alpha.getextrema()[1] == 0:
                warnings.append(f'{ident}: fully transparent frame')
            elif alpha.getextrema()[0] == 255:
                warnings.append(f'{ident}: fully opaque canvas; inspect background')
            width, height = image.size
            borders = [(0,0,width,1), (0,height-1,width,height), (0,0,1,height), (width-1,0,width,height)]
            if any(alpha.crop(box).getextrema()[1] > 224 for box in borders):
                warnings.append(f'{ident}: opaque pixels touch canvas edge; inspect clipping')
            paths[ident] = path
        except (ValueError, OSError) as exc:
            errors.append(f'{ident}: {exc}')
    joins = manifest.get('joins', [])
    if not isinstance(joins, list):
        errors.append('joins must be an array')
        joins = []
    seam_metrics = []
    for join in joins:
        if not isinstance(join, dict) or not all(isinstance(join.get(k), str) for k in ('from','to')):
            errors.append('Each join requires from/to frame IDs')
            continue
        if join['from'] not in paths or join['to'] not in paths:
            errors.append(f'Join references unavailable frames: {join}')
            continue
        images = []
        for key in ('from', 'to'):
            with Image.open(paths[join[key]]) as source:
                images.append(source.convert('RGBA').convert('RGBa'))
        if images[0].size != images[1].size:
            errors.append('Join has incompatible frame sizes')
            continue
        delta = sum(ImageStat.Stat(ImageChops.difference(*images)).mean) / (4 * 255)
        seam_metrics.append({'from': join['from'], 'to': join['to'], 'meanDelta': delta})
        limit = join.get('maxMeanDelta')
        if limit is not None:
            if not number(limit) or not 0 <= limit <= 1:
                errors.append('maxMeanDelta must be between zero and one')
            elif delta > limit:
                errors.append(f'Join exceeds configured tolerance: {join["from"]} -> {join["to"]}: {delta:.5f}')
    holds = manifest.get('holds', [])
    if not isinstance(holds, list):
        errors.append('holds must be an array')
        holds = []
    for hold in holds:
        if not isinstance(hold, dict) or not isinstance(hold.get('clip'), str):
            errors.append('Each hold requires a clip')
            continue
        steps = clips.get(hold['clip'])
        start, end = hold.get('startIndex'), hold.get('endIndex')
        if (not isinstance(steps, list) or type(start) is not int or type(end) is not int
                or not 0 <= start <= end < len(steps)):
            errors.append(f'Hold range is outside the clip: {hold}')
        if hold.get('mode') not in ('loop', 'pingpong', 'still'):
            errors.append(f'Unknown hold mode: {hold.get("mode")}')
    if sheet:
        contact_sheet(clips, paths, sheet)
    return {'success': not errors, 'errors': errors, 'warnings': warnings,
            'framesChecked': len(paths), 'clips': durations, 'framesWithTransparentPixels': transparent,
            'framesWithSoftAlpha': soft_alpha, 'joins': seam_metrics,
            'visualReviewRequired': ['identity and anatomy', 'face rules', 'edges and shadow', 'motion and loop seams']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    parser.add_argument('--frames-dir', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--sheet', type=Path)
    parser.add_argument('--strict', action='store_true', help='Treat visual warnings as failures')
    args = parser.parse_args()
    root = (args.frames_dir or args.manifest.parent / 'frames').resolve()
    try:
        manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
        if not isinstance(manifest, dict):
            raise ValueError('Manifest must be an object')
        result = audit(manifest, root, args.sheet)
        if args.strict and result.get('warnings'):
            result['success'] = False
    except (ValueError, OSError) as exc:
        result = {'success': False, 'errors': [str(exc)], 'warnings': []}
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result['success'] else 2


if __name__ == '__main__':
    sys.exit(main())
