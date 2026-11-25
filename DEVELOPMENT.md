# Development

PawPrints is a Windows x64 game mod. The current refactor source targets KSA 2026.11.25.5949 and StarMap 0.5.4. Source builds use the .NET 10 SDK and Python 3.11 or newer. Node.js 22 or newer is needed only for the HTML checklist tests. Players need only the packaged mod, the game and its loader.

## Configure native prerequisites

Use your own installation directories:

```powershell
python tools/configure.py --game "D:\Games\Kitten Space Agency" --loader "D:\Tools\StarMap"
```

This writes ignored `.local/development.json` and `.local/Development.props`. Alternatively set `PAWPRINTS_GAME_DIR` and `PAWPRINTS_LOADER_DIR` in the shell; command-line MSBuild `GameDir` / `LoaderDir` properties also work. The loader directory must contain the complete StarMap release, including `StarMap.API.dll` and `0Harmony.dll`.

```powershell
python tools/build.py --stage
```

The build checks the inspected KSA and toolkit hashes, restores locked dependencies, builds Release and writes `dist/PawPrints-<version>-preview.zip`. Its only install root is `PawPrints/`. No game is launched or updated by `--stage`. Build outputs and packages stay ignored. The main KSA and toolkit assembly fingerprints are intentional compatibility gates, not automatic update detectors to bypass.

## Portable checks

Run from the repository root on Windows. These checks use synthetic geometry and temporary images; they require no KSA install, extracted assets or private capture files.

```powershell
dotnet run --project tests/Blueprint.Tests.csproj -c Release -p:RestoreLockedMode=true
python tools/repository_checks.py
python -m unittest discover -s tests -p "test_*.py"
npm --prefix tests/manual ci --ignore-scripts
npm --prefix tests/manual test
```

The Python checks exercise archive extraction guards, process detection, fixture paths and side-effect-free help commands. They do not launch the game.

`.github/workflows/checks.yml` runs the portable checks on Windows for pull requests and pushes to `main`, using .NET 10, Python 3.12 and Node 22. It has read-only repository permissions, uses pinned Actions, and needs no game installation or repository secrets. It does not publish releases or certify native/UI acceptance. Dependabot proposes monthly updates for the pinned Actions and checklist dependencies; review and merge them normally.

After the workflow is committed and has completed once on GitHub, protect `main` and require **Portable checks**. Do not require a check that has never run. For a solo-maintainer project, require a pull request with passing checks but do not require another person's approval. Keep force pushes and branch deletion disabled. Repository setup and release steps are in [distribution/README.md](distribution/README.md).

The browser checklist and its Node test dependencies are contained in `tests/manual/`. Open [checklist.html](tests/manual/checklist.html) directly to record manual results across sessions; Node is needed only to test the checklist itself.

The test project is a console runner, not xUnit; `dotnet test` is not the entry point. The default runs all portable suites. Run a focused suite by appending `-- --foundation`, `-- --groups`, `-- --free-text`, `-- --drawing-placement`, `-- --exploded`, `-- --performance`, `-- --poses`, `-- --panels`, `-- --border-layout`, `-- --live-preview`, `-- --logos`, `-- --scale-references`, `-- --input`, `-- --templates`, `-- --part-support` or `-- --profile-storage`. Outputs are in ignored `tests/output/`. The foundation suite covers stale/cancelled jobs, accepted-state recovery, source bindings and internal draft schemas.

The optional `tests/host-probe/HostProbe.csproj` checks the actual game metadata and runs the production menu transpiler over real host IL without installing patches or starting the game. It requires explicit game, loader and built mod paths; it is separate from portable CI. Its success does not establish native editor or GPU behavior.

With native prerequisites configured, run `python tools/check_ui_text.py`. This headless ImGui probe checks literal percent text, native call guards and bounded private diagnostics without opening KSA. Its optional `--reproduce-unsafe` deliberately crashes a separate child probe to reproduce the unsafe native call; it is not a release check.

## Optional captured-asset checks

Version-matched private fixtures belong in `tests/fixtures/`. Never publish extracted game data or another player's craft/logo. The captured-asset suite requires:

- `captures/materials/lander-1.materials.json` and its referenced texture files;
- `captures/lander-1-native-annotation-base.png` and `captures/annotation-studies/part-catalog.json`;
- `lander-exploded-hierarchy.json` beside `captures/`.

```powershell
dotnet tests/bin/Release/net10.0/Blueprint.Tests.dll --fixtures tests/fixtures/captures
```

Missing fixtures fail explicitly. This suite checks accepted linework, PNG round trips, annotations, title layouts and real assembly hierarchy. It is additional to the portable suite, not a prerequisite for a clean source build.

The optional `tools/capture/CaptureProbe.csproj` is a separate development mod whose runtime guard still requires KSA 5901. Use matching 5901 game and loader paths for this historical capture workflow. `python tools/capture/prepare_stock_capture.py` prepares an isolated stock-craft profile; add `--launch` only while KSA is closed. `tools/capture/enrich_materials.py` reconstructs authored UV/normal/texture data from the matching installed game. Install its optional dependencies with `python -m pip install -r tools/capture/requirements.txt`. Capture output stays under `tests/fixtures/captures/`. Supply a matching Lander capture and reviewed reference image before running visual comparisons.

