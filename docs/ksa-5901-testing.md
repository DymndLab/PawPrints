# KSA 5901 migration and testing handoff

PawPrints **0.5.39** targets **KSA 2026.11.16.5901**, Windows x64 and **StarMap 0.5.4**. Focused native acceptance passed on 2026-11-18 with the limits below. The old 0.5.38 package is for 5801 and must not be relabelled. This handoff is intended to let a follow-up agent execute the tests without rediscovering the migration.

## Verified scope, 2026-11-18

The tested runtime DLL SHA-256 is `9f199233e34297ef75c6193a23e72a8fa3475aa10a360f9404079d905cef3305`. Drawing, performance and pose runs used the same frozen ZIP (`b3880f20eabada79fb49c36bf6787ef7b2a849e066e53ca583a7265cfae44a71`); all 16 installed files were compared with it. Later packaging of updated installation notes must retain identical runtime bytes and receive a separate archive hash and package audit.

- **PASS:** nine drawing records and nine performance records across Lander_1, Gemini7 and Rocket; native thrust-rating comparison error zero, verified propellants, flows and main gimbals, multi-core/auxiliary-nozzle cases, undo/redo, regeneration and unchanged live transforms.
- **PASS:** four pose records covering Lander legs and Gemini cargo bays at sampled 0/50/100 positions; vertex errors below one micrometre and unchanged live transforms.
- **PASS:** visible pending/completed print isolation from native resources/staging; editor return/reopen; window maximize/restore; settings and vehicle save/load dialogs; console; literal `100%` title text; PNG save and image inspection.
- **PASS:** real Borea 0.6.22-created and launched disposable profile, with the frozen package manually installed and no QA driver. First-run folder selection, engineering-template generation, 2400x3000 PNG export and profile-local preferences/logs worked. This does not establish catalog installation or updates.
- **PASS:** 814 shared and 41 profile-storage checks, headless native text/focus regressions, all seven host patch contracts and 16 panel-ownership cases, package/font checks and repository/release audits.

**Not covered:** solid engines, fixed/asymmetric main-gimbal native fixtures, new habitat interiors, animated solar panels and detailed material comparisons; the complete native input/focus/failure matrix; other GPUs/DPI configurations and third-party parts; the full manual release checklist; Borea catalog installation/removal and an actual version upgrade. Portable or earlier-build results do not fill these gaps.

A native flight Resources-panel pulse was reported both with PawPrints and with its folder physically removed (Core only) in the same Borea instance. It later stopped; focus timing and a craft-stage change confound attribution. The cause remains unresolved. No speculative native flight-panel patch was added.

## Implementation and evidence

`PerformanceCapture` uses `EngineController.Rockets` and `RocketControllerData.ComputeFromRockets` for nominal full-throttle vacuum/ASL ratings. It traverses each rocket's cores and each core's nozzles, using the rocket's virtual `ComputeCoreConditions` method. The highest positive finite vacuum-thrust nozzle still supplies both gimbal angles from its own parent; fixed nozzles give zero. Propellant names remain distinct. No flight commands or live craft transforms were added.

The runtime/build/capture guards now require KSA.dll SHA-256 `170c28aa34845026195c89a23c0f3c241db00ea4a8b86ef60ec097c32a7f8a39`. The toolkit guard stays `196607c6d4d704b5ee95c80f8e3cd799595b7d8a08108701894c211b138d2839`. Unknown builds remain rejected. Historical `5801` Harmony owner IDs remain stable identifiers, not supported-version declarations.

The host probe checks all seven runtime patch target signatures and named parameter bindings, the private ApplyGimbal signature, and the actual Exit Editor menu call. It executes the production transpiler on real 5901 IL and requires that only the intended calls change. It does not install patches. Static inspection also found unchanged keyframe evaluation, texture-reference data and the reflected ApplyGimbal body. Preview texture allocation still builds mipmaps and registers/removes an ImGui texture, but the game's Vulkan backend changed to dynamic rendering: only live tests can establish GPU correctness.

