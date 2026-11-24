# PawPrints roadmap

Updated November 22, 2026. PawPrints 0.5.40 is in local testing. The items below are future work, with no promised release dates. They are separate from the implemented changes in the [0.5.40 testing scope](release-0.5.40-testing.md).

## Large stations and complex craft

Support craft that exceed the current one-million-vertex capture limit, including stations with many repeated components. Work includes capture diagnostics, shared source geometry, lower allocation costs, measured memory and rendering budgets, and responsive cancellation. Initial diagnostics and source sharing are in the next test candidate; the export ceiling remains unchanged. Full large-station support requires real-craft measurements and validation before a higher limit is offered.

## Craft updates and drawing recovery

Refresh drawings more naturally after editing the craft, preserve references to surviving parts, and explain annotations removed when their source parts disappear. Decide how missing detail sources should behave. The current recovery controls do not complete this workflow redesign.

## Layout and placement

- Allow intentional overlap and clipping, with a nonblocking notice when content extends beyond the sheet.
- Let empty space inside exploded details show underlying drawings.
- Show a centered, resizable view preview before placement and make selection feedback consistent.
- Add direct resizing for main views, with agreed controls for resizing in place or scaling the arrangement while preserving shared physical scale.
- Review how custom templates fit sheets with different aspect ratios.
- Give Select Views a sheet preview like the template thumbnails. Let the user drag a view to another slot and shift the intervening views into place.
- Check whether changing initials size shifts the writing to the right in the title block.

## Dimensions

Simplify placement to selecting a view and clicking to place the preview. Improve hover and selection legibility and use clearer Delete labels. Add custom dimensions between two selected points as a later extension.

## Tolerances

Add tolerances to individual dimensions and a configurable **General Tolerances** area in the title block. Explore symmetric plus/minus, unequal upper/lower deviations and limit dimensions, with explicit units and precision. Individual tolerances override general values. Formats and defaults need design review. Future work outside the current workflow refactor.

## Exploded views

Investigate actual attachment points and placement axes for guide origins and disassembly directions. Separate assembly-guide style from annotation-leader preferences. Revisit hoses whose endpoints separate during explosion; this is lower priority than assembled hose correctness.

## Contained parts in exploded views

Decide how to depict parts such as fuel tanks inside a cargo bay. Compare moving them outward from the bay, leaving them inside and hidden, and sliding them along the existing explosion axis. Try each on the same craft before choosing a default or deciding whether the behavior should be selectable.

## Sectional and cutaway views

Explore a user-defined cutting plane for revealing internal craft structure, enclosed engines and other components. Let the player position and rotate the plane, including angled cuts, choose which side remains visible, and preview the result in the drawing. The cut affects only that view and leaves the craft intact. This covers internal craft geometry; IVA cabin rendering is a separate question. Validate geometry availability, section linework and memory/performance costs before committing to implementation. Future work, outside the current workflow refactor scope.

## Paper wear patterns

Explore a small set of distinct archival looks, such as lightly aged paper, fold marks, handling scuffs, or a localized stain. Compare them across all paper themes in the fitted preview and in exported PNGs. Keep the drawing and title block legible at every wear level.

## Linework refinement

Reduce residual specks around curved transitions while preserving genuine shoulders, collars, boundaries and silhouettes across different craft.

## Native Linux support

Plan native Linux support alongside the workflow refactor, beginning with feasibility checks for the game/loader and replacements for Windows text rendering, logo decoding and dialogs. Preserve Windows behavior and require native Linux game/export testing before advertising support. Current releases remain Windows-only; no Linux release date is promised.

## Feedback

Please include the PawPrints footer version, KSA version, reproduction steps, and whether the issue affects the preview, exported PNG, or both. Review logs and craft files for private information before sharing them. Roadmap items describe intended improvements, not features already available in the current release.
