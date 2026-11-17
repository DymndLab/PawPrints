"""Save machine-specific prerequisites without adding them to Git."""

import argparse
import json
from pathlib import Path
from xml.etree.ElementTree import Element, ElementTree, SubElement

p = argparse.ArgumentParser()
p.add_argument("--game", required=True, type=Path)
p.add_argument("--loader", required=True, type=Path)
a = p.parse_args()
for directory, filename in (
    (a.game, "KSA.dll"),
    (a.loader, "StarMap.API.dll"),
    (a.loader, "0Harmony.dll"),
    (a.loader, "StarMap.exe"),
):
    if not (directory / filename).is_file():
        p.error(f"Missing {directory/filename}")
local = Path(__file__).resolve().parents[1] / ".local"
local.mkdir(exist_ok=True)
values = {"game_dir": str(a.game.resolve()), "loader_dir": str(a.loader.resolve())}
(local / "development.json").write_text(
    json.dumps(values, indent=2) + "\n", encoding="utf-8"
)
root = Element("Project")
group = SubElement(root, "PropertyGroup")
SubElement(group, "GameDir").text = values["game_dir"]
SubElement(group, "LoaderDir").text = values["loader_dir"]
ElementTree(root).write(local / "Development.props", encoding="unicode")
print("Saved local game and loader configuration; no game files changed.")
