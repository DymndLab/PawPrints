"""Explicit local development paths; never infer another project's workspace."""

import json
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def configured(name, env):
    config = ROOT / ".local/development.json"
    value = os.environ.get(env) or (
        json.loads(config.read_text(encoding="utf-8")).get(name)
        if config.exists()
        else None
    )
    if not value:
        raise SystemExit(f"Set {env} or {config} key {name}; see DEVELOPMENT.md.")
    path = Path(value).expanduser()
    return (ROOT / path).resolve() if not path.is_absolute() else path.resolve()


def game_dir():
    return configured("game_dir", "PAWPRINTS_GAME_DIR")


def loader_dir():
    return configured("loader_dir", "PAWPRINTS_LOADER_DIR")


def assert_no_game():
    """Fail closed when process enumeration fails; never rely on readable EXE paths."""
    try:
        probe = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$ErrorActionPreference='Stop'; "
                "@(Get-Process | Where-Object { $_.ProcessName -in 'StarMap','KSA' }).Count",
            ],
            capture_output=True,
            text=True,
            timeout=15,
        )
        count = probe.stdout.strip()
        if probe.returncode != 0 or not count.isdecimal():
            raise ValueError("Process enumeration did not return a count")
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        raise SystemExit(
            "Could not verify that KSA is closed; no profile will be changed or launched."
        ) from error
    if int(count) > 0:
        raise SystemExit(
            "KSA is already running. Close it yourself before preparing or launching a test profile."
        )


def workspace_path(root, path):
    """Require a real child of the workspace, rejecting symlinks and junctions."""
    root = root.absolute()
    path = path.absolute()
    try:
        parts = path.relative_to(root).parts
    except ValueError as error:
        raise ValueError("Target is outside the development workspace") from error
    if not parts or ".." in parts:
        raise ValueError("Target must be a child of the development workspace")
    current = root
    for part in parts:
        current /= part
        if current.is_symlink() or (
            current.exists()
            and getattr(current.lstat(), "st_file_attributes", 0) & 0x400
        ):
            raise ValueError("Linked development targets are not supported")
    resolved = path.resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError("Target resolves outside the development workspace")
    return resolved
