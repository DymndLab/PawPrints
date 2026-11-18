"""Audit the public file set and package; optionally make a clean source archive.

Reads working candidates by default; --staged audits the exact next commit.
Release packages and reachable Git history are always inspected.
Does not commit, push, or delete anything. Findings report names, not secret values.
"""

import argparse
import hashlib
import json
import re
import subprocess
import zipfile
import tomllib
from pathlib import Path, PurePosixPath

from acceptance import validate_release_archive

ROOT = Path(__file__).resolve().parents[1]
# Reviewed public artwork only; other generated/exported images stay private.
APPROVED_ARTWORK = {
    "images/listing/icon.jpg":
        "6d7b1fda2270c5ba065855e224063ce6f25071df5dcab4de5c1301c4fda6db1a",
}
PRIVATE_DIRS = {
    ".agents",
    ".claude",
    ".codex",
    ".git",
    ".local",
    ".tools",
    "artifacts",
    "output",
    "dist",
    "exports",
    "node_modules",
    "bin",
    "obj",
    "fixtures",
    "tmp",
    "__pycache__",
    ".ruff_cache",
    ".pytest_cache",
    "logos",
    "release-private",
    "proof",
    ".vs",
    ".idea",
    ".vscode",
    "saves",
    "vehicles",
    ".venv",
    "venv",
}
PRIVATE_NAMES = {
    "starmapconfig.json",
    "title-settings.json",
    "export-settings.json",
    "agents.md",
    "claude.md",
    ".mcp.json",
}
SECRETS = re.compile(
    rb"(?:gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}|"
    rb"AKIA[A-Z0-9]{16}|sk-[A-Za-z0-9]{35,}|"
    rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)"
)
USER_PATH = re.compile(
    rb"(?i)(?:[a-z]:[/\\]+Users[/\\]+[^/\\\s<>\"]+|/home" rb"/[^/\s<>\"]+)"
)


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def candidates():
    paths = set(git("ls-files", "-z").decode().split("\0"))
    paths.update(
        git("ls-files", "--others", "--exclude-standard", "-z").decode().split("\0")
    )
    return sorted(p for p in paths if p and (ROOT / p).is_file())


def commit_identity_warnings(log):
    """Flag public contact metadata without copying addresses into audit output."""
    warnings = []
    for row in log.splitlines():
        commit, author, committer = row.split("\0")
        roles = []
        for role, email in (("author", author), ("committer", committer)):
            normalized = email.lower()
            if not normalized.endswith("@users.noreply.github.com") and normalized not in {
                "noreply@github.com", "web-flow@users.noreply.github.com"
            }:
                roles.append(role)
        if roles:
            warnings.append({
                "commit": commit,
                "reason": "non-noreply commit email; review identity exposure",
                "fields": roles,
            })
    return warnings


def scan(label, data, findings):
    # Compiled strings can be either UTF-8 or UTF-16LE. The package must not
    # expose the local username even in PDB/debug directory metadata.
    compact = data.replace(b"\0", b"")
    for name, pattern in (
        ("credential pattern", SECRETS),
        ("absolute user path", USER_PATH),
    ):
        if pattern.search(data) or pattern.search(compact):
            findings.append({"file": label, "reason": name})
    username = Path.home().name.encode()
    if len(username) >= 4 and username.lower() in compact.lower():
        findings.append({"file": label, "reason": "local account name"})


