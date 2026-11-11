"""Download the exact shared HUDI sprite archive for a clean checkout."""

import hashlib
from pathlib import Path
from tempfile import NamedTemporaryFile
from urllib.request import urlopen


URL = "https://github.com/GamoffVad/HUDI/releases/download/v7.3.19/HUDI-sprites.zip"
SHA256 = "a45f8197128c3314ce1cc188f7fe3c80e8ffb5b5fa60808aabf89ffe7a5b0b8b"
DESTINATION = Path(__file__).resolve().parents[1] / "output" / "hudi-blender-v1" / "sprites.zip"


def main() -> None:
    if DESTINATION.exists():
        with DESTINATION.open("rb") as current:
            if hashlib.file_digest(current, "sha256").hexdigest() == SHA256:
                print(DESTINATION)
                return
        raise SystemExit(f"Existing sprite archive differs from the release: {DESTINATION}")

    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256()
    with NamedTemporaryFile(dir=DESTINATION.parent, delete=False) as output:
        temporary = Path(output.name)
        try:
            with urlopen(URL, timeout=60) as response:
                for chunk in iter(lambda: response.read(1024 * 1024), b""):
                    output.write(chunk)
                    digest.update(chunk)
            output.close()
            if digest.hexdigest() != SHA256:
                raise ValueError("Release sprite archive failed SHA-256 verification.")
            temporary.replace(DESTINATION)
        finally:
            output.close()
            temporary.unlink(missing_ok=True)
    print(DESTINATION)


if __name__ == "__main__":
    main()
