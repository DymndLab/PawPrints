"""Guard native printf-style UI calls and run a headless percent-text regression."""

import argparse
import re
import subprocess
from pathlib import Path

from configuration import game_dir

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument(
    "--reproduce-unsafe",
    action="store_true",
    help="Also crash an isolated probe using the original unsafe call; never touches KSA",
)
args = parser.parse_args()
unsafe = re.compile(
    r"ImGui\.(?:TextWrapped|SetTooltip|TextColored|TextDisabled|BulletText|LabelText|SetItemTooltip|SeparatorText)\s*\("
)
for path in (ROOT / "src").rglob("*.cs"):
    if path.name != "PrintUiText.cs" and unsafe.search(
        path.read_text(encoding="utf-8")
    ):
        raise SystemExit(f"Use the literal-text wrapper in {path.relative_to(ROOT)}")
print(
    "PASS UI source guard: no direct native formatted-text calls outside PrintUiText",
    flush=True,
)
project = ROOT / "tests/ui-text-probe/UiTextProbe.csproj"
subprocess.run(
    ["dotnet", "build", str(project), "-c", "Release", "-p:RestoreLockedMode=true"],
    cwd=ROOT,
    check=True,
)
command = [
    "dotnet",
    str(project.parent / "bin/Release/net10.0/UiTextProbe.dll"),
    str(game_dir()),
]
subprocess.run(command, cwd=ROOT, check=True, timeout=30)
if args.reproduce_unsafe:
    result = subprocess.run(
        command + ["--unsafe"], cwd=ROOT, capture_output=True, timeout=30
    )
    evidence = ROOT / ".local/ui-text-regression"
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "original-call.log").write_bytes(result.stdout + result.stderr)
    code = result.returncode & 0xFFFFFFFF
    if code != 0xC0000409:
        raise SystemExit(f"Unexpected unsafe probe exit: 0x{code:08x}")
    print("PASS original call reproduces 0xc0000409 in the isolated probe")
