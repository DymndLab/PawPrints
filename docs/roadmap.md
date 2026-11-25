# PawPrints roadmap

The 0.6 candidate implements shared geometry, bounded station rendering, automatic craft refresh and editable projects. The [candidate release notes](../distribution/RELEASE-NOTES.md) describe the current workflow.

## Large stations and complex craft

The shared candidate exported a 1,729-part station with 3.84 million captured triangles in 4K Normal and Detailed on Windows. Remaining performance work is to measure interactive capture on other large craft and editing responsiveness with several large views on one sheet.

## Craft updates and drawing recovery

Explore reopening structurally edited craft across sessions when saved part references can be reconciled explicitly.

## Layout and placement

- Review how custom templates fit sheets with different aspect ratios.
- The local.36 preview includes a draggable layout in Select Views. Check the group move, unlink prompt and saved arrangement in game.
- Check whether changing initials size shifts the writing to the right in the title block.

## Dimensions

Simplify placement to selecting a view and clicking to place the preview. Improve hover and selection legibility and use clearer Delete labels. Add custom dimensions between two selected points as a later extension.

## Tolerances

Add tolerances to individual dimensions and a configurable **General Tolerances** area in the title block. Explore symmetric plus/minus, unequal upper/lower deviations and limit dimensions, with explicit units and precision. Individual tolerances override general values. Formats and defaults need design review. Future work outside the current workflow refactor.

## Sectional and cutaway views

Explore a user-defined cutting plane for revealing internal craft structure, enclosed engines and other components. Let the player position and rotate the plane, including angled cuts, choose which side remains visible, and preview the result in the drawing. The cut affects only that view and leaves the craft intact. This covers internal craft geometry; IVA cabin rendering is a separate question. Validate geometry availability, section linework and memory/performance costs before committing to implementation. Future work, outside the current workflow refactor scope.

## Paper wear patterns

The 0.6.0-local.35 preview applies a lettering rub at every wear level above Off. The count rises from one light scuff at level 1 to three or four stronger rubs at level 5. Tears vary in shape, edge, depth and reflection. Solid fibers cover the underlying print on all four themes.

| Level | Rubs | Any tear | Two tears | Coffee ring |
| --- | --- | --- | --- | --- |
| Off | 0 | 0% | 0% | 0% |
| 1 | 1 | 5% | 0% | 0% |
| 2 | 1 or 2 | 15% | 5% | 5% |
| 3 | 2 | 40% | 10% | 8% |
| 4 | 2 or 3 | 65% | 25% | 15% |
| 5 | 3 or 4 | 100% | 40% | 20% |

The two-tear percentage is included in the any-tear percentage. **Place > Place wear** offers thumbnails for four tears, four rubs and a coffee ring. Adjust the size and angle and click repeatedly to place marks. Edge snapping is optional for tears. Placed marks work with automatic wear Off and persist in the editable drawing. In-game placement testing is pending.

## Linework refinement

A trial filter for isolated specks changed 19 pixels on one captured landing leg and none on a second. Both kept the same silhouettes. Further work needs a drawing where the specks are easier to see.

Explore independent line thickness for part outlines and internal details. Small parts should retain their outline weight while their internal seams can print thinner.

## Native Linux support

The shared 0.6.0-local.36 preview targets Windows x64 and native Linux x86-64 on KSA 5949 with StarMap 0.5.4. Community testing on native Linux is pending. Test the same ZIP used on Windows and report startup, font and logo rendering, folder selection, editable save/reopen and PNG export.

## Feedback

Please include the PawPrints footer version, KSA version, reproduction steps, and whether the issue affects the preview, exported PNG, or both. Review logs and craft files for private information before sharing them. Roadmap items describe intended improvements, not features already available in the current release.
