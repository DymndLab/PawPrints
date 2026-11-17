"""Read local GLB/glTF fixtures without modifying game data."""

import json
import struct
from pathlib import Path


def read_gltf(path):
    path = Path(path)
    raw = path.read_bytes()
    if path.suffix.lower() == ".glb":
        if len(raw) < 28 or raw[:4] != b"glTF":
            raise ValueError("Invalid GLB header")
        version, total, size, kind = struct.unpack_from("<IIII", raw, 4)
        if (
            version != 2
            or total != len(raw)
            or kind != 0x4E4F534A
            or size > len(raw) - 28
        ):
            raise ValueError("Invalid GLB JSON chunk")
        doc = json.loads(raw[20 : 20 + size])
        offset = 20 + size
        binary_size, kind = struct.unpack_from("<II", raw, offset)
        if kind != 0x004E4942 or binary_size > len(raw) - offset - 8:
            raise ValueError("Invalid GLB binary chunk")
        return doc, raw[offset + 8 : offset + 8 + binary_size]
    doc = json.loads(raw)
    uri = doc["buffers"][0]["uri"]
    if (
        not isinstance(uri, str)
        or any(c in uri for c in ("/", "\\", ":"))
        or uri in ("", ".", "..")
    ):
        raise ValueError("Fixture buffer must be a sibling file")
    buffer = path.parent / uri
    if buffer.resolve().parent != path.parent.resolve():
        raise ValueError("Fixture buffer resolves outside its folder")
    return doc, buffer.read_bytes()
