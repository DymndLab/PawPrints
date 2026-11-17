"""Build a mod package and optionally prepare an isolated StarMap profile.

--stage only builds the package. Without it, prepare a disposable profile.
Add --launch to start that profile after preparation.
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

from configuration import assert_no_game, game_dir, loader_dir, workspace_path

parser = argparse.ArgumentParser(description=__doc__)
action = parser.add_mutually_exclusive_group()
action.add_argument(
    "--stage",
    action="store_true",
    help="Build the package without modifying a game profile",
)
action.add_argument(
    "--launch",
    action="store_true",
    help="Prepare the disposable profile and launch KSA",
)
parser.add_argument(
    "--profile",
    choices=["blueprint-runtime", "pawprints-ui-check"],
    default="blueprint-runtime",
)
args = parser.parse_args()

root = Path(__file__).resolve().parents[1]
project = root
game = game_dir()
if not args.stage:
    assert_no_game()
expected = "3A9B34894446A419193A1D9810A0902AA8EAA8B8AC910F04F687DC647818E586"
if hashlib.sha256((game / "KSA.dll").read_bytes()).hexdigest().upper() != expected:
    raise SystemExit("Installed KSA changed. Inspect the new build before packaging.")
subprocess.run(
    [
        "dotnet",
        "build",
        str(project / "src/PawPrints.csproj"),
        "-c",
        "Release",
        "-p:RestoreLockedMode=true",
        "-p:WarningsNotAsErrors=NU1900",
        f"-p:GameDir={game}",
        f"-p:LoaderDir={loader_dir()}",
    ],
    check=True,
)  # offline audit availability stays a warning; vulnerability findings still fail
task = workspace_path(root, root / ".tools" / args.profile)
loader = task / "loader"
instance = task / "instance"
package = workspace_path(root, project / "dist/PawPrints")
if package.exists():
    shutil.rmtree(package)
package.mkdir(parents=True, exist_ok=True)
output = project / "src/bin/Release/net10.0"
for name in ("PawPrints.dll", "PawPrints.deps.json", "BCnEncoder.dll"):
    shutil.copy2(output / name, package / name)
logo_folder = package / "Logos"
logo_folder.mkdir(exist_ok=True)
(logo_folder / "README.txt").write_text(
    "In PawPrints, open Title Block > Company > Open logo folder.\n"
    "Add PNG files there, then select Scan logo folder for new files.\n"
    "Choose PNG... can also import a logo directly.\n"
    "The Company tab shows suggested dimensions for the current title block.\n"
    "Image proportions and transparency are preserved.\n"
    "Logos and title preferences are stored in the active game profile.\n"
)
font_licenses = package / "Font-Licenses"
font_licenses.mkdir(exist_ok=True)
for slug in (
    "mrdafoe",
    "kristi",
    "reeniebeanie",
    "nothingyoucoulddo",
    "zeyada",
    "labelleaurore",
):
    shutil.copy2(
        project / f"assets/fonts/{slug}/OFL.txt",
        font_licenses / f"{slug}-OFL.txt",
    )
for name in ("source.json",):
    shutil.copy2(project / "assets/fonts" / name, font_licenses / name)
# KSA already owns this assembly. Never ship a second, possibly conflicting copy.
installed_toolkit = game / "CommunityToolkit.HighPerformance.dll"
# Same 8.4.0 assembly identity, game-specific binary. The isolated live acceptance
# exercises BC5/BC7 decoding against this exact game-owned copy.
if (
    hashlib.sha256(installed_toolkit.read_bytes()).hexdigest()
    != "196607c6d4d704b5ee95c80f8e3cd799595b7d8a08108701894c211b138d2839"
):
    raise SystemExit("Game toolkit changed; rerun decoder compatibility acceptance.")
(package / "mod.toml").write_text(
    'name = "PawPrints"\n\n[StarMap]\nEntryAssembly = "PawPrints"\n'
)
for name in (
    "INSTALL.md",
    "part-support.json",
    "THIRD-PARTY-NOTICES.txt",
    "LICENSE",
):
    shutil.copy2(project / name, package / name)
version = re.search(
    r'const\s+string\s+Version\s*=\s*"([^"]+)"',
    (project / "src/Rendering/PageLayout.cs").read_text(),
).group(1)
archive = project / f"dist/PawPrints-{version}-preview"
shutil.make_archive(str(archive), "zip", package.parent, package.name)
print("Packaged:", str(archive) + ".zip")
if args.stage:
    print("Staged only; running game/profile unchanged.")
    raise SystemExit(0)
assert_no_game()
workspace_path(root, loader)
workspace_path(root, instance)
shutil.copytree(loader_dir(), loader, dirs_exist_ok=True)
# The configured loader directory may contain binaries only. Each disposable
# launcher owns its configuration and must not depend on another checkout.
(loader / "StarMapConfig.json").write_text(
    json.dumps(
        {"GameLocation": str(game), "RepositoryLocation": "", "GameArguments": []},
        indent=2,
    ),
    encoding="utf-8",
)

instance.mkdir(parents=True, exist_ok=True)
if not (instance / "settings.toml").exists():
    (instance / "settings.toml").write_text(
        '[display]\nmode = "Windowed"\nwidth = 1440\nheight = 1000\n'
    )
legacy_mod = (instance / "mods/Blueprint").resolve()
if legacy_mod.exists():
    import time

    retired = (task / "retired-mods" / str(time.time_ns()) / "Blueprint").resolve()
    if not legacy_mod.is_relative_to(task.resolve()) or not retired.is_relative_to(
        task.resolve()
    ):
        raise SystemExit("Invalid disposable migration path")
    retired.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(legacy_mod, retired)
shutil.copytree(package, instance / "mods/PawPrints", dirs_exist_ok=True)
# StarMap discovers extra mod folders even when omitted from the manifest. Keep
# the development driver outside mods for an ordinary user preview launch.
qa = instance / "mods/BlueprintAcceptance"
if qa.exists():
    import time

    parked = (
        task / "disabled-test-drivers" / str(time.time_ns()) / "BlueprintAcceptance"
    )
    workspace_path(root, qa)
    workspace_path(root, parked)
    parked.parent.mkdir(parents=True, exist_ok=True)
    qa.rename(parked)
(instance / "manifest.toml").write_text(
    '[[mods]]\nid = "Core"\nenabled = true\n\n[[mods]]\nid = "PawPrints"\nenabled = true\n'
)
sources = {
    "Gemini7": game / "Content/Core/defaultvehicles/Gemini7",
    "Rocket": game / "Content/Core/defaultvehicles/Rocket",
}
local_lander = root / ".local/vehicles/Lander_1"
if local_lander.exists():
    sources["Lander_1"] = local_lander
for name, source in sources.items():
    if not (instance / "Vehicles" / name).exists():
        shutil.copytree(source, instance / "Vehicles" / name)
(task / "sources.json").write_text(
    json.dumps(
        {
            name: hashlib.sha256((source / "vehicle.xml").read_bytes()).hexdigest()
            for name, source in sources.items()
        },
        indent=2,
    )
)
print("Isolated profile:", instance)
if args.launch:
    assert_no_game()
    env = os.environ.copy()
    env["STARMAP_INSTANCE_PATH"] = str(instance)
    for key in (
        "KSA_CONTROLLER_CONFIG",
        "BLUEPRINT_CAPTURE_DIR",
        "BLUEPRINT_BATCH_SOURCE",
        "BLUEPRINT_ACCEPTANCE",
    ):
        env.pop(key, None)
    with (
        (task / "stdout.log").open("w") as stdout,
        (task / "stderr.log").open("w") as stderr,
    ):
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
    print("Launched isolated game:", process.pid)