## Before testing

Read `AGENTS.md` and `DEVELOPMENT.md`. Keep all evidence and game-derived data ignored. Do not stop a user's game, replace a running profile or launch a second KSA instance. Do not change other mods, publish, push or update the Borea listing as part of testing.

Use configured `.local/development.json` paths. Recheck the actual game version and both hashes above; if they differ, stop native testing and report that another host audit is needed. Never update a guard just to get a test running. The native suites require `.local/vehicles/Lander_1/vehicle.xml`; Gemini7 and Rocket come from the installed game. Missing fixture means blocked, not pass. Old captured-asset fixtures are not new-build evidence.

Keep a frozen copy of the candidate ZIP outside `dist/` before the first native run, record its SHA-256, and use that same file for every `--package` run. `acceptance.py` snapshots that archive before building its preparation files. It later overwrites `dist/`, so a newly built file there may not be the one tested. Any runtime source change requires rebuilding, freezing a new candidate and rerunning affected acceptance.

## Offline checks

Run from the repo root, record commands and exit codes, and stop on failure. Do not rerun already recorded successful checks unless files or prerequisites changed.

```powershell
dotnet run --project tests/Blueprint.Tests.csproj -c Release -p:RestoreLockedMode=true
python -m unittest discover -s tests -p "test_*.py"
python tools/repository_checks.py
npm --prefix tests/manual ci --ignore-scripts
npm --prefix tests/manual test
python tools/check_ui_text.py
dotnet build tests/Blueprint.Acceptance.csproj -c Release -p:RestoreLockedMode=true
dotnet build tools/capture/CaptureProbe.csproj -c Release -p:RestoreLockedMode=true
python tools/build.py --stage
python tools/release_audit.py
```

With `$gamePath` and `$loaderPath` set to the configured absolute paths:

```powershell
dotnet run --project tests/host-probe/HostProbe.csproj -c Release -p:RestoreLockedMode=true -- "$gamePath" "$loaderPath" src/bin/Release/net10.0/PawPrints.dll
```

Extract the frozen ZIP into a fresh ignored directory and run the package probe on its `PawPrints` folder and a locally generated PNG (see DEVELOPMENT.md). This verifies shipped fonts, definitions and Windows image decoding; it is not GPU acceptance. Recheck the archive hash after copying it.

## Native automated runs: one at a time

Use the alternate **pawprints-ui-check** profile (`--ui`), preserving the historical blueprint-runtime profile and its 5801 evidence. Before each scenario, verify no KSA process exists, then archive the existing `.tools/pawprints-ui-check` directory by moving it into a new ignored evidence folder. Verify source and destination absolute paths are inside this repository before a recursive move. Do not discard old logs or output. A fresh profile guarantees current stock crafts and prevents old JSON results from being mistaken for success.

Run the following separately, replacing `<frozen.zip>` with the recorded candidate:

```powershell
python tools/acceptance.py --ui --drawing --package <frozen.zip>
python tools/acceptance.py --ui --performance --package <frozen.zip>
python tools/acceptance.py --ui --poses --package <frozen.zip>
```

**The launcher returns after starting the game; exit code 0 means launched, not passed.** Wait for each game to finish before the next scenario. Check at intervals no longer than 60 seconds and report progress. If it hangs, inspect the current logs and report the state; do not start another instance. Keep the pointer outside the drawing canvas during automation because hover selects projection direction. Do not use `--hold` for these unattended runs.

For each run, record launch time/PID, installed game version/hash, `tested-package.sha256`, and current stdout/stderr. Confirm the installed mod files match the frozen archive. Require a newly created result JSON, no new `*-acceptance-error*.txt`, no PawPrints disabled/unhandled errors in logs, and inspect the generated PNGs. Archive the entire completed profile before the next run.

