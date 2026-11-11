"""Package Blender-exported masters, preserving the existing pet's layout."""
import importlib.util,json,hashlib
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
from concurrent.futures import ThreadPoolExecutor
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('pack_legacy',ROOT/'tools/package-tousled-girl.py')
pack=importlib.util.module_from_spec(spec);spec.loader.exec_module(pack)
pack.SRC=ROOT/'pet-sources/tousled-girl/animations/look-surprise-kling4/blender-v1'
pack.OUT=ROOT/'output/tousled-girl-blender-v2'
def main():
    v=json.loads((pack.SRC/'verification.json').read_text())
    assert v['all_193_rgba_frames_valid'] and v['first_last_identical'] and v['all_frame_borders_transparent']
    (pack.OUT/'frames').mkdir(parents=True,exist_ok=True)
    with ThreadPoolExecutor(max_workers=3) as pool:ids=list(pool.map(pack.encode,range(192)))
    manifest=json.loads((ROOT/'output/tousled-girl-v1/manifest.json').read_text(encoding='utf-8'))
    assert manifest['frames']==ids and manifest['id']=='tousled-girl'
    manifest['description']='Осматривается, удивляется и возвращается в исходную позу. Фон, волосы и тень обработаны в Blender через MCP.'
    (pack.OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    package=pack.OUT/'tousled-girl.pet'
    with ZipFile(package,'w',ZIP_STORED) as z:
        z.write(pack.OUT/'manifest.json','manifest.json')
        for fid in ids:z.write(pack.OUT/'frames'/f'{fid}.png',f'{fid}.png')
    report={'name':'Кудряшка','package':str(package),'sha256':pack.sha(package),'bytes':package.stat().st_size,
        'uniqueFrames':192,'clipSteps':193,'fps':24,'durationSeconds':193/24,'frameSize':[1000,1000],
        'masterSize':[1440,1440],'displayBounds':manifest['displayBounds'],
        'layoutPreserved':True,'matting':'Blender 5.2.2 LTS via MCP for Blender 2.2.7',
        'recipeSha256':pack.sha(pack.SRC/'recipe.json'),'firstLastFrame':'neutral'}
    (pack.OUT/'build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False),flush=True)
if __name__=='__main__':main()
