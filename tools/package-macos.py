"""Publish and package HUDI for one or both Mac architectures."""

import argparse
import json
from pathlib import Path
import plistlib
import re
import shutil
import stat
import subprocess
import sys
from tempfile import TemporaryDirectory
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "src" / "HudiMac" / "HudiMac.csproj"


def package(architecture: str, sprites: Path, output: Path, version: str, signing=None) -> Path:
    if signing is not None:
        identity, profile = signing
        output.mkdir(parents=True, exist_ok=True)
        with TemporaryDirectory(dir=output) as staging:
            work = Path(staging)
            bundle = work / "HUDI.app"
            publish_bundle(architecture, sprites, version, bundle)
            archive = output / f"HUDI-{version}-macOS-{architecture}-signed.zip"
            sign_and_notarize(bundle, work, identity, profile).replace(archive)
        print(archive)
        return archive

    bundle = output / architecture / "HUDI.app"
    host = publish_bundle(architecture, sprites, version, bundle)
    if sys.platform == "darwin":
        for native in host.parent.glob("*.dylib"):
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


def publish_bundle(architecture: str, sprites: Path, version: str, bundle: Path) -> Path:
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
    return host


def sign_and_notarize(bundle: Path, work: Path, identity: str, profile: str) -> Path:
    entitlements = work / "entitlements.plist"
    with entitlements.open("wb") as output:
        plistlib.dump({
            "com.apple.security.cs.allow-jit": True,
            "com.apple.security.cs.allow-unsigned-executable-memory": True,
        }, output)

    codesign = ["codesign", "--force", "--timestamp", "--options", "runtime", "--sign", identity]
    executable = bundle / "Contents" / "MacOS"
    for file in sorted(executable.rglob("*")):
        if file.is_file() and (file.suffix == ".dylib" or file.name == "createdump"):
            subprocess.run([*codesign, str(file)], check=True)
    subprocess.run([*codesign, "--entitlements", str(entitlements), str(bundle)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)

    ditto = ["ditto", "-c", "-k", "--keepParent"]
    submission = work / "submission.zip"
    subprocess.run([*ditto, str(bundle), str(submission)], check=True)
    result = subprocess.run(
        ["xcrun", "notarytool", "submit", str(submission), "--keychain-profile", profile,
         "--wait", "--output-format", "json"],
        check=True, stdout=subprocess.PIPE, text=True,
    )
    report = json.loads(result.stdout)
    if report.get("status") != "Accepted":
        raise SystemExit(f"Notarization {report.get('id')} returned {report.get('status')}.")
    subprocess.run(["xcrun", "stapler", "staple", str(bundle)], check=True)
    subprocess.run(["xcrun", "stapler", "validate", str(bundle)], check=True)
    subprocess.run(["spctl", "--assess", "--type", "execute", str(bundle)], check=True)
    archive = work / "signed.zip"
    subprocess.run([*ditto, str(bundle), str(archive)], check=True)
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
    parser.add_argument("--signing-identity", help="Developer ID Application identity in the Mac Keychain")
    parser.add_argument("--notary-profile", help="Existing notarytool Keychain profile")
    args = parser.parse_args()
    if bool(args.signing_identity) != bool(args.notary_profile):
        parser.error("provide --signing-identity and --notary-profile together")
    if args.signing_identity and sys.platform != "darwin":
        parser.error("Developer ID signing and notarization require macOS")
    signing = (args.signing_identity, args.notary_profile) if args.signing_identity else None
    sprites = args.sprites.resolve(strict=True)
    version = re.search(r"<Version>([^<]+)</Version>", PROJECT.read_text(encoding="utf-8"))[1]
    for architecture in ("arm64", "x64") if args.arch == "both" else (args.arch,):
        package(architecture, sprites, args.output.resolve(), version, signing)


if __name__ == "__main__":
    main()
