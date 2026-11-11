"""Publish and package HUDI for one or both Mac architectures."""

import argparse
from pathlib import Path
import plistlib
import re
import shutil
import stat
import subprocess
import sys
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src" / "HudiMac" / "HudiMac.csproj"


def package(architecture: str, sprites: Path, output: Path, version: str) -> Path:
    bundle = output / architecture / "HUDI.app"
    contents = bundle / "Contents"
    executable = contents / "MacOS"
    executable.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            "dotnet", "publish", str(PROJECT), "-c", "Release", "-r", f"osx-{architecture}",
            "--self-contained", "-o", str(executable), f"-p:HudiSprites={sprites}",
        ],
        check=True,
    )
    host = executable / "HUDI"
    host.chmod(host.stat().st_mode | 0o111)
    with (contents / "Info.plist").open("wb") as info:
        plistlib.dump(
            {
                "CFBundleExecutable": "HUDI",
                "CFBundleIdentifier": "com.hudi.desktop",
                "CFBundleName": "HUDI",
                "CFBundlePackageType": "APPL",
                "CFBundleShortVersionString": version,
                "CFBundleVersion": version,
                "LSMinimumSystemVersion": "14.0",
                "LSUIElement": True,
                "NSHighResolutionCapable": True,
            },
            info,
        )
    if sys.platform == "darwin":
        for native in executable.glob("*.dylib"):
            subprocess.run(["codesign", "--force", "--sign", "-", str(native)], check=True)
        subprocess.run(["codesign", "--force", "--sign", "-", str(bundle)], check=True)
        subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
    archive = output / f"HUDI-{version}-macOS-{architecture}.zip"
    with ZipFile(archive, "w", allowZip64=True, compresslevel=3) as zip_file:
        for file in sorted(bundle.rglob("*")):
            if not file.is_file():
                continue
            compress = ZIP_STORED if file.suffix == ".zip" else ZIP_DEFLATED
            info = zip_file_info(file, bundle.parent, compress, file == host)
            with file.open("rb") as source, zip_file.open(info, "w", force_zip64=file.stat().st_size >= 2**31) as target:
                shutil.copyfileobj(source, target, length=1024 * 1024)
    print(archive)
    return archive


def zip_file_info(file: Path, base: Path, compression: int, executable: bool) -> ZipInfo:
    info = ZipInfo.from_file(file, file.relative_to(base).as_posix())
    info.create_system = 3
    info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
    info.compress_type = compression
    return info


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--arch", choices=("arm64", "x64", "both"), default="both")
    parser.add_argument("--sprites", type=Path, default=ROOT / "output" / "hudi-blender-v1" / "sprites.zip")
    parser.add_argument("--output", type=Path, default=ROOT / "output" / "hudi-macos")
    args = parser.parse_args()
    sprites = args.sprites.resolve(strict=True)
    version = re.search(r"<Version>([^<]+)</Version>", PROJECT.read_text(encoding="utf-8"))[1]
    for architecture in ("arm64", "x64") if args.arch == "both" else (args.arch,):
        package(architecture, sprites, args.output.resolve(), version)


if __name__ == "__main__":
    main()
