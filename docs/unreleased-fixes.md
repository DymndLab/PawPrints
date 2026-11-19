# Unreleased fixes

These source changes are not in the published 0.5.39 ZIP. They need a new versioned package and the acceptance checks below before release. Build and headless checks do not establish in-game acceptance.

## Vehicle switching while viewing a drawing

A reported bracket-key press switched control to a vehicle and exposed flight UI over the open PawPrints workspace. The visual sequence was intermittent and has not been reproduced under controlled conditions.

The source guard consumes the configured next/previous-vehicle press actions while PawPrints is enabled, open and a vehicle editor exists. It uses native key bindings, including remapped keys, after native dialog and text-input handling. Other actions and closed-workspace behavior remain unchanged.

The host probe checks the patch signature and 56 production-guard scenarios, including default brackets, remapped Ctrl+F8, modifiers, press/release/repeat and workspace/editor states. It does not install the patch or launch the game.

Before release, verify:

- Both vehicle-switch bindings are blocked while viewing or generating a drawing, including after focus changes and native dialogs.
- Literal brackets still work in text fields; console, settings, save dialogs and Ctrl+Alt+P still work.
- Remapped switching is blocked only in the open print workspace and works after returning to the normal editor or flight.
- The drawing and live craft remain intact. If flight UI still appears, investigate that separately rather than treating the guard as proof the original incident is fixed.

## Tessera solar cells

Exact surface rules cover `TesseraPanelA_Subpart_WallCells` and `TesseraPanelA_Subpart_HalfCells` from Tessera 1.1.1. Their frames and other panel surfaces keep structural treatment. The [modded part support guide](modded-part-support.md) explains local edits and contributions.

Focused checks exercise both embedded and external definitions, stock behavior and non-cell exclusions. Before release, export both Tessera panel sizes from the front, back and an oblique view. Verify cell texture detail, clean frames, material warnings and low/high-resolution output. Texture availability and visual fidelity still require an actual game capture.

## Delivery

Run native acceptance with the exact new ZIP in a disposable profile, with at most one KSA instance running. Do not replace files in an active profile or stop someone's game for testing. Keep the released 0.5.39 artifact unchanged. After publishing a tested new release, verify its hosted checksum and a Borea-managed update preserves profile settings, logos and exports.
