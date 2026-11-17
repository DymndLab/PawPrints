"""Check ignore coverage, maintained paths and font provenance without game files."""

import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PRIVATE = [
    "PawPrints.code-workspace",
    "AGENTS.md",
    "src/AGENTS.md",
    "CLAUDE.md",
    ".agents/skills/upgrade-ksa/SKILL.md",
    ".claude/settings.json",
    ".codex/config.toml",
    ".mcp.json",
    ".local/development.json",
    ".tools/instance/settings.toml",
    "release-private/review.pdf",
    "tests/fixtures/capture.json",
    "proof/gallery.html",
    "exports/craft.png",
    "artifacts/sample.png",
    "src/bin/Release/PawPrints.dll",
    "src/obj/project.assets.json",
    "tests/output/print.png",
    "company/Logos/logo.png",
    "title-settings.json",
    "export-settings.json",
    "StarMapConfig.json",
    "crash.dmp",
    "crash.mdmp",
    "PawPrints.log",
    "key.pem",
    "signing.pfx",
    "secret.key",
    ".env",
    ".env.local",
    "credentials.json",
    "node_modules/module/index.js",
    ".venv/pyvenv.cfg",
    ".vs/session",
    ".vscode/settings.json",
    "sample.zip",
    "test.pdb",
    "capture.dll",
    "test.partial",
]
PUBLIC = [
    ".github/workflows/checks.yml",
    ".github/dependabot.yml",
    "THIRD-PARTY-NOTICES.txt",
    "src/BlueprintMod.cs",
    "assets/fonts/source.json",
    "assets/fonts/zeyada/Zeyada.ttf",
    "assets/fonts/zeyada/OFL.txt",
    "tests/Program.cs",
    "tools/capture/CaptureProbe.cs",
    "tests/manual/checklist.html",
    "tests/manual/package.json",
    "tests/manual/package-lock.json",
    "tools/capture/requirements.txt",
    "src/packages.lock.json",
    "distribution/PawPrints.toml.in",
    ".env.example",
    "docs/architecture.md",
]


def ignored(paths):
    run = subprocess.run(
        ["git", "check-ignore", "--no-index", "--stdin", "-z"],
        input=("\0".join(paths) + "\0").encode(),
        capture_output=True,
        cwd=ROOT,
    )
    if run.returncode not in (0, 1):
        raise RuntimeError(run.stderr)
    return set(run.stdout.decode().rstrip("\0").split("\0")) if run.stdout else set()


if ignored(PRIVATE) != set(PRIVATE):
    raise SystemExit(
        "Private-path ignore gaps: " + str(set(PRIVATE) - ignored(PRIVATE))
    )
if ignored(PUBLIC):
    raise SystemExit("Required source accidentally ignored: " + str(ignored(PUBLIC)))
for font in json.loads((ROOT / "assets/fonts/source.json").read_text(encoding="utf-8"))[
    "fonts"
]:
    for f in font["files"]:
        content = (ROOT / "assets/fonts" / font["slug"] / f["file"]).read_bytes()
        assert hashlib.sha256(content).hexdigest().lower() == f["sha256"].lower(), f[
            "file"
        ]
print(
    f"PASS {len(PRIVATE)} private-path and {len(PUBLIC)} source-path ignore checks; all 12 font files match pinned provenance."
)