def audit_package_licenses(archive, provenance, contents, findings):
    """Require original notices and exact OFL files beside the shipped assembly."""

    def verify(name, expected):
        path = "PawPrints/" + name
        try:
            actual = archive.read(path)
        except KeyError:
            findings.append(
                {"file": path, "reason": "required license/provenance missing"}
            )
            return
        if hashlib.sha256(actual).hexdigest().lower() != expected.lower():
            findings.append(
                {"file": path, "reason": "license/provenance hash mismatch"}
            )

    for name in ("LICENSE", "THIRD-PARTY-NOTICES.txt"):
        verify(name, hashlib.sha256(contents(name)).hexdigest())
    for font in provenance["fonts"]:
        for record in font["files"]:
            if record["file"] == "OFL.txt":
                verify(f"Font-Licenses/{font['slug']}-OFL.txt", record["sha256"])
    # JSON line endings do not change the provenance, but every value must agree.
    path = "PawPrints/Font-Licenses/source.json"
    try:
        if json.loads(archive.read(path)) != provenance:
            findings.append(
                {"file": path, "reason": "packaged font provenance differs from source"}
            )
    except (KeyError, ValueError, UnicodeError):
        findings.append({"file": path, "reason": "missing or invalid font provenance"})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-zip", action="store_true")
    parser.add_argument(
        "--staged",
        action="store_true",
        help="Inspect exact index blobs and reject unreviewed working changes",
    )
    args = parser.parse_args()
    version = re.search(
        r'const\s+string\s+Version\s*=\s*"([^"]+)"',
        (ROOT / "src/Rendering/PageLayout.cs").read_text(encoding="utf-8"),
    ).group(1)
    findings = []
    if args.staged:
        entries = [
            row.split("\t", 1)
            for row in git("ls-files", "--stage", "-z").decode().split("\0")
            if row
        ]
        paths = sorted(name for _, name in entries)
        for metadata, name in entries:
            mode, _, stage = metadata.split()
            if mode not in {"100644", "100755"} or stage != "0":
                findings.append(
                    {
                        "file": name,
                        "reason": "linked, submodule or unresolved index entry",
                    }
                )
        if git("diff", "--name-only").strip():
            findings.append(
                {"file": "index", "reason": "unstaged tracked changes remain"}
            )
        if git("ls-files", "--others", "--exclude-standard").strip():
            findings.append(
                {"file": "index", "reason": "untracked public files remain"}
            )
    else:
        paths = candidates()

    def contents(name):
        return git("show", ":" + name) if args.staged else (ROOT / name).read_bytes()

    ignored = subprocess.run(
        ["git", "check-ignore", "--no-index", "--stdin", "-z"],
        input=("\0".join(paths) + "\0").encode(),
        capture_output=True,
        cwd=ROOT,
    )
    if ignored.returncode not in (0, 1):
        raise RuntimeError("Could not validate ignore rules")
    for name in filter(None, ignored.stdout.decode().split("\0")):
        findings.append({"file": name, "reason": "ignored file in public candidates"})
    for name in paths:
        path = ROOT / name
        parts = {part.lower() for part in PurePosixPath(name).parts}
        if path.is_symlink() or not path.resolve().is_relative_to(ROOT):
            findings.append(
                {"file": name, "reason": "linked file outside public source"}
            )
            continue
        if parts & PRIVATE_DIRS or path.name.lower() in PRIVATE_NAMES:
            findings.append(
                {"file": name, "reason": "private or linked file in public candidates"}
            )
        if path.suffix.lower() in {
            ".code-workspace",
            ".pdb",
            ".dll",
            ".exe",
            ".pem",
            ".pfx",
            ".p12",
            ".key",
            ".log",
            ".zip",
            ".dmp",
            ".mdmp",
            ".7z",
        }:
            findings.append(
                {"file": name, "reason": "unexpected binary, key, log or archive"}
            )
        data = contents(name)
        if name in APPROVED_ARTWORK:
            if hashlib.sha256(data).hexdigest() != APPROVED_ARTWORK[name]:
                findings.append(
                    {"file": name, "reason": "reviewed artwork hash mismatch"}
                )
        elif path.suffix.lower() != ".ttf":
            try:
                data.decode("utf-8")
            except UnicodeError:
                findings.append(
                    {"file": name, "reason": "unexpected non-text source artifact"}
                )
        scan(name, data, findings)
    provenance = json.loads(contents("assets/fonts/source.json"))
    for font in provenance["fonts"]:
        for record in font["files"]:
            name = f"assets/fonts/{font['slug']}/{record['file']}"
            if (
                name not in paths
                or hashlib.sha256(contents(name)).hexdigest().lower()
                != record["sha256"].lower()
            ):
                findings.append(
                    {"file": name, "reason": "font provenance hash mismatch"}
                )
    # Keep the remaining dependency graph explicit; additions need review.
    approved_dependencies = {
        "BCnEncoder.Net": "2.3.0",
        "CommunityToolkit.HighPerformance": "8.4.0",
    }
    lock = json.loads(contents("src/packages.lock.json"))
    for packages in lock["dependencies"].values():
        for name, record in packages.items():
            if approved_dependencies.get(name) != record["resolved"]:
                findings.append(
                    {
                        "file": "src/packages.lock.json",
                        "reason": "dependency requires release review",
                    }
                )
    # Email visibility is a privacy choice, not necessarily a leaked credential.
    # Report it separately so an intentional public contact need not rewrite history.
    privacy_warnings = commit_identity_warnings(
        git("log", "--all", "--format=%H%x00%ae%x00%ce").decode("utf-8")
    )
    # Scan all reachable historical blobs. The source archive uses working files.
    for row in git("rev-list", "--objects", "--all").decode().splitlines():
        oid, _, name = row.partition(" ")
        if git("cat-file", "-t", oid).strip() == b"blob":
            scan("history/" + name, git("cat-file", "blob", oid), findings)
    package = ROOT / "dist" / f"PawPrints-{version}-preview.zip"
    with zipfile.ZipFile(package) as archive:
        validate_release_archive(archive)
        files = [i for i in archive.infolist() if not i.is_dir()]
        permitted_dlls = {
            "PawPrints.dll",
            "BCnEncoder.dll",
        }
        names = [item.filename.casefold() for item in files]
        if len(names) != len(set(names)):
            findings.append(
                {"file": package.name, "reason": "duplicate archive member"}
            )
        for item in files:
            name = PurePosixPath(item.filename)
            if (
                name.is_absolute()
                or ".." in name.parts
                or name.parts[0] != "PawPrints"
                or "\\" in item.filename
                or ":" in item.filename
                or (item.external_attr >> 16) & 0o170000 == 0o120000
            ):
                findings.append(
                    {"file": item.filename, "reason": "unsafe package path"}
                )
            if name.suffix.lower() == ".dll" and name.name not in permitted_dlls:
                findings.append(
                    {"file": item.filename, "reason": "unapproved assembly"}
                )
            if (
                name.suffix.lower()
                in {".code-workspace", ".pdb", ".exe", ".xml", ".png", ".webp"}
                or name.name.lower() in PRIVATE_NAMES
            ):
                findings.append(
                    {"file": item.filename, "reason": "private/unexpected package file"}
                )
            scan(item.filename, archive.read(item), findings)
        required = {
            "PawPrints/mod.toml",
            "PawPrints/part-support.json",
            "PawPrints/LICENSE",
            "PawPrints/THIRD-PARTY-NOTICES.txt",
            "PawPrints/PawPrints.deps.json",
            "PawPrints/INSTALL.md",
            "PawPrints/Logos/README.txt",
            "PawPrints/Font-Licenses/source.json",
        }
        required.update("PawPrints/" + name for name in permitted_dlls)
        required.update(
            f"PawPrints/Font-Licenses/{name}-OFL.txt"
            for name in (
                "mrdafoe",
                "kristi",
                "reeniebeanie",
                "nothingyoucoulddo",
                "zeyada",
                "labelleaurore",
            )
        )
        for extra in {i.filename for i in files} - required:
            findings.append({"file": extra, "reason": "unexpected package member"})
        for missing in required - set(archive.namelist()):
            findings.append(
                {"file": missing, "reason": "required package file missing"}
            )
        try:
            manifest = tomllib.loads(archive.read("PawPrints/mod.toml").decode("utf-8"))
            listing = tomllib.loads(
                contents("distribution/PawPrints.toml.in").decode("utf-8")
            )
            if (
                manifest.get("name") != "PawPrints"
                or manifest.get("StarMap", {}).get("EntryAssembly") != "PawPrints"
            ):
                findings.append(
                    {"file": "PawPrints/mod.toml", "reason": "incorrect mod identity"}
                )
            if listing.get("id") != "PawPrints" or listing.get("name") != "PawPrints":
                findings.append(
                    {
                        "file": "distribution/PawPrints.toml.in",
                        "reason": "listing identity differs from install folder",
                    }
                )
        except (KeyError, UnicodeError, tomllib.TOMLDecodeError):
            findings.append(
                {
                    "file": "PawPrints/mod.toml",
                    "reason": "missing or invalid mod/listing metadata",
                }
            )
        audit_package_licenses(archive, provenance, contents, findings)
        if "PawPrints/part-support.json" in archive.namelist() and archive.read(
            "PawPrints/part-support.json"
        ) != contents("part-support.json"):
            findings.append(
                {
                    "file": "PawPrints/part-support.json",
                    "reason": "packaged support definitions differ from source",
                }
            )
    report = {
        "version": version,
        "source": "index" if args.staged else "working tree",
        "source_files": len(paths),
        "package_files": len(files),
        "package_sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
        "findings": findings,
        "privacy_warnings": privacy_warnings,
    }
    report_path = ROOT / ".local/release-audit.json"
    report_path.parent.mkdir(exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    if findings:
        raise SystemExit("Public file audit failed; no source archive created.")
    if args.source_zip:
        destination = ROOT / "dist" / f"PawPrints-{version}-source.zip"
        with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
            for name in paths:
                archive.writestr("PawPrints/" + name, contents(name))
        print("Created clean source archive:", destination.name)


if __name__ == "__main__":
    main()