## Test the actual release package

A source build alone does not validate a distribution artifact. Audit the ZIP, then extract it into a fresh directory and run the package probe against that directory and locally generated PNG samples:

```powershell
dotnet build tests/package-probe/PackageProbe.csproj -c Release -p:RestoreLockedMode=true
dotnet tests/package-probe/bin/Release/net10.0/PackageProbe.dll <extracted-PawPrints-folder> <sample.png>
```

The probe exercises Windows PNG decoding through the packaged mod and loads all six bundled fonts. It also verifies each embedded font and packaged OFL notice against the pinned font provenance. It does not launch KSA.

Company logos use the built-in Windows PNG decoder through WIC. Image resizing and PNG export are implemented in the renderer; no additional image-codec DLL is packaged. Run the logo suite after changing image validation or resampling.

`python tools/acceptance.py` prepares a disposable StarMap profile and explicitly launches one game with a separate QA driver. The default drawing scenario can be replaced by `--performance` or `--poses`; `--hold` leaves the game open after completion. `--package <release.zip>` installs and tests an exact copy of the supplied archive after preparing the test profile. The test-only driver is never included in the release. The native suite additionally needs a local `.local/vehicles/Lander_1/vehicle.xml`; stock Gemini7 and Rocket are copied from the installed game. First-use folder selection, focus/keyboard behavior and general UI acceptance still require manual checks in [the manual checklist](tests/manual/checklist.html).

Close KSA before running native acceptance. The launcher refuses to start a second instance. Record failed runs alongside successful ones. The drawing fixture supplies its own projection coordinates and ignores physical pointer hover during placement.

## Publication checks

```powershell
python tools/release_audit.py --source-zip
# Review and stage the intended public files, then:
python tools/release_audit.py --staged --source-zip
git diff --cached --check
```

The audit checks common credentials and local-account paths in source, reachable history and package binaries; excludes private/generated files; validates font hashes and the exact package allowlist. `--staged` reads index blobs and refuses unstaged or untracked public changes. It creates the source ZIP only after passing. It is a guard against common leaks, not a proof of security against every possible attack.

Extract that source ZIP into a new directory without copying `.local`, outputs or caches. Run the portable checks there, then build using explicit prerequisite environment variables. Install the final ZIP itself in the disposable profile. After publishing, download the GitHub asset, compare its SHA-256, and repeat the package/game checks before enabling the Borea listing. See [distribution metadata](distribution/README.md).

## Code conventions

The [architecture map](docs/architecture.md) explains ownership boundaries. C# formatting follows `.editorconfig`; use `dotnet format whitespace <project> --no-restore` after restoring. Python tooling uses Black-compatible formatting. Explain game API assumptions and coordinate systems; keep UI state transitions readable. Keep logs, exported prints, dumps and review artifacts local.

## Part support definitions

See [Modded part support](docs/modded-part-support.md) for local installation steps, finding the correct subpart IDs, and contributing rules with regression checks.

Edit the root `part-support.json` to add surface rules or fallback labels. The same file is packaged beside `PawPrints.dll` and embedded as a recovery default. Each Generate PawPrint reads a fresh immutable copy from that DLL's directory. Existing captures keep their original rules; normal live appearance updates do not reload them. No recompilation is needed to edit the installed file. Mod-manager updates may replace that installed copy, so keep custom definitions backed up or contribute them upstream.

Each rule selects exactly one of `templateId` (exact), `templatePrefix` or `templateContains`. Matches are literal and case-sensitive. For each setting, exact matches win; otherwise the first matching pattern in file order wins. A rule may specify `surface` (`structure`, `solar`, `foil`), `fallbackLabel`, or both. A native display name always wins over a fallback label. Unknown parts receive ordinary structure treatment and readable names. `labelCategories` controls which mechanically formatted name categories are retained as prefixes.

```json
{"templateId": "ExampleMod_HeliosCell", "surface": "solar", "fallbackLabel": "Helios array"}
```

Append that object to `rules` for the actual template ID being supported. The shipped rules preserve existing stock behavior. Missing files use the embedded bundled rules; invalid files reject generation through the normal warning popup while retaining the previous print. Parsing has byte, depth, rule-count and string-length limits; unknown fields, duplicate selectors and unsupported versions are rejected. This config describes naming and surface treatment, not arbitrary game APIs or custom renderers. Real third-party part packs still require native compatibility testing.

## Large-craft capture measurements

Capture diagnostics in the profile log report observed exterior and interior vertices, triangles, mesh primitives and distinct part IDs, duration, the ten most vertex-heavy template IDs, and any geometry or material budget failure. Counts on failure include the primitive that crossed a limit, not geometry that was never visited; internal counts may be partial if detail was omitted. Template IDs can identify installed part packs: review the log before sharing it.

Capture shares detached source vertices, normals, UVs and triangle indices across mesh instances. The 0.6 renderer checks geometry phases against 512 MiB and includes retained drawings in a combined 2 GiB managed admission estimate. Source, raster and material limits also apply. These estimates cover mod work. Process measurements include KSA, its assets and other allocations. Native capture still runs synchronously on the editor thread.
