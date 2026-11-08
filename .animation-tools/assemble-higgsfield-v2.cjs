const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const oldRoot = path.join(root, 'output/hudi-higgsfield-natural-green-2026-11-07');
const fixesRoot = path.join(root, 'output/hudi-higgsfield-corrections-2026-11-07');
const destination = path.join(root, 'output/hudi-higgsfield-v2-green-2026-11-07');
const readJson = p => JSON.parse(fs.readFileSync(p, 'utf8').replace(/^\uFEFF/, ''));
const old = readJson(path.join(oldRoot, 'generation-run.json'));
const fixes = readJson(path.join(fixesRoot, 'generation-run.json'));
// Preserve useful original clips and replace the failed expressions/movements.
const selection = [
  ['old', 1, '01_idle_blink_wink'],
  ['fix', 2, '02_think_left'],
  ['fix', 10, '03_hover_two_eyes'],
  ['old', 3, '04_squat_stretch'],
  ['fix', 3, '05_sad_eyes'],
  ['fix', 4, '06_surprise_eyes'],
  ['fix', 5, '07_angry_lightning'],
  ['fix', 9, '08_natural_left_right'],
  ['old', 6, '09_combat_guard'],
  ['fix', 6, '10_dance_two_eyes'],
];
const clips = selection.map(([kind, index, name], position) => {
  const run = kind === 'old' ? old : fixes;
  const folder = kind === 'old' ? oldRoot : fixesRoot;
  const source = run.clips.find(c => c.index === index);
  if (!source || source.status !== 'completed') throw new Error(`Not ready: ${kind} ${index}`);
  const file = path.join(folder, source.name + '.mp4');
  if (!fs.existsSync(file)) throw new Error('Missing ' + file);
  return { ...source, index: position + 1, name, source_file: file, source_index: index };
});
fs.mkdirSync(destination, { recursive: true });
for (const clip of clips) fs.copyFileSync(clip.source_file, path.join(destination, clip.name + '.mp4'));
for (const name of ['00-master-blue-green-shadow.png', '00-hover-converged-keyframe.png', '00-moonwalk-left-keyframe.png', '00-moonwalk-right-keyframe.png']) {
  const file = path.join(fixesRoot, name);
  if (fs.existsSync(file)) fs.copyFileSync(file, path.join(destination, name));
}
const manifest = { schema_version: 2, status: 'completed', provider: 'Higgsfield',
  description: 'Selected animation sources: eye-only face, blue details, green background, contact shadow, natural left/right orientation. Moonwalk remains unresolved and is excluded.',
  unresolved: ['Accurate moonwalk in both directions; generated attempts lift the feet and are not accepted.'],
  compilation_method: 'stream copy; no invented/interpolated/reversed/mirrored movement',
  source_runs: [oldRoot, fixesRoot], clips };
fs.writeFileSync(path.join(destination, 'generation-run.json'), JSON.stringify(manifest, null, 2));
console.log(JSON.stringify({ destination, clips: clips.length }));
