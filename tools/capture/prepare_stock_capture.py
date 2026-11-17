"""Prepare a disposable stock-craft capture profile; optionally launch KSA."""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools"))
from configuration import assert_no_game, game_dir, loader_dir, workspace_path

root = Path(__file__).resolve().parents[2]
task = root / ".tools/blueprint-stock"
instance = task / "instance"
loader = task / "loader"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    "--launch",
    action="store_true",
    help="Launch the prepared disposable capture profile",
)
args = parser.parse_args()
assert_no_game()
workspace_path(root, loader)
workspace_path(root, instance)
shutil.copytree(loader_dir(), loader, dirs_exist_ok=True)
(loader / "StarMapConfig.json").write_text(
    json.dumps(
        {"GameLocation": str(game_dir()), "RepositoryLocation": "", "GameArguments": []}
    ),
    encoding="utf-8",
)
mod = instance / "mods/BlueprintProof"
mod.mkdir(parents=True, exist_ok=True)
(instance / "manifest.toml").write_text(
    '[[mods]]\nid = "Core"\nenabled = true\n[[mods]]\nid = "BlueprintProof"\nenabled = true\n'
)
if not (instance / "settings.toml").exists():
    (instance / "settings.toml").write_text('[display]\nmode = "Windowed"\n')
(mod / "mod.toml").write_text(
    'name = "Local capture proof"\n[StarMap]\nEntryAssembly = "CaptureProbe"\n'
)
for name in ("CaptureProbe.dll", "CaptureProbe.deps.json"):
    shutil.copy2(Path(__file__).parent / "bin/Release/net10.0" / name, mod / name)
sources = []
for craft in ("Gemini7", "Rocket"):
    source = game_dir() / "Content/Core/defaultvehicles" / craft
    if not (instance / "Vehicles" / craft).exists():
        shutil.copytree(source, instance / "Vehicles" / craft)
    sources.append(
        {
            "craft": craft,
            "source": str(source / "vehicle.xml"),
            "sha256": hashlib.sha256((source / "vehicle.xml").read_bytes()).hexdigest(),
        }
    )
output = workspace_path(root, root / "tests/fixtures/captures/stock-captures")
output.mkdir(parents=True, exist_ok=True)
(output / "sources.json").write_text(json.dumps(sources, indent=2))
if args.launch:
    assert_no_game()
    env = os.environ.copy()
    env.update(
        STARMAP_INSTANCE_PATH=str(instance),
        BLUEPRINT_CAPTURE_DIR=str(output),
        BLUEPRINT_CAPTURE_CRAFTS="Gemini7,Rocket",
        BLUEPRINT_BATCH_SOURCE=str(instance / "Vehicles"),
    )
    env.pop("KSA_CONTROLLER_CONFIG", None)
    with (task / "stdout.log").open("w") as stdout, (task / "stderr.log").open(
        "w"
    ) as stderr:
        # This is the interactive game window used for capture, not a hidden helper.
        p = subprocess.Popen(
            [str(loader / "StarMap.exe")],
            cwd=loader,
            env=env,
            stdout=stdout,
            stderr=stderr,
        )
    print("Launched isolated stock capture PID", p.pid)
else:
    print("Prepared", instance)
