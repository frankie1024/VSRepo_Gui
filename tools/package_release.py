from __future__ import annotations

import re
import shutil
import subprocess
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "VSRepo_Gui.csproj"
BUILD_DIR = ROOT / "bin" / "Release" / "net8.0-windows"
DIST_DIR = ROOT / "dist"

REQUIRED_FILES = {
    "VSRepo_Gui.exe": "VSRepo_Gui.exe",
    "VSRepo_Gui.dll": "VSRepo_Gui.dll",
    "Wpf.Ui.dll": "Wpf.Ui.dll",
    "Wpf.Ui.Abstractions.dll": "Wpf.Ui.Abstractions.dll",
}

ALTERNATE_NAMES = {
    "VSRepo_Gui.deps.json": ["VSRepo_Gui.deps.json", "vsrepo_Gui.deps.json"],
    "VSRepo_Gui.runtimeconfig.json": ["VSRepo_Gui.runtimeconfig.json", "vsrepo_Gui.runtimeconfig.json"],
}


def run(command: list[str]) -> None:
    subprocess.run(command, cwd=ROOT, check=True)


def get_version() -> str:
    text = PROJECT.read_text(encoding="utf-8")
    match = re.search(r"<Version>([^<]+)</Version>", text)
    if not match:
        raise RuntimeError("Unable to find <Version> in VSRepo_Gui.csproj")
    return match.group(1).strip()


def copy_release_files() -> list[Path]:
    DIST_DIR.mkdir(exist_ok=True)
    copied: list[Path] = []

    for source_name, target_name in REQUIRED_FILES.items():
        source = BUILD_DIR / source_name
        if not source.exists():
            raise FileNotFoundError(f"Missing build output: {source}")
        target = DIST_DIR / target_name
        shutil.copy2(source, target)
        copied.append(target)

    for target_name, candidates in ALTERNATE_NAMES.items():
        source = next((BUILD_DIR / name for name in candidates if (BUILD_DIR / name).exists()), None)
        if source is None:
            raise FileNotFoundError(f"Missing build output for {target_name}")
        target = DIST_DIR / target_name
        shutil.copy2(source, target)
        copied.append(target)

    pdb = DIST_DIR / "VSRepo_Gui.pdb"
    if pdb.exists():
        pdb.unlink()

    return copied


def build_zip(version: str, files: list[Path]) -> Path:
    zip_path = ROOT / f"VSRepo_Gui-v{version}-win-x64.zip"
    if zip_path.exists():
        zip_path.unlink()

    with ZipFile(zip_path, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for file in files:
            archive.write(file, arcname=file.name)

    return zip_path


def main() -> None:
    version = get_version()
    run(["dotnet", "build", "-c", "Release"])
    run(["dotnet", "publish", "-c", "Release", "--no-build"])
    files = copy_release_files()
    zip_path = build_zip(version, files)

    print(f"Prepared release package for v{version}")
    print(f"dist: {DIST_DIR}")
    print(f"zip:  {zip_path}")
    print("Upload the zip package to GitHub Releases. Do not upload a standalone exe.")


if __name__ == "__main__":
    main()
