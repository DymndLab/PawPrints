"""Recover authored UVs and normals from matching installed assets for local fixtures.

Requires exact captured vertex count and index order; does not modify game data.
"""

import argparse
import hashlib
import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from configuration import game_dir, workspace_path
from gltf import read_gltf

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    "--craft", default="lander-1", help="Simple filename stem for generated fixtures"
)
parser.add_argument("--capture", type=Path, help="Captured geometry JSON")
args = parser.parse_args()
if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,79}", args.craft):
    parser.error(
        "--craft must be a simple name using letters, numbers, hyphens or underscores"
    )

# Parse --help before requiring optional capture dependencies or local game paths.
import texture2ddecoder
import zstandard

CORE = game_dir() / "Content/Core"
OUT = workspace_path(ROOT, ROOT / "tests/fixtures/captures/materials")
OUT.mkdir(parents=True, exist_ok=True)
capture = json.loads(
    (args.capture or OUT.parent / (args.craft + ".geometry.json")).read_text(
        encoding="utf-8"
    )
)

elements = {}
owners = {}
for f in CORE.glob("*Assets.xml"):
    for e in ET.parse(f).getroot():
        if e.get("Id"):
            elements[e.get("Id")] = e
            owners[e.get("Id")] = f.stem.removesuffix("Assets")
groups = {}
textures = {}
evidence = []


def accessor(doc, raw, index):
    a = doc["accessors"][index]
    v = doc["bufferViews"][a["bufferView"]]
    assert "sparse" not in a and not a.get("normalized", False)
    count = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    fmt = "<" + {5121: "B", 5123: "H", 5125: "I", 5126: "f"}[a["componentType"]] * count
    size = struct.calcsize(fmt)
    stride = v.get("byteStride", size)
    offset = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    rows = [
        list(struct.unpack_from(fmt, raw, offset + i * stride))
        for i in range(a["count"])
    ]
    return [r[0] for r in rows] if count == 1 else rows


def texture(ref):
    if ref in textures:
        return textures[ref]
    src = CORE / elements[ref].get("Path")
    data = src.read_bytes()
    assert data[:12] == b"\xabKTX 20\xbb\r\n\x1a\n"
    fmt, _, w, h, depth, layers, faces, levels, compression = struct.unpack_from(
        "<9I", data, 12
    )
    assert depth == 0 and layers == 0 and faces == 1
    offset, length, unpacked = struct.unpack_from("<QQQ", data, 80)
    raw = data[offset : offset + length]
    if compression == 2:
        raw = zstandard.ZstdDecompressor().decompress(raw, max_output_size=unpacked)
    else:
        assert compression == 0
    decode = {
        131: texture2ddecoder.decode_bc1,
        132: texture2ddecoder.decode_bc1,
        141: texture2ddecoder.decode_bc5,
        145: texture2ddecoder.decode_bc7,
        146: texture2ddecoder.decode_bc7,
    }[fmt]
    # Decoder returns BGRA; renderer reads this explicitly. Keep values unmodified.
    pixel = decode(raw, w, h)
    assert len(pixel) == w * h * 4
    name = ref + ".bgra"
    workspace_path(OUT, OUT / name).write_bytes(pixel)
    result = {
        "path": name,
        "width": w,
        "height": h,
        "vkFormat": fmt,
        "source": str(src),
        "sourceSha256": hashlib.sha256(data).hexdigest(),
    }
    textures[ref] = result
    return result


for m in capture["meshes"]:
    name = m["PartId"]
    prefix = name.split("_")[0]
    candidates = [
        key
        for key, e in elements.items()
        if e.tag in ("Part", "SubPart")
        and (name == key or (name.startswith(key) and name[len(key) :].isdigit()))
    ]
    candidates.sort(key=len, reverse=True)
    if not (CORE / f"Meshes/{prefix}_MeshAtlas.glb").exists():
        assert candidates, ("No template for captured part", name)
        prefix = owners[candidates[0]]
    if prefix not in groups:
        doc, raw = read_gltf(CORE / f"Meshes/{prefix}_MeshAtlas.glb")
        groups[prefix] = (doc, raw, {g["name"]: g for g in doc["meshes"]})
    doc, raw, meshes = groups[prefix]
    matches = []
    for key in candidates:
        for model in elements[key].findall("./Components/*"):
            if model.tag not in ("PartModel", "PartModelDynamic", "PartModelGlass"):
                continue
            meshref = model.find("Mesh").get("Ref")
            if meshref not in meshes:
                continue
            for p in meshes[meshref]["primitives"]:
                attrs = p["attributes"]
                if doc["accessors"][attrs["POSITION"]]["count"] != len(m["Vertices"]):
                    continue
                if accessor(doc, raw, p["indices"]) != m["Indices"]:
                    continue
                matches.append((key, model, attrs))
        if matches:
            break
    assert len(matches) == 1, (name, len(matches))
    key, model, attrs = matches[0]
    m["Template"] = key
    m["SourceVertices"] = accessor(doc, raw, attrs["POSITION"])
    m["SourceNormals"] = accessor(doc, raw, attrs["NORMAL"])
    m["UV"] = accessor(doc, raw, attrs["TEXCOORD_0"])
    kind = (
        "solar"
        if "SolarPanelA_CellA" in key
        else "foil" if any(tag in key for tag in ("Cloth", "Foil")) else "structure"
    )
    m["Surface"] = kind
    if kind != "structure":
        material = elements[model.find("Material").get("Ref")]
        m["NormalTexture"] = texture(material.find("Normal").get("Ref"))
        m["DiffuseTexture"] = texture(material.find("Diffuse").get("Ref"))
    evidence.append(
        {"instance": name, "template": key, "surface": kind, "topologyExact": True}
    )
(OUT / (args.craft + ".materials.json")).write_text(json.dumps(capture))
(
    OUT
    / (
        ("provenance" if args.craft == "lander-1" else args.craft + ".provenance")
        + ".json"
    )
).write_text(
    json.dumps(
        {
            "matches": evidence,
            "textures": textures,
            "method": "Exact captured vertex count and triangle-index order, existing template/mesh references; no geometry replacement.",
        },
        indent=2,
    )
)
print(
    f"Enriched {len(evidence)} captured meshes; decoded {len(textures)} authored textures."
)