| Scenario | Result under `.tools/pawprints-ui-check/instance/` | Required evidence |
| --- | --- | --- |
| Drawing | `drawing-acceptance.json` | All three crafts (Lander_1, Gemini7, Rocket), projected/detail/exploded placement and generated PNGs. Current driver normally emits nine records; inspect the fields as well as count. |
| Performance | `performance-acceptance.json` | All three crafts; ratings, native mass/stage comparisons, both VAC/ASL flows, main-nozzle Y/Z, distinct propellant names, blocks/undo/redo/regeneration and unchanged native transforms. Read `engineContracts` to see what rocket/core/nozzle types were actually covered. |
| Poses | `pose-acceptance.json` | Both Lander_1 and Gemini7, available animation controls, view-specific poses and native transforms unchanged. No missing-control skip may be reported as exercised coverage. |

The performance driver now rejects nonfinite comparisons and records solid-rocket counts. If the stock/local craft set contains no solid engine, no fixed-nozzle engine, or no multiple-nozzle/controller case, explicitly retain that coverage gap and exercise it manually with a disposable craft. Do not invent coverage from a successful run.

## Focused visible acceptance

Use a clean disposable profile with only Core and PawPrints enabled. `python tools/build.py --profile pawprints-ui-check --launch` prepares a source-build UI run and parks the test driver; it does **not** promise the frozen ZIP. For final artifact acceptance, prepare without `--launch`, install the frozen ZIP contents into that disposable mod directory while closed, verify the bytes, and launch that profile using its prepared StarMap loader/configuration. No second game instance.

Record each result as PASS / FAIL / BLOCKED / NOT RUN, with version/hash and evidence:

- Open through Editor menu and Ctrl+Alt+P; empty editor disables opening. Check normal native keys, console/settings/popups, text editing, focus loss and the new native part-picking UI do not steal unintended input.
- First-use folder picker: cancel, choose a destination, save, reopen, change destination and fail gracefully when unavailable. User-supplied names/initials/logo preferences survive; save PNG reminders are visible.
- Generate all three crafts, zoom/resize, switch projections, enable live preview, edit while a render is pending and close/reopen repeatedly. No black/corrupt preview, GPU error, stale render replacing a newer edit, freeze or crash. Failed generation retains the previous print.
- Verify liquid and solid full-throttle VAC/ASL thrust/flow/Isp, fixed and asymmetric gimbals, auxiliary nozzles, propellant labels and native stage estimates. Use native part/controller data or the engine-contract evidence; do not compare nominal values with live throttle-limited thrust.
- Deploy/retract panels or other animated parts in the drawing only. Exercise internal/glass detail and an updated habitat interior. The actual craft, saves, fuel and native transforms must remain unchanged.
- Add text containing `100%`, PNG logos and scale references; export and reopen the PNG at full resolution. Check foil/solar materials and transparent glass against the game, not only old 5801 screenshots.
- Leave the editor, load another craft, close the game and restart. Check draft lifecycle, profile preferences and clean preview texture disposal. Repeat with a GPU/window resize. Pending edits are not persistently saved; export before leaving.
- Before publication, test the exact ZIP in a real Borea-managed instance as a separate recorded check. General release UI coverage remains in `tests/manual/checklist.html`; these focused checks do not mark the entire checklist passed.

## Completion record

Append dated results to ignored `release-private/release-review.md`, linking the frozen ZIP hash, commands/exit codes, scenario archives, PNGs and remaining manual items. Preserve failures and historical 5801 evidence. If runtime failures reproduce, fix the cause, add an appropriate regression and re-freeze/retest the changed artifact. Leave unrelated redesigns out of the migration.

After successful native/manual acceptance, remove the candidate/pending wording only where supported by the recorded results, rebuild if packaged documentation changed, and repeat exact-package verification. Keep Borea metadata unpublished until its release gates are met. Do not equate a successful build, launcher exit, old JSON, or headless probe with completed game acceptance.
