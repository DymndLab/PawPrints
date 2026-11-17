"""Enable the separate acceptance driver in the disposable runtime profile only."""

import argparse
import io
import json
import re
import tomllib
import hashlib
import zipfile
import os
import shutil
import subprocess
import sys
from pathlib import Path, PurePosixPath

from configuration import assert_no_game, game_dir, loader_dir, workspace_path

root = Path(__file__).resolve().parents[1]


def validate_release_archive(archive):
    """Validate every file and directory before a downloaded ZIP can be installed."""
    entries = archive.infolist()
    if (
        len(entries) > 128
        or sum(item.file_size for item in entries) > 128 * 1024 * 1024
    ):
        raise ValueError("Release archive exceeds its extraction budget")
    seen = {}
    for item in entries:
        if item.orig_filename != item.filename:
            raise ValueError("Unsupported archive path encoding")
        name = item.filename[:-1] if item.is_dir() else item.filename
        parts = name.split("/")
        if not parts or parts[0] != "PawPrints":
            raise ValueError("Release archive must contain only PawPrints/")
        for part in parts:
            reserved = re.fullmatch(
                r"CON|PRN|AUX|NUL|COM[1-9\u00b9\u00b2\u00b3]|LPT[1-9\u00b9\u00b2\u00b3]",
                part.split(".")[0],
                re.I,
            )
            if (
                not part
                or part in (".", "..")
                or part.endswith((".", " "))
                or reserved
                or any(ord(c) < 32 or c in '<>:"\\|?*' for c in part)
            ):
                raise ValueError("Unsafe Windows path in release archive")
        kind = (item.external_attr >> 16) & 0o170000
        if kind not in (0, 0o040000, 0o100000) or item.flag_bits & 1:
            raise ValueError(
                "Linked, special or encrypted archive members are not supported"
            )
        key = name.casefold()
        if key in seen:
            raise ValueError("Duplicate archive path")
        seen[key] = item.is_dir()
    for name in seen:
        if any(
            seen.get(str(parent)) is False for parent in PurePosixPath(name).parents
        ):
            raise ValueError("Archive file conflicts with a directory")
    if (
        seen.get("pawprints/pawprints.dll") is not False
        or seen.get("pawprints/mod.toml") is not False
    ):
        raise ValueError("Release archive is missing the runtime")
    manifest = tomllib.loads(archive.read("PawPrints/mod.toml").decode("utf-8"))
    if (
        manifest.get("name") != "PawPrints"
        or manifest.get("StarMap", {}).get("EntryAssembly") != "PawPrints"
    ):
        raise ValueError("Release archive has an unexpected mod identity")
    # Verify CRCs before replacing anything. The uncompressed size was bounded above.
    if archive.testzip() is not None:
        raise ValueError("Release archive contains damaged data")
    return [item for item in entries if not item.is_dir()]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--drawing", action="store_true", help="Run drawing checks (default)"
    )
    mode.add_argument(
        "--performance",
        action="store_true",
        help="Run engine and vehicle performance checks",
    )
    mode.add_argument(
        "--poses", action="store_true", help="Run detached part-pose checks"
    )
    parser.add_argument(
        "--hold",
        action="store_true",
        help="Leave the disposable game open after the checks",
    )
    parser.add_argument(
        "--ui", action="store_true", help="Use the alternate disposable UI test profile"
    )
    parser.add_argument(
        "--package",
        type=Path,
        help="Test this exact release ZIP instead of the new source build",
    )
    args = parser.parse_args(argv)
    assert_no_game()
    # Snapshot a downloaded artifact before preparation builds anything. The supplied
    # bytes, not a newly compiled DLL, are installed for the actual native run.
    package_bytes = None
    if args.package:
        if args.package.stat().st_size > 64 * 1024 * 1024:
            parser.error("Release ZIP exceeds its file-size budget")
        package_bytes = args.package.read_bytes()
        try:
            with zipfile.ZipFile(io.BytesIO(package_bytes)) as archive:
                validate_release_archive(archive)
        except (ValueError, KeyError, UnicodeError, zipfile.BadZipFile) as error:
            parser.error(str(error))
    if not (root / ".local/vehicles/Lander_1/vehicle.xml").is_file():
        raise SystemExit(
            "Native acceptance needs a local Lander_1 fixture. See DEVELOPMENT.md; no game launched."
        )
    profile = "pawprints-ui-check" if args.ui else "blueprint-runtime"
    subprocess.run(
        [sys.executable, str(root / "tools/build.py"), "--profile", profile], check=True
    )
    subprocess.run(
        [
            "dotnet",
            "build",
            str(root / "tests/Blueprint.Acceptance.csproj"),
            "-c",
            "Release",
            "-p:RestoreLockedMode=true",
            f"-p:GameDir={game_dir()}",
            f"-p:LoaderDir={loader_dir()}",
        ],
        check=True,
    )
    task = workspace_path(root, root / ".tools" / profile)
    instance = task / "instance"
    loader = task / "loader"
    assert_no_game()
    if package_bytes is not None:
        with zipfile.ZipFile(io.BytesIO(package_bytes)) as archive:
            files = validate_release_archive(archive)
            mod_root = workspace_path(root, instance / "mods/PawPrints")
            for item in archive.infolist():
                workspace_path(root, instance / "mods" / item.filename)
            if mod_root.exists():
                shutil.rmtree(mod_root)
            archive.extractall(instance / "mods")
            for item in files:
                installed = instance / "mods" / item.filename
                if (
                    hashlib.sha256(installed.read_bytes()).digest()
                    != hashlib.sha256(archive.read(item)).digest()
                ):
                    raise SystemExit("Installed release file differs from archive")
        (task / "tested-package.zip").write_bytes(package_bytes)
        (task / "tested-package.sha256").write_text(
            hashlib.sha256(package_bytes).hexdigest() + "\n", encoding="utf-8"
        )
        print(
            "Testing exact release ZIP:",
            hashlib.sha256(package_bytes).hexdigest(),
            flush=True,
        )
    # Automated rendering fixtures use an explicit destination. First-use folder
    # selection/cancellation is a separate visible UI acceptance test.
    export_settings = instance / "PawPrints/export-settings.json"
    if not export_settings.exists():
        export_destination = instance / "Exports/PawPrints"
        export_destination.mkdir(parents=True, exist_ok=True)
        export_settings.parent.mkdir(parents=True, exist_ok=True)
        export_settings.write_text(
            json.dumps({"Folder": str(export_destination)}), encoding="utf-8"
        )
    mod = instance / "mods/BlueprintAcceptance"
    mod.mkdir(parents=True, exist_ok=True)
    shutil.copy2(
        root / "tests/bin/Release/net10.0/Blueprint.Acceptance.dll",
        mod / "Blueprint.Acceptance.dll",
    )
    (mod / "mod.toml").write_text(
        'name = "Blueprint isolated acceptance"\n[StarMap]\nEntryAssembly = "Blueprint.Acceptance"\n'
    )
    with (instance / "manifest.toml").open("a") as f:
        f.write('\n[[mods]]\nid = "BlueprintAcceptance"\nenabled = true\n')
    env = os.environ.copy()
    env.update(STARMAP_INSTANCE_PATH=str(instance), BLUEPRINT_ACCEPTANCE="1")
    if args.drawing:
        env["BLUEPRINT_DRAWING_QA"] = "1"
    else:
        env.pop("BLUEPRINT_DRAWING_QA", None)
    if args.performance:
        env["BLUEPRINT_PERFORMANCE_QA"] = "1"
    else:
        env.pop("BLUEPRINT_PERFORMANCE_QA", None)
    if args.poses:
        env["BLUEPRINT_POSE_QA"] = "1"
    else:
        env.pop("BLUEPRINT_POSE_QA", None)
    if args.hold:
        env["BLUEPRINT_ACCEPTANCE_HOLD"] = "1"
    else:
        env.pop("BLUEPRINT_ACCEPTANCE_HOLD", None)
    env.pop("KSA_CONTROLLER_CONFIG", None)
    assert_no_game()
    with (
        (task / "stdout.log").open("w") as stdout,
        (task / "stderr.log").open("w") as stderr,
    ):
        # Override hidden-window hints inherited from the launching shell so the
        # interactive test window remains visible.
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 1
        process = subprocess.Popen(
            [str(loader / "StarMap.exe")],
            cwd=loader,
            env=env,
            stdout=stdout,
            stderr=stderr,
            startupinfo=startup,
        )
    (task / "pid.txt").write_text(str(process.pid))
    print("Isolated acceptance PID:", process.pid)


if __name__ == "__main__":
    main()
