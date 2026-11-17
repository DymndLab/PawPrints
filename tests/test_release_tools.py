"""Release-tool regressions; no game launches or installed-profile writes."""

import contextlib
import io
import json
import os
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import acceptance
import configuration
from release_audit import commit_identity_warnings
from gltf import read_gltf

MANIFEST = b'name = "PawPrints"\n[StarMap]\nEntryAssembly = "PawPrints"\n'


def archive(extra=(), manifest=MANIFEST):
    data = io.BytesIO()
    with zipfile.ZipFile(data, "w", zipfile.ZIP_DEFLATED) as output:
        output.writestr("PawPrints/mod.toml", manifest)
        output.writestr("PawPrints/PawPrints.dll", b"synthetic test data")
        for name, content in extra:
            output.writestr(name, content)
    raw = data.getvalue()
    # zipfile normalizes backslashes when writing on Windows. Put the literal
    # malformed name into both ZIP headers to exercise downloaded archives.
    for name, _ in extra:
        if isinstance(name, str) and "\\" in name:
            raw = raw.replace(name.replace("\\", "/").encode(), name.encode())
    return zipfile.ZipFile(io.BytesIO(raw))


class ReleaseToolsTests(unittest.TestCase):
    def test_commit_contact_warning_is_redacted(self):
        address = "creator@example.invalid"
        warnings = commit_identity_warnings("abc123\0" + address + "\0" + address)
        self.assertEqual(warnings[0]["fields"], ["author", "committer"])
        self.assertEqual(warnings[0]["commit"], "abc123")
        self.assertNotIn(address, json.dumps(warnings))

    def test_github_noreply_commit_identities(self):
        self.assertEqual(commit_identity_warnings(
            "abc123\0" + "123+Creator@users.noreply.github.com\0noreply@github.com"
        ), [])
        warnings = commit_identity_warnings(
            "abc123\0creator@users.noreply.github.com\0contact@example.invalid"
        )
        self.assertEqual(warnings[0]["fields"], ["committer"])

    def test_valid_archive_and_directory(self):
        with archive([("PawPrints/Logos/", b"")]) as source:
            self.assertEqual(len(acceptance.validate_release_archive(source)), 2)

    def test_windows_path_rejections(self):
        for name in [
            "../outside/",
            "/PawPrints/file",
            "PawPrints/../escape/",
            "PawPrints/a\\b",
            "PawPrints/stream:secret",
            "PawPrints//empty/",
            "PawPrints/./empty/",
            "PawPrints/trailing. ",
            "PawPrints/AUX.txt",
            "PawPrints/COM1",
            "PawPrints/LPT\u00b9.txt",
            "OtherMod/file",
            "PawPrints/control\x01",
            "PawPrints/wildcard*",
            "PawPrints/a./file",
        ]:
            with self.subTest(name=name), archive([(name, b"")]) as source:
                with self.assertRaises(ValueError):
                    acceptance.validate_release_archive(source)

    def test_case_alias_duplicate(self):
        with archive([("PawPrints/PAWPRINTS.DLL", b"")]) as source:
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_file_directory_collision(self):
        with archive(
            [("PawPrints/item", b""), ("PawPrints/item/child", b"")]
        ) as source:
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_symlink_rejected(self):
        entry = zipfile.ZipInfo("PawPrints/link")
        entry.external_attr = 0o120777 << 16
        with archive([(entry, b"outside")]) as source:
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_large_uncompressed_claim_rejected(self):
        with archive() as source:
            source.infolist()[1].file_size = 129 * 1024 * 1024
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_excess_entries_rejected(self):
        with archive([(f"PawPrints/item{i}", b"") for i in range(127)]) as source:
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_wrong_entry_assembly_rejected(self):
        with archive(
            manifest=MANIFEST.replace(
                b'EntryAssembly = "PawPrints"', b'EntryAssembly = "Other"'
            )
        ) as source:
            with self.assertRaises(ValueError):
                acceptance.validate_release_archive(source)

    def test_process_query_failure_is_not_a_closed_game(self):
        for result in [(1, ""), (0, ""), (0, "unexpected"), (0, "-1"), (0, "2")]:
            with self.subTest(result=result), mock.patch.object(
                configuration.subprocess,
                "run",
                return_value=subprocess.CompletedProcess([], result[0], result[1]),
            ):
                with self.assertRaises(SystemExit):
                    configuration.assert_no_game()
        for error in [OSError("missing shell"), subprocess.TimeoutExpired("probe", 15)]:
            with mock.patch.object(configuration.subprocess, "run", side_effect=error):
                with self.assertRaises(SystemExit):
                    configuration.assert_no_game()

    def test_confirmed_closed_game(self):
        with mock.patch.object(
            configuration.subprocess,
            "run",
            return_value=subprocess.CompletedProcess([], 0, "0\n"),
        ):
            configuration.assert_no_game()

    def test_workspace_containment(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.assertEqual(
                configuration.workspace_path(root, root / "child"),
                root.resolve() / "child",
            )
            for path in [root, root.parent / "outside", root / ".." / "outside"]:
                with self.assertRaises(ValueError):
                    configuration.workspace_path(root, path)
            with mock.patch.object(Path, "is_symlink", return_value=True):
                with self.assertRaises(ValueError):
                    configuration.workspace_path(root, root / "linked")

    def test_bad_archive_cannot_start_preparation(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bad.zip"
            path.write_bytes(b"not a zip")
            with mock.patch.object(acceptance, "assert_no_game"), mock.patch.object(
                acceptance.subprocess, "run"
            ) as run, contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit):
                    acceptance.main(["--package", str(path)])
                run.assert_not_called()

    def test_cli_help_has_no_prerequisites_or_side_effects(self):
        commands = [
            "acceptance.py",
            "build.py",
            "configure.py",
            "check_ui_text.py",
            "release_audit.py",
            "capture/prepare_stock_capture.py",
            "capture/enrich_materials.py",
        ]
        env = os.environ.copy()
        env["PAWPRINTS_GAME_DIR"] = "nonexistent-game"
        env["PAWPRINTS_LOADER_DIR"] = "nonexistent-loader"
        for script in commands:
            with self.subTest(script=script):
                result = subprocess.run(
                    [sys.executable, str(ROOT / "tools" / script), "--help"],
                    capture_output=True,
                    text=True,
                    env=env,
                    timeout=15,
                )
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("usage:", result.stdout.lower())

    def test_glb_lengths_and_sibling_buffer(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            data = b"1234"
            document = b'{"buffers":[{"byteLength":4}]}   '
            document += b" " * (-len(document) % 4)
            raw = b"glTF" + struct.pack("<II", 2, 28 + len(document) + len(data))
            raw += struct.pack("<II", len(document), 0x4E4F534A) + document
            raw += struct.pack("<II", len(data), 0x004E4942) + data
            path = root / "test.glb"
            path.write_bytes(raw)
            self.assertEqual(read_gltf(path)[1], data)
            path.write_bytes(raw[:-1])
            with self.assertRaises(ValueError):
                read_gltf(path)
            path = root / "test.gltf"
            (root / "local.bin").write_bytes(data)
            path.write_text(json.dumps({"buffers": [{"uri": "local.bin"}]}))
            self.assertEqual(read_gltf(path)[1], data)
            for uri in [
                "../outside.bin",
                "C:\\outside.bin",
                "http://example.invalid",
                "",
            ]:
                path.write_text(json.dumps({"buffers": [{"uri": uri}]}))
                with self.assertRaises(ValueError):
                    read_gltf(path)


if __name__ == "__main__":
    unittest.main()
