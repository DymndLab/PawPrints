# PawPrints 0.5.40 testing

The next local candidate is **0.5.40-local.4**, targeting KSA 5901 and StarMap 0.5.4 on Windows x64. The intended public version is **0.5.40**. This candidate combines implemented work; it is not yet a released or fully accepted build. [Future work](roadmap.md) is tracked separately.

## Included for testing

- Cleaner curved-part linework and solar sketch shading, including active-face selection and Tessera solar-cell support.
- Procedural fuel hoses in assembled drawings, with existing pose and exploded-view limitations.
- Restore previous print and New drawing recovery controls, plus fitting after page changes.
- Dimension selection/deletion, shared-scale limit explanations, text-tooltip layering, and the vehicle-switch shortcut guard.
- Title-linked or custom vehicle names in performance blocks.
- Exploded assembly-guide preference updates.
- Capture diagnostics, reuse of detached source mesh arrays across repeated instances, an estimated geometry-memory guard, and additional renderer cancellation checks.

The one-million-vertex limit remains. Large-station export is not a supported new feature. The 512 MiB geometry estimate is a separate guard, not a measured peak-memory guarantee; it excludes page buffers and other game memory and may reject index-heavy craft that previously passed the vertex check. Validate this behavior before release.

## Release checks

Continue the existing local checklist at scale/layout, text/keyboard, and export/stability. Earlier local.3 results remain evidence for that build; changed capture behavior needs checks on this candidate.

- Reproduce the reported craft-edit recovery failure: deleting the obsolete label still left generation blocked. Resolve or explicitly disposition this finding before release; do not describe recovery as fully fixed while it persists.
- Reproduce the reported exploded-guide undo/redo failure, with and without ordinary note leaders. Verify style persistence and export.
- Generate and export ordinary craft with repeated parts, solar panels and hoses. Check materials, labels, picking, print-only poses and exploded views after source sharing.
- Attempt the oversized station capture. Check that the warning reports observed counts and the threshold, diagnostics survive failure, and an existing drawing remains usable. Counts at early rejection are partial, not a full station census.
- Check capture/render duration and memory on representative craft, including an index-heavy case. No benchmark result or safe higher limit has been established.
- Complete shared-scale/layout, text/keyboard and mixed-content PNG checks; compare the saved image with the preview.
- Verify the exact final package, repository/package audits, and package/font checks before publication. Record the candidate version and hash with native results.

## Testing profile

Use the existing Borea Default instance for the player's manual testing. Its installed runtime was verified as local.3 on November 22; staging local.4 does not install it. Borea's catalog version can still describe the public package rather than the privately installed test runtime: verify the drawing footer. Updating or reinstalling through Borea may replace that private build.

Save PNGs before ending the game. Never replace files while KSA is running or launch a second instance for tests. Automated native acceptance uses its separate disposable profile only while KSA is closed.
